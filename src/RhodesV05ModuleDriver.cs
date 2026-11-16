using System;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Per-view presentation only. Native simulation owns deployment epochs/readiness.
    public sealed class RhodesV05ModuleDriver:MonoBehaviour
    {
        UnitView View;
        Entity ViewEntity;
        bool HaveEntity,HaveOwner,Initialized,HadGunTransitReapply;
        RhodesCarrierBinding Binding;
        CommanderID Owner;
        RhodesDeploymentSnapshot State;
        int LastRevision=-1;
        readonly RhodesGunneryDeploymentPose Deployment=new RhodesGunneryDeploymentPose();
        RhodesTopShutterSet Shutters;
        RhodesModuleEmissionMarkers Emitters;
        RhodesR19DeploymentPose R19;
        RhodesModuleWarningLights Warnings;
        Vector3[] BindPosition;
        float[] OffsetHeight;
        bool[] Desired,SourceDesired;
        string[] ResearchOwners;
        int[] AddonStartTicks;
        readonly List<SlotSelection> Slots=new List<SlotSelection>();
        public const float GunneryDeploymentSeconds=RhodesDeploymentProfile.GunneryFirstTicks*RhodesDeploymentProfile.NativeTickSeconds;
        public bool MainGunneryPresentationReady {get{return Initialized&&State.Branch==2&&State.MainReady;}}
        public float GunneryDeploymentYawOffsetDegrees {get{return Initialized?RhodesDeploymentProfile.Evaluate(State,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame).GunYaw:0f;}}
        public float GunneryDeploymentElapsedSeconds {get{return Initialized&&State.Branch==2?RhodesDeploymentProfile.AgeTicks(State.StartTick,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame)*.125f:0f;}}
        public int MainReferenceEpoch {get{return R19==null?0:(State.ForeReady?1:0)+(State.AftReady?2:0);}}

        sealed class SlotSelection {internal int[] Members;}
        public void Initialize(UnitView view,RhodesCarrierBinding binding)
        {
            if(view==null||view.EntityID.Equals(Entity.None))return;
            if(view==View&&binding==Binding&&BindPosition!=null&&HaveEntity&&ViewEntity.Equals(view.EntityID))return;
            RestorePose();View=view;Binding=binding;ViewEntity=view.EntityID;HaveEntity=true;
            HaveOwner=false;Initialized=false;LastRevision=-1;Slots.Clear();
            if(binding==null||!binding.IsV05)return;
            EndfieldDoK.ModelIntake.RhodesCarrierBatches.RebindLive(binding.GeometryRoot,binding.FindNode);
            NativeRead.Set(view,"mGeoRenderObjects",binding.Geometry);
            NativeRead.Call(view,"UpdateUnitMaterialPropertyBlock",true);
            RhodesRendererColourDiagnostic.Record(view,binding);
            Deployment.Initialize(binding);Shutters=new RhodesTopShutterSet(binding);
            R19=RhodesR19DeploymentData.IsR19(binding)?new RhodesR19DeploymentPose(binding):null;
            Warnings=RhodesModuleWarningLights.Attach(view,binding);
            Emitters=R19==null?new RhodesModuleEmissionMarkers(view,binding):null;
            int count=binding.ModuleRoots.Length;BindPosition=new Vector3[count];OffsetHeight=new float[count];
            Desired=new bool[count];SourceDesired=new bool[count];
            ResearchOwners=new string[count];AddonStartTicks=new int[count];
            Dictionary<string,List<int>> grouped=new Dictionary<string,List<int>>(StringComparer.Ordinal);
            for(int i=0;i<count;i++)
            {
                BindPosition[i]=binding.ModuleRoots[i].localPosition;
                ResearchOwners[i]=RhodesCarrierUpgradeAppearance.ResearchOwner(binding.ModuleSlots[i],binding.ModuleResearch[i]);
                AddonStartTicks[i]=-1;
                if(binding.ModuleRoles[i]=="base")continue;
                string slot=binding.ModuleSlots[i];if(string.IsNullOrEmpty(slot))slot=binding.ModuleIds[i];
                List<int> list;if(!grouped.TryGetValue(slot,out list)){list=new List<int>();grouped.Add(slot,list);}list.Add(i);
            }
            foreach(List<int> members in grouped.Values)Slots.Add(new SlotSelection{Members=members.ToArray()});
        }
        public void Step(bool nativeShown)
        {
            if(View==null||Binding==null||Binding.GeometryRoot==null)return;
            UnitState native=View.NonInterpolatedTickState;
            if(View.IsDriverDead||(native!=null&&native.UnitIsDead))
            {
                if(!HaveEntity||!ViewEntity.Equals(View.EntityID))return;
                // Preserve module selection and all local poses through the
                // native delay. Only the parent FOW gate remains ours; native
                // ShowModel(false) owns hiding its cached authored GEO children.
                if(Binding.GeometryRoot.gameObject.activeSelf!=nativeShown)Binding.GeometryRoot.gameObject.SetActive(nativeShown);
                return;
            }
            if(native==null||View.EntityID.Equals(Entity.None))
            {SetRootShown(false);return;}
            if(!HaveEntity||!ViewEntity.Equals(View.EntityID))
            {Initialize(View,Binding);if(BindPosition==null||!HaveEntity||!ViewEntity.Equals(View.EntityID))return;}
            CommanderID owner=View.CommanderID;
            if(!HaveOwner||!Owner.Equals(owner))
            {RestorePose();Owner=owner;HaveOwner=true;Initialized=false;LastRevision=-1;}
            RhodesResearch.Snapshot research;RhodesDeploymentSnapshot current;
            if(!RhodesResearch.TrySnapshot(owner,out research)||!RhodesDeploymentState.TryGet(View.EntityID,owner,out current))
            {SetRootShown(false);return;}
            if(research.AviationLevel>0&&research.GunneryLevel>0)throw new InvalidOperationException("Conflicting Rhodes branch research snapshot");
            bool stateChanged=!Initialized||research.Revision!=LastRevision||State.Branch!=current.Branch||State.SourceLevel!=current.SourceLevel||
                State.TargetLevel!=current.TargetLevel||State.Kind!=current.Kind||State.StartTick!=current.StartTick;
            State=current;
            if(stateChanged)
            {
                for(int i=0;i<Desired.Length;i++)
                {
                    if(ResearchOwners[i]==Binding.ModuleResearch[i])continue;
                    bool shown=Eligible(i,research,owner);
                    int completed=shown?RhodesResearch.CompletedSimTick(owner,ResearchOwners[i]):-1;
                    AddonStartTicks[i]=RhodesCarrierUpgradeAppearance.ObserveCompletion(Initialized,Desired[i],shown,AddonStartTicks[i],completed);
                }
                Select(research,owner,current.TargetLevel,Desired);
                Select(research,owner,current.SourceLevel,SourceDesired);
                LastRevision=research.Revision;Initialized=true;
                Runtime.Log("RHODES_V09_MODULE_STATE entity="+View.EntityID+"; branch="+current.Branch+"; sourceLevel="+current.SourceLevel+
                    "; targetLevel="+current.TargetLevel+"; kind="+current.Kind+"; startTick="+current.StartTick+
                    "; simulationOwnedReady=True; legacyCapSinkRemoved=True; runtimeVisualAcceptancePending=True");
            }
            // Reading current simulation state continues while hidden; only visual writes are culled.
            if(!nativeShown){SetRootShown(false);return;}
            int tick=ShipbreakersMain.CurrentSimTick;float sub=ShipbreakersMain.CurrentSimSubFrame;
            RhodesDeploymentPoseSample pose=RhodesDeploymentProfile.Evaluate(current,tick,sub);
            float tiltAge=research.TiltCompletedSimTick<0?-1f:Mathf.Max(0f,(tick+sub-research.TiltCompletedSimTick)*.125f);
            if(RhodesV17AviationGuideDriver.IsV17(Binding)&&research.AviationLevel>0&&research.AviationCompletedSimTick>=0)
            {
                float aviationFacilityAge=Mathf.Max(0f,(tick+sub-research.AviationCompletedSimTick)*.125f);
                if(tiltAge<0f||aviationFacilityAge>tiltAge)tiltAge=aviationFacilityAge;
            }
            float tiltHatch,tiltLift;RhodesTiltrotorGuideDriver.FacilityPose(View.EntityID,tiltAge,out tiltHatch,out tiltLift);
            for(int i=0;i<Desired.Length;i++)
            {
                string slot=Binding.ModuleSlots[i];bool main=slot=="DECK"||slot=="DECK_ADDON"||slot=="V17_DECK";
                bool vt=slot=="SHARED_VT"||slot=="SHARED_VT_ADDON"||slot=="SVT"||slot=="V17_SHARED";
                bool cap=Binding.ModuleRoles[i]=="cap";
                // The shared VT payload must remain visible while its old
                // hardware descends too; caps wait for the committed base pose.
                bool want=main&&pose.UseOutgoingDeck?SourceDesired[i]:Desired[i];
                // Articulated shared shutters replace the old cap's vertical sink entirely.
                if(cap&&main&&current.Branch!=0)want=false;
                float offset=0f;
                if(want&&!cap)
                {
                    if(main&&R19==null)offset=pose.UseOutgoingDeck?pose.DeckOutgoingOffset:pose.DeckIncomingOffset;
                    else if(vt&&Binding.ModuleIds[i]=="MOD_VT_L1__V03_PIVOT_SharedTiltrotorLift"&&!RhodesV17AviationGuideDriver.IsV17(Binding))offset=tiltLift;
                    else if(slot=="CIWS_ADDON"||slot=="REPAIR_ADDON")offset=RhodesDeploymentProfile.AddonOffset(AddonStartTicks[i],tick,sub);
                }
                if(offset!=0f||OffsetHeight[i]!=offset)SetOffset(i,offset);
                Transform root=Binding.ModuleRoots[i];if(root.gameObject.activeSelf!=want)root.gameObject.SetActive(want);
            }
            Shutters.Set(pose.DeckShutter,tiltHatch,research.CIWSLevel>=1?1f:0f,R19==null||current.Branch!=2);
            Deployment.ApplyFold(pose.MinerFold);
            if(R19!=null)
            {
                R19.Apply(current,tick,sub);
                Deployment.ApplyR19NativeAimHold(AimHold(false,current,tick,sub),AimHold(true,current,tick,sub));
                HadGunTransitReapply=current.Branch==2&&(!current.ForeReady||!current.AftReady);
            }
            else
            {
                Deployment.ApplyGunTransit(pose.GunTransit,OffsetHeight);
                Emitters.Apply(OffsetHeight,pose.GunTransit);HadGunTransitReapply=pose.GunTransit>1e-6f;
            }
            RhodesV17AviationGuideDriver v17=View.GetComponent<RhodesV17AviationGuideDriver>();if(v17!=null)v17.ApplyModulePose(current,tick,sub);
            if(Warnings!=null)Warnings.Step(current,tick,sub,true);
            SetRootShown(true);
        }
        // The other existing LateUpdate host writes right-pitch/recoil. Reapply
        // only active transport after it, independent of Unity script ordering.
        // No clock advances, native writes, scans, LUT/file reads or allocations.
        public void ReapplyGunTransit()
        {
            if(!Initialized||View==null||Binding==null||OffsetHeight==null||State.Branch!=2||
                !HaveEntity||!ViewEntity.Equals(View.EntityID)||!Binding.GeometryRoot.gameObject.activeInHierarchy||
                Convert.ToInt32(View.Visibility)!=2||View.ForceIconOnlyMode)return;
            UnitState native=View.NonInterpolatedTickState;if(native==null||native.UnitIsDead||View.IsDriverDead)return;
            if(R19!=null)
            {
                int tick=ShipbreakersMain.CurrentSimTick;float sub=ShipbreakersMain.CurrentSimSubFrame;
                Deployment.ApplyR19NativeAimHold(AimHold(false,State,tick,sub),AimHold(true,State,tick,sub));return;
            }
            float weight=RhodesDeploymentProfile.Evaluate(State,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame).GunTransit;
            if(weight<=1e-6f&&!HadGunTransitReapply)return;
            Deployment.ApplyGunTransit(weight,OffsetHeight);Emitters.Apply(OffsetHeight,weight);HadGunTransitReapply=weight>1e-6f;
        }
        static float AimHold(bool aft,RhodesDeploymentSnapshot state,int tick,float sub)
        {
            if(state.Branch!=2)return 1f;
            int start=aft?state.AftStartTick:state.StartTick;
            if(aft&&state.TargetLevel<2)return 1f;
            if(start<0)return 0f; // Newly spawned unit with pre-existing completed hardware.
            float seconds=RhodesDeploymentProfile.AgeTicks(start,tick,sub)*.125f;
            return 1f-RhodesDeploymentProfile.Smooth((seconds-(aft?15.5f:21.5f))/.125f);
        }
        void Select(RhodesResearch.Snapshot snapshot,CommanderID owner,int branchLevel,bool[] result)
        {
            Array.Clear(result,0,result.Length);
            snapshot.AviationLevel=State.Branch==1?branchLevel:0;snapshot.GunneryLevel=State.Branch==2?branchLevel:0;
            for(int i=0;i<result.Length;i++)if(Binding.ModuleRoles[i]=="base")result[i]=Binding.ModuleBranches[i]=="common";
            foreach(SlotSelection slot in Slots)
            {
                int highest=int.MinValue;
                foreach(int i in slot.Members)if(Binding.ModuleRoles[i]!="cap"&&Eligible(i,snapshot,owner))highest=Math.Max(highest,Binding.ModuleLevels[i]);
                foreach(int i in slot.Members)result[i]=Binding.ModuleRoles[i]=="cap"?highest==int.MinValue:
                    highest==Binding.ModuleLevels[i]&&Eligible(i,snapshot,owner);
            }
        }
        bool Eligible(int index,RhodesResearch.Snapshot snapshot,CommanderID owner)
        {
            string branch=Binding.ModuleBranches[index],slot=Binding.ModuleSlots[index].ToUpperInvariant();int level=Binding.ModuleLevels[index];
            if(slot.StartsWith("SHARED_VT",StringComparison.Ordinal)||slot=="SVT"||slot=="V17_SHARED")
                return level==1&&(RhodesResearch.IsCompleted(owner,RhodesResearch.TiltAssembly)||RhodesV17AviationGuideDriver.IsV17(Binding)&&snapshot.AviationLevel>0);
            string research=ResearchOwners[index];
            if(research!=Binding.ModuleResearch[index])
                return RhodesCarrierUpgradeAppearance.Eligible(branch,snapshot.AviationLevel,snapshot.GunneryLevel,RhodesResearch.IsCompleted(owner,research));
            if(branch=="aviation"&&snapshot.AviationLevel<level)return false;
            if(branch=="gunnery"&&snapshot.GunneryLevel<level)return false;
            if(!string.IsNullOrEmpty(research))return RhodesResearch.IsCompleted(owner,research);
            if(branch=="aviation")return snapshot.AviationLevel>0;if(branch=="gunnery")return snapshot.GunneryLevel>0;
            if(slot=="CIWS")return snapshot.CIWSLevel>=level;if(slot=="REPAIR")return snapshot.RepairLevel>=level;
            return false;
        }
        void SetOffset(int index,float metres)
        {
            Transform root=Binding.ModuleRoots[index];Vector3 world=Binding.GeometryRoot.TransformVector(Vector3.up*metres);
            root.localPosition=BindPosition[index]+(root.parent==null?world:root.parent.InverseTransformVector(world));OffsetHeight[index]=metres;
        }
        void SetRootShown(bool shown)
        {if(!shown&&Warnings!=null)Warnings.Hide();if(Binding.GeometryRoot.gameObject.activeSelf!=shown)Binding.GeometryRoot.gameObject.SetActive(shown);}
        void RestorePose()
        {
            if(Warnings!=null)Warnings.Hide();
            HadGunTransitReapply=false;Deployment.Restore();if(Shutters!=null)Shutters.Restore();if(Emitters!=null)Emitters.Restore();if(R19!=null)R19.Restore();
            if(Binding!=null&&BindPosition!=null)for(int i=0;i<BindPosition.Length;i++)if(Binding.ModuleRoots[i]!=null)SetOffset(i,0f);
        }
        void OnDisable(){RestorePose();Initialized=false;LastRevision=-1;}
        void OnDestroy()
        {RestorePose();HaveEntity=false;View=null;Binding=null;Slots.Clear();BindPosition=null;OffsetHeight=null;Desired=null;SourceDesired=null;ResearchOwners=null;AddonStartTicks=null;Shutters=null;Emitters=null;R19=null;}
    }
}
