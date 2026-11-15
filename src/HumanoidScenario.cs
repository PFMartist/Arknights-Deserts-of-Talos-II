using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Core.Data;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Native simulation owns positions and orders. This scenario only creates
    // the initial roster once; no scripted movement, damage, or respawning.
    public static class HumanoidScenario
    {
        static readonly CommanderID Player = new CommanderID(1);
        static readonly CommanderID Enemy = new CommanderID(2);
        static Entity Target = Entity.None;
        static int LastTargetHealth = -1;
        static bool Complete;
        static Entity[] Fleet;
        static uint LastReport;
        public static void Reset() { Complete=false;Fleet=null;LastReport=0;Target=Entity.None;LastTargetHealth=-1;HumanoidMovementDiagnostics.Reset(); }
        public static StartingUnit[] CreateFriendlyFleet()
        {
            StartingUnit lead=new StartingUnit();lead.Unit=HumanoidUnits.PerlicaKey;lead.Tag="EF_HUMANOID_PERLICA";
            return new StartingUnit[]{lead};
        }
        public static Entity[] EnsureRoster(object sim)
        {
            if(Complete)return Fleet;
            object initial=Runtime.FindOwnedUnit(sim,HumanoidUnits.PerlicaKey);
            if(initial==null)return null;
            Vector2r origin=Position(initial);
            Fixed64 inward=origin.X<=Fixed64.Zero?Fixed64.FromInt(1):Fixed64.FromInt(-1);
            Vector2r forward=new Vector2r(inward,Fixed64.Zero);
            Vector2r a=origin+new Vector2r(inward*Fixed64.FromInt(14),Fixed64.FromInt(8));
            Vector2r b=origin+new Vector2r(inward*Fixed64.FromInt(14),Fixed64.FromInt(-8));
            Entity first=Spawn(sim,Player,HumanoidUnits.GuardSquadKey,a,forward,"EF_HUMANOID_SQUAD_01");
            Entity second=Spawn(sim,Player,HumanoidUnits.GuardSquadKey,b,forward,"EF_HUMANOID_SQUAD_02");
            Fleet=new Entity[]{(Entity)NativeRead.Get(initial,"Entity"),first,second};
            HumanoidRangeTarget.VerifyRegistered();
            Vector2r targetPoint=origin+forward*Fixed64.FromInt(HumanoidRangeProfile.TargetForwardDistance);
            Target=Spawn(sim,Enemy,HumanoidRangeTarget.UnitKey,targetPoint,new Vector2r(-forward.X,-forward.Y),"EF_HUMANOID_RANGE_TARGET");
            int perlica=0,squads=0,targets=0,foreign=0;
            foreach(object unit in Units(sim))
            {
                Entity entity=(Entity)NativeRead.Get(unit,"Entity");
                if((CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity)!=Player)
                {
                    if(entity.Equals(Target)&&(string)NativeRead.Get(unit,"TypeName")==HumanoidRangeTarget.UnitKey&&
                        (CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity)==Enemy)targets++;
                    else foreign++;
                    continue;
                }
                string key=(string)NativeRead.Get(unit,"TypeName");
                if(key==HumanoidUnits.PerlicaKey)perlica++;else if(key==HumanoidUnits.GuardSquadKey)squads++;else foreign++;
            }
            if(perlica!=1||squads!=2||targets!=1||foreign!=0)throw new InvalidOperationException("Humanoid trial roster must be 1 elite, 2 squads and exactly 1 owned range target");
            Complete=true;
            Runtime.Log("HUMANOID_SCENARIO_READY elite=1; squads=2; visibleMembersPerSquad=6; logicalEntities=3; nativeCarriers=0; nativeLAVs=0; hostileUnits=1; targetHP=50000; targetForwardMetres=150; manualTargetOnly=True; scriptedMoveOrders=0; autoRespawn=False; flatMap=True; combatNotYetAccepted=True");
            return Fleet;
        }
        public static void Tick(object sim)
        {
            if(!Complete)return;
            HumanoidMovementDiagnostics.Tick(sim);
            foreach(object unit in Units(sim))
            {
                Entity id=(Entity)NativeRead.Get(unit,"Entity");
                CommanderID owner=(CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",id);
                if(owner!=Enemy)continue;
                if(!id.Equals(Target)||(string)NativeRead.Get(unit,"TypeName")!=HumanoidRangeTarget.UnitKey)
                    throw new InvalidOperationException("Unexpected enemy in humanoid target range");
                UnitState targetState=(UnitState)NativeRead.Call(unit,"GetState");
                if(targetState.Hitpoints!=LastTargetHealth)
                {
                    Runtime.Log("HUMANOID_TARGET_HEALTH entity="+id+"; previous="+LastTargetHealth+"; current="+targetState.Hitpoints+"; dead="+targetState.UnitIsDead+"; nativeDamageOnly=True; healingOrRespawnCommands=0");
                    LastTargetHealth=targetState.Hitpoints;
                }
            }
            uint frame=Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));
            if(LastReport!=0&&frame-LastReport<160)return;LastReport=frame;
            foreach(object unit in Units(sim))
            {
                string key=(string)NativeRead.Get(unit,"TypeName");
                if(key!=HumanoidUnits.PerlicaKey&&key!=HumanoidUnits.GuardSquadKey)continue;
                UnitState state=(UnitState)NativeRead.Call(unit,"GetState");
                Runtime.Log("HUMANOID_NATIVE_STATE frame="+frame+"; key="+key+"; entity="+NativeRead.Get(unit,"Entity")+"; position="+state.StartOfFrameDynamicState.Position+"; hp="+state.Hitpoints+"; commandsIssuedByObserver=0");
            }
        }
        static Entity Spawn(object sim,CommanderID owner,string key,Vector2r point,Vector2r forward,string tag)
        {
            HashSet<Entity> prior=new HashSet<Entity>();foreach(object unit in Units(sim))prior.Add((Entity)NativeRead.Get(unit,"Entity"));
            Type factory=typeof(Sim).Assembly.GetType("BBI.Game.Commands.SimCommandFactory",true);
            object command=NativeRead.Call(factory,"CreateSpawnUnitsCommand",owner,new string[]{key},new string[]{tag},point,Orientation2.FromDirection(forward));
            if(!(bool)NativeRead.Call(command,"RunCommand",sim,NativeRead.Get(sim,"InteractionProvider"),NativeRead.Get(sim,"GlobalFrameCount")))throw new InvalidOperationException("Humanoid native spawn rejected: "+tag);
            Entity result=Entity.None;int count=0;
            foreach(object unit in Units(sim))
            {
                Entity entity=(Entity)NativeRead.Get(unit,"Entity");if(prior.Contains(entity))continue;
                if((string)NativeRead.Get(unit,"TypeName")!=key||(CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity)!=owner)throw new InvalidOperationException("Humanoid spawn identity mismatch");
                if((Position(unit)-point).Length>Fixed64.FromInt(8))throw new InvalidOperationException("Humanoid initial spawn displaced excessively");
                result=entity;count++;
            }
            if(count!=1)throw new InvalidOperationException("Humanoid native spawn did not create one squad entity");
            return result;
        }
        static IEnumerable Units(object sim) { return (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"); }
        static Vector2r Position(object unit) { return ((UnitState)NativeRead.Call(unit,"GetState")).StartOfFrameDynamicState.Position; }
    }
}
