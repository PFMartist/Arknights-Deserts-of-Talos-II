using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BBI.Unity.Game.UI;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static class RhodesCustomizationPreview
    {
        // Reuse the existing Blackbird panel-initialization callback. No new
        // assembly hooks, scene searches, battle Update or registry dependency.
        public static void PreparePanel(object item)
        {
            PlayerCustomizationPanel panel=item as PlayerCustomizationPanel;
            if(!Runtime.SkirmishMode||panel==null||panel.GetComponent<RhodesCustomizationPreviewDriver>()!=null)return;
            panel.gameObject.AddComponent<RhodesCustomizationPreviewDriver>();
        }
    }

    public sealed class RhodesCustomizationPreviewOwner:MonoBehaviour
    {
        internal RhodesFleetPreviewModel Loaded;
        internal Action<RhodesFleetPreviewModel> NativeDestroyed;
        void OnDestroy()
        {
            if(Loaded==null)return;
            RhodesFleetPreviewModel loaded=Loaded;Loaded=null;
            // The parent is already being destroyed by native preview cleanup;
            // release owned meshes/textures without recursively destroying it.
            try{if(NativeDestroyed!=null)NativeDestroyed(loaded);}
            finally{NativeDestroyed=null;loaded.DisposeForNativeDestroy();}
        }
    }

    public sealed class RhodesCustomizationPreviewDriver:MonoBehaviour
    {
        PlayerCustomizationPanel Panel;
        object Scene;
        PlayerCustomizationPreviewController Controller;
        GameObject Wrapper;
        RhodesFleetPreviewModel PreviewModel;
        RhodesCustomizationPreviewOwner ViewOwner;
        RhodesFleetPreviewModel CachedModel;
        object CacheScene;
        PlayerCustomizationPreviewController CacheController;
        Material CacheMaterial;
        int CacheLayer;
        GameObject ParkingRoot;
        int CacheReports;
        RhodesFleetBuildJob PendingLoad;
        object PendingScene;PlayerCustomizationPreviewController PendingController;Material PendingMaterial;int PendingLayer;
        System.Diagnostics.Stopwatch PendingWatch;int PendingFrames,PendingSteps;
        UILabel LoadingLabel;
        RhodesFleetTab Tab;
        bool InitialContextEvaluated;
        readonly List<GameObject> Hidden=new List<GameObject>();
        float NextCheck;
        bool Failed;
        bool Registered;

        void OnEnable(){Panel=GetComponent<PlayerCustomizationPanel>();NextCheck=0f;Failed=false;InitialContextEvaluated=false;CacheReports=0;}
        void OnDisable(){Release(false);ReleaseTab();}
        void OnDestroy(){Release(false);ReleaseTab();}

        void Update()
        {
            // Only this active menu panel checks explicit native references.
            // Colour changes themselves use the original event-driven setter.
            if(Failed)return;
            try
            {
                if(PendingLoad!=null){PumpPendingLoad();if(PendingLoad!=null)return;NextCheck=0f;}
                if(Time.realtimeSinceStartup<NextCheck)return;NextCheck=Time.realtimeSinceStartup+.25f;
                if(Panel==null){Release(false);ReleaseTab();return;}
                if(Tab==null)Tab=RhodesFleetTab.TryCreate(Panel,OnPageChanged);
                if(Tab==null)return;
                object content=NativeRead.Get(Panel,"mContentManager");
                object scene=content==null?null:NativeRead.Get(content,"Scene");
                if(scene==null){Release(false);return;}
                PlayerCustomizationPreviewController controller=NativeRead.Get(scene,"mPreviewController") as PlayerCustomizationPreviewController;
                if(controller==null){Release(false);return;}
                if(CachedModel!=null&&(!ReferenceEquals(scene,CacheScene)||CacheController!=controller))Release(false);
                // Wait for native scene initialization/restore before choosing
                // the skirmish slot's default page. An earlier selection could
                // otherwise be overwritten by native asynchronous loading.
                if(!InitialContextEvaluated)
                {InitialContextEvaluated=true;if(RhodesSkirmishFaction.IsRhodesCustomization(Panel))Tab.Select();}
                if(!Tab.Selected){Release(true);return;}
                List<GameObject> instances=NativeRead.Get(scene,"mInstancedPreviews") as List<GameObject>;
                if(Wrapper!=null&&ReferenceEquals(scene,Scene)&&instances!=null&&instances.Contains(Wrapper))return;
                Release(true);Build(scene,controller);
            }
            catch(Exception error)
            {
                Release(false);Failed=true;
                Runtime.Log("RHODES_CUSTOMIZATION_PREVIEW_FAILED originalPreviewRetained=True; "+error);
            }
        }

        void OnPageChanged(bool selected)
        {
            Release(true);NextCheck=0f;
            if(selected)Failed=false;
        }
        void ReleaseTab(){if(Tab!=null)Tab.Dispose();Tab=null;}

        void Build(object scene,PlayerCustomizationPreviewController controller)
        {
            System.Diagnostics.Stopwatch buildTime=System.Diagnostics.Stopwatch.StartNew();
            GameObject spawn=NativeRead.Get(scene,"mCustomSpawnPoint") as GameObject;
            List<GameObject> instances=NativeRead.Get(scene,"mInstancedPreviews") as List<GameObject>;
            List<GameObject> overridden=NativeRead.Get(scene,"mOverridenGameObjects") as List<GameObject>;
            Array sets=NativeRead.Get(controller,"mModelSets") as Array;
            Camera camera=NativeRead.Get(scene,"mPreviewCamera") as Camera;
            if(spawn==null||instances==null||overridden==null||sets==null||camera==null)return;
            List<GameObject> candidates=new List<GameObject>();
            foreach(object set in sets)
            {
                IEnumerable models=NativeRead.Get(set,"Models") as IEnumerable;if(models==null)continue;
                foreach(object model in models)
                {GameObject root=NativeRead.Get(model,"Model") as GameObject;if(root!=null&&!candidates.Contains(root))candidates.Add(root);}
            }
            foreach(GameObject root in instances)if(root!=null&&!candidates.Contains(root))candidates.Add(root);
            List<GameObject> originals=new List<GameObject>();Material material=null;
            float largest=0f;int layer=spawn.layer;
            foreach(GameObject root in candidates)
            {
                    if(root==null||!root.activeInHierarchy)continue;
                    bool any=false;Bounds bounds=new Bounds();Material candidate=null;int candidateLayer=layer;
                    foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if(!(renderer is MeshRenderer)&&!(renderer is SkinnedMeshRenderer))continue;
                        if(!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;
                        if(!any){bounds=renderer.bounds;any=true;}else bounds.Encapsulate(renderer.bounds);
                        Material source=renderer.sharedMaterial;
                        if(candidate==null&&source!=null&&source.shader!=null&&source.shader.isSupported&&source.HasProperty("_OverlayMask"))
                        {candidate=source;candidateLayer=renderer.gameObject.layer;}
                    }
                    if(!any)continue;
                    originals.Add(root);
                    float size=bounds.extents.sqrMagnitude;
                    if(candidate!=null&&size>largest)
                    {largest=size;material=candidate;layer=candidateLayer;}
            }
            if(material==null)return;
            Scene=scene;Controller=controller;
            RhodesFleetPreviewModel loaded=null;bool owned=false;
            try
            {
                RhodesFleetPlacement[] placements=CaptureCoalitionPlacements(sets,spawn.transform.position.y,camera);
                bool cacheHit;
                System.Diagnostics.Stopwatch modelTime=System.Diagnostics.Stopwatch.StartNew();
                loaded=AcquireModel(scene,controller,material,layer,placements,out cacheHit);
                modelTime.Stop();if(loaded==null)return;
                Wrapper=new GameObject("EF_Rhodes_CustomizationPreview");Wrapper.layer=layer;
                Wrapper.transform.parent=spawn.transform;
                // Models already use captured native world-space slot poses.
                // Cancel the scaled native spawn container in AttachInMetreSpace.
                Wrapper.transform.position=Vector3.zero;
                Wrapper.transform.rotation=Quaternion.identity;Wrapper.transform.localScale=Vector3.one;
                loaded.AttachInMetreSpace(Wrapper.transform);
                RhodesCustomizationPreviewOwner owner=Wrapper.AddComponent<RhodesCustomizationPreviewOwner>();
                owner.Loaded=loaded;owner.NativeDestroyed=OnNativeModelDestroyed;ViewOwner=owner;owned=true;
                PreviewModel=loaded;
                loaded.Root.SetActive(true);
                Type viewType=typeof(PlayerCustomizationPreviewController).Assembly.GetType("BBI.Unity.Game.UI.PlayerCustomizationUnitView",true);
                Component view=Wrapper.AddComponent(viewType);
                // Native RendererInit stops after finding any skin. Override its
                // cache explicitly so both hull meshes and track skins recolour.
                NativeRead.Set(view,"mGeoRenderObjects",loaded.Renderers);
                controller.AddTempCustomizationController(Wrapper);
                Registered=true;
                instances.Add(Wrapper);
                foreach(GameObject original in originals)
                {original.SetActive(false);overridden.Add(original);Hidden.Add(original);}
                Runtime.Log("RHODES_CUSTOMIZATION_PREVIEW_READY nativeColors=True; fixedCamera=True; addedRotation=False; allRenderers="+loaded.Renderers.Length+
                    "; nativeCleanupLists=True; layout=LiveCoalitionSlots; nativeRootScales=True; independentOversizeClamp=True; viewportMargin=.02; aggregateRescale=False; independentFifthPage=True; fleetModels=12; battleFrameWork=False; visualAcceptancePending=True");
                buildTime.Stop();
                if(CacheReports++<8)Runtime.Log("RHODES_FLEET_PREVIEW_CACHE cacheHit="+cacheHit+"; modelAcquireMS="+modelTime.ElapsedMilliseconds+
                    "; totalBuildMS="+buildTime.ElapsedMilliseconds+"; lifetime=OpenCustomizationPanel; resourcesReloaded="+(!cacheHit)+"; coldLoadStillSynchronous=False; immutableResourcesShared=True");
                loaded=null; // Lifetime belongs to the wrapper owner.
            }
            catch
            {
                if(!owned&&loaded!=null){ForgetCachedModel(loaded);loaded.Dispose();}
                throw;
            }
        }

        internal static RhodesFleetPlacement[] CaptureCoalitionPlacements(Array sets,float groundY,Camera camera)
        {
            if(sets==null||sets.Length==0||float.IsNaN(groundY)||float.IsInfinity(groundY))
                throw new InvalidDataException("Coalition preview set or platform plane is missing");
            // Native CustomizationFactionSetting.Coalition is index zero.
            // Read its authored roots even when the current skin hides them.
            IEnumerable models=NativeRead.Get(sets.GetValue(0),"Models") as IEnumerable;
            if(models==null)throw new InvalidDataException("Coalition preview models are missing");
            Dictionary<string,Transform> roots=new Dictionary<string,Transform>(StringComparer.Ordinal);
            RhodesFleetCamera cameraSnapshot=new RhodesFleetCamera(camera);
            foreach(object model in models)
            {
                GameObject root=NativeRead.Get(model,"Model") as GameObject;if(root==null)continue;
                if(roots.ContainsKey(root.name))throw new InvalidDataException("Duplicate Coalition preview slot: "+root.name);
                roots.Add(root.name,root.transform);
            }
            RhodesFleetPlacement[] result=new RhodesFleetPlacement[RhodesFleetModels.NativeSlots.Length];
            for(int i=0;i<result.Length;i++)
            {
                string name=RhodesFleetModels.NativeSlots[i];Transform root;
                if(!roots.TryGetValue(name,out root))throw new InvalidDataException("Missing Coalition preview slot: "+name);
                Vector3 scale=root.lossyScale,position=root.position;Quaternion rotation=root.rotation;
                float finite=position.sqrMagnitude+scale.sqrMagnitude+rotation.x*rotation.x+rotation.y*rotation.y+rotation.z*rotation.z+rotation.w*rotation.w;
                if(float.IsNaN(finite)||float.IsInfinity(finite)||scale.x<=0f||scale.y<=0f||scale.z<=0f||
                    Math.Abs(scale.x-scale.y)>.001f||Math.Abs(scale.x-scale.z)>.001f||Vector3.Dot(rotation*Vector3.up,Vector3.up)<.999f)
                    throw new InvalidDataException("Coalition preview slot requires a finite, upright, uniform-scale pose: "+name);
                result[i]=new RhodesFleetPlacement(name,position,rotation,scale.x,groundY,cameraSnapshot);
            }
            return result;
        }

        RhodesFleetPreviewModel AcquireModel(object scene,PlayerCustomizationPreviewController controller,Material material,int layer,
            RhodesFleetPlacement[] placements,out bool cacheHit)
        {
            cacheHit=CachedModel!=null&&CachedModel.Root!=null&&ReferenceEquals(CacheScene,scene)&&CacheController==controller&&CacheMaterial==material&&CacheLayer==layer;
            if(cacheHit)return CachedModel;
            DisposeCachedModel();CancelPendingLoad();
            PendingLoad=RhodesFleetModels.BeginLoad(material,layer,placements);
            PendingScene=scene;PendingController=controller;PendingMaterial=material;PendingLayer=layer;
            PendingWatch=System.Diagnostics.Stopwatch.StartNew();PendingFrames=PendingSteps=0;ShowLoading();
            return null;
        }

        void PumpPendingLoad()
        {
            if(Panel==null||!gameObject.activeInHierarchy||Tab==null||!Tab.Selected||PendingController==null||PendingMaterial==null)
            {CancelPendingLoad();return;}
            object content=NativeRead.Get(Panel,"mContentManager");object scene=content==null?null:NativeRead.Get(content,"Scene");
            if(!ReferenceEquals(scene,PendingScene)||scene==null||
                NativeRead.Get(scene,"mPreviewController") as PlayerCustomizationPreviewController!=PendingController)
            {CancelPendingLoad();return;}
            System.Diagnostics.Stopwatch frame=System.Diagnostics.Stopwatch.StartNew();int steps=0;PendingFrames++;
            // A PNG decode/Unity upload is atomic. This is a soft budget
            // between operations; no Unity work runs on a background thread.
            do
            {
                PendingSteps++;steps++;
                if(!PendingLoad.Step())
                {
                    RhodesFleetBuildJob completed=PendingLoad;PendingLoad=null;
                    CachedModel=completed.TakeResult();completed.Dispose();
                    CacheScene=PendingScene;CacheController=PendingController;CacheMaterial=PendingMaterial;CacheLayer=PendingLayer;
                    long elapsed=PendingWatch==null?0:PendingWatch.ElapsedMilliseconds;HideLoading();ClearPendingContext();
                    Runtime.Log("RHODES_FLEET_PREVIEW_INCREMENTAL_READY frames="+PendingFrames+"; steps="+PendingSteps+"; elapsedMS="+elapsed+
                        "; maxOperationsPerFrame=64; softBudgetMS=4; sourceDecodeAndUnityBuildSeparated=True; cancellationOwned=True; immutableResourceLeases=True");
                    return;
                }
                if(PendingLoad.IsWaitingForInput)break;
            }while(steps<64&&frame.ElapsedMilliseconds<4);
        }
        void ClearPendingContext()
        {PendingScene=null;PendingController=null;PendingMaterial=null;PendingLayer=0;PendingWatch=null;}
        void CancelPendingLoad()
        {RhodesFleetBuildJob job=PendingLoad;PendingLoad=null;if(job!=null)job.Dispose();ClearPendingContext();HideLoading();}
        void ShowLoading()
        {
            HideLoading();if(Panel==null)return;UILabel font=null;
            foreach(UILabel candidate in Panel.GetComponentsInChildren<UILabel>(true))
                if(candidate!=null&&(candidate.bitmapFont!=null||candidate.trueTypeFont!=null)){font=candidate;break;}
            if(font==null)return;GameObject root=new GameObject("EF_Rhodes_FleetPreparing");root.layer=Panel.gameObject.layer;
            root.transform.parent=Panel.transform;root.transform.localPosition=new Vector3(0f,120f,0f);root.transform.localRotation=Quaternion.identity;root.transform.localScale=Vector3.one;
            LoadingLabel=root.AddComponent<UILabel>();if(font.bitmapFont!=null)LoadingLabel.bitmapFont=font.bitmapFont;else LoadingLabel.trueTypeFont=font.trueTypeFont;
            LoadingLabel.fontSize=18;LoadingLabel.pivot=UIWidget.Pivot.Center;LoadingLabel.alignment=NGUIText.Alignment.Center;
            LoadingLabel.width=440;LoadingLabel.height=36;LoadingLabel.depth=font.depth+10;LoadingLabel.color=new Color(1f,.88f,.28f,1f);LoadingLabel.text="正在准备罗德岛舰队…";
        }
        void HideLoading(){if(LoadingLabel!=null)UnityEngine.Object.Destroy(LoadingLabel.gameObject);LoadingLabel=null;}

        void ForgetCachedModel(RhodesFleetPreviewModel model)
        {
            if(!ReferenceEquals(CachedModel,model))return;
            CachedModel=null;CacheScene=null;CacheController=null;CacheMaterial=null;CacheLayer=0;
        }

        void OnNativeModelDestroyed(RhodesFleetPreviewModel model)
        {
            // Unexpected native cleanup owns disposal. Invalidate only; never
            // retain a root already scheduled for destruction or dispose twice.
            ForgetCachedModel(model);
            if(ReferenceEquals(PreviewModel,model))PreviewModel=null;
        }

        void ParkModel()
        {
            if(CachedModel==null||CachedModel.Root==null||ViewOwner==null||!ReferenceEquals(ViewOwner.Loaded,CachedModel))return;
            if(ParkingRoot==null)
            {
                // This identity-scale root is deliberately outside both the
                // scaled NGUI hierarchy and native faction cleanup lists.
                ParkingRoot=new GameObject("EF_Rhodes_FleetPreview_Parking");
                ParkingRoot.hideFlags=HideFlags.HideAndDontSave;ParkingRoot.SetActive(false);
            }
            CachedModel.Root.SetActive(false);
            CachedModel.Root.transform.parent=ParkingRoot.transform;
            CachedModel.Root.transform.localPosition=Vector3.zero;CachedModel.Root.transform.localRotation=Quaternion.identity;
            CachedModel.Root.transform.localScale=Vector3.one;
            ViewOwner.Loaded=null;ViewOwner.NativeDestroyed=null;
        }

        void DisposeCachedModel()
        {
            RhodesFleetPreviewModel model=CachedModel;
            CachedModel=null;CacheScene=null;CacheController=null;CacheMaterial=null;CacheLayer=0;
            if(model!=null)model.Dispose();
            if(ParkingRoot!=null)UnityEngine.Object.Destroy(ParkingRoot);ParkingRoot=null;
        }

        void Release(bool preserveCache)
        {
            CancelPendingLoad();
            if(Scene!=null&&Wrapper!=null)
            {
                List<GameObject> instances=NativeRead.Get(Scene,"mInstancedPreviews") as List<GameObject>;
                bool knownLive=instances!=null&&instances.Contains(Wrapper);
                // The Rhodes false-toggle callback normally executes before
                // the native selected-tab callback destroys faction previews.
                // If native cleanup already removed this instance, do not try
                // rescuing a hierarchy whose destruction may be queued.
                if(knownLive)ParkModel();
                else if(ViewOwner!=null&&ViewOwner.Loaded!=null)
                {ForgetCachedModel(ViewOwner.Loaded);ViewOwner.NativeDestroyed=null;}
                if(Registered&&Controller!=null)Controller.RemoveTempCustomizationController(Wrapper);
                // Native faction/skin switches may already have cleared the
                // instance and restored their own models. Do not undo that state.
                if(instances!=null&&instances.Remove(Wrapper))
                {
                    List<GameObject> overridden=NativeRead.Get(Scene,"mOverridenGameObjects") as List<GameObject>;
                    foreach(GameObject original in Hidden)
                        if(original!=null&&overridden!=null&&overridden.Remove(original))original.SetActive(true);
                }
                UnityEngine.Object.Destroy(Wrapper);
            }
            Wrapper=null;Scene=null;Controller=null;ViewOwner=null;PreviewModel=null;Registered=false;Hidden.Clear();
            if(!preserveCache)DisposeCachedModel();
        }
    }
}
