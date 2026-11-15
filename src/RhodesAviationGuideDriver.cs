using System;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Unity.Game;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Pure managed profile shared with the private native bay configuration.
    // Interceptor_Launch is not scaled by native queue duration. Its wings
    // open at clip time3.2667..4.3333s. A private per-aircraft speed lease
    // delays only this clip's playback by8.625s, retaining the accepted03
    // wing/gear/catapult relationship with the longer lift. No clip/controller
    // asset, animation time, aircraft Transform or simulation owner is written.
    // The aircraft stays natively guide-owned throughout the12s lift; only
    // after deck arrival and an additional1.125s hold may catapult begin.
    // Queue and whole-bay clocks are updated together to native8Hz boundaries.
    internal static class RhodesAviationLaunchProfile
    {
        internal const float LiftSeconds=12f,ReadySeconds=13.125f,NativeSequenceSeconds=13.875f,NativeTickSeconds=.125f,ReleaseSpeed=350f;
        internal const float WholeBayCycleSeconds=14.625f,BringInSeconds=12f;
        // Empty-platform return is independent of the occupied recovery descent.
        // A following group can reuse a lane before the old12s return completed.
        internal const float EmptyReturnSeconds=4f;
        internal static float EmptyReturnProgress(float nativeAge)
        {return SmoothFraction(nativeAge/EmptyReturnSeconds);}
        internal static bool AcceptNativeDetach(int launchMode,bool hasAircraft,bool aircraftMatches)
        {return launchMode!=0&&hasAircraft&&aircraftMatches;}
        internal const float LaunchAnimationDelaySeconds=ReadySeconds-4.5f;
        internal const float AlignmentSeconds=5f,ApproachSeconds=6f,RecoverySettleSeconds=.5f;
        internal static bool HoldLaunchAnimation(float nativeAge)
        {return nativeAge>=0f&&nativeAge<LaunchAnimationDelaySeconds;}
        internal static float RecoveryPlatformProgress(float nativeAge)
        {return SmoothFraction(nativeAge/(AlignmentSeconds+ApproachSeconds-RecoverySettleSeconds));}
        internal static float NativePresentationAge(int startTick,float startSubFrame,int currentTick,float subFrame)
        {
            if(startTick<0||currentTick<startTick)return 0f;
            if(float.IsNaN(startSubFrame)||float.IsInfinity(startSubFrame))startSubFrame=0f;
            if(float.IsNaN(subFrame)||float.IsInfinity(subFrame))subFrame=0f;
            return (float)Math.Max(0d,((long)currentTick-startTick+Math.Max(0f,Math.Min(1f,subFrame))-Math.Max(0f,Math.Min(1f,startSubFrame)))*NativeTickSeconds);
        }
        // PullUndockReadyRequestListEntity first marks an expired slot ready,
        // returning false. Actual native detach occurs on the following tick.
        internal const float ReleaseSeconds=NativeSequenceSeconds+NativeTickSeconds;
        // Actual opaque source triangle planes, in converted RHS2 metre space.
        // The native Launch clip's deployed main wheels are BELOW the root.
        internal const float LowerSurfaceY=71.566421795f,DeckSurfaceY=128.954421997f,RailSurfaceY=129.720474243f;
        internal const float MainWheelY=-.131996140f,EjectStartMetres=26.213251f;
        internal const float LowerRootY=LowerSurfaceY-MainWheelY,DeckRootY=DeckSurfaceY-MainWheelY,RailRootY=RailSurfaceY-MainWheelY;
        // V06 well surfaces are permanent authored markers. They move neither
        // the original HPs nor the simulation runway. The lowered platform and
        // aircraft now sit inside the cut hull, below the original main deck.
        internal static float SurfaceRoot(float surfaceY)
        {
            if(float.IsNaN(surfaceY)||float.IsInfinity(surfaceY))throw new ArgumentOutOfRangeException("surfaceY");
            return surfaceY-MainWheelY;
        }
        internal static float PlatformPin(float aircraftRootY,float topAbovePivot)
        {
            if(topAbovePivot<=0f||float.IsNaN(topAbovePivot)||float.IsInfinity(topAbovePivot))throw new ArgumentOutOfRangeException("topAbovePivot");
            return aircraftRootY+MainWheelY-topAbovePivot;
        }
        internal static bool PresentationAllowed(bool alive,bool rootShown,bool aviationReady)
        {return alive&&rootShown&&aviationReady;}
        internal static float SmoothFraction(float value)
        {
            if(float.IsNaN(value)||float.IsInfinity(value))return 0f;
            float t=Math.Max(0f,Math.Min(1f,value));return t*t*(3f-2f*t);
        }
        internal static float NativeAge(int startTick,int currentTick,float subFrame)
        {
            if(startTick<0||currentTick<startTick)return 0f;
            if(float.IsNaN(subFrame)||float.IsInfinity(subFrame))subFrame=0f;
            subFrame=Math.Max(0f,Math.Min(1f,subFrame));
            return (float)(((long)currentTick-startTick+subFrame)*NativeTickSeconds);
        }
        internal static float CatapultPower(float horizontalMetres)
        {
            if(float.IsNaN(horizontalMetres)||float.IsInfinity(horizontalMetres)||horizontalMetres<=0f)
                throw new ArgumentOutOfRangeException("horizontalMetres");
            float power=ReleaseSpeed*(ReleaseSeconds-ReadySeconds)/horizontalMetres;
            if(power<=1f)throw new InvalidOperationException("V05 runway cannot accelerate from rest within its native release timing");
            return power;
        }
        internal static float CatapultProgress(float age,float horizontalMetres)
        {
            float power=CatapultPower(horizontalMetres);
            if(age<=ReadySeconds)return 0f;if(age>=ReleaseSeconds)return 1f;
            double t=(age-ReadySeconds)/(ReleaseSeconds-ReadySeconds);
            // Positive acceleration through the edge. Smoothstep would brake
            // back to zero before native simulation takes over at 350m/s.
            return (float)Math.Pow(t,power);
        }
        internal static float ContactBlend(float runwayMetres,float ejectStartMetres)
        {
            if(ejectStartMetres<=0f||float.IsNaN(ejectStartMetres)||float.IsInfinity(ejectStartMetres))
                throw new ArgumentOutOfRangeException("ejectStartMetres");
            float t=Math.Max(0f,Math.Min(1f,runwayMetres/ejectStartMetres));
            return t*t*(3f-2f*t);
        }
    }

    // Pure presentation guides driven by actual native hangar events. Native
    // UnitView remains the only code attaching/moving the aircraft's visual;
    // native simulation remains the only owner of production/flight/docking.
    public sealed class RhodesAviationGuideDriver:MonoBehaviour
    {
        const string AircraftType=RhodesCarrierUnits.AircraftKey;
        const string LaunchPrefix="EF_AV_UndockControl_";
        const string DockPrefix="EF_AV_DockControl_";
        const string LowerContactPrefix="EF_AV_LiftLowerContact_",DeckContactPrefix="EF_AV_LiftDeckContact_";
        const string ExitContactPrefix="EF_AV_RunwayExitContact_",EjectStartPrefix="EF_AV_CatapultEjectStart_";
        const int InterceptorClass=1179649;
        static readonly Dictionary<Entity,RhodesAviationGuideDriver> Carriers=new Dictionary<Entity,RhodesAviationGuideDriver>();
        static readonly Dictionary<Entity,RhodesAviationGuideDriver> Aircraft=new Dictionary<Entity,RhodesAviationGuideDriver>();
        UnitView View;RhodesCarrierBinding Binding;
        Entity RegisteredOwner=Entity.None;
        Lane[] Lanes;
        int LastFrame=-1;
        int LaunchTraces;
        // These match the retained C native bay. Root must update both bay and
        // this presentation profile if it later changes native launch timings.
        const float LiftSeconds=RhodesAviationLaunchProfile.LiftSeconds;
        const float AlignmentSeconds=RhodesAviationLaunchProfile.AlignmentSeconds,ApproachSeconds=RhodesAviationLaunchProfile.ApproachSeconds,BringInSeconds=RhodesAviationLaunchProfile.BringInSeconds;
        // Measured rigid platform top minus its permanent pivot. Binding-pose
        // whole-mesh minima are not the deployed landing-gear contact offset.
        static readonly float[] PlatformTopAbovePivot={1.007987976f,1.007972717f,1.007987976f};
        sealed class Lane
        {
            public Transform Hangar,Deck,Recovery,Exit,EjectStart,LaunchControl,DockControl,LiftPivot,GeometryRoot;
            public Vector3 AuthoredLiftLocalPosition;
            public int LaunchMode,DockMode;public float LaunchAge,DockAge;
            public int Index,LaunchStartTick=-1,DockStartTick=-1,ReturnStartTick=-1;
            public RhodesLaunchAnimationLease LaunchLease;
            public bool LaunchAnimationCancelled;
            public float DockStartSubFrame,ReturnStartSubFrame;
            public bool NativeReleaseComplete;
            public float PlatformT,LaunchStartT,RecoveryStartT,ReturnStartT,ReturnAge,PinDelta;
            public bool PlatformReturning;
            public Entity LaunchEntity=Entity.None,DockEntity=Entity.None;
        }
        public static void PrepareTemplate(RhodesCarrierBinding binding)
        {
            if(binding==null||!binding.IsV05)return;
            if(RhodesV17AviationGuideDriver.PrepareTemplate(binding))return;
            for(int i=1;i<=3;i++)
            {
                string tag=i.ToString("00");
                Transform sourceHangar=Require(binding,"V03_HP_AV_"+tag+"_HangarTransfer");
                Transform sourceDeck=Require(binding,"V03_HP_AV_"+tag+"_Dock");
                Transform sourceExit=Require(binding,"V03_HP_AV_"+tag+"_Undock");
                Transform recovery=Require(binding,"V03_HP_AV_"+tag+"_RecoveryGuide");
                Transform storageSurface=Require(binding,"V06_HP_AV_"+tag+"_StorageSurface");
                Transform deckSurface=Require(binding,"V06_HP_AV_"+tag+"_DeckSurface");
                ValidateWellSurfaces(binding,sourceHangar,sourceDeck,storageSurface,deckSurface,Require(binding,"V03_PIVOT_AV_Lift_"+tag));
                Transform hangar=AddSurfaceContact(binding,LowerContactPrefix+tag,sourceHangar,storageSurface);
                AddSurfaceContact(binding,DeckContactPrefix+tag,sourceDeck,deckSurface);
                AddContact(binding,ExitContactPrefix+tag,sourceExit,RhodesAviationLaunchProfile.RailRootY,0f);
                // Derived main-wheel rail entry, not an authored source marker.
                AddContact(binding,EjectStartPrefix+tag,sourceDeck,RhodesAviationLaunchProfile.RailRootY,RhodesAviationLaunchProfile.EjectStartMetres);
                AddControl(binding,LaunchPrefix+tag,hangar);AddControl(binding,DockPrefix+tag,recovery);
            }
            RhodesTiltrotorGuideDriver.PrepareTemplate(binding);
        }
        static void ValidateWellSurfaces(RhodesCarrierBinding binding,Transform sourceHangar,Transform sourceDeck,Transform storage,Transform deck,Transform pivot)
        {
            Vector3 low=binding.GeometryRoot.InverseTransformPoint(storage.position),high=binding.GeometryRoot.InverseTransformPoint(deck.position);
            Vector3 oldLow=binding.GeometryRoot.InverseTransformPoint(sourceHangar.position),oldHigh=binding.GeometryRoot.InverseTransformPoint(sourceDeck.position);
            if(Math.Abs(low.y-RhodesAviationLaunchProfile.LowerSurfaceY)>.02f||Math.Abs(high.y-RhodesAviationLaunchProfile.DeckSurfaceY)>.02f||high.y-low.y<50f||high.y-low.y>70f)
                throw new InvalidOperationException("V06 true-well lower/deck surfaces differ from the reviewed geometry contract");
            if(Math.Abs(low.x-oldLow.x)>.02f||Math.Abs(low.z-oldLow.z)>.02f||Math.Abs(high.x-oldHigh.x)>.02f||Math.Abs(high.z-oldHigh.z)>.02f)
                throw new InvalidOperationException("V06 well endpoints must retain the original three-lane runway XZ");
            if(storage.IsChildOf(pivot)||deck.IsChildOf(pivot))
                throw new InvalidOperationException("V06 storage contact must be permanent, outside the moving lift subtree");
        }
        static Transform AddSurfaceContact(RhodesCarrierBinding binding,string name,Transform heading,Transform surface)
        {
            Transform contact=AddControl(binding,name,heading);
            Vector3 local=binding.GeometryRoot.InverseTransformPoint(surface.position);
            local.y=RhodesAviationLaunchProfile.SurfaceRoot(local.y);contact.localPosition=local;return contact;
        }
        static Transform AddContact(RhodesCarrierBinding binding,string name,Transform source,float rootY,float forwardMetres)
        {
            Transform contact=AddControl(binding,name,source);
            Vector3 local=binding.GeometryRoot.InverseTransformPoint(source.position);
            Vector3 forward=binding.GeometryRoot.InverseTransformDirection(source.forward).normalized;
            local+=forward*forwardMetres;local.y=rootY;contact.localPosition=local;return contact;
        }
        static Transform AddControl(RhodesCarrierBinding binding,string name,Transform endpoint)
        {
            if(binding.FindNode(name)!=null)throw new InvalidOperationException("Duplicate V05 aviation control: "+name);
            GameObject control=new GameObject(name);control.layer=binding.GeometryRoot.gameObject.layer;control.transform.parent=binding.GeometryRoot;
            control.transform.position=endpoint.position;control.transform.rotation=endpoint.rotation;control.transform.localScale=Vector3.one;
            binding.RegisterNode(name,control.transform);
            return control.transform;
        }
        static Transform Require(RhodesCarrierBinding binding,string name)
        {Transform node=binding.FindNode(name);if(node==null)throw new InvalidOperationException("V05 aviation endpoint absent: "+name);return node;}
        public void Initialize(UnitView view,RhodesCarrierBinding binding)
        {
            if(view!=null&&RhodesV17AviationGuideDriver.Attach(view,binding))return;
            if(View==view&&Binding==binding&&Lanes!=null){Register();return;}
            Unregister();View=view;Binding=binding;LastFrame=-1;
            if(view==null||binding==null||!binding.IsV05){Lanes=null;return;}
            Lanes=new Lane[3];
            for(int i=0;i<3;i++)
            {
                string tag=(i+1).ToString("00");Lane lane=new Lane();Lanes[i]=lane;lane.Index=i;
                lane.Hangar=Require(binding,LowerContactPrefix+tag);lane.Deck=Require(binding,DeckContactPrefix+tag);
                lane.Recovery=Require(binding,"V03_HP_AV_"+tag+"_RecoveryGuide");lane.Exit=Require(binding,ExitContactPrefix+tag);
                lane.EjectStart=Require(binding,EjectStartPrefix+tag);
                lane.LaunchControl=Require(binding,LaunchPrefix+tag);lane.DockControl=Require(binding,DockPrefix+tag);
                lane.LiftPivot=Require(binding,"V03_PIVOT_AV_Lift_"+tag);lane.AuthoredLiftLocalPosition=lane.LiftPivot.localPosition;lane.GeometryRoot=binding.GeometryRoot;
                lane.PinDelta=RhodesAviationLaunchProfile.PlatformPin(0f,PlatformTopAbovePivot[i]);
                RhodesAviationLaunchProfile.CatapultPower(HorizontalRunwayMetres(lane));
                if(lane.Hangar.IsChildOf(lane.LiftPivot)||lane.Deck.IsChildOf(lane.LiftPivot)||lane.Recovery.IsChildOf(lane.LiftPivot)||lane.Exit.IsChildOf(lane.LiftPivot))
                    throw new InvalidOperationException("Immutable aircraft endpoints must not be children of a moving lift pivot");
                lane.PlatformT=0f;UpdatePlatform(lane);
                Pose(lane.LaunchControl,lane.Hangar,lane.Hangar,0f);Pose(lane.DockControl,lane.Recovery,lane.Recovery,0f);
            }
            Runtime.Log("RHODES_AV_CONTACT_PROFILE deckSurfaceY="+RhodesAviationLaunchProfile.DeckSurfaceY+
                "; railSurfaceY="+RhodesAviationLaunchProfile.RailSurfaceY+"; launchMainWheelY="+RhodesAviationLaunchProfile.MainWheelY+
                "; liftSeconds="+LiftSeconds+"; catapultAt="+RhodesAviationLaunchProfile.ReadySeconds+"; actualReleaseAt="+RhodesAviationLaunchProfile.ReleaseSeconds+
                "; emptyReturnSeconds="+RhodesAviationLaunchProfile.EmptyReturnSeconds+
                "; lowerSurfaceY="+RhodesAviationLaunchProfile.LowerSurfaceY+"; trueHullWellMarkersRequired=True; travelMetres="+(RhodesAviationLaunchProfile.DeckSurfaceY-RhodesAviationLaunchProfile.LowerSurfaceY)+
                "; derivedControls=LiftLowerContact,LiftDeckContact,CatapultEjectStart,RunwayExitContact; sourceHPMarkersUnchanged=True"+
                "; mainRailContactCalibrated=True; frontWheelSurfaceMismatchMetres=0.323477; animatedMainWheelGapUpToMetres=0.107562; runtimeContactAcceptancePending=True");
            Register();
        }
        void Register()
        {
            if(View==null||View.EntityID.Equals(Entity.None))return;
            if(!RegisteredOwner.Equals(View.EntityID))
            {Unregister();if(Lanes!=null)foreach(Lane lane in Lanes){lane.PlatformT=0f;UpdatePlatform(lane);}}
            RegisteredOwner=View.EntityID;Carriers[RegisteredOwner]=this;
        }
        void Unregister()
        {
            RhodesAviationGuideDriver current;
            if(!RegisteredOwner.Equals(Entity.None)&&Carriers.TryGetValue(RegisteredOwner,out current)&&object.ReferenceEquals(current,this))Carriers.Remove(RegisteredOwner);
            RegisteredOwner=Entity.None;
            if(Lanes!=null)foreach(Lane lane in Lanes)
            {
                if(lane==null)continue;Forget(lane.LaunchEntity);Forget(lane.DockEntity);
                ReleaseLaunchAnimation(lane,true);
                lane.LaunchEntity=Entity.None;lane.DockEntity=Entity.None;lane.LaunchMode=lane.DockMode=0;lane.PlatformReturning=false;
            }
        }
        void Forget(Entity entity)
        {RhodesAviationGuideDriver current;if(!entity.Equals(Entity.None)&&Aircraft.TryGetValue(entity,out current)&&object.ReferenceEquals(current,this))Aircraft.Remove(entity);}
        static RhodesAviationGuideDriver Ensure(UnitView carrier)
        {
            if(carrier==null)return null;RhodesCarrierBinding binding=carrier.GetComponent<RhodesCarrierBinding>();
            if(binding==null||!binding.IsV05)return null;
            RhodesAviationGuideDriver driver=carrier.GetComponent<RhodesAviationGuideDriver>();if(driver==null)driver=carrier.gameObject.AddComponent<RhodesAviationGuideDriver>();
            driver.Initialize(carrier,binding);return driver;
        }
        // Prefix UnitView.OnHangarOperationEvent. Starts/finishes only private
        // carrier interceptor lanes; the original native callback still runs.
        public static void OnHangarOperation(UnitView view,HangarOperationEvent evt)
        {
            if(RhodesV17AviationGuideDriver.OnHangarOperation(view,evt))return;
            RhodesTiltrotorGuideDriver.OnHangarOperation(view,evt);
            if(evt==null||view==null||!view.EntityID.Equals(evt.HangarOwner)||(int)evt.UnitClass!=InterceptorClass||evt.DockSlotIndex<0||evt.DockSlotIndex>=3)return;
            RhodesAviationGuideDriver driver=Ensure(view);if(driver==null)return;Lane lane=driver.Lanes[evt.DockSlotIndex];
            int phase=(int)evt.DockingPhase;
            if(phase==7)driver.BeginLaunch(lane,Entity.None);
            else if(phase==6)
            {
                lane.DockMode=2;lane.DockAge=0f;lane.DockStartTick=ShipbreakersMain.CurrentSimTick;lane.DockStartSubFrame=ShipbreakersMain.CurrentSimSubFrame;lane.PlatformReturning=false;lane.PlatformT=1f;UpdatePlatform(lane);Pose(lane.DockControl,lane.Deck,lane.Deck,0f);
            }
            else if(phase==8)
            {
                // This is a native queue notification, not permission to detach
                // an aircraft. Only its actual action1 ends guide ownership.
                lane.NativeReleaseComplete=true;driver.TraceLaunch(lane,"queue-release-complete");
            }
            else if(phase==3)
            {lane.DockMode=0;lane.PlatformReturning=false;lane.PlatformT=0f;UpdatePlatform(lane);Pose(lane.DockControl,lane.Hangar,lane.Hangar,0f);driver.Forget(lane.DockEntity);lane.DockEntity=Entity.None;}
        }
        // Prefix UnitView.OnDockingUnitOperationEvent. The aircraft is not moved
        // here; its native callback finds and follows these named proxy guides.
        public static void OnDockingUnitOperation(UnitView view,DockingUnitOperationEvent evt)
        {
            RhodesProductionExit.OnDockingUnitOperation(view,evt);
            if(RhodesV17AviationGuideDriver.OnDockingUnitOperation(view,evt))return;
            RhodesTiltrotorGuideDriver.OnDockingUnitOperation(view,evt);
            if(evt==null||view==null||!view.EntityID.Equals(evt.DockingEntity)||evt.DockSlotIndex<0||evt.DockSlotIndex>=3||string.IsNullOrEmpty(evt.BoneName))return;
            bool launch=evt.BoneName.StartsWith(LaunchPrefix,StringComparison.Ordinal),dock=evt.BoneName.StartsWith(DockPrefix,StringComparison.Ordinal);
            if(!launch&&!dock)return;
            RhodesAviationGuideDriver driver;if(!Carriers.TryGetValue(evt.HangarOwner,out driver)||driver==null)return;
            Lane lane=driver.Lanes[evt.DockSlotIndex];int action=(int)evt.Action;
            if(action==0&&launch){driver.BeginLaunch(lane,evt.DockingEntity);driver.AcquireLaunchAnimation(lane,view);driver.TraceLaunch(lane,"aircraft-attach");}
            else if(action==1&&launch&&RhodesAviationLaunchProfile.AcceptNativeDetach(
                lane.LaunchMode,!lane.LaunchEntity.Equals(Entity.None),lane.LaunchEntity.Equals(evt.DockingEntity)))
            {
                driver.FinishLaunch(lane);driver.Forget(lane.LaunchEntity);lane.LaunchEntity=Entity.None;
            }
            else if(action==2&&dock)
            {
                driver.Forget(lane.DockEntity);lane.DockEntity=evt.DockingEntity;Aircraft[evt.DockingEntity]=driver;
                AdvanceEmptyReturn(lane);
                lane.DockMode=1;lane.DockAge=0f;lane.DockStartTick=ShipbreakersMain.CurrentSimTick;lane.DockStartSubFrame=ShipbreakersMain.CurrentSimSubFrame;lane.PlatformReturning=false;lane.RecoveryStartT=lane.PlatformT;Pose(lane.DockControl,lane.Recovery,lane.Recovery,0f);
            }
        }
        void BeginLaunch(Lane lane,Entity aircraft)
        {
            if(lane.LaunchMode==0)
            {
                // Native events can arrive while sensor/LOD gating skips Step.
                // Catch up the empty lift before consuming its cached position.
                AdvanceEmptyReturn(lane);
                ReleaseLaunchAnimation(lane,false);lane.LaunchAnimationCancelled=false;
                lane.LaunchMode=1;lane.LaunchAge=0f;lane.PlatformReturning=false;lane.LaunchStartT=lane.PlatformT;
                lane.LaunchStartTick=ShipbreakersMain.CurrentSimTick;lane.NativeReleaseComplete=false;
                // A very fast repeated sortie may reuse a partially returned
                // platform. Attach the newly visible plane to its current lift
                // height instead of teleporting the platform down.
                Pose(lane.LaunchControl,lane.Hangar,lane.Deck,lane.LaunchStartT);
                TraceLaunch(lane,"queue-launch-start");
            }
            if(!aircraft.Equals(Entity.None))
            {Forget(lane.LaunchEntity);lane.LaunchEntity=aircraft;Aircraft[aircraft]=this;}
        }
        void FinishLaunch(Lane lane)
        {
            ReleaseLaunchAnimation(lane,false);
            TraceLaunch(lane,"aircraft-native-detach");
            // Actual native detach is after deck arrival, including when no
            // carrier presentation frame ran during the entire launch.
            lane.PlatformT=1f;UpdatePlatform(lane);
            lane.LaunchMode=0;Pose(lane.LaunchControl,lane.Exit,lane.Exit,0f);
            if(lane.DockMode==0&&!lane.PlatformReturning)
            {lane.PlatformReturning=true;lane.ReturnAge=0f;lane.ReturnStartTick=ShipbreakersMain.CurrentSimTick;lane.ReturnStartSubFrame=ShipbreakersMain.CurrentSimSubFrame;lane.ReturnStartT=lane.PlatformT;}
        }
        // Prefix UnitView.UpdateUnitAsPresentationControlOnly. This guarantees
        // guide motion is available before native aircraft-follow processing.
        public static void BeforeAircraftPresentationUpdate(UnitView view)
        {if(RhodesV17AviationGuideDriver.BeforeAircraftPresentationUpdate(view))return;RhodesTiltrotorGuideDriver.BeforeAircraftPresentationUpdate(view);RhodesAviationGuideDriver driver;if(view!=null&&Aircraft.TryGetValue(view.EntityID,out driver)&&driver!=null){driver.StepFrame();driver.UpdateLaunchAnimation(view);}}
        public static Fixed64 ResolveUndockLift(UnitHangarAttributes attrs,DockingUnitOperationEvent evt)
        {
            if(attrs==null||evt==null)throw new ArgumentNullException("attrs/evt");
            if(evt.BoneName==RhodesProductionExit.ControlBone)
            {
                foreach(HangarBay ground in attrs.HangarBays)
                    if(ground!=null&&ground.Name==RhodesProductionExit.BayName&&string.IsNullOrEmpty(ground.EntityType))return ground.UndockLiftTime;
                throw new InvalidOperationException("Private ground spawn event has no native ground bay");
            }
            if(evt.BoneName==RhodesCarrierUnits.HeavyBomberLaunchBone)
            {
                foreach(HangarBay bay in attrs.HangarBays)if(bay!=null&&bay.EntityType==RhodesCarrierUnits.HeavyBomberKey)return bay.UndockLiftTime;
                throw new InvalidOperationException("Heavy bomber native bay missing");
            }
            if(evt.BoneName==RhodesTiltrotorProfile.LaunchBone)
            {
                foreach(HangarBay bay in attrs.HangarBays)if(bay!=null&&bay.EntityType==FactionUnits.TiltStrikeKey)return bay.UndockLiftTime;
                throw new InvalidOperationException("Tiltrotor native bay missing");
            }
            if(!string.IsNullOrEmpty(evt.BoneName)&&evt.BoneName.StartsWith(LaunchPrefix,StringComparison.Ordinal))
            {
                foreach(HangarBay bay in attrs.HangarBays)if(bay!=null&&bay.EntityType==AircraftType)return bay.UndockLiftTime;
                throw new InvalidOperationException("Private V05 aircraft has no tracked native bay");
            }
            // Preserve the original method's behavior for every native unit.
            return attrs.HangarBays[evt.DockSlotIndex].UndockLiftTime;
        }
        void StepFrame()
        {
            if(View==null||Lanes==null||LastFrame==Time.frameCount)return;LastFrame=Time.frameCount;Register();
            bool alive=View.NonInterpolatedTickState!=null&&!View.NonInterpolatedTickState.UnitIsDead;
            RhodesResearch.Snapshot research;bool aviationReady=RhodesResearch.TrySnapshot(View.CommanderID,out research)&&research.AviationLevel>0&&research.GunneryLevel==0;
            bool shown=View.SimStateInterpolator!=null&&Convert.ToInt32(View.Visibility)==2&&!View.ForceIconOnlyMode&&Binding!=null&&Binding.GeometryRoot!=null&&Binding.GeometryRoot.gameObject.activeInHierarchy;
            if(!RhodesAviationLaunchProfile.PresentationAllowed(alive,shown,aviationReady))
            {foreach(Lane lane in Lanes)ReleaseLaunchAnimation(lane,true);return;}
            float dt=Runtime.PresentationDeltaTime;if(float.IsNaN(dt)||float.IsInfinity(dt)||dt<0f)dt=0f;
            foreach(Lane lane in Lanes)
            {
                if(lane.LaunchMode!=0)
                {
                    // The native presentation frame/subframe is also the clock
                    // used by UnitView interpolation. Wall delta can reach the
                    // runway edge while the 8Hz native queue is still occupied.
                    if(dt>0f)lane.LaunchAge=Math.Max(lane.LaunchAge,RhodesAviationLaunchProfile.NativeAge(
                        lane.LaunchStartTick,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame));
                    if(lane.LaunchAge<LiftSeconds)
                    {lane.PlatformT=Mathf.Lerp(lane.LaunchStartT,1f,Smooth(lane.LaunchAge/LiftSeconds));Pose(lane.LaunchControl,lane.Hangar,lane.Deck,lane.PlatformT);}
                    else
                    {
                        // Keep the plane on the raised lift until its native
                        // wing deployment ends; then accelerate on the authored
                        // catapult to the unchanged native release endpoint.
                        lane.PlatformT=1f;PoseCatapult(lane,
                            RhodesAviationLaunchProfile.CatapultProgress(lane.LaunchAge,HorizontalRunwayMetres(lane)));
                    }
                }
                if(lane.DockMode==1)
                {
                    if(dt>0f)lane.DockAge=Math.Max(lane.DockAge,RhodesAviationLaunchProfile.NativePresentationAge(lane.DockStartTick,lane.DockStartSubFrame,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame));
                    float approach=Smooth((lane.DockAge-AlignmentSeconds)/ApproachSeconds);
                    Pose(lane.DockControl,lane.Recovery,lane.Deck,approach);
                    // The empty lift starts during alignment, moves for10.5s,
                    // then settles0.5s before the nominal11s TouchDown.
                    lane.PlatformT=Mathf.Lerp(lane.RecoveryStartT,1f,RhodesAviationLaunchProfile.RecoveryPlatformProgress(lane.DockAge));
                }
                else if(lane.DockMode==2)
                {
                    if(dt>0f)lane.DockAge=Math.Max(lane.DockAge,RhodesAviationLaunchProfile.NativePresentationAge(lane.DockStartTick,lane.DockStartSubFrame,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame));
                    float down=Smooth(lane.DockAge/BringInSeconds);Pose(lane.DockControl,lane.Deck,lane.Hangar,down);lane.PlatformT=1f-down;
                    if(lane.DockAge>=BringInSeconds){lane.DockMode=0;lane.PlatformT=0f;Forget(lane.DockEntity);lane.DockEntity=Entity.None;}
                }
                else if(lane.LaunchMode==0&&lane.PlatformReturning)
                {
                    AdvanceEmptyReturn(lane);
                }
                UpdatePlatform(lane);
            }
        }
        static void AdvanceEmptyReturn(Lane lane)
        {
            if(!lane.PlatformReturning||lane.LaunchMode!=0||lane.DockMode!=0)return;
            // Sim tick/subframe freezes with pause and advances while culled.
            // This also makes reattachment independent of Unity frame order.
            lane.ReturnAge=Math.Max(lane.ReturnAge,RhodesAviationLaunchProfile.NativePresentationAge(
                lane.ReturnStartTick,lane.ReturnStartSubFrame,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame));
            lane.PlatformT=Mathf.Lerp(lane.ReturnStartT,0f,RhodesAviationLaunchProfile.EmptyReturnProgress(lane.ReturnAge));
            if(lane.ReturnAge>=RhodesAviationLaunchProfile.EmptyReturnSeconds){lane.PlatformReturning=false;lane.PlatformT=0f;}
            UpdatePlatform(lane);
        }
        bool LaunchAnimationAllowed()
        {
            return View!=null&&View.NonInterpolatedTickState!=null&&!View.NonInterpolatedTickState.UnitIsDead&&View.SimStateInterpolator!=null&&
                Convert.ToInt32(View.Visibility)==2&&!View.ForceIconOnlyMode&&Binding!=null&&Binding.GeometryRoot!=null&&Binding.GeometryRoot.gameObject.activeInHierarchy;
        }
        void AcquireLaunchAnimation(Lane lane,UnitView aircraft)
        {
            ReleaseLaunchAnimation(lane,false);
            if(lane.LaunchAnimationCancelled||!LaunchAnimationAllowed())return;
            if(aircraft.UnitAttrs==null||(int)aircraft.UnitAttrs.Class!=InterceptorClass)return;
            float age=RhodesAviationLaunchProfile.NativeAge(lane.LaunchStartTick,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame);
            if(!RhodesAviationLaunchProfile.HoldLaunchAnimation(age))return;
            // Native C Interceptor prefab has its unique Animator on this same
            // UnitView root. Never find/modify a carrier or shared asset Animator.
            RhodesLaunchAnimationLease lease=aircraft.GetComponent<RhodesLaunchAnimationLease>();
            if(lease==null)lease=aircraft.gameObject.AddComponent<RhodesLaunchAnimationLease>();
            if(lease.Acquire(aircraft)){lane.LaunchLease=lease;TraceLaunch(lane,"private-launch-animation-held");}
            else TraceLaunch(lane,"private-launch-animation-hold-unavailable");
        }
        void UpdateLaunchAnimation(UnitView aircraft)
        {
            if(Lanes==null)return;
            foreach(Lane lane in Lanes)
            {
                if(!lane.LaunchEntity.Equals(aircraft.EntityID)||lane.LaunchLease==null)continue;
                bool alive=aircraft.NonInterpolatedTickState!=null&&!aircraft.NonInterpolatedTickState.UnitIsDead&&lane.LaunchLease.IsOwnedBy(aircraft);
                if(!alive||!LaunchAnimationAllowed()){ReleaseLaunchAnimation(lane,true);continue;}
                if(Runtime.PresentationDeltaTime<=0f)continue;
                float age=RhodesAviationLaunchProfile.NativeAge(lane.LaunchStartTick,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame);
                if(!RhodesAviationLaunchProfile.HoldLaunchAnimation(age)){ReleaseLaunchAnimation(lane,false);TraceLaunch(lane,"private-launch-animation-resumed");}
            }
        }
        static void ReleaseLaunchAnimation(Lane lane,bool cancel)
        {
            if(lane.LaunchLease!=null)lane.LaunchLease.Release();lane.LaunchLease=null;
            if(cancel)lane.LaunchAnimationCancelled=true;
        }
        static void UpdatePlatform(Lane lane)
        {
            // Move only along authored model-local Y, including when the native
            // wrapper is tilted. Platform, aircraft and named display guide all
            // share the permanent low/high contact surfaces inside this hull.
            Vector3 low=lane.GeometryRoot.InverseTransformPoint(lane.Hangar.position),high=lane.GeometryRoot.InverseTransformPoint(lane.Deck.position);
            Vector3 current=lane.GeometryRoot.InverseTransformPoint(lane.LiftPivot.position);
            float targetY=Mathf.Lerp(low.y,high.y,lane.PlatformT)+lane.PinDelta;
            Vector3 delta=lane.GeometryRoot.TransformVector(Vector3.up*(targetY-current.y));
            lane.LiftPivot.localPosition+=lane.LiftPivot.parent==null?delta:lane.LiftPivot.parent.InverseTransformVector(delta);
        }
        static float Smooth(float value){return RhodesAviationLaunchProfile.SmoothFraction(value);}
        static float HorizontalRunwayMetres(Lane lane)
        {Vector3 delta=lane.Exit.position-lane.Deck.position;return (float)Math.Sqrt(delta.x*delta.x+delta.z*delta.z);}
        void PoseCatapult(Lane lane,float progress)
        {
            Vector3 deck=Binding.GeometryRoot.InverseTransformPoint(lane.Deck.position);
            Vector3 exit=Binding.GeometryRoot.InverseTransformPoint(lane.Exit.position);
            Vector3 entry=Binding.GeometryRoot.InverseTransformPoint(lane.EjectStart.position)-deck;
            Vector3 point=Vector3.Lerp(deck,exit,progress);
            // The lift stops flush with the flat deck, not atop the raised rails.
            // During the first measured26.213m, transfer from deck contact to
            // main-wheel rail contact; the remaining runway is level.
            Vector3 delta=exit-deck;float length=(float)Math.Sqrt(delta.x*delta.x+delta.z*delta.z);
            float entryDistance=(float)Math.Sqrt(entry.x*entry.x+entry.z*entry.z);
            point.y=Mathf.Lerp(deck.y,exit.y,RhodesAviationLaunchProfile.ContactBlend(progress*length,entryDistance));
            lane.LaunchControl.position=Binding.GeometryRoot.TransformPoint(point);
            lane.LaunchControl.rotation=Quaternion.Slerp(lane.Deck.rotation,lane.Exit.rotation,progress);
        }
        void TraceLaunch(Lane lane,string phase)
        {
            if(LaunchTraces>=48)return;LaunchTraces++;
            Runtime.Log("RHODES_AV_LAUNCH phase="+phase+"; lane="+(lane.Index+1)+"; tick="+ShipbreakersMain.CurrentSimTick+
                "; subFrame="+ShipbreakersMain.CurrentSimSubFrame+"; startTick="+lane.LaunchStartTick+
                "; nativeAge="+RhodesAviationLaunchProfile.NativeAge(lane.LaunchStartTick,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame)+
                "; guideAge="+lane.LaunchAge+"; queueReleaseComplete="+lane.NativeReleaseComplete+
                "; platformT="+lane.PlatformT+"; launchStartT="+lane.LaunchStartT+"; emptyReturnAge="+lane.ReturnAge+"; emptyReturnActive="+lane.PlatformReturning+
                "; actualAircraftOwnershipStillNative=True; releasePixelAcceptancePending=True");
        }
        static void Pose(Transform control,Transform from,Transform to,float t)
        {control.position=Vector3.Lerp(from.position,to.position,t);control.rotation=Quaternion.Slerp(from.rotation,to.rotation,t);}
        void LateUpdate(){StepFrame();}
        void OnEnable(){Register();}
        void OnDisable(){Unregister();}
        void OnDestroy()
        {
            Unregister();if(Lanes!=null)foreach(Lane lane in Lanes)if(lane.LiftPivot!=null)lane.LiftPivot.localPosition=lane.AuthoredLiftLocalPosition;
            View=null;Binding=null;Lanes=null;
        }
    }
    // Per-instance native aircraft speed lease. No Update/LateUpdate, no
    // Animator.Play/time/Transform writes. OnDisable handles plane pooling too.
    public sealed class RhodesLaunchAnimationLease:MonoBehaviour
    {
        Animator OwnedAnimator;UnitView OwnerView;Entity Owner=Entity.None;
        float OriginalSpeed;bool Held;
        public bool Acquire(UnitView view)
        {
            Release();
            if(view==null||view.gameObject!=gameObject||view.EntityID.Equals(Entity.None)||!gameObject.activeInHierarchy)return false;
            Animator animator=view.GetComponent<Animator>();
            if(animator==null||!animator.enabled||animator.runtimeAnimatorController==null)return false;
            float speed=animator.speed;if(float.IsNaN(speed)||float.IsInfinity(speed)||speed<=0f)return false;
            OwnerView=view;Owner=view.EntityID;OwnedAnimator=animator;OriginalSpeed=speed;Held=true;animator.speed=0f;return true;
        }
        public bool IsOwnedBy(UnitView view)
        {return Held&&view!=null&&view==OwnerView&&view.EntityID.Equals(Owner)&&OwnedAnimator!=null&&OwnedAnimator.gameObject==gameObject;}
        public void Release()
        {
            // Undo our instance mutation even if pooling has already cleared
            // its old EntityID. Never follow a rebound UnitView to another root.
            if(Held&&OwnedAnimator!=null&&OwnedAnimator.gameObject==gameObject&&OwnedAnimator.speed==0f)
                OwnedAnimator.speed=OriginalSpeed;
            Held=false;OwnedAnimator=null;OwnerView=null;Owner=Entity.None;
        }
        void OnDisable(){Release();}
        void OnDestroy(){Release();}
    }
}
