using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Passive gameplay evidence. It never issues commands, writes state, or
    // infers a successful visual test from simulation counters.
    public static class BvmUnitTelemetry
    {
        sealed class Record
        {
            public Vector2r LastPosition;
            public float Distance, MaxSpeed;
            public int HP, Rounds, Reports;
            public uint LastShotFrame;
            public bool HaveShot, Dead;
        }
        static readonly Dictionary<Entity,Record> Records=new Dictionary<Entity,Record>();
        static float TickSeconds;
        static uint LastFrame;

        public static void Reset() { Records.Clear();TickSeconds=0f;LastFrame=0; }
        public static void Tick(object sim)
        {
            uint frame=Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));
            if(frame==LastFrame)return;LastFrame=frame;
            if(TickSeconds<=0f)TickSeconds=Fixed64.UnsafeFloatValue((Fixed64)NativeRead.Get(typeof(SimController),"kSimTickPeriodS"));
            foreach(object unit in (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"))
            {
                if((string)NativeRead.Get(unit,"TypeName")!=CustomUnits.UnitKey)continue;
                Entity entity=(Entity)NativeRead.Get(unit,"Entity");
                UnitState state=(UnitState)NativeRead.Call(unit,"GetState");
                UnitDynamicState dynamic=state.StartOfFrameDynamicState;
                int rounds=BvmNativeState.GetRoundSequence(entity);
                Record record;
                if(!Records.TryGetValue(entity,out record))
                {
                    record=new Record();record.LastPosition=dynamic.Position;record.HP=state.Hitpoints;record.Rounds=rounds;Records.Add(entity,record);
                    Runtime.Log("BVM_GAMEPLAY_OBSERVED entity="+entity+"; actualInitialHP="+state.Hitpoints+"; simTickSeconds="+TickSeconds+"; passiveOnly=True; visualAcceptanceNotInferred=True");
                }
                record.Distance+=Fixed64.UnsafeFloatValue((dynamic.Position-record.LastPosition).Length);
                record.LastPosition=dynamic.Position;
                record.MaxSpeed=Math.Max(record.MaxSpeed,Fixed64.UnsafeFloatValue(dynamic.Velocity.Length));
                if(rounds<record.Rounds){record.Rounds=rounds;record.HaveShot=false;Runtime.Log("BVM_PRESENTATION_COUNTER_REBOUND entity="+entity+"; newBaseline="+rounds+"; negativeRoundsNotReported=True");}
                if(rounds!=record.Rounds)
                {
                    int count=unchecked(rounds-record.Rounds);
                    float interval=record.HaveShot?(frame-record.LastShotFrame)*TickSeconds:0f;
                    if(record.Reports<24)
                    {
                        Runtime.Log("BVM_FIRE_CADENCE_OBSERVED entity="+entity+"; roundSequence="+rounds+"; newRounds="+count+"; observedIntervalSeconds="+interval+"; firstObservedRound="+(!record.HaveShot)+"; maxNativeSpeedMetersPerSecond="+record.MaxSpeed+"; travelMeters="+record.Distance+"; passiveOnly=True; timingSource=presentationCounterSample; exactRateAssertion=False");
                        record.Reports++;
                    }
                    record.Rounds=rounds;record.LastShotFrame=frame;record.HaveShot=true;
                }
                if(record.HP!=state.Hitpoints)
                {
                    Runtime.Log("BVM_HEALTH_OBSERVED entity="+entity+"; previous="+record.HP+"; current="+state.Hitpoints+"; nativeDeath="+state.UnitIsDead+"; passiveOnly=True");
                    record.HP=state.Hitpoints;
                }
                if(state.UnitIsDead&&!record.Dead)
                {
                    record.Dead=true;
                    Runtime.Log("BVM_NATIVE_DEATH_OBSERVED entity="+entity+"; hp="+state.Hitpoints+"; maxNativeSpeedMetersPerSecond="+record.MaxSpeed+"; travelMeters="+record.Distance+"; roundSequence="+rounds+"; wreckAppearanceStillRequiresVisualTest=True");
                }
            }
        }
    }
}