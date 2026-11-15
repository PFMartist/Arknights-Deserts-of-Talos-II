using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BBI.Core.ComponentModel;
using BBI.Game.Events;
using BBI.Unity.Game;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Drives proxy bones only. Native UnitView retains aircraft transform
    // ownership; simulation retains production, flight, return and storage.
    public sealed class RhodesTiltrotorGuideDriver:MonoBehaviour
    {
        static bool SourceVerified;
        static readonly Dictionary<Entity,RhodesTiltrotorGuideDriver> Owners=new Dictionary<Entity,RhodesTiltrotorGuideDriver>();
        static readonly Dictionary<Entity,RhodesTiltrotorGuideDriver> Planes=new Dictionary<Entity,RhodesTiltrotorGuideDriver>();
        UnitView View;RhodesCarrierBinding Binding;Transform LaunchControl,DockControl;
        Entity Owner=Entity.None,Launching=Entity.None,Receiving=Entity.None;
        int LaunchTick=-1,DockTick=-1,ResetTick=-1,DockMode,LastFrame=-1,Traces;
        float LaunchSub,DockSub,ResetSub,LaunchAge,DockAge;

        public static void PrepareTemplate(RhodesCarrierBinding binding)
        {
            if(binding==null||!binding.IsV05)return;
            if(!SourceVerified)
            {
                string path=Path.Combine(Path.Combine(CustomUnits.AssetDirectory,"rhodes_v05"),"rhodes_v05.bin");
                using(SHA256 hash=SHA256.Create())using(FileStream stream=File.OpenRead(path))
                {
                    string actual=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-",string.Empty).ToLowerInvariant();
                    if(actual!=RhodesTiltrotorProfile.HullSha256)throw new InvalidDataException("Side runway/rectangular lift coordinates require the measured V16.1 hull");
                }
                SourceVerified=true;
            }
            Add(binding,RhodesTiltrotorProfile.LaunchBone,RhodesTiltrotorProfile.Start);
            Add(binding,RhodesTiltrotorProfile.DockBone,RhodesTiltrotorProfile.Approach);
        }
        static void Add(RhodesCarrierBinding binding,string name,RhodesTiltrotorProfile.Point point)
        {
            if(binding.FindNode(name)!=null)throw new InvalidOperationException("Duplicate tiltrotor proxy "+name);
            GameObject obj=new GameObject(name);obj.layer=binding.GeometryRoot.gameObject.layer;
            obj.transform.parent=binding.GeometryRoot;obj.transform.localPosition=Vector(point);
            obj.transform.localRotation=Quaternion.identity;obj.transform.localScale=Vector3.one;
            binding.RegisterNode(name,obj.transform);
        }
        static Vector3 Vector(RhodesTiltrotorProfile.Point point){return new Vector3(point.X,point.Y,point.Z);}
        static RhodesTiltrotorGuideDriver Ensure(UnitView view)
        {
            if(view==null)return null;RhodesCarrierBinding binding=view.GetComponent<RhodesCarrierBinding>();
            if(binding==null||!binding.IsV05)return null;
            RhodesTiltrotorGuideDriver driver=view.GetComponent<RhodesTiltrotorGuideDriver>();
            if(driver==null)driver=view.gameObject.AddComponent<RhodesTiltrotorGuideDriver>();
            driver.Initialize(view,binding);return driver;
        }
        void Initialize(UnitView view,RhodesCarrierBinding binding)
        {
            if(View==view&&Binding==binding&&LaunchControl!=null&&Owner.Equals(view.EntityID)){Register();return;}
            ForgetAll();View=view;Binding=binding;LaunchTick=DockTick=ResetTick=-1;DockMode=0;LastFrame=-1;
            LaunchControl=binding.FindNode(RhodesTiltrotorProfile.LaunchBone);DockControl=binding.FindNode(RhodesTiltrotorProfile.DockBone);
            if(LaunchControl==null||DockControl==null)throw new InvalidOperationException("Side runway/rectangular lift proxies missing");
            Pose(LaunchControl,RhodesTiltrotorProfile.Start);Pose(DockControl,RhodesTiltrotorProfile.Approach);Register();
        }
        void Register()
        {
            if(View==null||View.EntityID.Equals(Entity.None))return;
            if(!Owner.Equals(View.EntityID)){ForgetAll();Owner=View.EntityID;LaunchTick=DockTick=ResetTick=-1;DockMode=0;}
            Owners[Owner]=this;
        }
        void Forget(Entity entity)
        {RhodesTiltrotorGuideDriver driver;if(Planes.TryGetValue(entity,out driver)&&object.ReferenceEquals(driver,this))Planes.Remove(entity);}
        void ForgetAll()
        {
            Forget(Launching);Forget(Receiving);RhodesTiltrotorGuideDriver driver;
            if(Owners.TryGetValue(Owner,out driver)&&object.ReferenceEquals(driver,this))Owners.Remove(Owner);
            Owner=Launching=Receiving=Entity.None;
        }
        public static void OnHangarOperation(UnitView view,HangarOperationEvent evt)
        {
            if(evt==null||view==null||!view.EntityID.Equals(evt.HangarOwner)||evt.DockSlotIndex!=0||(int)evt.UnitClass!=RhodesTiltrotorProfile.AircraftClass)return;
            RhodesTiltrotorGuideDriver driver=Ensure(view);if(driver==null)return;
            int phase=(int)evt.DockingPhase;
            if(phase==7)driver.BeginLaunch(Entity.None,true);
            else if(phase==6)
            {
                driver.DockMode=2;driver.ResetTick=-1;driver.DockTick=ShipbreakersMain.CurrentSimTick;driver.DockSub=ShipbreakersMain.CurrentSimSubFrame;
                driver.DockAge=0f;driver.Pose(driver.DockControl,RhodesTiltrotorProfile.Capture);driver.Trace("lift-touchdown");
            }
            else if(phase==3)
            {
                if(driver.DockMode==2&&!driver.Receiving.Equals(Entity.None))
                {driver.ResetTick=ShipbreakersMain.CurrentSimTick;driver.ResetSub=ShipbreakersMain.CurrentSimSubFrame;}
                driver.DockMode=0;driver.Forget(driver.Receiving);driver.Receiving=Entity.None;
                driver.Pose(driver.DockControl,RhodesTiltrotorProfile.Approach);driver.Trace("native-storage-lift-reset");
            }
        }
        public static void OnDockingUnitOperation(UnitView view,DockingUnitOperationEvent evt)
        {
            if(evt==null||view==null||!view.EntityID.Equals(evt.DockingEntity)||evt.DockSlotIndex!=0)return;
            bool launch=evt.BoneName==RhodesTiltrotorProfile.LaunchBone,dock=evt.BoneName==RhodesTiltrotorProfile.DockBone;
            if(!launch&&!dock)return;
            RhodesTiltrotorGuideDriver driver;if(!Owners.TryGetValue(evt.HangarOwner,out driver)||driver==null)return;
            int action=(int)evt.Action;
            if(action==0&&launch)driver.BeginLaunch(evt.DockingEntity,false);
            else if(action==1&&launch&&driver.Launching.Equals(evt.DockingEntity))
            {driver.Pose(driver.LaunchControl,RhodesTiltrotorProfile.Exit);driver.Forget(driver.Launching);driver.Launching=Entity.None;driver.LaunchTick=-1;driver.Trace("native-flight-release");}
            else if(action==2&&dock)
            {
                driver.Forget(driver.Receiving);driver.Receiving=evt.DockingEntity;Planes[evt.DockingEntity]=driver;
                driver.DockMode=1;driver.DockTick=ShipbreakersMain.CurrentSimTick;driver.DockSub=ShipbreakersMain.CurrentSimSubFrame;driver.DockAge=0f;
                driver.Pose(driver.DockControl,RhodesTiltrotorProfile.Approach);driver.Trace("lift-approach");
            }
        }
        void BeginLaunch(Entity aircraft,bool queueStart)
        {
            if(LaunchTick<0||queueStart)
            {Forget(Launching);Launching=Entity.None;LaunchTick=ShipbreakersMain.CurrentSimTick;LaunchSub=ShipbreakersMain.CurrentSimSubFrame;LaunchAge=0f;Pose(LaunchControl,RhodesTiltrotorProfile.Start);Trace("side-runway-start");}
            if(!aircraft.Equals(Entity.None)){Forget(Launching);Launching=aircraft;Planes[aircraft]=this;}
        }
        public static void BeforeAircraftPresentationUpdate(UnitView view)
        {RhodesTiltrotorGuideDriver driver;if(view!=null&&Planes.TryGetValue(view.EntityID,out driver)&&driver!=null)driver.Step();}
        public static bool TryNacelleAngle(Entity plane,out float degrees)
        {
            int v17Epoch;if(RhodesV17AviationGuideDriver.TryRotorAngle(plane,out degrees,out v17Epoch))return true;
            degrees=0f;RhodesTiltrotorGuideDriver driver;
            if(!Planes.TryGetValue(plane,out driver)||driver==null)return false;
            driver.Step();
            if(driver.Launching.Equals(plane)&&driver.LaunchTick>=0)
            {degrees=RhodesTiltrotorProfile.LaunchNacelleAngle(driver.LaunchAge);return true;}
            if(driver.Receiving.Equals(plane)&&driver.DockMode!=0)
            {degrees=driver.DockMode==1?RhodesTiltrotorProfile.ReceiveNacelleAngle(driver.DockAge):0f;return true;}
            return false;
        }
        internal static bool TryRecoveryEpoch(Entity plane,out int epoch)
        {
            float v17Angle;if(RhodesV17AviationGuideDriver.TryRotorAngle(plane,out v17Angle,out epoch)&&epoch>=0)return true;
            epoch=-1;RhodesTiltrotorGuideDriver driver;
            if(!Planes.TryGetValue(plane,out driver)||driver==null||!driver.Receiving.Equals(plane)||driver.DockMode==0)return false;
            epoch=driver.DockTick;return true;
        }
        void Pose(Transform control,RhodesTiltrotorProfile.Point point)
        {control.position=Binding.GeometryRoot.TransformPoint(Vector(point));control.rotation=Binding.GeometryRoot.rotation;}
        // The existing module driver alone writes the platform and shutter
        // nodes. The guide exposes an immutable sampled pose, never a second
        // competing writer. C05's epoch is independent of main-route research.
        internal static void FacilityPose(Entity owner,float unlockAge,out float hatch,out float lift)
        {
            hatch=RhodesTiltrotorProfile.FacilityHatch(unlockAge);
            lift=RhodesTiltrotorProfile.FacilityLiftOffset(unlockAge);
            if(unlockAge<RhodesTiltrotorProfile.FacilityDeploymentSeconds)return;
            RhodesTiltrotorGuideDriver driver;if(!Owners.TryGetValue(owner,out driver)||object.ReferenceEquals(driver,null))return;
            if(driver.DockMode==2)
            {
                float age=RhodesAviationLaunchProfile.NativePresentationAge(driver.DockTick,driver.DockSub,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame);
                lift=RhodesTiltrotorProfile.RecoveryLiftOffset(age);
            }
            else if(driver.ResetTick>=0)
            {
                float age=RhodesAviationLaunchProfile.NativePresentationAge(driver.ResetTick,driver.ResetSub,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame);
                lift=RhodesTiltrotorProfile.ResetLiftOffset(age);
            }
        }
        void Step()
        {
            if(View==null||Binding==null||LaunchControl==null||DockControl==null||LastFrame==Time.frameCount)return;
            LastFrame=Time.frameCount;Register();
            bool alive=View.NonInterpolatedTickState!=null&&!View.NonInterpolatedTickState.UnitIsDead;
            if(!alive||Binding.GeometryRoot==null||!Binding.GeometryRoot.gameObject.activeInHierarchy||View.ForceIconOnlyMode||(int)View.Visibility!=2)return;
            if(LaunchTick>=0)
            {
                if(Runtime.PresentationDeltaTime>0f)LaunchAge=Math.Max(LaunchAge,RhodesAviationLaunchProfile.NativePresentationAge(LaunchTick,LaunchSub,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame));
                Pose(LaunchControl,RhodesTiltrotorProfile.Launch(LaunchAge));
            }
            if(DockMode!=0)
            {
                if(Runtime.PresentationDeltaTime>0f)DockAge=Math.Max(DockAge,RhodesAviationLaunchProfile.NativePresentationAge(DockTick,DockSub,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame));
                Pose(DockControl,DockMode==1?RhodesTiltrotorProfile.Receive(DockAge):RhodesTiltrotorProfile.Stow(DockAge));
            }
        }
        void Trace(string phase)
        {if(Traces++<16)Runtime.Log("RHODES_TILTROT_ROUTE phase="+phase+"; oneSideRunway=True; rectangularElevatorRecovery=True; independentC05Facility=True; nativeAircraftOwnership=True; visualAcceptancePending=True");}
        void LateUpdate(){Step();}
        void OnDisable(){ForgetAll();LaunchTick=DockTick=ResetTick=-1;DockMode=0;}
        void OnDestroy()
        {
            ForgetAll();View=null;Binding=null;LaunchControl=DockControl=null;
        }
    }
}
