using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // A menu-only owner. No unit definitions, native UnitViews, battle
    // templates, simulation objects or movement drivers are constructed.
    public sealed class RhodesFleetPreviewModel : IDisposable
    {
        public GameObject Root { get; internal set; }
        public Renderer[] Renderers { get; internal set; }
        public Bounds PresentationBounds { get; internal set; }
        public Bounds[] FramingBounds { get; internal set; }
        internal readonly List<RhodesLoadedModel> Models=new List<RhodesLoadedModel>();

        public void AttachInMetreSpace(Transform parent)
        {
            if(Root==null||parent==null)throw new ArgumentNullException("parent");
            Vector3 scale=parent.lossyScale;
            if(scale.x<=0f||scale.y<=0f||scale.z<=0f||Math.Abs(scale.x-scale.y)>.001f||Math.Abs(scale.x-scale.z)>.001f)
                throw new InvalidDataException("Fleet preview needs an unmirrored uniform-scale wrapper");
            Root.transform.parent=parent;Root.transform.localPosition=Vector3.zero;Root.transform.localRotation=Quaternion.identity;
            Root.transform.localScale=new Vector3(1f/scale.x,1f/scale.y,1f/scale.z);
        }

        public void Dispose()
        {
            // Individual models own material instances and resource leases plus their
            // exact FTK cache entry. Never clear the shared battle caches.
            for(int i=Models.Count-1;i>=0;i--)Models[i].Dispose();
            Models.Clear();
            if(Root!=null)UnityEngine.Object.DestroyImmediate(Root);
            Root=null;Renderers=new Renderer[0];FramingBounds=new Bounds[0];
        }

        public void DisposeForNativeDestroy()
        {
            // Called by the owner component on the native preview wrapper.
            // Native destruction owns the child GameObjects, while we still
            // own the separately allocated mesh/material/texture resources.
            // Release the exact FTK key before clearing its identifying root.
            for(int i=Models.Count-1;i>=0;i--)
            {
                RhodesLoadedModel model=Models[i];FactionTrackLoader.ReleaseModel(model);
                model.Root=null;model.Dispose();
            }
            Models.Clear();Root=null;Renderers=new Renderer[0];FramingBounds=new Bounds[0];
        }
    }

    internal sealed class RhodesFleetCamera
    {
        internal readonly Matrix4x4 WorldToView;
        internal readonly double VerticalTan,Aspect,OrthoSize,Near,Far;
        internal readonly bool Orthographic;

        internal RhodesFleetCamera(Camera camera)
        {
            if(camera==null)throw new ArgumentNullException("camera");
            WorldToView=Matrix4x4.TRS(camera.transform.position,camera.transform.rotation,Vector3.one).inverse;
            VerticalTan=Math.Tan(camera.fieldOfView*Math.PI/360.0);Aspect=camera.aspect;
            OrthoSize=camera.orthographicSize;Near=camera.nearClipPlane;Far=camera.farClipPlane;Orthographic=camera.orthographic;
        }
    }

    internal sealed class RhodesFleetPlacement
    {
        internal readonly string NativeName;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal readonly float Scale,GroundY;
        internal readonly RhodesFleetCamera Camera;

        internal RhodesFleetPlacement(string nativeName,Vector3 position,Quaternion rotation,float scale,float groundY)
            :this(nativeName,position,rotation,scale,groundY,null){}
        internal RhodesFleetPlacement(string nativeName,Vector3 position,Quaternion rotation,float scale,float groundY,RhodesFleetCamera camera)
        {NativeName=nativeName;Position=position;Rotation=rotation;Scale=scale;GroundY=groundY;Camera=camera;}
    }

    public static class RhodesFleetModels
    {
        static readonly string[] Keys={"RI_U01_HARVESTER","RI_U02_FIELD_SURVEY","RI_U03_ENGINEERING","RI_U05_SUPPRESSION",
            "RI_U04_LIGHT_ESCORT","RI_U06_RAILGUN","RI_U07_AIR_DEFENCE","RI_U09_ARTILLERY",
            "RI_U08_TILT_STRIKE","EF_Rhodes_Interceptor_MP","EF_Rhodes_HeavyBomber_MP"};

        // Slots come from the live Coalition preview set in the same order as
        // carrier, then Keys. Former cruiser slots retain our medium geometry.
        internal static readonly string[] NativeSlots={"C_Carrier_MP","C_Harvester_MP","C_Baserunner_MP","C_SupportCruiser_MP",
            "C_HAC_MP","C_Escort_MP","C_Railgun_MP","C_HAC_Upgrade01_MP","C_ArtilleryCruiser_MP",
            "C_GunShip_MP","C_Interceptor_MP","C_Bomber_MP"};

        internal static RhodesFleetPreviewModel Load(Material template,int layer,RhodesFleetPlacement[] placements)
        {
            using(RhodesFleetBuildJob job=BeginWithScheduling(template,layer,placements,false))
            {while(job.Step()){}return job.TakeResult();}
        }
        internal static RhodesFleetBuildJob BeginLoad(Material template,int layer,RhodesFleetPlacement[] placements)
        {return BeginWithScheduling(template,layer,placements,true);}
        static RhodesFleetBuildJob BeginWithScheduling(Material template,int layer,RhodesFleetPlacement[] placements,bool queuedInput)
        {
            if(placements==null||placements.Length!=NativeSlots.Length)throw new ArgumentException("Fleet preview requires all Coalition slots");
            placements=(RhodesFleetPlacement[])placements.Clone();
            for(int i=0;i<placements.Length;i++)
                if(placements[i]==null||placements[i].NativeName!=NativeSlots[i])throw new ArgumentException("Fleet preview slot order is invalid");
            RhodesFleetBuildJob job=new RhodesFleetBuildJob();
            job.Initialize(BuildIncrementally(template,layer,placements,queuedInput,job));return job;
        }
        static IEnumerator BuildIncrementally(Material template,int layer,RhodesFleetPlacement[] placements,bool queuedInput,RhodesFleetBuildJob job)
        {
            if(template==null)throw new ArgumentNullException("template");
            if(layer<0||layer>31)throw new ArgumentOutOfRangeException("layer");
            RhodesFleetPreviewModel result=new RhodesFleetPreviewModel();
            result.Root=new GameObject("EF_Rhodes_FleetPreview");result.Root.SetActive(false);result.Root.layer=layer;
            result.Root.hideFlags=HideFlags.HideAndDontSave;
            List<Renderer> renderers=new List<Renderer>();bool completed=false;
            try
            {
                result.FramingBounds=new Bounds[Keys.Length+1];
                for(int index=-1;index<Keys.Length;index++)
                {
                    bool aircraft=index>=0&&RhodesFleetPreviewPoses.IsAircraft(Keys[index]);
                    string file=index<0?Path.Combine(Path.Combine(CustomUnits.AssetDirectory,"rhodes_v05"),"rhodes_v05.bin"):
                        Path.Combine(Path.Combine(Path.Combine(CustomUnits.AssetDirectory,aircraft?"air_wing_v01":"faction_v01"),Keys[index]),"model.bin");
                    RhodesLoadedModel model;
                    using(RhodesModelBuildJob build=RhodesModelBuilder.BeginLoad(file,template,layer,index>=0,queuedInput))
                    {
                        while(StepPreservingTrackTemplate(build))yield return build.IsWaitingForInput?RhodesBuildSignal.WaitingForInput:null;
                        model=build.TakeResult();
                    }
                    result.Models.Add(model);model.AttachInMetreSpace(result.Root.transform);
                    if(model.LodGroup!=null)model.LodGroup.ForceLOD(1);model.Root.SetActive(true);
                    if(aircraft)RhodesFleetPreviewPoses.ApplyDeckReady(model,file);
                    RhodesFleetPlacement placement=placements[index+1];
                    Bounds placed=Place(model,placement);result.FramingBounds[index+1]=placed;
                    if(index<0)result.PresentationBounds=placed;
                    else{Bounds aggregate=result.PresentationBounds;aggregate.Encapsulate(placed);result.PresentationBounds=aggregate;}
                    renderers.AddRange(model.Renderers);
                    Runtime.Log("RHODES_FLEET_PREVIEW_NATIVE_SLOT key="+(index<0?"RhodesCarrier":Keys[index])+"; native="+placement.NativeName+
                        "; position="+placement.Position+"; rotation="+placement.Rotation+"; nativeScale="+placement.Scale+
                        "; displayScale="+model.Root.transform.localScale.x+"; groundY="+placement.GroundY+"; placedBounds="+placed);
                    job.ModelsCompleted=index+2;yield return null;
                }
                result.Renderers=renderers.ToArray();
                Runtime.Log("RHODES_FLEET_PREVIEW_MODELS_READY models="+(Keys.Length+1)+"; layout=LiveCoalitionSlots; nativeIndividualRotations=True; nativeRootScales=True; independentOversizeClamp=True; viewportMargin=.02; aircraftGrounded=True"+
                    "; aircraftDeckReadySourceFrame=45; poseBoundsFromAuthoredVertices=True; rigidAndSkinnedPreviewCompatible=True; allModelsActualMetres=True; aggregateRescale=False; gameplayScaleChanged=False; nativeTeamColours="+RhodesTeamColours.CustomColoursEnabled+"; staticLOD=1; registryRequired=False; battleCachePreserved=True; incrementalBuild=True; runtimeVisualAcceptancePending=True");
                job.Publish(result);completed=true;
            }
            finally{if(!completed)result.Dispose();}
        }
        static bool StepPreservingTrackTemplate(RhodesModelBuildJob job)
        {
            object snapshot=RhodesTrackLoader.CaptureTemplate();
            try{return job.Step();}finally{RhodesTrackLoader.RestoreTemplate(snapshot);}
        }

        internal static Bounds Place(RhodesLoadedModel model,RhodesFleetPlacement placement)
        {
            if(model==null||model.Root==null||placement==null)throw new ArgumentNullException("model/placement");
            Bounds original=model.PresentationBounds;
            if(placement.Scale<=0f||float.IsNaN(placement.Scale)||float.IsInfinity(placement.Scale)||
                original.size.x<=0f||original.size.y<=0f||original.size.z<=0f)
                throw new InvalidDataException("Invalid fleet preview model bounds or native scale");
            float scale=placement.Scale*ViewportScale(model,placement);
            Bounds rotated=RotatedBounds(original,placement.Rotation,scale);
            // Native pivots vary by unit. Align our own footprint centre and
            // bottom to its slot and the authored showroom platform plane.
            Vector3 offset=new Vector3(placement.Position.x-rotated.center.x,placement.GroundY-rotated.min.y,
                placement.Position.z-rotated.center.z);
            model.Root.transform.localRotation=placement.Rotation;
            model.Root.transform.localScale=Vector3.one*scale;
            model.Root.transform.localPosition=offset;
            return new Bounds(rotated.center+offset,rotated.size);
        }

        static Bounds RotatedBounds(Bounds original,Quaternion rotation,float scale)
        {
            Matrix4x4 pose=Matrix4x4.TRS(Vector3.zero,rotation,Vector3.one*scale);
            Bounds rotated=new Bounds();bool first=true;
            for(int corner=0;corner<8;corner++)
            {
                Vector3 point=pose.MultiplyPoint3x4(new Vector3((corner&1)==0?original.min.x:original.max.x,
                    (corner&2)==0?original.min.y:original.max.y,(corner&4)==0?original.min.z:original.max.z));
                if(float.IsNaN(point.sqrMagnitude)||float.IsInfinity(point.sqrMagnitude))throw new InvalidDataException("Nonfinite fleet preview pose");
                if(first){rotated=new Bounds(point,Vector3.zero);first=false;}else rotated.Encapsulate(point);
            }
            return rotated;
        }

        static float ViewportScale(RhodesLoadedModel model,RhodesFleetPlacement placement)
        {
            RhodesFleetCamera camera=placement.Camera;if(camera==null)return 1f;
            Bounds source=model.PresentationBounds;
            Matrix4x4 rootToView=camera.WorldToView*Matrix4x4.TRS(Vector3.zero,placement.Rotation,Vector3.one*placement.Scale);
            Bounds rotated=RotatedBounds(source,placement.Rotation,placement.Scale);
            Vector3 viewPivot=camera.WorldToView.MultiplyVector(new Vector3(rotated.center.x,rotated.min.y,rotated.center.z));
            Vector3 anchor=camera.WorldToView.MultiplyPoint3x4(new Vector3(placement.Position.x,placement.GroundY,placement.Position.z));
            List<double> offsets=new List<double>();
            AddBoundsOffsets(source,Matrix4x4.identity,rootToView,viewPivot,offsets);
            if(FitScale(camera,anchor,offsets)>=1f)return 1f;
            offsets.Clear();Matrix4x4 worldToRoot=model.Root.transform.worldToLocalMatrix;
            foreach(RhodesMesh mesh in model.Package.Meshes)
            {
                Transform node=model.Nodes[mesh.Node];
                if((mesh.Lod!=1&&mesh.Lod!=-1)||!VisibleWithinModel(node,model.Root.transform))continue;
                Bounds local=new Bounds();bool first=true;
                for(int at=0;at<mesh.Vertices.Length;at+=10)
                {
                    Vector3 point=new Vector3(mesh.Vertices[at],mesh.Vertices[at+1],mesh.Vertices[at+2]);
                    if(first){local=new Bounds(point,Vector3.zero);first=false;}else local.Encapsulate(point);
                }
                AddBoundsOffsets(local,worldToRoot*node.localToWorldMatrix,rootToView,viewPivot,offsets);
            }
            foreach(Renderer renderer in model.Renderers)
            {
                if(renderer==null||!VisibleWithinModel(renderer.transform,model.Root.transform)||
                    renderer.GetComponent<RhodesRigidBatchTag>()!=null||renderer.GetComponent<RhodesCarrierBatchTag>()!=null)continue;
                MeshFilter filter=renderer.GetComponent<MeshFilter>();SkinnedMeshRenderer skin=renderer as SkinnedMeshRenderer;
                if(filter!=null&&filter.sharedMesh!=null&&filter.sharedMesh.name.StartsWith("EF_RHS2_",StringComparison.Ordinal))continue;
                Bounds local;
                if(filter!=null&&filter.sharedMesh!=null)local=filter.sharedMesh.bounds;
                else if(skin!=null&&skin.sharedMesh!=null)local=skin.localBounds;
                else continue;
                Matrix4x4 toRoot=worldToRoot*renderer.transform.localToWorldMatrix;
                AddBoundsOffsets(local,toRoot,rootToView,viewPivot,offsets);
            }
            return Math.Min(1f,FitScale(camera,anchor,offsets));
        }

        static bool VisibleWithinModel(Transform node,Transform root)
        {
            for(Transform current=node;current!=root;current=current.parent)
            {if(current==null)throw new InvalidDataException("Preview geometry belongs to another model");if(!current.gameObject.activeSelf)return false;}
            return true;
        }

        static void AddBoundsOffsets(Bounds bounds,Matrix4x4 toRoot,Matrix4x4 rootToView,Vector3 viewPivot,List<double> result)
        {
            for(int corner=0;corner<8;corner++)
            {
                Vector3 point=toRoot.MultiplyPoint3x4(new Vector3((corner&1)==0?bounds.min.x:bounds.max.x,
                    (corner&2)==0?bounds.min.y:bounds.max.y,(corner&4)==0?bounds.min.z:bounds.max.z));
                point=rootToView.MultiplyVector(point)-viewPivot;
                result.Add(point.x);result.Add(point.y);result.Add(point.z);
            }
        }

        static float FitScale(RhodesFleetCamera camera,Vector3 anchor,List<double> offsets)
        {
            double fit=RhodesPreviewFraming.FitFixedScale(anchor.x,anchor.y,anchor.z,offsets.ToArray(),camera.VerticalTan,camera.Aspect,
                camera.OrthoSize,camera.Orthographic,camera.Near,camera.Far,.02,.98,.02,.98);
            if(fit<=0||double.IsNaN(fit)||double.IsInfinity(fit))throw new InvalidDataException("Native preview slot cannot fit its visible geometry");
            return (float)fit;
        }
    }
}
