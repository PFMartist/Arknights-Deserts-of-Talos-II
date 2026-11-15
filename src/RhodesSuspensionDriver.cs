using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Core.Utility.FixedPoint;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public struct RhodesSuspensionDiagnostics
    {
        public int Feet,SampledFeet,SupportedPoints,SaturatedFeet,UnsupportedFeet,StaleFeet,RateLimitedFeet;
        public long Rays,StepCalls,SampleCalls,PoseApplications,RotationFeedForwards,WorldHitProjections,GeneratedSurfaceQueries,ContactSafetyProjections;
        public bool GeneratedSurfaceMode,AllFootprintsSupported;public float NativeSupportTarget,NativeSupportPendingDelta;
        public int VisibleFeet,HeaveSaturatedFeet,PitchSaturatedFeet;
        public bool Eligible,Shown,Alive,OwnerResearchReady,RootActive,ViewActive,InterpolatorReady,MetricRoot,NativeVisible,IconOnly,BudgetReady,CameraEnabled,CameraActive,DetailsAvailable;
        public string CameraName;
        public bool GroundDatumCalibrated,WaitingForNativeGroundUpdate;public float AuthoredContactDatum,NativeSettleOffset;
        public float RootY,CameraDistance,GroundMinimum,GroundMaximum,UnclampedHeaveMinimum,UnclampedHeaveMaximum,TargetHeaveMinimum,TargetHeaveMaximum,ActualHeaveMinimum,ActualHeaveMaximum,TargetPitchMinimum,TargetPitchMaximum,ActualPitchMinimum,ActualPitchMaximum;
        public float SampledEnvelopePenetration,SampledEnvelopeGap,OldestSampleSeconds,MinimumRamOverlap;
    }

    // Native LOD chooses work tiers. The low tier remains a bounded ground
    // contact safety path even when the donor disables cosmetic suspension.
    internal static class RhodesCarrierLodCadence
    {
        internal static float SampleInterval(int nativeLod,bool suspensionActive)
        {return suspensionActive&&nativeLod==0?.2f:suspensionActive&&nativeLod==1?.5f:1f;}
        internal static float PoseInterval(int nativeLod,bool suspensionActive)
        {return suspensionActive&&nativeLod==0?0f:suspensionActive&&nativeLod==1?1f/15f:.2f;}
        internal static Bounds TransformBounds(Bounds source,Matrix4x4 toGroup)
        {
            Vector3 min=source.min,max=source.max;Bounds result=new Bounds();
            for(int corner=0;corner<8;corner++)
            {Vector3 point=toGroup.MultiplyPoint3x4(new Vector3((corner&1)==0?min.x:max.x,(corner&2)==0?min.y:max.y,(corner&4)==0?min.z:max.z));if(corner==0)result=new Bounds(point,Vector3.zero);else result.Encapsulate(point);}
            return result;
        }
    }

    // Explicit host Step only. No Update, LateUpdate, rigid bodies, force,
    // native-mover changes, mesh rewrites or independent whole-hull pose.
    public sealed class RhodesSuspensionDriver:MonoBehaviour
    {
        const int MaximumFeet=24,AuthoredContactsPerFoot=6,SupportsPerFoot=7,RaysPerFoot=6;
        const float NativeSupportHeadroom=.08f,MaximumGeneratedSupportLift=64f;
        static readonly FieldInfo GroundRaycastField=typeof(UnitView).GetField("mGroundRaycastInfo",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly FieldInfo NativeSettleField=typeof(UnitView).GetField("mCalculatedSuspensionSettleYOffset",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly FieldInfo NativeSettleCalculatedField=typeof(UnitView).GetField("mSuspensionSettleOffsetCalculated",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly FieldInfo NativePhysicsField=typeof(UnitView).GetField("mPhysicsEnabled",BindingFlags.Instance|BindingFlags.NonPublic);
        const float RayRise=2000f,RayLength=4000f,BarrelLength=7.660631065f,RodLength=9.2733955f;
        static readonly string[] ContactTags={"LF","LR","LC","RF","RR","RC"};
        static readonly int[] ContactEnds={1,-1,0,1,-1,0};
        static readonly List<WeakReference> Owners=new List<WeakReference>();
        static readonly RhodesSuspensionRayBudget GlobalBudget=new RhodesSuspensionRayBudget();
        static bool[] OwnerReady=new bool[0];
        static int NextOwner;
        UnitView View;RhodesCarrierBinding Binding;Transform Root;Entity Owner;
        Foot[] Feet;
        RhodesSharedSuspensionCommit[] SharedCommits;
        Transform StaticV13MainChassis;
        Transform StaticV14MainChassis;
        bool Attempted,Initialized,Registered,WasShown,Eligible,HaveBounds;
        int LastFrame=-1,NextFoot,TerrainLayer=-1;
        double Clock,CameraRetryAt;
        Camera CameraCache;
        Bounds OriginalGeometryBounds,OriginalVisualBounds;
        LODGroup Lod;Vector3 OriginalLodCentre;float OriginalLodSize;
        long Rays;
        float CameraDistance=float.PositiveInfinity;
        float ActiveSampleInterval=.2f,ActivePoseInterval;
        int NativeCadenceLod=-1;bool NativeCadenceActive;
        long StepCalls,SampleCalls,PoseApplications,DiagnosticLastRays,RotationFeedForwards,WorldHitProjections;
        bool LastShown,LastAlive,LastResearchReady,LastActive;
        int DiagnosticRemaining=4,DiagnosticTotal;
        float NextDiagnosticAt;
        bool MovementOriginReady,MovementDiagnosticStarted;
        Vector3 MovementOrigin;
        bool GroundDatumCalibrated,PreviousSettleCalculated;
        float AuthoredContactDatum,CalibratedNativeSettle,PreviousSettleOffset;
        int GroundDatumApplyFrame=-1;
        long GeneratedSurfaceQueries,ContactSafetyProjections;
        bool GeneratedSurfaceMode,AllFootprintsSupported,TerrainDiagnosticStarted,SupportReset=true,HaveSupportRoot;
        float NativeSupportTarget,NativeSupportPendingDelta;
        Vector3 SupportPreviousRoot;
        // Bounded snapshots: four at startup, eight at the first confirmed
        // motion; Ctrl+Alt+F7 on this selected carrier requests eight more.
        // At most32 per binding.
        // No frame scans, no diagnostic raycasts, no persistent file writes.

        sealed class Ram
        {
            public Transform Barrel,Rod,Base,End;
            public Vector3 BarrelPosition,RodPosition,BarrelAxisRoot,RodAxisRoot;
            public Quaternion BarrelRotation,RodRotation,BarrelRootRotation,RodRootRotation;
            public float BarrelRadius,RodRadius,AuthoredBarrelLength,AuthoredRodLength;
        }
        sealed class Foot
        {
            public Foot Peer;
            public Transform V14Carrier;
            public Quaternion V14CarrierRotation;
            public string Name;
            public Transform Lift,Bogie;
            public Vector3 LiftPosition,PivotRoot;
            public Quaternion BogieRotation;
            public Transform[] Contacts;
            public Vector3[] RestContacts;
            public RhodesGroundPoint[] SupportQuad=new RhodesGroundPoint[4],ClipA=new RhodesGroundPoint[8],ClipB=new RhodesGroundPoint[8];
            public float SoleY,FootMinX,FootMaxX,FootMinZ,FootMaxZ;
            public bool GeneratedFootprintKnown;
            public float RequiredPlaneHeave,PlaneNormalY,PlaneUpDot;
            public Vector3[] WorldHits;public Quaternion PoseRootRotation;public Vector3 PoseRootPosition;public bool HavePoseHistory;
            public RhodesSuspensionSupport[] Samples;
            public Ram[] Rams;
            public Hose[] Hoses;
            public SkinnedMeshRenderer Skin;
            public RhodesSuspensionLimits Limits;
            public RhodesSuspensionSolution Solution;
            public RhodesSuspensionResidual Residual;
            public float Heave,Pitch,NoHitAge,SampleAge,PoseAge,MinOverlap;
            public double NextSample;
            public bool HaveSample,Visible,WasVisible,RateLimited;
            public long SupportRevision;
        }
        sealed class Hose
        {
            public Transform[] Points,Segments;
            public Vector3[] PointPosition,PointRoot,SegmentPosition,SegmentScale,SegmentAxisRoot;
            public Quaternion[] SegmentRotation,SegmentRootRotation;
            public float[] Lengths;
        }
        public void Initialize(UnitView view,RhodesCarrierBinding binding)
        {Initialize(view,binding,null);}
        // Optional per-bogie limits are consumed once. No live disk/config read.
        public void Initialize(UnitView view,RhodesCarrierBinding binding,IDictionary<string,RhodesSuspensionLimits> limitsByBogie)
        {
            if(view==null||view.EntityID.Equals(Entity.None))return;
            if(Attempted&&View==view&&Binding==binding&&Owner.Equals(view.EntityID))return;
            Release();View=view;Binding=binding;Root=binding==null?null:binding.GeometryRoot;Owner=view.EntityID;Attempted=true;
            try
            {
                if(binding==null||!binding.IsV05||Root==null||!Root.IsChildOf(view.transform)||!MetricRoot())throw new InvalidOperationException("Suspension needs its owned, uniform positive1m presentation root");
                TerrainLayer=LayerMask.NameToLayer("Terrain");if(TerrainLayer<0)throw new InvalidOperationException("Named native Terrain physics layer is absent");
                BindStaticV14Contract();BindStaticV13Contract();
                List<Foot> feet=new List<Foot>();
                for(int index=0;index<(StaticV14MainChassis==null?MaximumFeet:0);index++)
                {
                    string tag=index.ToString("00"),prefix="CHASSIS_"+tag;
                    Transform lift=binding.FindNode(prefix+"_PIVOT_SuspensionLift");
                    if(lift==null)continue; // An unchanged V06 model is inert, never rebuilt.
                    if(index!=feet.Count)throw new InvalidOperationException("Suspension bogie numbering must be contiguous from zero");
                    RhodesSuspensionLimits limits;
                    if(limitsByBogie==null||!limitsByBogie.TryGetValue(prefix+"_BogiePivot",out limits))limits=StaticV13MainChassis==null?RhodesSuspensionLimits.Default:RhodesSuspensionV13Contract.Limits;
                    if(StaticV13MainChassis!=null&&(limits.MinHeave<RhodesSuspensionV13Contract.Limits.MinHeave||limits.MaxHeave>RhodesSuspensionV13Contract.Limits.MaxHeave||limits.MaxPitchDegrees>RhodesSuspensionV13Contract.Limits.MaxPitchDegrees||limits.ContactClearance!=.24f))throw new InvalidOperationException("V13 requested limits exceed geometry or alter the retained contact allowance");
                    limits.Validate();Foot foot=BindFoot(prefix,lift,limits);feet.Add(foot);
                }
                if(StaticV14MainChassis!=null)for(int track=0;track<RhodesSuspensionV14Contract.TrackCount;track++)feet.Add(BindV14Belt(track));
                Feet=feet.ToArray();
                if(StaticV14MainChassis!=null)for(int track=0;track<Feet.Length;track++)Feet[track].Peer=Feet[track^1];
                if(Feet.Length!=0&&!SupportedFootCount())throw new InvalidOperationException("Unsupported authored suspension count for this geometry contract");
                if(StaticV13MainChassis!=null&&Feet.Length!=20)throw new InvalidOperationException("V13 static main chassis requires exactly twenty authored feet");
                Initialized=Feet.Length>0;
                if(Initialized){CalibrateGroundDatum();ExpandVisualBounds();Register();}
                Runtime.Log("RHODES_SUSPENSION_CANDIDATE_READY entity="+Owner+"; compatibleFeet="+Feet.Length+"; staticV13UpperChassis="+(StaticV13MainChassis!=null)+"; authoredContactsPerFoot=6; supportSlotsPerFoot=7; finiteGeneratedSurfaceGuard=True; terrainLayerOnly=True; totalGlobalRaysPerFrame=12; explicitHostStep=True; sharedShoeSkinAndPhaseUnchanged=True; nativeHullOwnershipUnchanged=True; physicalLoadOrRuntimeAcceptance=False");
            }
            catch(Exception error)
            {
                RestorePoseAndBounds();Feet=null;Initialized=false;
                Runtime.Log("RHODES_SUSPENSION_CANDIDATE_BIND_FAILED entity="+Owner+"; attemptedOnceForOwnedRoot=True; error="+error.Message);
            }
        }
        // The donor wheels remain for the native wrapper contract, but their
        // cached ride height is not this metre-based model's ground datum.
        // Correct the owned LIVE view, not only its non-serialized template.
        // Native UpdateGroundUnitPosition remains the sole hull pose writer.
        void CalibrateGroundDatum()
        {
            if(!SupportedFootCount())throw new InvalidOperationException("Ground datum requires a supported authored suspension count");
            if(Binding.NativeRoot!=View.transform||Root.parent!=View.transform||Root.localPosition.sqrMagnitude>.000001f||Quaternion.Angle(Root.localRotation,Quaternion.identity)>.01f)
                throw new InvalidOperationException("Ground datum needs the owned unshifted model attached directly to its native UnitView");
            object lod=NativeRead.Get(View,"LODTunings");
            if(lod==null||Convert.ToInt32(NativeRead.Get(lod,"SuspensionActiveLODs"))!=0)
                throw new InvalidOperationException("Ground datum calibration requires the custom native-suspension LOD mask to remain None");
            float low=float.PositiveInfinity,high=float.NegativeInfinity;double sum=0;int count=0;
            foreach(Foot f in Feet)for(int contactIndex=0;contactIndex<f.Contacts.Length;contactIndex++)
            {
                // V14's verified direct-parent identity rest frames already
                // provide model-local coordinates. Going through kilometre-
                // scale world positions can erase submillimetre precision.
                float y=StaticV14MainChassis==null?Root.InverseTransformPoint(f.Contacts[contactIndex].position).y:
                    f.PivotRoot.y+f.RestContacts[contactIndex].y;
                if(!RhodesSuspensionMath.Finite(y))throw new InvalidOperationException("Nonfinite authored ground-contact datum");
                low=Math.Min(low,y);high=Math.Max(high,y);sum+=y;count++;
            }
            // Frozen V07 markers share modelY=0 within27micrometres. A later
            // non-planar/shifted marker scheme must provide a new contract.
            float expectedDatum=StaticV14MainChassis==null?0f:RhodesSuspensionV14Contract.AuthoredContactDatumMetres;
            if(count!=Feet.Length*AuthoredContactsPerFoot||high-low>.001f||Math.Abs(sum/count-expectedDatum)>.001)
                throw new InvalidOperationException("Authored V07 ground-contact datum is not the expected zero plane");
            AuthoredContactDatum=expectedDatum;CalibratedNativeSettle=-AuthoredContactDatum;
            PreviousSettleCalculated=(bool)NativeRead.Get(View,"mSuspensionSettleOffsetCalculated");
            PreviousSettleOffset=Convert.ToSingle(NativeRead.Get(View,"mCalculatedSuspensionSettleYOffset"));
            float donor=View.SuspensionSettleYOffset;
            if(!RhodesSuspensionMath.Finite(donor))throw new InvalidOperationException("Native donor suspension-settle cache is nonfinite");
            GroundDatumCalibrated=true;GroundDatumApplyFrame=Time.frameCount;
            NativeRead.Set(View,"mCalculatedSuspensionSettleYOffset",CalibratedNativeSettle);
            NativeRead.Set(View,"mSuspensionSettleOffsetCalculated",true);
            Runtime.Log("RHODES_SUSPENSION_DATUM_CALIBRATED entity="+Owner+"; donorNativeSettle="+Number(donor)+
                "; markerRootMinimumY="+Number(low)+"; markerRootMaximumY="+Number(high)+"; authoredDatum="+Number(AuthoredContactDatum)+
                "; calibratedNativeSettle="+Number(View.SuspensionSettleYOffset)+"; nativePhysicsEnabledAtBind="+NativeRead.Get(View,"mPhysicsEnabled")+"; liveOwnedBinding=True; nativeNextGroundUpdateOwnsHullPose=True; visualRootNotShifted=True; nativeSolverPhysicsAndSimPositionUnchanged=True; runtimeContactAcceptancePending=True");
        }
        void RestoreGroundDatum()
        {
            if(!GroundDatumCalibrated)return;GroundDatumCalibrated=false;
            if(View==null||Binding==null||Binding.NativeRoot!=View.transform||Root==null||!Root.IsChildOf(View.transform))return;
            if(!Owner.Equals(View.EntityID)&&!View.EntityID.Equals(Entity.None))return;
            // Return only the still-owned cache mutation; do not overwrite a
            // later owner's or another component's replacement calibration.
            if((bool)NativeRead.Get(View,"mSuspensionSettleOffsetCalculated")&&
                Convert.ToSingle(NativeRead.Get(View,"mCalculatedSuspensionSettleYOffset"))==CalibratedNativeSettle)
            {
                NativeRead.Set(View,"mCalculatedSuspensionSettleYOffset",PreviousSettleOffset);
                NativeRead.Set(View,"mSuspensionSettleOffsetCalculated",PreviousSettleCalculated);
            }
        }
        bool SupportedFootCount()
        {return Feet!=null&&(StaticV14MainChassis!=null?Feet.Length==RhodesSuspensionV14Contract.TrackCount:(Feet.Length==20||Feet.Length==24));}
        void BindStaticV14Contract()
        {
            StaticV14MainChassis=Binding.FindNode("PIVOT_V14_MainChassis");
            if(StaticV14MainChassis==null)return;
            RhodesSuspensionV14Contract.RequireVerified();
            Transform authoredRoot=Binding.FindNode("ROOT_Rhodes");
            if(authoredRoot==null||authoredRoot.parent!=Root||authoredRoot.localPosition.sqrMagnitude>.000001f||
                Quaternion.Angle(authoredRoot.localRotation,Quaternion.identity)>.01f||(authoredRoot.localScale-Vector3.one).sqrMagnitude>.000001f)
                throw new InvalidOperationException("V14 authored root must be identity under the owned presentation wrapper");
            if(Binding.FindNode("PIVOT_V13_MainChassis")!=null||StaticV14MainChassis.parent!=authoredRoot||StaticV14MainChassis.localPosition.sqrMagnitude>.000001f||
                Quaternion.Angle(StaticV14MainChassis.localRotation,Quaternion.identity)>.01f||(StaticV14MainChassis.localScale-Vector3.one).sqrMagnitude>.000001f)
                throw new InvalidOperationException("V14 static main chassis must be the sole identity main frame under ROOT_Rhodes");
        }
        Foot BindV14Belt(int track)
        {
            string prefix="TRACK_"+track.ToString("00"),module="CHASSIS_"+(track/RhodesSuspensionV14Contract.BeltsPerModule).ToString("00");
            Foot f=new Foot();f.Name=prefix+"_PitchPivot";f.Bogie=Require(f.Name);f.Lift=Require(module+"_PIVOT_SuspensionLift");
            Transform carrier=Require(module+"_PIVOT_CarrierPitch");
            f.V14Carrier=carrier;f.V14CarrierRotation=carrier.localRotation;
            if(f.Lift.parent!=StaticV14MainChassis||carrier.parent!=f.Lift||f.Bogie.parent!=carrier||
                Quaternion.Angle(f.Bogie.localRotation,Quaternion.identity)>.01f||Quaternion.Angle(f.Lift.localRotation,Quaternion.identity)>.01f||
                (f.Lift.localScale-Vector3.one).sqrMagnitude>.000001f||(carrier.localScale-Vector3.one).sqrMagnitude>.000001f||(f.Bogie.localScale-Vector3.one).sqrMagnitude>.000001f)
                throw new InvalidOperationException("V14 lift/carrier/independent pitch hierarchy differs: "+prefix);
            // The immediate parent is checked above. Validate the authored
            // local rest frame directly; a world-space roundtrip at x/z of
            // several kilometres can exceed the 0.1mm collinearity tolerance.
            Vector3 hinge=f.Bogie.localPosition;
            if(Math.Abs(hinge.y)>.0001f||Math.Abs(hinge.z)>.0001f||Quaternion.Angle(carrier.localRotation,Quaternion.identity)>.01f)
                throw new InvalidOperationException("V14 central and belt transverse hinges must be collinear in the rest frame: "+prefix);
            f.Limits=RhodesSuspensionV14Contract.Limits;f.Limits.Validate();f.LiftPosition=f.Lift.localPosition;f.BogieRotation=f.Bogie.localRotation;
            // ROOT_Rhodes/main and all three rest rotations are identity.
            // Sum these verified local translations without world cancellation.
            f.PivotRoot=f.Lift.localPosition+carrier.localPosition+f.Bogie.localPosition;
            f.Contacts=new Transform[AuthoredContactsPerFoot];f.RestContacts=new Vector3[AuthoredContactsPerFoot];
            f.Samples=new RhodesSuspensionSupport[SupportsPerFoot];f.WorldHits=new Vector3[SupportsPerFoot];f.Rams=new Ram[0];f.Hoses=null;
            for(int k=0;k<AuthoredContactsPerFoot;k++)
            {
                Transform marker=Require(prefix+"_HP_Contact_"+ContactTags[k]);
                if(marker.parent!=f.Bogie)throw new InvalidOperationException("V14 contact must directly follow its own belt pitch: "+marker.name);
                Vector3 local=marker.localPosition;f.Contacts[k]=marker;f.RestContacts[k]=local;f.SoleY+=local.y/AuthoredContactsPerFoot;
                f.Samples[k]=new RhodesSuspensionSupport{RestY=local.y,RestZ=local.z,End=ContactEnds[k]};
            }
            foreach(SkinnedMeshRenderer candidate in f.Bogie.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if(candidate.name=="GEO_EF_TKR_"+f.Name){if(f.Skin!=null)throw new InvalidOperationException("Duplicate V14 belt skin");f.Skin=candidate;}
            if(f.Skin==null||f.Skin.rootBone==null||f.Skin.rootBone.parent!=f.Bogie||f.Skin.rootBone.name!="EF_TrackRigSpace_"+f.Name)
                throw new InvalidOperationException("V14 independent belt skin/rig-space absent: "+prefix);
            f.FootMinX=f.FootMinZ=float.PositiveInfinity;f.FootMaxX=f.FootMaxZ=float.NegativeInfinity;
            for(int k=0;k<8;k++)
            {
                Vector3 p=f.Bogie.InverseTransformPoint(RendererCorner(f.Skin,k));
                f.FootMinX=Math.Min(f.FootMinX,p.x);f.FootMaxX=Math.Max(f.FootMaxX,p.x);f.FootMinZ=Math.Min(f.FootMinZ,p.z);f.FootMaxZ=Math.Max(f.FootMaxZ,p.z);
            }
            f.FootMinX-=.002f;f.FootMaxX+=.002f;f.FootMinZ-=.002f;f.FootMaxZ+=.002f;
            return f;
        }
        void BindStaticV13Contract()
        {
            StaticV13MainChassis=Binding.FindNode("PIVOT_V13_MainChassis");
            if(StaticV13MainChassis==null)return;
            if(!RhodesSuspensionV13Contract.InterfaceVerified)throw new InvalidOperationException("V13 authored geometry interface has not been verified");
            if(StaticV13MainChassis.parent!=Root||StaticV13MainChassis.localPosition.sqrMagnitude>.000001f||
                Quaternion.Angle(StaticV13MainChassis.localRotation,Quaternion.identity)>.01f||(StaticV13MainChassis.localScale-Vector3.one).sqrMagnitude>.000001f)
                throw new InvalidOperationException("V13 static main chassis must be an identity rest frame directly under Root");
            if(RhodesSuspensionV13Contract.FootBarrelLengths.Length!=80||RhodesSuspensionV13Contract.FootRodLengths.Length!=80)
                throw new InvalidOperationException("V13 foot hydraulic interface requires eighty measured lengths per part type");
            for(int k=0;k<80;k++)
                if(!RhodesSuspensionMath.Finite(RhodesSuspensionV13Contract.FootBarrelLengths[k])||!RhodesSuspensionMath.Finite(RhodesSuspensionV13Contract.FootRodLengths[k])||
                    RhodesSuspensionV13Contract.FootBarrelLengths[k]<=0f||RhodesSuspensionV13Contract.FootRodLengths[k]<=0f)
                    throw new InvalidOperationException("V13 foot hydraulic lengths must be finite and positive");
            // This grouping transform and all six upper hydraulic attachments
            // remain static. No primary solver, writes, extra rays or bounds.
        }
        Foot BindFoot(string prefix,Transform lift,RhodesSuspensionLimits limits)
        {
            Foot f=new Foot();f.Name=prefix+"_BogiePivot";f.Lift=lift;f.Bogie=Require(f.Name);f.Limits=limits;
            if(f.Bogie.parent!=lift||lift.parent==null||!lift.IsChildOf(Root)||Quaternion.Angle(Root.rotation,f.Bogie.rotation)>.01f||Quaternion.Angle(Root.rotation,lift.parent.rotation)>.01f)
                throw new InvalidOperationException("Suspension rest frame/parent contract differs: "+prefix);
            if((lift.localScale-Vector3.one).sqrMagnitude>.000001f||(f.Bogie.localScale-Vector3.one).sqrMagnitude>.000001f)
                throw new InvalidOperationException("Suspension control scales must stay1: "+prefix);
            if(StaticV13MainChassis!=null&&lift.parent!=StaticV13MainChassis)throw new InvalidOperationException("V13 lift is not directly owned by the static main chassis: "+prefix);
            f.LiftPosition=lift.localPosition;f.BogieRotation=f.Bogie.localRotation;f.PivotRoot=Root.InverseTransformPoint(f.Bogie.position);
            f.Contacts=new Transform[AuthoredContactsPerFoot];f.RestContacts=new Vector3[AuthoredContactsPerFoot];f.Samples=new RhodesSuspensionSupport[SupportsPerFoot];f.WorldHits=new Vector3[SupportsPerFoot];
            for(int k=0;k<AuthoredContactsPerFoot;k++)
            {
                Transform marker=Require(prefix+"_HP_Contact_"+ContactTags[k]);
                if(!marker.IsChildOf(f.Bogie))throw new InvalidOperationException("Contact marker is outside bogie: "+marker.name);
                Vector3 local=f.Bogie.InverseTransformPoint(marker.position);
                f.Contacts[k]=marker;f.RestContacts[k]=local;f.SoleY+=local.y/AuthoredContactsPerFoot;f.Samples[k]=new RhodesSuspensionSupport{RestY=local.y,RestZ=local.z,End=ContactEnds[k]};
            }
            foreach(SkinnedMeshRenderer candidate in f.Bogie.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if(candidate.name=="GEO_EF_TKR_"+f.Name){if(f.Skin!=null)throw new InvalidOperationException("Duplicate live bogie skin");f.Skin=candidate;}
            if(f.Skin==null||f.Skin.rootBone!=f.Bogie)throw new InvalidOperationException("Existing TKR skin/rootBone contract absent: "+prefix);
            f.FootMinX=f.FootMinZ=float.PositiveInfinity;f.FootMaxX=f.FootMaxZ=float.NegativeInfinity;
            for(int k=0;k<8;k++)
            {
                Vector3 p=f.Bogie.InverseTransformPoint(RendererCorner(f.Skin,k));
                f.FootMinX=Math.Min(f.FootMinX,p.x);f.FootMaxX=Math.Max(f.FootMaxX,p.x);f.FootMinZ=Math.Min(f.FootMinZ,p.z);f.FootMaxZ=Math.Max(f.FootMaxZ,p.z);
            }
            // Immutable TKR phase-sweep XZ bounds, not just six sparse markers.
            f.FootMinX-=.002f;f.FootMaxX+=.002f;f.FootMinZ-=.002f;f.FootMaxZ+=.002f;
            f.Rams=new Ram[4];
            for(int k=0;k<4;k++)
            {
                string suffix=k.ToString();Ram r=new Ram();f.Rams[k]=r;
                r.Barrel=Require(prefix+"_PIVOT_RamBarrel_"+suffix);r.Rod=Require(prefix+"_PIVOT_RamRod_"+suffix);
                r.Base=Require(prefix+"_HP_CylinderBasePin_"+suffix);r.End=Require(prefix+"_HP_RodEndPin_"+suffix);
                if(r.Base.IsChildOf(lift)||!r.End.IsChildOf(f.Bogie)||r.Barrel.IsChildOf(lift)||r.Rod.IsChildOf(lift))throw new InvalidOperationException("Fixed/moving cylinder attachment contract differs: "+prefix);
                if((r.Barrel.localScale-Vector3.one).sqrMagnitude>.000001f||(r.Rod.localScale-Vector3.one).sqrMagnitude>.000001f)throw new InvalidOperationException("Hydraulic rigid part scale must remain1");
                r.BarrelPosition=r.Barrel.localPosition;r.RodPosition=r.Rod.localPosition;r.BarrelRotation=r.Barrel.localRotation;r.RodRotation=r.Rod.localRotation;
                r.BarrelAxisRoot=Root.InverseTransformDirection(r.Barrel.up).normalized;r.RodAxisRoot=Root.InverseTransformDirection(r.Rod.up).normalized;
                r.BarrelRootRotation=Quaternion.Inverse(Root.rotation)*r.Barrel.rotation;r.RodRootRotation=Quaternion.Inverse(Root.rotation)*r.Rod.rotation;
                Vector3 direction=(r.End.position-r.Base.position).normalized;
                if(Vector3.Dot(r.Barrel.up,direction)<.999f||Vector3.Dot(r.Rod.up,-direction)<.999f)throw new InvalidOperationException("Hydraulic local+Y axes disagree with source+Z pins: "+prefix);
                r.BarrelRadius=PartRadius(r.Barrel);r.RodRadius=PartRadius(r.Rod);
                r.AuthoredBarrelLength=BarrelLength;r.AuthoredRodLength=RodLength;
                if(StaticV13MainChassis!=null)
                {
                    if(!r.Base.IsChildOf(StaticV13MainChassis)||!r.Barrel.IsChildOf(StaticV13MainChassis)||!r.Rod.IsChildOf(StaticV13MainChassis))throw new InvalidOperationException("V13 foot ram fixed parts must follow static main chassis: "+prefix);
                    int group=int.Parse(prefix.Substring(8,2),System.Globalization.CultureInfo.InvariantCulture);
                    r.AuthoredBarrelLength=RhodesSuspensionV13Contract.FootBarrelLengths[group*4+k];r.AuthoredRodLength=RhodesSuspensionV13Contract.FootRodLengths[group*4+k];
                }
            }
            f.Hoses=new Hose[2];for(int side=0;side<2;side++)f.Hoses[side]=BindHose(prefix,side==0?"L":"R");
            if(StaticV13MainChassis!=null)foreach(Hose h in f.Hoses)if(h!=null)
            {foreach(Transform point in h.Points)if(!point.IsChildOf(StaticV13MainChassis)||point.IsChildOf(lift))throw new InvalidOperationException("V13 hose points must belong to the static main chassis");foreach(Transform segment in h.Segments)if(!segment.IsChildOf(StaticV13MainChassis)||segment.IsChildOf(lift))throw new InvalidOperationException("V13 hose display pivots must belong to the static main chassis");}
            return f;
        }
        Hose BindHose(string prefix,string side)
        {
            if(Binding.FindNode(prefix+"_HP_HosePoint_"+side+"_0")==null)return null;
            Hose h=new Hose();h.Points=new Transform[9];h.PointPosition=new Vector3[9];h.PointRoot=new Vector3[9];
            h.Segments=new Transform[8];h.SegmentPosition=new Vector3[8];h.SegmentScale=new Vector3[8];h.SegmentRotation=new Quaternion[8];h.SegmentRootRotation=new Quaternion[8];h.SegmentAxisRoot=new Vector3[8];h.Lengths=new float[8];
            for(int k=0;k<9;k++){h.Points[k]=Require(prefix+"_HP_HosePoint_"+side+"_"+k);h.PointPosition[k]=h.Points[k].localPosition;h.PointRoot[k]=Root.InverseTransformPoint(h.Points[k].position);}
            for(int k=0;k<8;k++)
            {
                Transform axis=Require(prefix+"_PIVOT_Hose_"+side+"_"+k);h.Segments[k]=axis;h.SegmentPosition[k]=axis.localPosition;h.SegmentRotation[k]=axis.localRotation;h.SegmentScale[k]=axis.localScale;
                h.SegmentRootRotation[k]=Quaternion.Inverse(Root.rotation)*axis.rotation;h.SegmentAxisRoot[k]=Root.InverseTransformDirection(axis.up).normalized;h.Lengths[k]=Vector3.Distance(h.Points[k].position,h.Points[k+1].position);
                if(h.Lengths[k]<.01f)throw new InvalidOperationException("Hose display segment rest length is invalid");
            }
            return h;
        }
        Transform Require(string name)
        {Transform result=Binding.FindNode(name);if(result==null||!result.IsChildOf(Root))throw new InvalidOperationException("Suspension marker/control absent: "+name);return result;}
        bool MetricRoot()
        {
            if(Root==null)return false;Vector3 s=Root.lossyScale;
            return s.x>0f&&s.y>0f&&s.z>0f&&Math.Abs(s.x-1f)<.001f&&Math.Abs(s.y-1f)<.001f&&Math.Abs(s.z-1f)<.001f;
        }
        public void Step(UnitView view,RhodesCarrierBinding binding,bool shown)
        {
            if(view==null)return;
            if(!Attempted||View!=view||Binding!=binding||Root!=(binding==null?null:binding.GeometryRoot)||!Owner.Equals(view.EntityID))Initialize(view,binding);
            if(!Initialized||LastFrame==Time.frameCount)return;LastFrame=Time.frameCount;
            StepCalls++;float delta=Runtime.PresentationDeltaTime;
            RhodesResearch.Snapshot research;bool ownerReady=RhodesResearch.TrySnapshot(view.CommanderID,out research)&&!(research.AviationLevel>0&&research.GunneryLevel>0);
            bool alive=view.NonInterpolatedTickState!=null&&!view.NonInterpolatedTickState.UnitIsDead;
            bool active=Root!=null&&Root.gameObject.activeInHierarchy&&Root.IsChildOf(view.transform)&&view.SimStateInterpolator!=null&&MetricRoot();
            LastShown=shown;LastAlive=alive;LastResearchReady=ownerReady;LastActive=active;
            // Give the native ground updater one frame to consume the new datum
            // before rays sample against the previous donor-height pose.
            Eligible=RhodesSuspensionMath.CanStep(shown,active,alive,ownerReady,delta)&&!(GroundDatumCalibrated&&Time.frameCount<=GroundDatumApplyFrame);
            if(!Eligible){WasShown=false;EmitDiagnostics();return;}
            Register();Clock+=delta;UpdateCameraDistance();UpdateWorkCadence();TrackFirstMovement();
            if(!WasShown){SupportReset=true;foreach(Foot f in Feet){f.NextSample=0;f.PoseAge=0;f.HavePoseHistory=false;f.HaveSample=false;ClearSamples(f);}}
            if(HaveSupportRoot&&(Root.position-SupportPreviousRoot).sqrMagnitude>625f)
            {SupportReset=true;foreach(Foot f in Feet){f.HavePoseHistory=false;f.HaveSample=false;f.NextSample=0;ClearSamples(f);}}
            SupportPreviousRoot=Root.position;HaveSupportRoot=true;
            WasShown=true;bool anyVisible=false;
            foreach(Foot f in Feet)
            {
                f.Visible=(f.Skin!=null&&f.Skin.isVisible)||(f.Peer!=null&&f.Peer.Skin!=null&&f.Peer.Skin.isVisible);anyVisible|=f.Visible;
                if(!f.Visible){f.WasVisible=false;f.PoseAge=0;f.HavePoseHistory=false;continue;}
                if(!f.WasVisible)f.NextSample=0;f.WasVisible=true;
                f.SampleAge+=delta;if(f.HaveSample&&f.Solution.Unsupported)f.NoHitAge+=delta;
                if(f.HaveSample&&f.Solution.Unsupported&&f.NoHitAge>f.Limits.MissGraceSeconds)
                    f.Solution=RhodesSuspensionMath.Solve(f.Samples,SupportsPerFoot,f.Limits,f.Solution.TargetHeave,f.Solution.TargetPitchDegrees,f.NoHitAge);
            }
            float ignored;GeneratedSurfaceMode=CustomFlatMap.TrySampleGeneratedGround(Root.position.x,Root.position.z,out ignored);
            Eligible=anyVisible||GeneratedSurfaceMode;if(!Eligible){EmitDiagnostics();return;}
            AllFootprintsSupported=GeneratedSurfaceMode;
            if(GeneratedSurfaceMode)foreach(Foot f in Feet)
            {
                RefreshGeneratedSupports(f,f.Heave,f.Pitch);AllFootprintsSupported&=f.GeneratedFootprintKnown;
            }
            // Non-generated scenes retain the original shared six-ray groups.
            // Generated terrain uses its exact finite collider triangles instead.
            // Round-robin groups; exactly six rays are reserved atomically.
            for(int checkedFeet=0;!GeneratedSurfaceMode&&checkedFeet<Feet.Length;checkedFeet++)
            {
                int index=NextFoot;Foot f=Feet[index];
                if(!f.Visible||Clock<f.NextSample){NextFoot=(NextFoot+1)%Feet.Length;continue;}
                if(!ReserveRays(this))break;NextFoot=(NextFoot+1)%Feet.Length;Sample(f);f.NextSample=Clock+ActiveSampleInterval;
            }
            float poseInterval=ActivePoseInterval;
            foreach(Foot f in Feet)
            {
                if(!f.Visible||!f.HaveSample)continue;f.PoseAge+=delta;if(f.PoseAge<poseInterval)continue;
                float dt=f.PoseAge;f.PoseAge=0;
                CompensateRootRotation(f);
                if(GeneratedSurfaceMode)RefreshGeneratedSupports(f,f.Heave,f.Pitch);else ProjectCachedWorldHits(f);
                f.Solution=RhodesSuspensionMath.Solve(f.Samples,SupportsPerFoot,f.Limits,f.Solution.TargetHeave,f.Solution.TargetPitchDegrees,f.NoHitAge);
                f.Heave=RhodesSuspensionMath.Chase(f.Heave,f.Solution.TargetHeave,dt,f.Limits.ResponseSeconds,f.Limits.HeaveMetresPerSecond);
                f.Pitch=RhodesSuspensionMath.Chase(f.Pitch,f.Solution.TargetPitchDegrees,dt,f.Limits.ResponseSeconds,f.Limits.PitchDegreesPerSecond);
                f.Heave=RhodesSuspensionMath.Clamp(f.Heave,f.Limits.MinHeave,f.Limits.MaxHeave);f.Pitch=RhodesSuspensionMath.Clamp(f.Pitch,-f.Limits.MaxPitchDegrees,f.Limits.MaxPitchDegrees);
                if(GeneratedSurfaceMode)RefreshGeneratedSupports(f,f.Heave,f.Pitch);
                float safe=RhodesSuspensionMath.ProjectContactHeave(f.Heave,f.Samples,SupportsPerFoot,f.Pitch,f.Limits);
                if(GeneratedSurfaceMode&&f.GeneratedFootprintKnown)safe=RhodesSuspensionMath.Clamp(Math.Max(safe,f.RequiredPlaneHeave),f.Limits.MinHeave,f.Limits.MaxHeave);
                if(safe>f.Heave+.00001f){ContactSafetyProjections++;f.Heave=safe;if(GeneratedSurfaceMode)RefreshGeneratedSupports(f,f.Heave,f.Pitch);}
                f.Heave=RhodesSuspensionMath.ProjectContactHeave(f.Heave,f.Samples,SupportsPerFoot,f.Pitch,f.Limits);
                if(GeneratedSurfaceMode&&f.GeneratedFootprintKnown)f.Heave=RhodesSuspensionMath.Clamp(Math.Max(f.Heave,f.RequiredPlaneHeave),f.Limits.MinHeave,f.Limits.MaxHeave);
                f.RateLimited=Math.Abs(f.Heave-f.Solution.TargetHeave)>.005f||Math.Abs(f.Pitch-f.Solution.TargetPitchDegrees)>.005f;
                if(StaticV14MainChassis==null){Apply(f);PoseApplications++;}f.Residual=RhodesSuspensionMath.Residual(f.Samples,SupportsPerFoot,f.Heave,f.Pitch,f.Limits.ContactClearance);
                f.PoseRootRotation=Root.rotation;f.PoseRootPosition=Root.position;f.HavePoseHistory=f.Solution.Hits>0&&!f.Solution.Unsupported;
            }
            if(StaticV14MainChassis!=null)ApplyV14SharedLifts();
            if(GeneratedSurfaceMode){AllFootprintsSupported=true;foreach(Foot f in Feet)AllFootprintsSupported&=f.GeneratedFootprintKnown;}
            UpdateNativeSupportDatum(delta);
            if(Runtime.DetailedDiagnosticsEnabled&&!TerrainDiagnosticStarted&&(NativeSupportTarget>.025f||HasTerrainConstraint()))
            {TerrainDiagnosticStarted=true;DiagnosticRemaining=Math.Max(DiagnosticRemaining,8);NextDiagnosticAt=0f;}
            EmitDiagnostics();
        }
        void ApplyV14SharedLifts()
        {
            if(SharedCommits==null)SharedCommits=new RhodesSharedSuspensionCommit[RhodesSuspensionV14Contract.ModuleCount];
            for(int module=0;module<RhodesSuspensionV14Contract.ModuleCount;module++)
            {
                Foot a=Feet[module*2],b=Feet[module*2+1];
                if(a.Lift!=b.Lift)throw new InvalidOperationException("V14 paired belt lift ownership mismatch");
                if(!a.Visible&&!b.Visible)continue;
                RhodesSharedSuspensionCommit commit=SharedCommits[module];
                // Generated test terrain has a separate exact moving footprint
                // guard. Keep that solver untouched; ordinary maps can reuse a
                // stable result until pose inputs or projected contacts change.
                if(!GeneratedSurfaceMode&&commit.SameInputs(a.Heave,a.Pitch,b.Heave,b.Pitch,a.SupportRevision,b.SupportRevision))continue;
                RhodesSuspensionV14Angles angles=RhodesSuspensionV14Math.DecomposePitch(a.Pitch,b.Pitch,RhodesSuspensionV14Contract.MainPitchDegrees,RhodesSuspensionV14Contract.RelativeBeltPitchDegrees,RhodesSuspensionV14Contract.EffectiveBeltPitchDegrees);
                a.Pitch=angles.FirstEffective;b.Pitch=angles.SecondEffective;
                if(GeneratedSurfaceMode){RefreshGeneratedSupports(a,a.Heave,a.Pitch);RefreshGeneratedSupports(b,b.Heave,b.Pitch);}
                float ah=RhodesSuspensionMath.ProjectContactHeave(a.Heave,a.Samples,SupportsPerFoot,a.Pitch,a.Limits);
                float bh=RhodesSuspensionMath.ProjectContactHeave(b.Heave,b.Samples,SupportsPerFoot,b.Pitch,b.Limits);
                if(GeneratedSurfaceMode&&a.GeneratedFootprintKnown)ah=RhodesSuspensionMath.Clamp(Math.Max(ah,a.RequiredPlaneHeave),a.Limits.MinHeave,a.Limits.MaxHeave);
                if(GeneratedSurfaceMode&&b.GeneratedFootprintKnown)bh=RhodesSuspensionMath.Clamp(Math.Max(bh,b.RequiredPlaneHeave),b.Limits.MinHeave,b.Limits.MaxHeave);
                float shared=RhodesSuspensionV14Math.SharedHeave(ah,bh,a.Limits);a.Heave=b.Heave=shared;
                if(!commit.HavePose||!RhodesSharedSuspensionCommit.Same(commit.Lift,shared))
                {a.Lift.localPosition=a.LiftPosition+Vector3.up*shared;commit.Lift=shared;}
                if(!commit.HavePose||!RhodesSharedSuspensionCommit.Same(commit.Common,angles.Common))
                {a.V14Carrier.localRotation=a.V14CarrierRotation*Quaternion.AngleAxis(angles.Common,Vector3.right);commit.Common=angles.Common;PoseApplications++;}
                if(!commit.HavePose||!RhodesSharedSuspensionCommit.Same(commit.First,angles.FirstRelative))
                {a.Bogie.localRotation=a.BogieRotation*Quaternion.AngleAxis(angles.FirstRelative,Vector3.right);commit.First=angles.FirstRelative;PoseApplications++;}
                if(!commit.HavePose||!RhodesSharedSuspensionCommit.Same(commit.Second,angles.SecondRelative))
                {b.Bogie.localRotation=b.BogieRotation*Quaternion.AngleAxis(angles.SecondRelative,Vector3.right);commit.Second=angles.SecondRelative;PoseApplications++;}
                commit.HavePose=true;
                // Central rod inherits lift only. The enlarged carrier rocks;
                // belt hinges supply bounded relative pitch, not a second copy
                // of the desired total angle. Collinear axes make sums exact.
                if(GeneratedSurfaceMode){RefreshGeneratedSupports(a,shared,a.Pitch);RefreshGeneratedSupports(b,shared,b.Pitch);}
                a.Residual=RhodesSuspensionMath.Residual(a.Samples,SupportsPerFoot,shared,a.Pitch,a.Limits.ContactClearance);
                b.Residual=RhodesSuspensionMath.Residual(b.Samples,SupportsPerFoot,shared,b.Pitch,b.Limits.ContactClearance);
                commit.Inputs(a.Heave,a.Pitch,b.Heave,b.Pitch,a.SupportRevision,b.SupportRevision);SharedCommits[module]=commit;
            }
        }
        static void ClearSamples(Foot f)
        {for(int k=0;k<SupportsPerFoot;k++){RhodesSuspensionSupport s=f.Samples[k];s.Hit=false;f.Samples[k]=s;}f.GeneratedFootprintKnown=false;f.SupportRevision++;}
        bool HasTerrainConstraint()
        {foreach(Foot f in Feet)if(f.HaveSample&&(f.Solution.HeaveSaturated||f.Residual.Penetration>.025f))return true;return false;}
        void RefreshGeneratedSupports(Foot f,float heave,float pitch)
        {
            Quaternion rotation=Root.rotation*Quaternion.AngleAxis(pitch,Vector3.right);
            Vector3 origin=Root.TransformPoint(f.PivotRoot+Vector3.up*heave);
            bool complete=true;
            for(int k=0;k<AuthoredContactsPerFoot;k++)
            {
                Vector3 p=origin+rotation*f.RestContacts[k];float y;bool hit=CustomFlatMap.TrySampleGeneratedGround(p.x,p.z,out y);GeneratedSurfaceQueries++;
                RhodesSuspensionSupport s=f.Samples[k];s.Hit=hit;
                if(hit){f.WorldHits[k]=new Vector3(p.x,y,p.z);Vector3 local=Root.InverseTransformPoint(f.WorldHits[k])-f.PivotRoot;s.GroundY=local.y;s.SampleZ=local.z;}
                f.Samples[k]=s;complete&=hit;
            }
            for(int k=0;k<4;k++)
            {
                Vector3 p=origin+rotation*new Vector3(k==0||k==3?f.FootMinX:f.FootMaxX,f.SoleY,k<2?f.FootMinZ:f.FootMaxZ);
                f.SupportQuad[k]=new RhodesGroundPoint(p.x,p.y,p.z);
            }
            Vector3 normal=rotation*Vector3.up;RhodesGroundPoint support=new RhodesGroundPoint();
            bool known=normal.y>.25f&&CustomFlatMap.TryFindGeneratedGroundSupport(f.SupportQuad,-normal.x/normal.y,-normal.z/normal.y,f.ClipA,f.ClipB,out support);
            RhodesSuspensionSupport guard=new RhodesSuspensionSupport();
            f.RequiredPlaneHeave=float.NaN;f.PlaneNormalY=normal.y;f.PlaneUpDot=Vector3.Dot(normal,Root.up);
            if(known)
            {
                GeneratedSurfaceQueries++;
                Vector3 ground=new Vector3(support.X,support.Y,support.Z),planeOrigin=origin+rotation*new Vector3(0f,f.SoleY,0f);
                float planeY=planeOrigin.y-(normal.x*(ground.x-planeOrigin.x)+normal.z*(ground.z-planeOrigin.z))/normal.y;
                Vector3 rest=Quaternion.Inverse(rotation)*(new Vector3(ground.x,planeY,ground.z)-origin),local=Root.InverseTransformPoint(ground)-f.PivotRoot;
                guard=new RhodesSuspensionSupport{RestY=f.SoleY,RestZ=rest.z,SampleZ=local.z,GroundY=local.y,End=0,Hit=true};f.WorldHits[AuthoredContactsPerFoot]=ground;
                f.RequiredPlaneHeave=RhodesSuspensionMath.PlaneRequiredHeave(Vector3.Dot(normal,ground-Root.TransformPoint(f.PivotRoot)),f.SoleY,f.PlaneUpDot,f.Limits.ContactClearance);
            }
            f.Samples[AuthoredContactsPerFoot]=guard;f.GeneratedFootprintKnown=complete&&known;f.HaveSample=true;f.SampleAge=0f;
            f.Solution=RhodesSuspensionMath.Solve(f.Samples,SupportsPerFoot,f.Limits,f.Solution.TargetHeave,f.Solution.TargetPitchDegrees,f.NoHitAge);
            if(f.Solution.Hits>0)f.NoHitAge=0f;
        }
        void UpdateNativeSupportDatum(float delta)
        {
            NativeSupportPendingDelta=0f;
            if(!GroundDatumCalibrated||!GeneratedSurfaceMode||!AllFootprintsSupported||!SupportedFootCount()||
               GroundRaycastField==null||NativeSettleField==null||NativeSettleCalculatedField==null||NativePhysicsField==null||
               (bool)NativePhysicsField.GetValue(View)||Binding.NativeRoot!=View.transform||!Owner.Equals(View.EntityID))return;
            object lod=NativeRead.Get(View,"LODTunings");
            if(lod==null||Convert.ToInt32(NativeRead.Get(lod,"SuspensionActiveLODs"))!=0)return;
            if(!(bool)NativeSettleCalculatedField.GetValue(View)||(float)NativeSettleField.GetValue(View)!=CalibratedNativeSettle)return;
            RaycastHit centre=(RaycastHit)GroundRaycastField.GetValue(View);
            if(centre.collider==null||centre.collider.gameObject.layer!=TerrainLayer||centre.collider.isTrigger)return;
            Vector3 offset=centre.point-Root.position;offset.y=0f;if(offset.sqrMagnitude>625f)return;
            float requiredDelta=float.NegativeInfinity;
            foreach(Foot f in Feet)
            {
                int hits;float required=RhodesSuspensionMath.RequiredHeave(f.Samples,SupportsPerFoot,f.Pitch,f.Limits.ContactClearance,out hits);
                if(!f.GeneratedFootprintKnown||hits<SupportsPerFoot)return;
                if(!RhodesSuspensionMath.Finite(required)||!RhodesSuspensionMath.Finite(f.RequiredPlaneHeave))return;
                float worldDelta=RhodesSuspensionMath.PlaneRootLiftDelta(f.RequiredPlaneHeave,f.Limits.MaxHeave,f.PlaneUpDot,f.PlaneNormalY);
                if(!RhodesSuspensionMath.Finite(worldDelta))return;requiredDelta=Math.Max(requiredDelta,worldDelta);
            }
            float target=RhodesSuspensionMath.AbsoluteNativeSupportLift(Root.position.y,centre.point.y,requiredDelta,MaximumGeneratedSupportLift,NativeSupportHeadroom);
            if(!RhodesSuspensionMath.Finite(target))return;NativeSupportTarget=target;
            float next=SupportReset||target>CalibratedNativeSettle?target:RhodesSuspensionMath.Chase(CalibratedNativeSettle,target,delta,.3f,.65f);
            SupportReset=false;NativeSupportPendingDelta=next-(Root.position.y-centre.point.y);
            if(Math.Abs(next-CalibratedNativeSettle)>.00001f){NativeSettleField.SetValue(View,next);CalibratedNativeSettle=next;}
            // The next normal native update consumes this absolute cache once.
            // Do not re-enter native ground methods or move a visual-only root.
        }
        void Sample(Foot f)
        {
            SampleCalls++;f.SupportRevision++;bool wasUnsupported=f.HaveSample&&f.Solution.Unsupported;
            for(int k=AuthoredContactsPerFoot;k<SupportsPerFoot;k++){RhodesSuspensionSupport extra=f.Samples[k];extra.Hit=false;f.Samples[k]=extra;}
            for(int k=0;k<AuthoredContactsPerFoot;k++)
            {
                Vector3 contact=f.Contacts[k].position;RaycastHit hit;bool valid=Physics.Raycast(contact+Vector3.up*RayRise,Vector3.down,out hit,RayLength,1<<TerrainLayer,QueryTriggerInteraction.Ignore);Rays++;
                valid=valid&&hit.collider!=null&&hit.collider.gameObject.layer==TerrainLayer&&!hit.collider.isTrigger&&hit.normal.y>.25f&&hit.collider.GetComponentInParent<UnitView>()==null;
                RhodesSuspensionSupport sample=f.Samples[k];sample.Hit=valid;
                if(valid){f.WorldHits[k]=hit.point;Vector3 local=Root.InverseTransformPoint(hit.point)-f.PivotRoot;sample.GroundY=local.y;sample.SampleZ=local.z;}
                f.Samples[k]=sample;
            }
            int hits=0;foreach(RhodesSuspensionSupport s in f.Samples)if(RhodesSuspensionMath.Valid(s))hits++;
            if(hits>0||!wasUnsupported)f.NoHitAge=0f;
            f.Solution=RhodesSuspensionMath.Solve(f.Samples,SupportsPerFoot,f.Limits,f.Solution.TargetHeave,f.Solution.TargetPitchDegrees,f.NoHitAge);
            f.HaveSample=true;f.SampleAge=0f;
        }
        void ProjectCachedWorldHits(Foot f)
        {
            bool changed=false;
            for(int k=0;k<SupportsPerFoot;k++)
            {
                RhodesSuspensionSupport sample=f.Samples[k];if(!sample.Hit)continue;
                Vector3 local=Root.InverseTransformPoint(f.WorldHits[k])-f.PivotRoot;
                changed|=!RhodesSharedSuspensionCommit.Same(sample.GroundY,local.y)||!RhodesSharedSuspensionCommit.Same(sample.SampleZ,local.z);
                sample.GroundY=local.y;sample.SampleZ=local.z;f.Samples[k]=sample;WorldHitProjections++;
            }
            if(changed)f.SupportRevision++;
            // These remain actual last hit points, not a plane extrapolated to
            // a new XZ location. Fresh terrain still needs the original rays.
        }
        void CompensateRootRotation(Foot f)
        {
            if(!f.HavePoseHistory||f.Solution.Hits<1||f.Solution.Unsupported)return;
            Quaternion current=Root.rotation;float angle=Quaternion.Angle(f.PoseRootRotation,current);
            if((Root.position-f.PoseRootPosition).sqrMagnitude>625f||angle>10f){f.HavePoseHistory=false;return;}
            // Do not use a tiny Quaternion.Angle gate here: float dot/acos
            // rounds ordinary high-FPS micro-rotations to zero every frame.
            float oldPivotY=(f.PoseRootRotation*f.PivotRoot).y,newPivotY=(current*f.PivotRoot).y;
            float oldUpY=(f.PoseRootRotation*Vector3.up).y,newUpY=(current*Vector3.up).y;
            f.Heave=RhodesSuspensionMath.ReexpressHeaveForRootRotation(f.Heave,oldPivotY,oldUpY,newPivotY,newUpY,f.Limits);
            Vector3 oldForward=f.PoseRootRotation*(Quaternion.AngleAxis(f.Pitch,Vector3.right)*Vector3.forward);
            Vector3 localForward=Quaternion.Inverse(current)*oldForward;
            f.Pitch=RhodesSuspensionMath.ReexpressPitchForRootRotation(f.Pitch,localForward.y,localForward.z,f.Limits);
            RotationFeedForwards++;
            // Express both rotations around the SAME current root position.
            // Normal climb/vertical translation therefore is not cancelled or
            // locked to the old worldY. Fresh terrain residuals still Chase.
        }
        void Apply(Foot f)
        {
            f.Lift.localPosition=f.LiftPosition+Vector3.up*f.Heave;
            f.Bogie.localRotation=f.BogieRotation*Quaternion.AngleAxis(f.Pitch,Vector3.right);
            f.MinOverlap=float.PositiveInfinity;
            foreach(Ram r in f.Rams)
            {
                Vector3 direction=Root.InverseTransformDirection(r.End.position-r.Base.position).normalized;
                r.Barrel.position=r.Base.position;r.Barrel.rotation=Root.rotation*(Quaternion.FromToRotation(r.BarrelAxisRoot,direction)*r.BarrelRootRotation);
                r.Rod.position=r.End.position;r.Rod.rotation=Root.rotation*(Quaternion.FromToRotation(r.RodAxisRoot,-direction)*r.RodRootRotation);
                f.MinOverlap=Math.Min(f.MinOverlap,(StaticV13MainChassis==null?BarrelLength+RodLength:r.AuthoredBarrelLength+r.AuthoredRodLength)-Vector3.Distance(r.Base.position,r.End.position));
                // Each rigid barrel/rod keeps its authored mesh and scale.
            }
            if(f.Hoses!=null)foreach(Hose h in f.Hoses)if(h!=null)ApplyHose(h,f.Heave);
        }
        void ApplyHose(Hose h,float heave)
        {
            for(int k=0;k<9;k++)
            {float t=k/8f;h.Points[k].position=Root.TransformPoint(h.PointRoot[k]+Vector3.up*(t*t*t*heave));}
            for(int k=0;k<8;k++)
            {
                Vector3 delta=h.Points[k+1].position-h.Points[k].position;float length=delta.magnitude;
                h.Segments[k].position=h.Points[k].position;
                h.Segments[k].rotation=Root.rotation*(Quaternion.FromToRotation(h.SegmentAxisRoot[k],Root.InverseTransformDirection(delta).normalized)*h.SegmentRootRotation[k]);
                Vector3 scale=h.SegmentScale[k];scale.y*=length/h.Lengths[k];h.Segments[k].localScale=scale;
                // Only authored flexible display segments scale axially (+Y).
                // This is not a fluid-pressure, elastic-hose or rigid-ram model.
            }
        }
        void UpdateWorkCadence()
        {
            if(Binding==null||!Binding.NativeLodConfigured)
            {ActiveSampleInterval=RhodesSuspensionMath.SampleInterval(CameraDistance);ActivePoseInterval=RhodesSuspensionMath.PoseInterval(CameraDistance);return;}
            int lod=NativeUnitLodAdapter.ResolveLod(NativeUnitLodAdapter.CurrentLod(View),Binding.NativeAlwaysHighLOD);
            bool active=NativeUnitLodAdapter.AnimationActive(lod,Binding.NativeSuspensionActiveLODs,false);
            ActiveSampleInterval=RhodesCarrierLodCadence.SampleInterval(lod,active);
            ActivePoseInterval=RhodesCarrierLodCadence.PoseInterval(lod,active);
            if(lod!=NativeCadenceLod||active!=NativeCadenceActive)
                foreach(Foot foot in Feet){foot.NextSample=Clock;foot.PoseAge=0f;}
            NativeCadenceLod=lod;NativeCadenceActive=active;
        }
        static bool UsableCamera(Camera camera)
        {return camera!=null&&camera.enabled&&camera.gameObject.activeInHierarchy;}
        void UpdateCameraDistance()
        {
            if(!UsableCamera(CameraCache))
            {
                // Sensor toggles can leave an enabled camera on an inactive
                // GameObject. Never retain it as the close-view LOD source.
                CameraCache=null;
                if(Clock>=CameraRetryAt)
                {
                    Camera main=Camera.main;
                    if(UsableCamera(main))CameraCache=main;
                    else foreach(UserCamera user in UnityEngine.Object.FindObjectsOfType(typeof(UserCamera)))
                        if(user!=null&&user.gameObject.activeInHierarchy&&UsableCamera(user.ActiveCamera)){CameraCache=user.ActiveCamera;break;}
                    CameraRetryAt=Clock+2.0;
                }
            }
            CameraDistance=CameraCache==null?float.PositiveInfinity:Vector3.Distance(CameraCache.transform.position,Root.position);
        }
        static float PartRadius(Transform pivot)
        {
            float radius=0f;foreach(Renderer renderer in pivot.GetComponentsInChildren<Renderer>(true))
                for(int k=0;k<8;k++){Vector3 p=RendererCorner(renderer,k);radius=Math.Max(radius,Vector3.Distance(p,pivot.position));}
            if(radius<=0f)throw new InvalidOperationException("Hydraulic rigid part has no render geometry");return radius;
        }
        static Vector3 Corner(Bounds bounds,int k)
        {return bounds.center+new Vector3((k&1)==0?-bounds.extents.x:bounds.extents.x,(k&2)==0?-bounds.extents.y:bounds.extents.y,(k&4)==0?-bounds.extents.z:bounds.extents.z);}
        static Vector3 RendererCorner(Renderer renderer,int k)
        {
            SkinnedMeshRenderer skin=renderer as SkinnedMeshRenderer;
            if(skin!=null)return skin.transform.TransformPoint(Corner(skin.localBounds,k));
            MeshFilter filter=renderer.GetComponent<MeshFilter>();
            if(filter!=null&&filter.sharedMesh!=null)return renderer.transform.TransformPoint(Corner(filter.sharedMesh.bounds,k));
            return Corner(renderer.bounds,k);
        }
        void ExpandVisualBounds()
        {
            OriginalGeometryBounds=Binding.GeometryBounds;OriginalVisualBounds=Binding.VisualBounds;HaveBounds=true;
            Bounds swept=Binding.GeometryBounds;
            foreach(Foot f in Feet)
            {
                foreach(Renderer renderer in f.Lift.GetComponentsInChildren<Renderer>(true))
                {
                    bool pitches=renderer.transform.IsChildOf(f.Bogie)||(f.V14Carrier!=null&&renderer.transform.IsChildOf(f.V14Carrier));float maxY=0f,maxZ=0f;Bounds rest=new Bounds();
                    for(int k=0;k<8;k++)
                    {Vector3 root=Root.InverseTransformPoint(RendererCorner(renderer,k));if(k==0)rest=new Bounds(root,Vector3.zero);else rest.Encapsulate(root);Vector3 local=root-f.PivotRoot;maxY=Math.Max(maxY,Math.Abs(local.y));maxZ=Math.Max(maxZ,Math.Abs(local.z));}
                    double angle=f.Limits.MaxPitchDegrees*Math.PI/180.0;
                    float dy=pitches?(float)(maxY*(1-Math.Cos(angle))+maxZ*Math.Sin(angle)):0f;
                    float dz=pitches?(float)(maxZ*(1-Math.Cos(angle))+maxY*Math.Sin(angle)):0f;
                    Vector3 lo=rest.min,hi=rest.max;lo.y+=f.Limits.MinHeave-dy;hi.y+=f.Limits.MaxHeave+dy;lo.z-=dz;hi.z+=dz;swept.Encapsulate(lo);swept.Encapsulate(hi);
                }
                foreach(Ram r in f.Rams)
                {
                    Vector3 b=Root.InverseTransformPoint(r.Base.position);swept.Encapsulate(b-Vector3.one*r.BarrelRadius);swept.Encapsulate(b+Vector3.one*r.BarrelRadius);
                    Vector3 e=Root.InverseTransformPoint(r.End.position),q=e-f.PivotRoot;double a=f.Limits.MaxPitchDegrees*Math.PI/180.0;
                    float dy=(float)(Math.Abs(q.y)*(1-Math.Cos(a))+Math.Abs(q.z)*Math.Sin(a));float dz=(float)(Math.Abs(q.z)*(1-Math.Cos(a))+Math.Abs(q.y)*Math.Sin(a));
                    Vector3 low=e-Vector3.one*r.RodRadius,high=e+Vector3.one*r.RodRadius;low.y+=f.Limits.MinHeave-dy;high.y+=f.Limits.MaxHeave+dy;low.z-=dz;high.z+=dz;swept.Encapsulate(low);swept.Encapsulate(high);
                }
                if(f.Hoses!=null)foreach(Hose h in f.Hoses)if(h!=null)for(int k=0;k<9;k++)
                {Vector3 low=h.PointRoot[k]-Vector3.one*.3f,high=h.PointRoot[k]+Vector3.one*.3f;low.y+=f.Limits.MinHeave;high.y+=f.Limits.MaxHeave;swept.Encapsulate(low);swept.Encapsulate(high);}
            }
            Binding.GeometryBounds=swept;Bounds visual=Binding.VisualBounds;visual.Encapsulate(swept);Binding.VisualBounds=visual;
            Lod=View.GetComponent<LODGroup>();if(Lod!=null)
            {
                OriginalLodCentre=Lod.localReferencePoint;OriginalLodSize=Lod.size;
                Bounds groupSwept=RhodesCarrierLodCadence.TransformBounds(swept,Lod.transform.worldToLocalMatrix*Root.localToWorldMatrix);
                Lod.localReferencePoint=groupSwept.center;Lod.size=Math.Max(groupSwept.size.x,Math.Max(groupSwept.size.y,groupSwept.size.z));
            }
            // TKR renderer+rootBone move together under Bogie. Its immutable
            // existing shoe-phase localBounds is retained; root swept bounds
            // above include the full rigid parent stroke/pitch. Pick/nav stay.
        }
        bool BudgetParticipant()
        {return Initialized&&Eligible&&View!=null&&Root!=null&&Root.gameObject.activeInHierarchy&&View.gameObject.activeInHierarchy&&Convert.ToInt32(View.Visibility)==2&&!View.ForceIconOnlyMode;}
        bool HasDueVisibleFoot()
        {if(GeneratedSurfaceMode)return false;if(Feet!=null)foreach(Foot f in Feet)if(f.Visible&&Clock>=f.NextSample)return true;return false;}
        void Register()
        {if(Registered)return;Owners.Add(new WeakReference(this));Registered=true;}
        void Unregister()
        {
            for(int i=Owners.Count-1;i>=0;i--){RhodesSuspensionDriver d=Owners[i].Target as RhodesSuspensionDriver;if(d==null||object.ReferenceEquals(d,this)){Owners.RemoveAt(i);if(NextOwner>=i)NextOwner--;}}
            Registered=false;if(Owners.Count==0){NextOwner=0;GlobalBudget.Reset();OwnerReady=new bool[0];}else NextOwner=(NextOwner%Owners.Count+Owners.Count)%Owners.Count;
        }
        static bool ReserveRays(RhodesSuspensionDriver requester)
        {
            if(OwnerReady.Length!=Owners.Count)OwnerReady=new bool[Owners.Count];int requesterIndex=-1;
            for(int n=0;n<Owners.Count;n++)
            {RhodesSuspensionDriver d=Owners[n].Target as RhodesSuspensionDriver;OwnerReady[n]=d!=null&&d.BudgetParticipant()&&d.HasDueVisibleFoot();if(object.ReferenceEquals(d,requester))requesterIndex=n;}
            return GlobalBudget.TryTakeOwned(Time.frameCount,RaysPerFoot,requesterIndex,OwnerReady,ref NextOwner);
        }
        // Gate snapshots remain available when disabled; hidden/FOW/icon
        // states expose no terrain/support counters or sampled ground data.
        public bool TryGetDiagnostics(out RhodesSuspensionDiagnostics result)
        {
            result=new RhodesSuspensionDiagnostics();if(!Initialized||Feet==null)return false;
            result.Eligible=Eligible;result.Shown=LastShown;result.Alive=LastAlive;result.OwnerResearchReady=LastResearchReady;
            result.RootActive=Root!=null&&Root.gameObject.activeInHierarchy;result.ViewActive=View!=null&&View.gameObject.activeInHierarchy;
            result.InterpolatorReady=View!=null&&View.SimStateInterpolator!=null;result.MetricRoot=MetricRoot();
            result.NativeVisible=View!=null&&Convert.ToInt32(View.Visibility)==2;result.IconOnly=View!=null&&View.ForceIconOnlyMode;
            result.CameraName=CameraCache==null?"none":CameraCache.name;result.CameraEnabled=CameraCache!=null&&CameraCache.enabled;
            result.CameraActive=CameraCache!=null&&CameraCache.gameObject.activeInHierarchy;
            result.CameraDistance=CameraDistance;result.RootY=Root==null?float.NaN:Root.position.y;
            result.GroundDatumCalibrated=GroundDatumCalibrated;result.WaitingForNativeGroundUpdate=GroundDatumCalibrated&&Time.frameCount<=GroundDatumApplyFrame;result.AuthoredContactDatum=AuthoredContactDatum;
            result.NativeSettleOffset=View==null?float.NaN:View.SuspensionSettleYOffset;
            result.StepCalls=StepCalls;result.BudgetReady=BudgetParticipant();result.RotationFeedForwards=RotationFeedForwards;result.WorldHitProjections=WorldHitProjections;
            // In a shown root, zero renderer visibility is itself diagnostic.
            // Do not require Eligible here: that would hide this failure mode.
            if(!LastShown||!LastActive||!result.ViewActive||!result.NativeVisible||result.IconOnly)return true;
            result.GeneratedSurfaceMode=GeneratedSurfaceMode;result.AllFootprintsSupported=AllFootprintsSupported;result.NativeSupportTarget=NativeSupportTarget;result.NativeSupportPendingDelta=NativeSupportPendingDelta;result.GeneratedSurfaceQueries=GeneratedSurfaceQueries;result.ContactSafetyProjections=ContactSafetyProjections;
            result.DetailsAvailable=true;result.Feet=Feet.Length;result.Rays=Rays;result.SampleCalls=SampleCalls;result.PoseApplications=PoseApplications;
            result.MinimumRamOverlap=float.PositiveInfinity;
            result.GroundMinimum=result.UnclampedHeaveMinimum=result.TargetHeaveMinimum=result.ActualHeaveMinimum=result.TargetPitchMinimum=result.ActualPitchMinimum=float.PositiveInfinity;
            result.GroundMaximum=result.UnclampedHeaveMaximum=result.TargetHeaveMaximum=result.ActualHeaveMaximum=result.TargetPitchMaximum=result.ActualPitchMaximum=float.NegativeInfinity;
            foreach(Foot f in Feet)
            {
                if(f.Visible)result.VisibleFeet++;
                if(!f.HaveSample)continue;result.SampledFeet++;result.SupportedPoints+=f.Solution.Hits;
                if(f.Solution.HeaveSaturated||f.Solution.PitchSaturated)result.SaturatedFeet++;
                if(f.Solution.HeaveSaturated)result.HeaveSaturatedFeet++;if(f.Solution.PitchSaturated)result.PitchSaturatedFeet++;
                if(f.Solution.Unsupported)result.UnsupportedFeet++;if(f.RateLimited)result.RateLimitedFeet++;
                if(f.SampleAge>ActiveSampleInterval*1.5f)result.StaleFeet++;
                result.OldestSampleSeconds=Math.Max(result.OldestSampleSeconds,f.SampleAge);
                if(f.Residual.Known){result.SampledEnvelopePenetration=Math.Max(result.SampledEnvelopePenetration,f.Residual.Penetration);result.SampledEnvelopeGap=Math.Max(result.SampledEnvelopeGap,f.Residual.Gap);}
                result.MinimumRamOverlap=Math.Min(result.MinimumRamOverlap,f.MinOverlap);
                result.UnclampedHeaveMinimum=Math.Min(result.UnclampedHeaveMinimum,f.Solution.UnclampedHeave);result.UnclampedHeaveMaximum=Math.Max(result.UnclampedHeaveMaximum,f.Solution.UnclampedHeave);
                result.TargetHeaveMinimum=Math.Min(result.TargetHeaveMinimum,f.Solution.TargetHeave);result.TargetHeaveMaximum=Math.Max(result.TargetHeaveMaximum,f.Solution.TargetHeave);
                result.ActualHeaveMinimum=Math.Min(result.ActualHeaveMinimum,f.Heave);result.ActualHeaveMaximum=Math.Max(result.ActualHeaveMaximum,f.Heave);
                result.TargetPitchMinimum=Math.Min(result.TargetPitchMinimum,f.Solution.TargetPitchDegrees);result.TargetPitchMaximum=Math.Max(result.TargetPitchMaximum,f.Solution.TargetPitchDegrees);
                result.ActualPitchMinimum=Math.Min(result.ActualPitchMinimum,f.Pitch);result.ActualPitchMaximum=Math.Max(result.ActualPitchMaximum,f.Pitch);
                foreach(RhodesSuspensionSupport support in f.Samples)if(RhodesSuspensionMath.Valid(support))
                {result.GroundMinimum=Math.Min(result.GroundMinimum,support.GroundY);result.GroundMaximum=Math.Max(result.GroundMaximum,support.GroundY);}
            }
            return true;
        }
        static string Number(float value){return value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);}
        void TrackFirstMovement()
        {
            if(!Runtime.DetailedDiagnosticsEnabled)return;
            if(MovementDiagnosticStarted||Root==null||View==null||View.NonInterpolatedTickState==null)return;
            // Ignore initial placement and sub-metre jitter. Native speed is
            // already cached on this owned view; no reflection or world scan.
            if(!MovementOriginReady||Clock<2.0)
            {MovementOrigin=Root.position;MovementOriginReady=true;return;}
            float speed=Fixed64.UnsafeFloatValue(View.NonInterpolatedTickState.StartOfFrameDynamicState.ForwardSpeed);
            if(!RhodesSuspensionMath.Finite(speed)||Math.Abs(speed)<.1f||(Root.position-MovementOrigin).sqrMagnitude<=1f)return;
            MovementDiagnosticStarted=true;DiagnosticRemaining=Math.Max(DiagnosticRemaining,8);NextDiagnosticAt=0f;
        }
        void EmitDiagnostics()
        {
            if(!Runtime.DetailedDiagnosticsEnabled)return;
            if(DiagnosticTotal>=32)return;
            if(View!=null&&View.Selected&&!UICamera.inputHasFocus&&Input.GetKeyDown(KeyCode.F7)&&
                (Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl))&&
                (Input.GetKey(KeyCode.LeftAlt)||Input.GetKey(KeyCode.RightAlt)))
            {DiagnosticRemaining=8;NextDiagnosticAt=0f;}
            float now=Time.realtimeSinceStartup;if(DiagnosticRemaining<=0||now<NextDiagnosticAt)return;
            DiagnosticRemaining--;DiagnosticTotal++;NextDiagnosticAt=now+3f;
            RhodesSuspensionDiagnostics d;if(!TryGetDiagnostics(out d))return;
            long rayDelta=d.DetailsAvailable?d.Rays-DiagnosticLastRays:0;if(d.DetailsAvailable)DiagnosticLastRays=d.Rays;
            Runtime.Log("RHODES_SUSPENSION_SNAPSHOT entity="+Owner+"; snapshot="+DiagnosticTotal+"/32; stepCalls="+d.StepCalls+
                "; firstMovementWindow="+MovementDiagnosticStarted+"; shown="+d.Shown+"; rootActive="+d.RootActive+"; viewActive="+d.ViewActive+"; alive="+d.Alive+"; researchReady="+d.OwnerResearchReady+
                "; interpolator="+d.InterpolatorReady+"; metricRoot="+d.MetricRoot+"; nativeVisible="+d.NativeVisible+"; iconOnly="+d.IconOnly+
                "; eligible="+d.Eligible+"; budgetReady="+d.BudgetReady+"; camera="+d.CameraName+"; cameraEnabled="+d.CameraEnabled+"; cameraActive="+d.CameraActive+
                "; generatedSurface="+d.GeneratedSurfaceMode+"; allAuthoredFootprints="+d.AllFootprintsSupported+"; supportLiftTarget="+Number(d.NativeSupportTarget)+"; supportLiftPending="+Number(d.NativeSupportPendingDelta)+"; generatedQueries="+d.GeneratedSurfaceQueries+"; safetyProjections="+d.ContactSafetyProjections+
                "; cameraMetres="+Number(d.CameraDistance)+"; rootY="+Number(d.RootY)+"; groundDatumCalibrated="+d.GroundDatumCalibrated+"; waitingForNativeGroundUpdate="+d.WaitingForNativeGroundUpdate+"; authoredDatum="+Number(d.AuthoredContactDatum)+"; nativeSettle="+Number(d.NativeSettleOffset)+"; details="+d.DetailsAvailable+
                "; visibleFeet="+d.VisibleFeet+"/"+d.Feet+"; sampledFeet="+d.SampledFeet+"; rays="+d.Rays+"; raysSinceSnapshot="+rayDelta+
                "; samples="+d.SampleCalls+"; poses="+d.PoseApplications+"; rotationFeedForwards="+d.RotationFeedForwards+"; cachedWorldHitProjections="+d.WorldHitProjections+"; supports="+d.SupportedPoints+"; unsupported="+d.UnsupportedFeet+
                "; heaveSaturated="+d.HeaveSaturatedFeet+"; pitchSaturated="+d.PitchSaturatedFeet+"; stale="+d.StaleFeet+"; rateLimited="+d.RateLimitedFeet+
                "; oldestSeconds="+Number(d.OldestSampleSeconds)+"; sampledGroundLocal="+Number(d.GroundMinimum)+","+Number(d.GroundMaximum)+
                "; unclampedHeave="+Number(d.UnclampedHeaveMinimum)+","+Number(d.UnclampedHeaveMaximum)+
                "; targetHeave="+Number(d.TargetHeaveMinimum)+","+Number(d.TargetHeaveMaximum)+"; actualHeave="+Number(d.ActualHeaveMinimum)+","+Number(d.ActualHeaveMaximum)+
                "; targetPitch="+Number(d.TargetPitchMinimum)+","+Number(d.TargetPitchMaximum)+"; actualPitch="+Number(d.ActualPitchMinimum)+","+Number(d.ActualPitchMaximum)+
                "; envelopePenetration="+Number(d.SampledEnvelopePenetration)+"; envelopeGap="+Number(d.SampledEnvelopeGap)+"; minRamOverlap="+Number(d.MinimumRamOverlap));
        }
        void RestorePoseAndBounds()
        {
            if(Feet!=null)foreach(Foot f in Feet)
            {
                if(f.Lift!=null)f.Lift.localPosition=f.LiftPosition;if(f.Bogie!=null)f.Bogie.localRotation=f.BogieRotation;
                if(f.V14Carrier!=null)f.V14Carrier.localRotation=f.V14CarrierRotation;
                if(f.Rams!=null)foreach(Ram r in f.Rams)if(r!=null){if(r.Barrel!=null){r.Barrel.localPosition=r.BarrelPosition;r.Barrel.localRotation=r.BarrelRotation;}if(r.Rod!=null){r.Rod.localPosition=r.RodPosition;r.Rod.localRotation=r.RodRotation;}}
                if(f.Hoses!=null)foreach(Hose h in f.Hoses)if(h!=null)
                {for(int k=0;k<9;k++)if(h.Points[k]!=null)h.Points[k].localPosition=h.PointPosition[k];for(int k=0;k<8;k++)if(h.Segments[k]!=null){h.Segments[k].localPosition=h.SegmentPosition[k];h.Segments[k].localRotation=h.SegmentRotation[k];h.Segments[k].localScale=h.SegmentScale[k];}}
            }
            if(HaveBounds&&Binding!=null){Binding.GeometryBounds=OriginalGeometryBounds;Binding.VisualBounds=OriginalVisualBounds;}
            if(HaveBounds&&Lod!=null){Lod.localReferencePoint=OriginalLodCentre;Lod.size=OriginalLodSize;}HaveBounds=false;RestoreGroundDatum();
        }
        void Release()
        {SharedCommits=null;Unregister();RestorePoseAndBounds();StaticV13MainChassis=null;StaticV14MainChassis=null;Feet=null;View=null;Binding=null;Root=null;CameraCache=null;Attempted=Initialized=WasShown=Eligible=false;LastFrame=-1;Clock=0;CameraRetryAt=0;NativeCadenceLod=-1;NativeCadenceActive=false;ActiveSampleInterval=.2f;ActivePoseInterval=0f;NextFoot=0;Rays=0;Lod=null;StepCalls=SampleCalls=PoseApplications=DiagnosticLastRays=RotationFeedForwards=WorldHitProjections=0;LastShown=LastAlive=LastResearchReady=LastActive=false;DiagnosticRemaining=4;DiagnosticTotal=0;NextDiagnosticAt=0f;MovementOriginReady=MovementDiagnosticStarted=false;MovementOrigin=Vector3.zero;GroundDatumCalibrated=PreviousSettleCalculated=false;AuthoredContactDatum=CalibratedNativeSettle=PreviousSettleOffset=0f;GroundDatumApplyFrame=-1;GeneratedSurfaceQueries=ContactSafetyProjections=0;GeneratedSurfaceMode=AllFootprintsSupported=TerrainDiagnosticStarted=HaveSupportRoot=false;SupportReset=true;NativeSupportTarget=NativeSupportPendingDelta=0f;SupportPreviousRoot=Vector3.zero;}
        void OnDisable(){Release();}
        void OnDestroy(){Release();}
    }
}
