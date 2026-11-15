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

    public static class RhodesFleetModels
    {
        static readonly string[] Keys={"RI_U01_HARVESTER","RI_U02_FIELD_SURVEY","RI_U03_ENGINEERING","RI_U05_SUPPRESSION",
            "RI_U04_LIGHT_ESCORT","RI_U06_RAILGUN","RI_U07_AIR_DEFENCE","RI_U09_ARTILLERY",
            "RI_U08_TILT_STRIKE","EF_Rhodes_Interceptor_MP","EF_Rhodes_HeavyBomber_MP"};

        public static RhodesFleetPreviewModel Load(Material template,int layer,Vector3 viewRightLocal,Vector3 foregroundLocal)
        {
            using(RhodesFleetBuildJob job=BeginWithScheduling(template,layer,viewRightLocal,foregroundLocal,false))
            {while(job.Step()){}return job.TakeResult();}
        }
        public static RhodesFleetBuildJob BeginLoad(Material template,int layer,Vector3 viewRightLocal,Vector3 foregroundLocal)
        {return BeginWithScheduling(template,layer,viewRightLocal,foregroundLocal,true);}
        static RhodesFleetBuildJob BeginWithScheduling(Material template,int layer,Vector3 viewRightLocal,Vector3 foregroundLocal,bool queuedInput)
        {
            RhodesFleetBuildJob job=new RhodesFleetBuildJob();
            job.Initialize(BuildIncrementally(template,layer,viewRightLocal,foregroundLocal,queuedInput,job));return job;
        }
        static IEnumerator BuildIncrementally(Material template,int layer,Vector3 viewRightLocal,Vector3 foregroundLocal,bool queuedInput,RhodesFleetBuildJob job)
        {
            if(template==null)throw new ArgumentNullException("template");
            if(layer<0||layer>31)throw new ArgumentOutOfRangeException("layer");
            ValidateDirections(ref viewRightLocal,ref foregroundLocal);
            RhodesFleetPreviewModel result=new RhodesFleetPreviewModel();
            result.Root=new GameObject("EF_Rhodes_FleetPreview");result.Root.SetActive(false);result.Root.layer=layer;
            result.Root.hideFlags=HideFlags.HideAndDontSave;
            List<Renderer> renderers=new List<Renderer>();bool completed=false;
            try
            {
                RhodesLoadedModel[] vehicles=new RhodesLoadedModel[Keys.Length];
                Bounds[] scaledBounds=new Bounds[Keys.Length];Bounds bounds=new Bounds();
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
                    if(index<0)
                    {bounds=Place(model,Vector3.zero,1f);result.PresentationBounds=bounds;result.FramingBounds[0]=bounds;renderers.AddRange(model.Renderers);}
                    else
                    {
                        vehicles[index]=model;if(aircraft)RhodesFleetPreviewPoses.ApplyDeckReady(model,file);
                        scaledBounds[index]=ScaledBounds(model.PresentationBounds,1f);
                    }
                    job.ModelsCompleted=index+2;yield return null;
                }
                Vector3[] positions=ComputeLayout(bounds,scaledBounds,viewRightLocal,foregroundLocal);
                for(int i=0;i<Keys.Length;i++)
                {
                    Bounds placed=Place(vehicles[i],positions[i],1f);result.FramingBounds[i+1]=placed;
                    Bounds aggregate=result.PresentationBounds;aggregate.Encapsulate(placed);result.PresentationBounds=aggregate;renderers.AddRange(vehicles[i].Renderers);
                }
                result.Renderers=renderers.ToArray();
                Runtime.Log("RHODES_FLEET_PREVIEW_MODELS_READY models="+(Keys.Length+1)+"; cameraAlignedForegroundRows=True; rows=4_4_3; individualDisplayScale=1; aircraftGrounded=True"+
                    "; aircraftDeckReadySourceFrame=45; poseBoundsFromUploadedMeshBounds=True; allModelsActualMetres=True; aggregateCameraFitOnly=True; gameplayScaleChanged=False; nativeTeamColours="+RhodesTeamColours.CustomColoursEnabled+"; staticLOD=1; registryRequired=False; battleCachePreserved=True; incrementalBuild=True; runtimeVisualAcceptancePending=True");
                job.Publish(result);completed=true;
            }
            finally{if(!completed)result.Dispose();}
        }
        static bool StepPreservingTrackTemplate(RhodesModelBuildJob job)
        {
            object snapshot=RhodesTrackLoader.CaptureTemplate();
            try{return job.Step();}finally{RhodesTrackLoader.RestoreTemplate(snapshot);}
        }

        static void ValidateDirections(ref Vector3 right,ref Vector3 foreground)
        {
            if(float.IsNaN(right.sqrMagnitude+foreground.sqrMagnitude)||float.IsInfinity(right.sqrMagnitude+foreground.sqrMagnitude)||
                Math.Abs(right.y)>.001f||Math.Abs(foreground.y)>.001f||right.sqrMagnitude<.0001f||foreground.sqrMagnitude<.0001f)
                throw new ArgumentException("Preview layout needs two finite ground-plane directions");
            right.Normalize();foreground.Normalize();
            if(Math.Abs(Vector3.Dot(right,foreground))>.001f)throw new ArgumentException("Preview ground-plane directions must be orthogonal");
        }

        static float HalfSupport(Bounds bounds,Vector3 direction)
        {return Math.Abs(direction.x)*bounds.extents.x+Math.Abs(direction.z)*bounds.extents.z;}

        // Pure geometry used by Load. Row separating planes keep every
        // vehicle beyond the hull footprint; column/row supports account for
        // each model's actual (possibly preview-scaled) AABB dimensions.
        internal static Vector3[] ComputeLayout(Bounds hull,Bounds[] vehicles,Vector3 right,Vector3 foreground)
        {
            if(vehicles==null||vehicles.Length<1||vehicles.Length>16)throw new ArgumentException("Fleet preview requires one to sixteen vehicle bounds");
            ValidateDirections(ref right,ref foreground);
            ScaledBounds(hull,1f);
            float hullRight=HalfSupport(hull,right),hullFront=HalfSupport(hull,foreground);
            float gap=Math.Max(10f,hullRight*.05f);
            float[] halfWidth=new float[vehicles.Length];
            int rowCount=(vehicles.Length+3)/4;
            float[] rowHalfDepth=new float[rowCount];
            for(int i=0;i<vehicles.Length;i++)
            {
                ScaledBounds(vehicles[i],1f);
                halfWidth[i]=HalfSupport(vehicles[i],right);
                rowHalfDepth[i/4]=Math.Max(rowHalfDepth[i/4],HalfSupport(vehicles[i],foreground));
            }
            float rowDistance=hullFront+gap+rowHalfDepth[0];
            Vector3 centre=new Vector3(hull.center.x,0f,hull.center.z);
            Vector3[] result=new Vector3[vehicles.Length];
            for(int row=0;row<rowCount;row++)
            {
                int columns=Math.Min(4,vehicles.Length-row*4);
                if(row>0)rowDistance=rowDistance+rowHalfDepth[row-1]+gap+rowHalfDepth[row];
                float itemWidth=0f;for(int column=0;column<columns;column++)itemWidth+=2f*halfWidth[row*4+column];
                float span=Math.Max(1.6f*hullRight,itemWidth+(columns-1)*gap);
                float spacing=columns>1?(span-itemWidth)/(columns-1):0f;
                float cursor=columns>1?-span*.5f:-halfWidth[row*4];
                for(int column=0;column<columns;column++)
                {
                    int i=row*4+column;cursor+=halfWidth[i];
                    result[i]=centre+right*cursor+foreground*rowDistance;
                    cursor+=halfWidth[i]+spacing;
                }
            }
            return result;
        }

        static Bounds ScaledBounds(Bounds original,float displayScale)
        {
            if(displayScale<=0f||float.IsNaN(displayScale)||float.IsInfinity(displayScale)||
                original.size.x<=0f||original.size.y<=0f||original.size.z<=0f||float.IsNaN(original.extents.sqrMagnitude)||float.IsInfinity(original.extents.sqrMagnitude))
                throw new InvalidDataException("Invalid fleet preview model bounds");
            return new Bounds(original.center*displayScale,original.size*displayScale);
        }

        static Bounds Place(RhodesLoadedModel model,Vector3 position,float displayScale)
        {
            Bounds original=ScaledBounds(model.PresentationBounds,displayScale);
            // The aggregate root has unit world scale while models are placed.
            // This changes only private menu geometry, never the package or
            // a battle template. Ground remains y=position.y for every scale.
            model.Root.transform.localScale=Vector3.one*displayScale;
            Vector3 offset=PlacementOffset(original,position);
            model.Root.transform.localPosition=offset;
            return new Bounds(original.center+offset,original.size);
        }
        internal static Vector3 PlacementOffset(Bounds original,Vector3 position)
        {return new Vector3(position.x-original.center.x,position.y-original.min.y,position.z-original.center.z);}
    }
}
