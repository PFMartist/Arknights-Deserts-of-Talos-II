// Simulation-thread, pre-fire gate. The native state machine still owns
// windup, burst cancellation, reload, targets, commands and damage.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    public static class HumanoidFireGate
    {
        public const float StopSpeedMetersPerSecond=.06f;
        public const uint StationarySettleFrames=1;
        static readonly Fixed64 StopSpeedSquared=Fixed64.FromConstFloat(StopSpeedMetersPerSecond)*Fixed64.FromConstFloat(StopSpeedMetersPerSecond);
        sealed class StillState { public bool Stationary;public uint StationarySince,LastFrame; }
        static readonly Dictionary<Entity,StillState> Still=new Dictionary<Entity,StillState>();
        static Type Extensions,FireType;
        static MethodInfo ManeuverGetter,WeaponsGetter;
        static int Reports;
        public static void Reset(){RhodesArtilleryPredictiveFireControl.Reset();Still.Clear();Reports=0;}

        // Exact native hook contract:
        // static WeaponFireOperation.ProcessWeaponFire(Entity owner,Fixed64 dt,
        // WeaponFireOperation operation), prefix ldarg.0 / ldarg.2 / call here.
        public static void BeforeProcess(Entity owner,object fireOperation)
        {
            if(Runtime.RhodesCarrier){RhodesDeploymentState.BeforeProcess(owner,fireOperation);RhodesTiltrotorFlightControl.BeforeProcessWeapon(owner,fireOperation);RhodesEmbarkedOperatorSkills.BeforeProcess(owner,fireOperation);RhodesArtilleryPredictiveFireControl.BeforeProcess(owner,fireOperation);}            if(!Runtime.HumanoidServicesEnabled)return;
            EnsureReflection();object operation=fireOperation;
            if(operation==null||!FireType.IsInstanceOfType(operation)||(bool)NativeRead.Call(Extensions,"IsValid",owner)==false)return;
            string key=(string)NativeRead.Call(Extensions,"GetTypeName",owner);
            if(key!=HumanoidUnits.GuardSquadKey&&key!=HumanoidUnits.PerlicaKey&&!RhodesOperatorRoster.Contains(key))return;
            IList weapons=(IList)WeaponsGetter.Invoke(null,new object[]{owner,6});
            // Only loadout binding zero. In particular this does not gate the
            // independently activated orbital weapon at binding one.
            if(weapons==null||weapons.Count==0||!object.ReferenceEquals(operation,weapons[0]))return;
            if((bool)NativeRead.Call(typeof(Sim),"IsEntityDead",owner)){NativeRead.Set(operation,"FiringCriteriaMet",false);Still.Remove(owner);return;}
            object movement=ManeuverGetter.Invoke(null,new object[]{owner,12});
            if(movement==null){NativeRead.Set(operation,"FiringCriteriaMet",false);return;}
            // This is the live simulation component at the exact firing point,
            // not the end-of-tick presentation snapshot or interpolated view.
            Vector2r velocity=((UnitDynamicState)NativeRead.Get(movement,"DynamicState")).Velocity;
            object sim=NativeRead.Get(typeof(Sim),"Instance");
            uint frame=Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));
            bool allowed=ApplyBasicGate(owner,operation,velocity,frame);
            if(!allowed&&Reports<8){Reports++;Runtime.Log("HUMANOID_STOP_TO_FIRE owner="+owner+"; key="+key+"; velocity="+velocity+"; frame="+frame+"; nativeCriteriaSuppressed=True; commandsUnchanged=True; reloadStateMachineContinues=True; orbitalBindingExcluded=True");}
        }
        // Factored only to verify the actual native fire-operation flag with
        // deterministic fixed-point velocities in an offline fixture.
        static bool ApplyBasicGate(Entity owner,object operation,Vector2r velocity,uint frame)
        {
            StillState state;if(!Still.TryGetValue(owner,out state)){state=new StillState();Still.Add(owner,state);}
            if(frame<state.LastFrame)state.Stationary=false;
            state.LastFrame=frame;
            Fixed64 speedSquared=velocity.X*velocity.X+velocity.Y*velocity.Y;
            bool moving=speedSquared>StopSpeedSquared;
            if(moving)state.Stationary=false;
            else if(!state.Stationary){state.Stationary=true;state.StationarySince=frame;}
            bool allowed=!moving&&frame-state.StationarySince>=StationarySettleFrames;
            // Never set true: native range, LOS, target, ammo and other guards
            // must still pass. Do not write RequestEnabled or CurrentTarget.
            if(!allowed)NativeRead.Set(operation,"FiringCriteriaMet",false);
            return allowed;
        }
        static void EnsureReflection()
        {
            if(ManeuverGetter!=null&&WeaponsGetter!=null)return;
            Assembly native=typeof(Sim).Assembly;
            Extensions=native.GetType("BBI.Game.Simulation.EntityComponentExtensions",true);
            FireType=native.GetType("BBI.Game.Simulation.WeaponFireOperation",true);
            Type maneuver=native.GetType("BBI.Game.Simulation.Maneuvering",true);
            const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
            foreach(MethodInfo method in Extensions.GetMethods(flags))
                if(method.Name=="GetComponent"&&method.IsGenericMethodDefinition&&method.GetParameters().Length==2){ManeuverGetter=method.MakeGenericMethod(new Type[]{maneuver});break;}
            Type listExtensions=native.GetType("BBI.Game.Simulation.EntityListComponentExtensions",true);
            foreach(MethodInfo method in listExtensions.GetMethods(flags))
                if(method.Name=="GetListComponent"&&method.IsGenericMethodDefinition&&method.GetParameters().Length==2){WeaponsGetter=method.MakeGenericMethod(new Type[]{FireType});break;}
            if(ManeuverGetter==null||WeaponsGetter==null)throw new MissingMethodException("Native humanoid fire gate component accessors absent");
        }
    }
}
