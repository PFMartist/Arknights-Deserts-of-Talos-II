using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BBI.Core.ComponentModel;
using BBI.Core.Events;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Events;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Passive evidence only: actual native production events, queue and balances.
    // No production/spawn/move/rally/refund commands are issued by this observer.
    public static class RhodesProductionTelemetry
    {
        const int ProductionComponentID=15;
        const int CompletedState=4; // ProductionEvent.ProductionState.ProductionCompleted in build 12339551.
        static readonly CommanderID Player=new CommanderID(1);
        static readonly object Gate=new object();
        static readonly Queue<Observation> Events=new Queue<Observation>();
        static readonly BBI.Core.Events.EventHandler<ProductionEvent> Handler=OnProduction;
        static readonly Dictionary<Entity,Observation> AwaitingActualUnits=new Dictionary<Entity,Observation>();
        static readonly HashSet<Entity> SeenUnits=new HashSet<Entity>();
        static IEventReceiver Receiver;
        static Entity Producer=Entity.None;
        static bool HaveProducer,Initialized,HaveFrame,Overflow;
        static uint LastFrame,LastProgressFrame;
        static string LastQueue="",LastBalance="";
        static int EventReports;

        struct Observation
        {
            public Entity Source,Produced;
            public string Ability,Unit;
            public int State;
            public bool Immediate;
        }

        public static void Reset()
        {
            if(Receiver!=null)Receiver.RemoveHandler<ProductionEvent>(Handler);
            Receiver=null;AwaitingActualUnits.Clear();SeenUnits.Clear();
            lock(Gate){Events.Clear();Producer=Entity.None;HaveProducer=false;Overflow=false;}
            Initialized=HaveFrame=false;LastFrame=LastProgressFrame=0;LastQueue=LastBalance="";EventReports=0;
        }

        public static void Tick(object sim)
        {
            object carrier=Runtime.FindOwnedUnit(sim,RhodesCarrierUnits.UnitKey);
            if(carrier==null)return;
            Entity entity=(Entity)NativeRead.Get(carrier,"Entity");
            lock(Gate){Producer=entity;HaveProducer=true;}
            IEventReceiver current=(IEventReceiver)NativeRead.Get(sim,"SimEventSystem");
            if(!object.ReferenceEquals(current,Receiver))
            {
                if(Receiver!=null)Receiver.RemoveHandler<ProductionEvent>(Handler);
                Receiver=current;if(Receiver!=null)Receiver.AddHandler<ProductionEvent>(Handler);
            }
            uint frame=Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));
            if(HaveFrame&&frame==LastFrame)return;HaveFrame=true;LastFrame=frame;
            UnitState carrierState=(UnitState)NativeRead.Call(carrier,"GetState");
            IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
            object production=system.GetComponent<object>(entity,ProductionComponentID);
            if(production==null)
            {
                if(!carrierState.UnitIsDead&&frame>160)throw new InvalidOperationException("Rhodes carrier has no native production component");
                return;
            }
            Observation observation;
            while(Take(out observation))
            {
                bool matching=AllowedPair(observation.Ability,observation.Unit);
                if(EventReports<96)
                {
                    EventReports++;
                    Runtime.Log("RHODES_PRODUCTION_EVENT source="+observation.Source+"; state="+observation.State+
                        "; ability="+observation.Ability+"; unit="+observation.Unit+"; entity="+observation.Produced+
                        "; producedImmediately="+observation.Immediate+"; matchingPrivateAbilityAndTarget="+matching+
                        "; nativeEventOnly=True; spawnOrRefundCommandsIssued=0");
                }
                if(observation.State==CompletedState&&matching)
                {
                    if(!observation.Produced.Equals(Entity.None))AwaitingActualUnits[observation.Produced]=observation;
                    else Runtime.Log("RHODES_PRODUCTION_ENTITY_MAPPING_UNAVAILABLE nativeCompletionEvent=True; entity=None; noSyntheticReplacement=True");
                }
            }
            if(Overflow){Runtime.Log("RHODES_PRODUCTION_TELEMETRY_EVENT_LIMIT queueObservationsDropped=True; simulationUnchanged=True");Overflow=false;}
            ProductionState state=(ProductionState)NativeRead.Call(production,"GetState");
            List<string> types=new List<string>(),progress=new List<string>();
            for(int queue=0;queue<state.ProductionQueues.Count;queue++)
            {
                List<ProductionState.QueueEntryState> entries=state.ProductionQueues[queue];
                for(int slot=0;slot<entries.Count;slot++)
                {
                    ProductionState.QueueEntryState entry=entries[slot];
                    types.Add(queue+":"+slot+":"+entry.TypeID);
                    progress.Add(queue+":"+slot+":"+entry.TypeID+"@"+F(entry.Completion01)+"/"+F(entry.TimeRemaining));
                }
            }
            string queueKey=string.Join(",",types.ToArray());
            if(queueKey!=LastQueue||(types.Count>0&&(LastProgressFrame==0||frame-LastProgressFrame>=32)))
            {
                Runtime.Log("RHODES_PRODUCTION_QUEUE frame="+frame+"; queues="+state.ProductionQueues.Count+
                    "; emptySlots="+state.EmptySlots+"; entries="+string.Join(",",progress.ToArray())+
                    "; nativeGroundRally="+state.GroundRallyPoint+"; nativeUnitRally="+state.UnitRallyPoint+
                    "; autoRallyCorrection=False; observerCommands=0");
                LastQueue=queueKey;LastProgressFrame=frame;
            }
            object commander=NativeRead.Call(NativeRead.Get(sim,"CommanderManager"),"GetCommanderFromID",Player);
            object storage=NativeRead.Get(commander,"Storage");
            int cu=Convert.ToInt32(NativeRead.Call(storage,"GetCurrentAmount","Resource1"));
            int ru=Convert.ToInt32(NativeRead.Call(storage,"GetCurrentAmount","Resource2"));
            string balance=cu+"/"+ru+"/"+NativeRead.Get(commander,"CurrentPopCapUsage")+"/"+NativeRead.Get(commander,"PopCap");
            if(balance!=LastBalance)
            {
                Runtime.Log("RHODES_PRODUCTION_BALANCE frame="+frame+"; CU="+cu+"; RU="+ru+
                    "; populationUsed="+NativeRead.Get(commander,"CurrentPopCapUsage")+"; populationCap="+NativeRead.Get(commander,"PopCap")+
                    "; resourceWritesByObserver=0; refundUsesNativeCancelCommand=True");
                LastBalance=balance;
            }
            foreach(object unit in (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"))
            {
                Entity id=(Entity)NativeRead.Get(unit,"Entity");
                if((CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",id)!=Player||SeenUnits.Contains(id))continue;
                string key=(string)NativeRead.Get(unit,"TypeName");
                if(key!=RhodesCarrierUnits.UnitKey&&key!=CustomUnits.UnitKey&&key!=HumanoidUnits.GuardSquadKey&&key!=RhodesCarrierUnits.AircraftKey&&key!=RhodesManualDamage.SupportKey)
                    throw new InvalidOperationException("Unexpected owned unit in Rhodes production test: "+key);
                UnitState actual=(UnitState)NativeRead.Call(unit,"GetState");
                if(actual.UnitIsDead||Convert.ToBoolean(NativeRead.Get(unit,"PresentationOnly")))continue;
                SeenUnits.Add(id);Observation completion;
                bool traced=AwaitingActualUnits.TryGetValue(id,out completion);
                Vector2r delta=actual.StartOfFrameDynamicState.Position-carrierState.StartOfFrameDynamicState.Position;
                Vector2r back=carrierState.StartOfFrameDynamicState.Orientation.Back,right=carrierState.StartOfFrameDynamicState.Orientation.Right;
                float localBack=Fixed64.UnsafeFloatValue(delta.X*back.X+delta.Y*back.Y);
                float localRight=Fixed64.UnsafeFloatValue(delta.X*right.X+delta.Y*right.Y);
                bool outsideHull=Math.Abs(localBack)>RhodesCarrierUnits.LengthMetres*.5f||Math.Abs(localRight)>RhodesCarrierUnits.LengthMetres*(13.499716f/29.762559f)*.5f;
                if(Initialized||traced)
                    Runtime.Log("RHODES_PRODUCTION_UNIT_OBSERVED entity="+id+"; key="+key+"; matchedNativeCompletionEvent="+traced+
                        "; location="+actual.StartOfFrameDynamicState.Position+"; relativeBackMetres="+localBack+"; relativeRightMetres="+localRight+
                        "; centreOutsideApproxHull="+outsideHull+"; expectedVisibleMembers="+(key==HumanoidUnits.GuardSquadKey?6:1)+
                        "; newUnitProducedByObserver=False; movementCommandsIssued=0; gameVisualAcceptance=False");
                if(traced)AwaitingActualUnits.Remove(id);
            }
            Initialized=true;
        }

        static string F(Fixed64 value)
        { return Fixed64.UnsafeFloatValue(value).ToString("0.###",CultureInfo.InvariantCulture); }
        static bool AllowedPair(string ability,string unit)
        { return (ability==RhodesCarrierUnits.TankProductionKey&&unit==CustomUnits.UnitKey)||(ability==RhodesCarrierUnits.SquadProductionKey&&unit==HumanoidUnits.GuardSquadKey); }
        static void OnProduction(ProductionEvent message)
        {
            if(message==null)return;
            lock(Gate)
            {
                if(!HaveProducer||!message.Source.Equals(Producer))return;
                if(Events.Count>=512){Overflow=true;return;}
                Events.Enqueue(new Observation{Source=message.Source,Produced=message.EntityProduced,Ability=message.AbilityType,
                    Unit=message.UnitProductionType,State=Convert.ToInt32(message.CurrentProductionState),Immediate=message.ProducedImmediately});
            }
        }
        static bool Take(out Observation observation)
        {
            lock(Gate){if(Events.Count>0){observation=Events.Dequeue();return true;}}
            observation=new Observation();return false;
        }
    }
}
