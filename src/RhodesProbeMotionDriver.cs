using System;
using BBI.Core.ComponentModel;
using BBI.Game.Simulation;
using BBI.Unity.Game.World;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Explicit host Step only. Each live entity owns one SMALL dynamic wire mesh;
    // the large hull/track meshes and all native simulation state stay untouched.
    public sealed class RhodesProbeMotionDriver:MonoBehaviour
    {
        UnitView View;
        RhodesCarrierBinding Binding;
        Entity Owner;
        Transform Root,Pivot;
        MeshFilter Filter;
        Renderer WireRenderer;
        Mesh TemplateMesh,LiveMesh;
        RhodesProbePackage Package;
        Vector3[] Positions,Normals;
        Vector3 RestPivotPosition;
        Quaternion RestPivotRotation;
        bool Attempted,Initialized,HaveRestPose;
        double Phase;
        int LastFrame=-1;
        public void Initialize(UnitView view,RhodesCarrierBinding binding)
        {
            if(view==null||view.EntityID.Equals(Entity.None))return;
            Transform root=binding==null?null:binding.GeometryRoot;
            if(Attempted&&View==view&&Root==root&&Owner.Equals(view.EntityID))return;
            ReleaseLive();View=view;Binding=binding;Root=root;Owner=view.EntityID;Attempted=true;Initialized=false;Phase=0;LastFrame=-1;
            try
            {
                if(binding==null||!binding.IsV05||root==null||root==view.transform||!root.IsChildOf(view.transform)||binding.Geometry==null)throw new InvalidOperationException("Probe live root is outside its current native owner");
                foreach(Renderer candidate in binding.Geometry)
                {
                    if(candidate==null||!RhodesProbeLoader.IsCableName(candidate.name))continue;
                    MeshFilter filter=candidate.GetComponent<MeshFilter>();RhodesProbePackage package;
                    if(filter==null||!RhodesProbeLoader.TryGetPackage(filter.sharedMesh,out package)||candidate.name!=package.CableName)throw new InvalidOperationException("Probe cable/template cache identity mismatch");
                    if(Filter!=null)throw new InvalidOperationException("Probe live cable renderer is not unique");
                    if(!candidate.transform.IsChildOf(root)||filter.transform!=candidate.transform)throw new InvalidOperationException("Probe cable renderer escaped the live owner subtree");
                    Filter=filter;WireRenderer=candidate;TemplateMesh=filter.sharedMesh;Package=package;
                }
                if(Filter==null||Package==null)throw new InvalidOperationException("Exact live probe cable/template cache was not found");
                Pivot=binding.FindNode(Package.PivotName);
                if(Pivot==null||Pivot==root||!Pivot.IsChildOf(root))throw new InvalidOperationException("Probe authored pivot escaped the live owner subtree");
                if(TemplateMesh.vertexCount!=Package.VertexCount)throw new InvalidOperationException("Probe template vertex layout changed");
                RestPivotPosition=Pivot.localPosition;RestPivotRotation=Pivot.localRotation;HaveRestPose=true;
                LiveMesh=UnityEngine.Object.Instantiate(TemplateMesh) as Mesh;
                if(LiveMesh==null||LiveMesh==TemplateMesh)throw new InvalidOperationException("Probe dynamic mesh was not independently cloned");
                LiveMesh.name="EF_RPA_Live_"+Owner;LiveMesh.hideFlags=HideFlags.HideAndDontSave;LiveMesh.MarkDynamic();LiveMesh.bounds=RhodesProbeLoader.WireBounds(Package);
                Positions=new Vector3[Package.VertexCount];Normals=new Vector3[Package.VertexCount];Filter.sharedMesh=LiveMesh;Initialized=true;ApplyFrame(0,1,0f,true);
                Runtime.Log("RHODES_PROBE_RIG_BOUND entity="+Owner+"; frames="+Package.Frames.Length+"; duration="+Package.Duration+"; wireVertices="+Package.VertexCount+"; immutableTemplateMeshKey=True; privateDynamicMesh=True; liveSubtreeOwnershipVerified=True; sourceAuthoredPoseAndCable=True; hullAndTrackVerticesChanged=False; runtimeVisualAcceptancePending=True");
            }
            catch(Exception error)
            {ReleaseLive();Runtime.Log("RHODES_PROBE_BIND_FAILED entity="+Owner+"; attemptedOnceForEntityRoot=True; error="+error.Message);}
        }
        public void Step(UnitView view,bool shown)
        {
            if(view==null)return;
            if(!Attempted||View!=view||!Owner.Equals(view.EntityID)){if(Binding!=null)Initialize(view,Binding);else return;}
            if(!Initialized||LastFrame==Time.frameCount)return;LastFrame=Time.frameCount;
            float delta=Runtime.PresentationDeltaTime;
            if(!shown||float.IsNaN(delta)||float.IsInfinity(delta)||delta<=0f||Binding==null||Binding.GeometryRoot!=Root||Root==null||!Root.IsChildOf(view.transform)||!Root.gameObject.activeInHierarchy||Pivot==null||!Pivot.IsChildOf(Root)||Filter==null||!Filter.transform.IsChildOf(Root)||Filter.sharedMesh!=LiveMesh||WireRenderer==null||WireRenderer.transform!=Filter.transform||view.SimStateInterpolator==null)return;
            UnitState state=view.NonInterpolatedTickState;if(state==null||state.UnitIsDead)return;
            Phase=(Phase+delta)%Package.Duration;double sample=Phase*Package.FramesPerSecond;
            int a=(int)Math.Floor(sample);float blend=(float)(sample-a);
            if(a>=Package.Frames.Length-1){a=Package.Frames.Length-2;blend=1f;}
            ApplyFrame(a,a+1,blend,WireRenderer!=null&&WireRenderer.isVisible);
        }
        void ApplyFrame(int first,int second,float blend,bool wireVisible)
        {
            if(Pivot==null||LiveMesh==null||Package==null)return;
            RhodesProbeFrame a=Package.Frames[first],b=Package.Frames[second];float[] ap=a.Pose,bp=b.Pose;
            Pivot.localPosition=Vector3.Lerp(new Vector3(ap[0],ap[1],ap[2]),new Vector3(bp[0],bp[1],bp[2]),blend);
            Pivot.localRotation=Quaternion.Slerp(new Quaternion(ap[3],ap[4],ap[5],ap[6]),new Quaternion(bp[3],bp[4],bp[5],bp[6]),blend);
            if(!wireVisible)return;
            for(int i=0;i<Positions.Length;i++)
            {
                int at=i*6;float[] av=a.Wire,bv=b.Wire;
                Positions[i]=new Vector3(Mathf.Lerp(av[at],bv[at],blend),Mathf.Lerp(av[at+1],bv[at+1],blend),Mathf.Lerp(av[at+2],bv[at+2],blend));
                Vector3 normal=new Vector3(Mathf.Lerp(av[at+3],bv[at+3],blend),Mathf.Lerp(av[at+4],bv[at+4],blend),Mathf.Lerp(av[at+5],bv[at+5],blend));
                Normals[i]=normal.sqrMagnitude>1e-10f?normal.normalized:new Vector3(av[at+3],av[at+4],av[at+5]).normalized;
            }
            LiveMesh.vertices=Positions;LiveMesh.normals=Normals;
        }
        void ReleaseLive()
        {
            if(Filter!=null&&LiveMesh!=null&&Filter.sharedMesh==LiveMesh)Filter.sharedMesh=TemplateMesh;
            if(HaveRestPose&&Pivot!=null){Pivot.localPosition=RestPivotPosition;Pivot.localRotation=RestPivotRotation;}
            if(LiveMesh!=null)UnityEngine.Object.Destroy(LiveMesh);
            LiveMesh=null;TemplateMesh=null;Filter=null;WireRenderer=null;Package=null;Pivot=null;Positions=null;Normals=null;HaveRestPose=false;Initialized=false;
        }
        void OnDisable(){LastFrame=-1;}
        void OnDestroy(){ReleaseLive();}
    }
}
