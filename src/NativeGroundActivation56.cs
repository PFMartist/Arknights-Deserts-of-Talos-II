using System;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Unity.Core.World;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // UnitView starts with a 0.2s suspension settling timer. Only the native
    // SuspensionUpdate decrements it, even though the unit-level Grounded FX
    // flag also waits for it. Our no-suspension views must finish that wait at
    // binding. The following native fixed tick still owns Grounded/airborne;
    // no state flags, physics, colliders, quality settings or FX are forced.
    internal sealed class NativeGroundActivation56
    {
        static readonly HashSet<string> ObservedKeys=new HashSet<string>(StringComparer.Ordinal);
        readonly string Key;
        readonly Entity Entity;
        float NextSample;
        int Samples;

        NativeGroundActivation56(string key,UnitView view)
        {Key=key;Entity=view.EntityID;NextSample=Time.realtimeSinceStartup+.5f;}

        internal static NativeGroundActivation56 BindFaction(UnitView view,FactionModelBinding binding)
        {
            if(binding==null||binding.Air||binding.gameObject!=view.gameObject)return null;
            return Bind(view,binding.UnitKey);
        }

        internal static NativeGroundActivation56 BindCarrier(UnitView view,RhodesCarrierBinding binding)
        {
            if(binding==null||!binding.IsV05||binding.gameObject!=view.gameObject)return null;
            return Bind(view,"RhodesCarrier");
        }

        static NativeGroundActivation56 Bind(UnitView view,string key)
        {
            // This is optional FX activation. A diagnostic failure must never
            // disable a vehicle's geometry or presentation driver.
            try
            {
                object spawn=NativeRead.Get(view,"mSpawnSettings");
                UnitViewAttributes settings=NativeRead.Get(spawn,"UnitViewSettings") as UnitViewAttributes;
                if(settings==null||settings.LODTunings==null||
                    Convert.ToInt32(NativeRead.Get(settings.LODTunings,"SuspensionActiveLODs"))!=0||
                    Convert.ToInt32(NativeRead.Get(settings.LODTunings,"BodyMotionActiveLODs"))!=0)
                    throw new InvalidOperationException("Expected private no-suspension ground view");
                if(NativeRead.Get(view,"mUnitFXController")==null)
                    throw new InvalidOperationException("Native unit FX controller not initialized");
                float before=CompleteSuspensionSettle(view);
                if(!ObservedKeys.Add(key))return null;
                Runtime.Log("RHODES_GROUND_FX56_SETTLE key="+key+"; entity="+view.EntityID+"; before="+before+
                    "; after="+NativeRead.Get(view,"mSuspensionSettleTime")+
                    "; nativeGroundedTickPending=True; suspension=False; forcedFlags=0; customUpdates=0");
                return new NativeGroundActivation56(key,view);
            }
            catch(Exception error)
            {Runtime.Log("RHODES_GROUND_FX56_BIND_DISABLED key="+key+"; reason="+error.Message+"; geometryRetained=True");return null;}
        }

        // Field-only operation. Native IL establishes the blocking timer and
        // next-fixed-tick Grounded path; rendering still needs game validation.
        internal static float CompleteSuspensionSettle(UnitView view)
        {
            float before=Convert.ToSingle(NativeRead.Get(view,"mSuspensionSettleTime"));
            if(float.IsNaN(before)||float.IsInfinity(before))throw new InvalidOperationException("Nonfinite native suspension wait");
            if(before>0f)NativeRead.Set(view,"mSuspensionSettleTime",0f);
            return before;
        }

        // Read-only, at most six samples for the first instance of each type
        // per registry. Called by existing presentation hosts, with no added
        // scene scans, raycasts, Update callback or persistent per-frame work.
        internal bool Observe(UnitView view)
        {
            if(view==null||!Entity.Equals(view.EntityID))return true;
            if(Time.realtimeSinceStartup<NextSample)return false;
            NextSample=Time.realtimeSinceStartup+2f;
            try
            {
                object controller=NativeRead.Get(view,"mUnitFXController");
                int flags=Convert.ToInt32(NativeRead.Get(controller,"mCurrentStateFlags"));
                Array updaters=NativeRead.Get(controller,"mFrillUpdaters") as Array;
                int dust=0,prints=0,activeDust=0,activePrints=0,instances=0;
                if(updaters!=null)foreach(object updater in updaters)
                {
                    if(updater==null)continue;
                    object definition=NativeRead.Get(updater,"InternalFXDefinition");
                    if(definition==null)continue;
                    string name=NativeRead.Get(definition,"Name") as string;
                    FrillType type=(FrillType)NativeRead.Get(definition,"FrillType");
                    bool print=name!=null&&name.IndexOf("EF_NativeTracks55_",StringComparison.Ordinal)>=0;
                    bool cloud=!print&&(type==FrillType.WheelContactRange||type==FrillType.Nitrous);
                    if(!print&&!cloud)continue;
                    bool active=Convert.ToBoolean(NativeRead.Get(updater,"IsActive"));
                    if(print){prints++;if(active)activePrints++;}
                    else{dust++;if(active)activeDust++;}
                    Array spawned=NativeRead.Get(updater,"mEffectInstances") as Array;
                    if(spawned!=null)foreach(Transform effect in spawned)if(effect!=null)instances++;
                }
                Runtime.Log("RHODES_GROUND_FX56_LIVE key="+Key+"; entity="+Entity+"; sample="+(++Samples)+
                    "; settle="+NativeRead.Get(view,"mSuspensionSettleTime")+"; flags="+flags+"; grounded="+((flags&64)!=0)+
                    "; idle="+((flags&1)!=0)+"; visible="+((flags&256)!=0)+"; airborne="+NativeRead.Get(view,"mIsAirborne")+
                    "; nativeLOD="+NativeRead.Get(view,"mCurrLod")+"; dust="+activeDust+"/"+dust+"; prints="+activePrints+"/"+prints+
                    "; spawnedInstances="+instances+"; wheelFX="+ShipbreakersMain.UserSettings.Video.UnitFXToggles.WheelFX+
                    "; tracksPersistence="+ShipbreakersMain.UserSettings.Video.TracksPersistence+"; pixelsVerified=False");
                return Samples>=6||activeDust>0&&activePrints>0&&instances>0;
            }
            catch(Exception error)
            {Runtime.Log("RHODES_GROUND_FX56_OBSERVATION_DISABLED key="+Key+"; reason="+error.Message);return true;}
        }

        internal static void Reset(){ObservedKeys.Clear();}
    }
}
