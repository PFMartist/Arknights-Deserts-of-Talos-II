using System;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Game.Events;
using BBI.Unity.Game;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Native callbacks own aircraft attachment/detachment. This adapter writes
    // only owned guide bones and the seven distinct authored lift mechanisms.
    public sealed class RhodesV17AviationGuideDriver:MonoBehaviour
    {
        sealed class NodeCurve {internal Transform Node;internal V17NodeCurve Curve;internal Vector3 Position;internal Quaternion Rotation;}
        sealed class Lane
        {
            internal string Name;internal bool Shared;internal V17AirPath LaunchPath,RecoveryPath;
            internal Transform Launch,Dock;internal NodeCurve[] LaunchNodes,RecoveryNodes;
            internal int LaunchTick=-1,DockTick=-1,LaunchReturnTick=-1,DockReturnTick=-1,DockMode;
            internal float LaunchSub,DockSub,LaunchReturnSub,DockReturnSub;
            internal float DockResetSeconds=RhodesBomberRecoveryProfile.StandardResetSeconds;
            internal Entity LaunchEntity=Entity.None,DockEntity=Entity.None;
            internal UnitView LaunchView,DockView;
        }
        static readonly Dictionary<Entity,RhodesV17AviationGuideDriver> Owners=new Dictionary<Entity,RhodesV17AviationGuideDriver>();
        static readonly Dictionary<Entity,RhodesV17AviationGuideDriver> Planes=new Dictionary<Entity,RhodesV17AviationGuideDriver>();
        UnitView View;RhodesCarrierBinding Binding;RhodesV17AviationData Data;
        Lane[] Fighters;Lane Shared;NodeCurve Cabin,Mast;
        Entity Registered=Entity.None;int LastFrame=-1;static int RemovalWarnings;
        internal static bool IsV17(RhodesCarrierBinding binding)
        {return binding!=null&&binding.FindNode("V17_PIVOT_AV_ModuleCarrier")!=null;}
        internal static bool PrepareTemplate(RhodesCarrierBinding binding)
        {
            if(!IsV17(binding))return false;RhodesV17AviationData data=RhodesV17AviationData.Load();
            for(int i=1;i<=3;i++)
            {
                string tag=i.ToString("00"),path="F"+tag;
                AddControl(binding,"EF_AV_UndockControl_"+tag,data.Paths[path+"_Launch"].Sample(0f));
                AddControl(binding,"EF_AV_DockControl_"+tag,data.Paths[path+"_Recovery"].Sample(0f));
            }
            AddControl(binding,RhodesTiltrotorProfile.LaunchBone,data.Paths["U08_Launch"].Sample(0f));
            AddControl(binding,RhodesTiltrotorProfile.DockBone,data.Paths["U08_Recovery"].Sample(0f));
            AddControl(binding,RhodesCarrierUnits.HeavyBomberLaunchBone,data.Paths["B01_Launch"].Sample(0f));
            AddControl(binding,RhodesCarrierUnits.HeavyBomberDockBone,data.Paths["B01_Recovery"].Sample(0f));
            return true;
        }
        static void AddControl(RhodesCarrierBinding binding,string name,V17AirPose pose)
        {
            if(binding.FindNode(name)!=null)throw new InvalidOperationException("Duplicate V17 guide "+name);
            GameObject node=new GameObject(name);node.layer=binding.GeometryRoot.gameObject.layer;node.transform.parent=binding.GeometryRoot;
            node.transform.localPosition=pose.Position;node.transform.localRotation=pose.Rotation;node.transform.localScale=Vector3.one;binding.RegisterNode(name,node.transform);
        }
        internal static bool Attach(UnitView view,RhodesCarrierBinding binding)
        {
            if(!IsV17(binding))return false;
            RhodesV17AviationGuideDriver driver=view.GetComponent<RhodesV17AviationGuideDriver>();
            if(driver==null)driver=view.gameObject.AddComponent<RhodesV17AviationGuideDriver>();driver.Initialize(view,binding);return true;
        }
        void Initialize(UnitView view,RhodesCarrierBinding binding)
        {
            if(View==view&&Binding==binding&&Fighters!=null){Register();return;}
            Unregister();View=view;Binding=binding;Data=RhodesV17AviationData.Load();Fighters=new Lane[3];
            for(int i=0;i<3;i++)
            {
                string tag=(i+1).ToString("00"),name="F"+tag;
                Fighters[i]=CreateLane(name,false,"EF_AV_UndockControl_"+tag,"EF_AV_DockControl_"+tag);
            }
            Shared=CreateLane("U08",true,RhodesTiltrotorProfile.LaunchBone,RhodesTiltrotorProfile.DockBone);
            Cabin=Curve("V17_PIVOT_AV_IslandCabin");Mast=Curve("V17_PIVOT_AV_IslandMast");Register();
            Runtime.Log("RHODES_V17_AVIATION_BOUND fighterLaunchLifts=3; fighterRecoveryLifts=3; sharedRecoveryLift=1; fixedSideLaunch=True; exactAuthorPoseTables=True; nativeAircraftOwnership=True; runtimeVisualAcceptancePending=True");
        }
        Lane CreateLane(string name,bool shared,string launch,string dock)
        {
            Lane lane=new Lane{Name=name,Shared=shared,LaunchPath=Data.Paths[name+"_Launch"],RecoveryPath=Data.Paths[name+"_Recovery"],Launch=Require(launch),Dock=Require(dock)};
            List<NodeCurve> launchNodes=new List<NodeCurve>();
            if(!shared)launchNodes.AddRange(Curves("V17_"+name+"_Launch_"));
            launchNodes.Add(Curve(shared?"V17_Shared_SHUTTLE_PIVOT":"V17_"+name+"_SHUTTLE_PIVOT"));lane.LaunchNodes=launchNodes.ToArray();
            lane.RecoveryNodes=shared?new NodeCurve[]{Curve("V03_PIVOT_SharedTiltrotorLift")}:Curves("V17_"+name+"_Recovery_");
            Set(lane.LaunchNodes,0f);Set(lane.RecoveryNodes,0f);Pose(lane.Launch,lane.LaunchPath.Sample(0f));Pose(lane.Dock,lane.RecoveryPath.Sample(0f));return lane;
        }
        Transform Require(string name){Transform node=Binding.FindNode(name);if(node==null)throw new InvalidOperationException("V17 aviation node missing: "+name);return node;}
        NodeCurve Curve(string name){return new NodeCurve{Node=Require(name),Curve=Data.Curves[name],Position=Require(name).localPosition,Rotation=Require(name).localRotation};}
        NodeCurve[] Curves(string prefix)
        {List<NodeCurve> nodes=new List<NodeCurve>();foreach(KeyValuePair<string,V17NodeCurve> item in Data.Curves)if(item.Key.StartsWith(prefix,StringComparison.Ordinal))nodes.Add(Curve(item.Key));return nodes.ToArray();}
        static void Set(NodeCurve[] nodes,float time){foreach(NodeCurve node in nodes)node.Curve.Apply(node.Node,time);}
        void Pose(Transform control,V17AirPose pose)
        {control.position=Binding.GeometryRoot.TransformPoint(pose.Position);control.rotation=Binding.GeometryRoot.rotation*pose.Rotation;}
        void Register()
        {
            if(View==null||View.EntityID.Equals(Entity.None))return;
            if(!Registered.Equals(View.EntityID))Unregister();Registered=View.EntityID;Owners[Registered]=this;
        }
        void Forget(Entity plane)
        {RhodesV17AviationGuideDriver driver;if(!plane.Equals(Entity.None)&&Planes.TryGetValue(plane,out driver)&&object.ReferenceEquals(driver,this))Planes.Remove(plane);}
        void Clear(Lane lane)
        {if(lane==null)return;ReleasePose(lane.LaunchView,lane.LaunchEntity);ReleasePose(lane.DockView,lane.DockEntity);Forget(lane.LaunchEntity);Forget(lane.DockEntity);lane.LaunchView=lane.DockView=null;lane.LaunchEntity=lane.DockEntity=Entity.None;lane.LaunchTick=lane.DockTick=lane.LaunchReturnTick=lane.DockReturnTick=-1;lane.DockMode=0;}
        void Unregister()
        {
            RhodesV17AviationGuideDriver driver;if(!Registered.Equals(Entity.None)&&Owners.TryGetValue(Registered,out driver)&&object.ReferenceEquals(driver,this))Owners.Remove(Registered);
            Registered=Entity.None;if(Fighters!=null)foreach(Lane lane in Fighters)Clear(lane);Clear(Shared);
        }
        static RhodesV17AviationGuideDriver Ensure(UnitView view)
        {RhodesCarrierBinding binding=view.GetComponent<RhodesCarrierBinding>();if(!Attach(view,binding))return null;return view.GetComponent<RhodesV17AviationGuideDriver>();}
        Lane ForClass(int unitClass,int index)
        {return unitClass==1179649&&index>=0&&index<3?Fighters[index]:unitClass==RhodesTiltrotorProfile.AircraftClass||unitClass==1572865?Shared:null;}
        Lane ForBone(string bone,int index)
        {if(bone==null)return null;if(bone.StartsWith("EF_AV_",StringComparison.Ordinal))return index>=0&&index<3?Fighters[index]:null;return bone==RhodesTiltrotorProfile.LaunchBone||bone==RhodesTiltrotorProfile.DockBone||bone==RhodesCarrierUnits.HeavyBomberLaunchBone||bone==RhodesCarrierUnits.HeavyBomberDockBone?Shared:null;}
        void ChooseShared(bool bomber)
        {
            string name=bomber?"B01":"U08";Shared.Name=name;Shared.LaunchPath=Data.Paths[name+"_Launch"];Shared.RecoveryPath=Data.Paths[name+"_Recovery"];
            Shared.Launch=Require(bomber?RhodesCarrierUnits.HeavyBomberLaunchBone:RhodesTiltrotorProfile.LaunchBone);
            Shared.Dock=Require(bomber?RhodesCarrierUnits.HeavyBomberDockBone:RhodesTiltrotorProfile.DockBone);
        }
        static float Age(int tick,float sub){return RhodesAviationLaunchProfile.NativePresentationAge(tick,sub,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame);}
        void BeginLaunch(Lane lane,Entity plane,UnitView view)
        {
            if(lane.LaunchTick<0){lane.LaunchTick=ShipbreakersMain.CurrentSimTick;lane.LaunchSub=ShipbreakersMain.CurrentSimSubFrame;lane.LaunchReturnTick=-1;}
            if(!plane.Equals(Entity.None)){Forget(lane.LaunchEntity);lane.LaunchEntity=plane;lane.LaunchView=view;Planes[plane]=this;}Step();
        }
        internal static bool OnHangarOperation(UnitView view,HangarOperationEvent evt)
        {
            if(view==null||evt==null||!view.EntityID.Equals(evt.HangarOwner))return false;
            RhodesV17AviationGuideDriver driver=Ensure(view);if(driver==null)return false;
            Lane lane=driver.ForClass((int)evt.UnitClass,evt.DockSlotIndex);if(lane==null)return true;
            int phase=(int)evt.DockingPhase;
            if(phase!=7&&phase!=6&&phase!=3)return true;
            if(lane.Shared)
            {
                string target=(int)evt.UnitClass==1572865?"B01":"U08";
                // An idle/stored notification from the other logical bay must
                // never replace the active shared guide's aircraft identity.
                if(phase!=7&&lane.Name!=target)return true;
                driver.ChooseShared(target=="B01");
            }
            if(phase==7)driver.BeginLaunch(lane,Entity.None,null);
            else if(phase==6){lane.DockMode=2;lane.DockTick=ShipbreakersMain.CurrentSimTick;lane.DockSub=ShipbreakersMain.CurrentSimSubFrame;}
            else if(phase==3&&lane.DockMode!=0)driver.FinishRecovery(lane);
            return true;
        }
        internal static bool OnDockingUnitOperation(UnitView view,DockingUnitOperationEvent evt)
        {
            if(view==null||evt==null||!view.EntityID.Equals(evt.DockingEntity))return false;
            RhodesV17AviationGuideDriver driver;if(!Owners.TryGetValue(evt.HangarOwner,out driver)||driver==null)return false;
            Lane lane=driver.ForBone(evt.BoneName,evt.DockSlotIndex);if(lane==null)return true;
            bool bomber=evt.BoneName==RhodesCarrierUnits.HeavyBomberLaunchBone||evt.BoneName==RhodesCarrierUnits.HeavyBomberDockBone;
            if(lane.Shared)driver.ChooseShared(bomber);
            bool launch=evt.BoneName==RhodesCarrierUnits.HeavyBomberLaunchBone||evt.BoneName==RhodesTiltrotorProfile.LaunchBone||evt.BoneName.StartsWith("EF_AV_Undock",StringComparison.Ordinal);
            int action=(int)evt.Action;
            if(action==0&&launch)driver.BeginLaunch(lane,evt.DockingEntity,view);
            else if(action==1&&launch&&lane.LaunchEntity.Equals(evt.DockingEntity))
            {
                driver.Pose(lane.Launch,lane.LaunchPath.Sample(14f));RhodesAirWingModels.ReleaseToFlight(view);
                lane.LaunchTick=-1;lane.LaunchReturnTick=ShipbreakersMain.CurrentSimTick;lane.LaunchReturnSub=ShipbreakersMain.CurrentSimSubFrame;
                driver.Forget(lane.LaunchEntity);lane.LaunchEntity=Entity.None;lane.LaunchView=null;
            }
            else if(action==2&&!launch)
            {
                driver.Forget(lane.DockEntity);lane.DockEntity=evt.DockingEntity;lane.DockView=view;Planes[evt.DockingEntity]=driver;
                lane.DockMode=1;lane.DockTick=ShipbreakersMain.CurrentSimTick;lane.DockSub=ShipbreakersMain.CurrentSimSubFrame;lane.DockReturnTick=-1;
                driver.Pose(lane.Dock,lane.RecoveryPath.Sample(0f));
            }
            return true;
        }
        void FinishRecovery(Lane lane)
        {
            // Snapshot the completed aircraft's clock. A later logical-bay
            // event may switch Shared.Name while this empty return is active.
            lane.DockResetSeconds=RhodesBomberRecoveryProfile.ResetSeconds(lane.Shared&&lane.Name=="B01");
            Pose(lane.Dock,lane.RecoveryPath.Sample(18f));
            if(lane.DockView!=null&&lane.DockView.EntityID.Equals(lane.DockEntity))RhodesAirWingModels.SetStoredPose(lane.DockView);
            Forget(lane.DockEntity);lane.DockEntity=Entity.None;lane.DockView=null;lane.DockMode=0;
            lane.DockReturnTick=ShipbreakersMain.CurrentSimTick;lane.DockReturnSub=ShipbreakersMain.CurrentSimSubFrame;
        }
        internal static bool BeforeAircraftPresentationUpdate(UnitView view)
        {RhodesV17AviationGuideDriver driver;if(view!=null&&Planes.TryGetValue(view.EntityID,out driver)&&driver!=null){driver.Step();return true;}return false;}
        internal static void OnNativeRemoved(UnitView view)
        {
            try{RemoveNativeView(view);}
            catch(Exception error){if(RemovalWarnings++<4)Runtime.Log("WARNING RHODES_V17_AVIATION_REMOVE "+error.GetType().Name+": "+error.Message+"; nativeDespawnContinues=True");}
        }
        static void RemoveNativeView(UnitView view)
        {
            if(view==null)return;RhodesV17AviationGuideDriver driver;
            if(Owners.TryGetValue(view.EntityID,out driver)&&driver!=null&&object.ReferenceEquals(driver.View,view)){driver.Unregister();return;}
            if(!Planes.TryGetValue(view.EntityID,out driver)||driver==null)return;
            if(driver.Fighters!=null)foreach(Lane lane in driver.Fighters)driver.RemovePlane(lane,view);
            driver.RemovePlane(driver.Shared,view);
        }
        void RemovePlane(Lane lane,UnitView view)
        {
            if(lane==null)return;
            if(object.ReferenceEquals(lane.LaunchView,view)&&lane.LaunchEntity.Equals(view.EntityID))
            {
                ReleasePose(view,lane.LaunchEntity);Forget(lane.LaunchEntity);lane.LaunchView=null;lane.LaunchEntity=Entity.None;lane.LaunchTick=-1;
                lane.LaunchReturnTick=ShipbreakersMain.CurrentSimTick;lane.LaunchReturnSub=ShipbreakersMain.CurrentSimSubFrame;
            }
            if(object.ReferenceEquals(lane.DockView,view)&&lane.DockEntity.Equals(view.EntityID))FinishRecovery(lane);
        }
        internal static bool TryRotorAngle(Entity plane,out float degrees,out int epoch)
        {
            degrees=0f;epoch=-1;RhodesV17AviationGuideDriver driver;
            if(!Planes.TryGetValue(plane,out driver)||driver==null||driver.Shared==null||driver.Shared.Name!="U08")return false;
            Lane lane=driver.Shared;
            if(lane.LaunchEntity.Equals(plane)&&lane.LaunchTick>=0){degrees=75f*RhodesDeploymentProfile.Smooth((Age(lane.LaunchTick,lane.LaunchSub)-5f)/4f);return true;}
            if(lane.DockEntity.Equals(plane)&&lane.DockMode!=0){degrees=0f;epoch=lane.DockTick;return true;}return false;
        }
        internal static bool TryAircraftAttitude(Entity plane,out Transform guide,out float contactWeight)
        {
            guide=null;contactWeight=0f;RhodesV17AviationGuideDriver driver;
            if(!Planes.TryGetValue(plane,out driver)||driver==null)return false;
            if(driver.Fighters!=null)foreach(Lane lane in driver.Fighters)if(TryLaneAttitude(lane,plane,out guide,out contactWeight))return true;
            return TryLaneAttitude(driver.Shared,plane,out guide,out contactWeight);
        }
        static bool TryLaneAttitude(Lane lane,Entity plane,out Transform guide,out float contactWeight)
        {
            guide=null;contactWeight=0f;if(lane==null)return false;
            if(lane.LaunchEntity.Equals(plane)&&lane.LaunchTick>=0){guide=lane.Launch;contactWeight=1f;return true;}
            if(lane.DockEntity.Equals(plane)&&lane.DockMode!=0)
            {guide=lane.Dock;contactWeight=RhodesAviationPoseMath.RecoveryContactWeight(lane.DockMode,Age(lane.DockTick,lane.DockSub));return true;}
            return false;
        }
        void Step()
        {
            if(View==null||Binding==null||LastFrame==Time.frameCount)return;LastFrame=Time.frameCount;
            if(View.NonInterpolatedTickState!=null&&View.NonInterpolatedTickState.UnitIsDead){Unregister();return;}Register();
            if(View.NonInterpolatedTickState==null||Binding.GeometryRoot==null||!Binding.GeometryRoot.gameObject.activeInHierarchy||View.ForceIconOnlyMode||(int)View.Visibility!=2)return;
            if(Fighters!=null)foreach(Lane lane in Fighters)StepLane(lane);if(Shared!=null)StepLane(Shared);
        }
        void StepLane(Lane lane)
        {
            if(!lane.LaunchEntity.Equals(Entity.None)&&!AliveIdentity(lane.LaunchView,lane.LaunchEntity))
            {
                ReleasePose(lane.LaunchView,lane.LaunchEntity);Forget(lane.LaunchEntity);lane.LaunchEntity=Entity.None;lane.LaunchView=null;lane.LaunchTick=-1;
                lane.LaunchReturnTick=ShipbreakersMain.CurrentSimTick;lane.LaunchReturnSub=ShipbreakersMain.CurrentSimSubFrame;
            }
            if(!lane.DockEntity.Equals(Entity.None)&&!AliveIdentity(lane.DockView,lane.DockEntity))FinishRecovery(lane);
            if(lane.LaunchTick>=0)
            {
                float age=Age(lane.LaunchTick,lane.LaunchSub),duration=lane.Shared?9f:14f,authorTime;
                V17AirPose pose;
                if(lane.Shared)
                {
                    if(age<=5f){authorTime=9.5f*Mathf.Clamp01(age/5f);pose=lane.LaunchPath.Sample(authorTime);}
                    else
                    {
                        V17AirPose start=lane.LaunchPath.Sample(9.5f),end=lane.LaunchPath.Sample(14f);Vector3 delta=end.Position-start.Position;
                        float length=new Vector2(delta.x,delta.z).magnitude,speed=lane.Name=="B01"?300f:90f;
                        float power=speed*(duration-5f)/length,p=(float)Math.Pow(Mathf.Clamp01((age-5f)/(duration-5f)),power);
                        authorTime=9.5f+4.5f*p;pose=V17AirPose.Mix(start,end,p);
                    }
                }
                else if(age<=RhodesAviationLaunchProfile.ReadySeconds){authorTime=9.5f*Mathf.Clamp01(age/RhodesAviationLaunchProfile.ReadySeconds);pose=lane.LaunchPath.Sample(authorTime);}
                else
                {
                    V17AirPose start=lane.LaunchPath.Sample(9.5f),end=lane.LaunchPath.Sample(14f);Vector3 delta=end.Position-start.Position;
                    float p=RhodesAviationLaunchProfile.CatapultProgress(age,new Vector2(delta.x,delta.z).magnitude);authorTime=9.5f+4.5f*p;pose=V17AirPose.Mix(start,end,p);
                }
                Pose(lane.Launch,pose);Set(lane.LaunchNodes,authorTime);
                if(lane.LaunchView!=null)RhodesAirWingModels.SetGuidedPose(lane.LaunchView,RhodesAirWingStage.Launch,1f-pose.Fold,lane.Launch,1f);
            }
            else if(lane.LaunchReturnTick>=0)
            {
                float age=Age(lane.LaunchReturnTick,lane.LaunchReturnSub);Set(lane.LaunchNodes,14f+10f*Mathf.Clamp01(age/4f));if(age>=4f)lane.LaunchReturnTick=-1;
            }
            if(lane.DockMode!=0)
            {
                float age=Age(lane.DockTick,lane.DockSub),time=RhodesBomberRecoveryProfile.RecoveryPathTime(lane.Shared&&lane.Name=="B01",lane.DockMode,age);
                V17AirPose pose=lane.RecoveryPath.Sample(time);Pose(lane.Dock,pose);Set(lane.RecoveryNodes,time);
                if(lane.DockView!=null)
                {
                    // B01 V02 has its own fan/vector/gear sequence. The
                    // unchanged native11s approach and private3s bring-in
                    // clocks move the same complete authored guide/lift path.
                    if(lane.Shared&&lane.Name=="B01")
                    {
                        RhodesAirWingStage stage;float fraction;
                        BomberRecoveryPose(lane.DockMode,age,time,out stage,out fraction);
                        RhodesAirWingModels.SetGuidedPose(lane.DockView,stage,fraction,lane.Dock,RhodesAviationPoseMath.RecoveryContactWeight(lane.DockMode,age));
                    }
                    else
                    {
                        RhodesAirWingStage stage;float fraction;FighterRecoveryPose(lane.DockMode,age,time,out stage,out fraction);
                        RhodesAirWingModels.SetGuidedPose(lane.DockView,stage,fraction,lane.Dock,RhodesAviationPoseMath.RecoveryContactWeight(lane.DockMode,age));
                    }
                }
            }
            else if(lane.DockReturnTick>=0)
            {float age=Age(lane.DockReturnTick,lane.DockReturnSub);Set(lane.RecoveryNodes,RhodesBomberRecoveryProfile.RecoveryResetPathTime(age,lane.DockResetSeconds));if(age>=lane.DockResetSeconds)lane.DockReturnTick=-1;}
        }
        internal static void BomberRecoveryPose(int mode,float age,float pathTime,out RhodesAirWingStage stage,out float fraction)
        {
            if(mode==1&&age<RhodesBomberRecoveryProfile.AlignmentSeconds){stage=RhodesAirWingStage.Approach;fraction=Mathf.Clamp01(age/RhodesBomberRecoveryProfile.AlignmentSeconds);}
            else if(mode==1){stage=RhodesAirWingStage.Touchdown;fraction=Mathf.Clamp01((age-RhodesBomberRecoveryProfile.AlignmentSeconds)/RhodesBomberRecoveryProfile.ApproachSeconds);}
            else
            {
                // Source345->360 stops vector thrust, ->375 closes doors,
                // ->420 folds wings by path t8. The platform lowers after t9.
                stage=RhodesAirWingStage.Stow;fraction=Mathf.Clamp01((pathTime-5f)/3f);
            }
        }
        internal static void FighterRecoveryPose(int mode,float age,float pathTime,out RhodesAirWingStage stage,out float fraction)
        {
            if(mode==1&&age<5f){stage=RhodesAirWingStage.Approach;fraction=Mathf.Clamp01(age/5f);}
            else if(mode==1){stage=RhodesAirWingStage.Touchdown;fraction=Mathf.Clamp01((age-5f)/6f);}
            else
            {
                // Exact F01 AFM: frame230..260 secures the aircraft; 260..300
                // folds both wings. Complete this by author path t8, before
                // the recovery platform begins descending at author t9.
                stage=RhodesAirWingStage.Stow;fraction=Mathf.Clamp01((pathTime-5f)/3f);
            }
        }
        static bool AliveIdentity(UnitView view,Entity entity)
        {return view!=null&&view.EntityID.Equals(entity)&&view.NonInterpolatedTickState!=null&&!view.NonInterpolatedTickState.UnitIsDead;}
        static void ReleasePose(UnitView view,Entity entity)
        {if(view!=null&&view.EntityID.Equals(entity))RhodesAirWingModels.ClearGuide(view);}
        internal void ApplyModulePose(RhodesDeploymentSnapshot state,int tick,float sub)
        {
            // R19D exclusively owns carrier/cabin/mast; the unchanged V17
            // aircraft path/guide/lift cycles remain authoritative here.
            if(RhodesR19DeploymentData.IsR19(Binding))return;
            if(Cabin==null||Mast==null)return;
            float time=state.Branch!=1?0f:state.Kind==1?RhodesDeploymentProfile.AgeTicks(state.StartTick,tick,sub)*.125f:state.Kind==4?24f-RhodesDeploymentProfile.AgeTicks(state.StartTick,tick,sub)*.125f:24f;
            Cabin.Curve.Apply(Cabin.Node,Mathf.Clamp(time,0f,24f));Mast.Curve.Apply(Mast.Node,Mathf.Clamp(time,0f,24f));
        }
        static void Restore(NodeCurve node){if(node!=null&&node.Node!=null){node.Node.localPosition=node.Position;node.Node.localRotation=node.Rotation;}}
        void Restore(Lane lane){if(lane==null)return;foreach(NodeCurve node in lane.LaunchNodes)Restore(node);foreach(NodeCurve node in lane.RecoveryNodes)Restore(node);}
        void LateUpdate(){Step();}
        void OnEnable(){Register();}
        // Temporary presentation hiding is not despawn. Exact native lifecycle
        // removal below retires pooled entities; retain clocks across hiding.
        void OnDisable(){LastFrame=-1;}
        void OnDestroy(){Unregister();if(Fighters!=null)foreach(Lane lane in Fighters)Restore(lane);Restore(Shared);Restore(Cabin);Restore(Mast);}
    }
}
