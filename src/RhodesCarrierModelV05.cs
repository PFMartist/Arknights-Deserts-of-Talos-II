using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BBI.Unity.Game.World;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Runtime adapter for an already metre-based RHS2 package. It does not
    // assign simulation dimensions, weapons, production or movement.
    public static class RhodesCarrierModelV05
    {
        static GameObject Templates;
        static readonly List<RhodesLoadedModel> Loaded=new List<RhodesLoadedModel>();
        static readonly List<UnityEngine.Object> Owned=new List<UnityEngine.Object>();

        public static GameObject Create(GameObject nativeWrapper,string packageFile)
        {
            if(nativeWrapper==null)throw new ArgumentNullException("nativeWrapper");
            Material template=FindMaterial(nativeWrapper);
            if(template==null)throw new InvalidOperationException("V05 requires a supported native carrier material template");
            if(Templates==null)
            {
                Templates=new GameObject("MapUnitLab.RhodesV05Templates");Templates.SetActive(false);Templates.hideFlags=HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(Templates);
            }
            GameObject model=null;RhodesLoadedModel loaded=null;
            try
            {
                model=(GameObject)UnityEngine.Object.Instantiate(nativeWrapper);model.name=nativeWrapper.name;
                model.transform.parent=Templates.transform;model.transform.localPosition=Vector3.zero;
                model.transform.localRotation=nativeWrapper.transform.localRotation;model.transform.localScale=nativeWrapper.transform.localScale;
                List<Renderer> donorGeometry=new List<Renderer>();int layer=model.layer;
                foreach(SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(!Protected(renderer.transform))donorGeometry.Add(renderer);
                foreach(MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))if(!Protected(renderer.transform)&&!donorGeometry.Contains(renderer))donorGeometry.Add(renderer);
                Mesh empty=new Mesh();empty.name="EF_RhodesV05_EmptyDonorGeometry";empty.hideFlags=HideFlags.HideAndDontSave;Owned.Add(empty);
                foreach(Renderer renderer in donorGeometry)
                {
                    layer=renderer.gameObject.layer;renderer.enabled=false;
                    SkinnedMeshRenderer skin=renderer as SkinnedMeshRenderer;if(skin!=null)skin.sharedMesh=empty;
                    MeshFilter filter=renderer.GetComponent<MeshFilter>();if(filter!=null)filter.sharedMesh=empty;
                }
                // Native skeleton, hardpoints, particles, lights, FX and native
                // UnitView are deliberately retained. No StripInheritedEffects.
                loaded=RhodesModelBuilder.Load(packageFile,template,layer);
                if(loaded.Package.Version!=2)throw new InvalidDataException("rhodes_v05.bin must be RHS2; use the original RHS1 fallback path for legacy");
                if(!loaded.Package.Selection.Specified)throw new InvalidDataException("V05 requires explicit selection bounds excluding probe extents");
                loaded.AttachInMetreSpace(model.transform);
                RhodesCarrierBinding binding=model.AddComponent<RhodesCarrierBinding>();binding.IsV05=true;binding.NativeRoot=model.transform;
                binding.GeometryRoot=loaded.Root.transform;binding.Geometry=loaded.Renderers;
                binding.GeometryBounds=loaded.PresentationBounds;binding.VisualBounds=binding.GeometryBounds;
                binding.SelectionBounds=Bound(loaded.Package.Selection);binding.HasSelectionBounds=true;
                binding.HasNavigationBounds=loaded.Package.Navigation.Specified;if(binding.HasNavigationBounds)binding.NavigationBounds=Bound(loaded.Package.Navigation);
                binding.TargetLength=binding.SelectionBounds.size.z;
                binding.Nodes=loaded.Nodes;binding.NodeNames=new string[loaded.Package.Nodes.Length];
                for(int i=0;i<binding.NodeNames.Length;i++)binding.NodeNames[i]=loaded.Package.Nodes[i].Name;
                int count=loaded.Package.Modules.Length;
                binding.ModuleIds=new string[count];binding.ModuleSlots=new string[count];binding.ModuleRoles=new string[count];binding.ModuleBranches=new string[count];
                binding.ModuleResearch=new string[count];binding.ModuleLevels=new int[count];binding.ModuleRoots=new Transform[count];binding.ModuleRiseDepths=new float[count];
                Dictionary<string,float> depths=ReadDepths(Path.GetDirectoryName(packageFile));
                for(int i=0;i<count;i++)
                {
                    RhodesModule module=loaded.Package.Modules[i];binding.ModuleIds[i]=module.Id;binding.ModuleSlots[i]=module.Slot;binding.ModuleRoles[i]=module.Role;
                    binding.ModuleBranches[i]=module.Branch;binding.ModuleResearch[i]=module.Research;binding.ModuleLevels[i]=module.Level;binding.ModuleRoots[i]=loaded.Nodes[module.Node];
                    float depth;string depthSlot=module.Slot.ToUpperInvariant();
                    if(!depths.TryGetValue(depthSlot,out depth))
                    {int suffix=depthSlot.IndexOf("_ADDON",StringComparison.Ordinal);if(suffix<0||!depths.TryGetValue(depthSlot.Substring(0,suffix),out depth))depth=6f;}
                    binding.ModuleRiseDepths[i]=depth;
                }
                RhodesAviationGuideDriver.PrepareTemplate(binding);
                binding.CaptureAuthoredLocalPose();
                UnitView view=model.GetComponent<UnitView>();if(view==null)throw new InvalidDataException("Native wrapper has no UnitView");
                RhodesAuthorCredit58.Attach(loaded,binding,template,layer);                loaded.LodGroup=NativeUnitLodAdapter.AdoptGeometry(view,loaded.LodGroup);
                NativeRead.Set(view,"mGeoRenderObjects",binding.Geometry);
                // Research initialization will choose the final modules before
                // visibility. This template remains inactive under Templates.
                loaded.Root.SetActive(false);model.SetActive(true);Loaded.Add(loaded);
                Runtime.Log("RHODES_V05_MODEL_PREPARED authoredNodes="+loaded.Package.Nodes.Length+"; nodesWithControls="+binding.Nodes.Length+"; renderers="+binding.Geometry.Length+"; modules="+count+
                    "; metreGeometryUnscaled=True; nativeWrapperRigFxPreserved=True; donorGeometryBlanked="+donorGeometry.Count+
                    "; visualBounds="+binding.VisualBounds+"; selectionBounds="+binding.SelectionBounds+"; navigationSpecified="+binding.HasNavigationBounds+
                    "; simulationFootprintAssigned=False; nativeLodRendererControl=True; runtimeAcceptancePending=True");
                return model;
            }
            catch
            {
                if(loaded!=null)loaded.Dispose();if(model!=null)UnityEngine.Object.DestroyImmediate(model);throw;
            }
        }
        public static Transform FindNode(RhodesCarrierBinding binding,string name)
        {return binding==null?null:binding.FindNode(name);}
        public static Transform NativeRoot(RhodesCarrierBinding binding)
        {return binding==null?null:binding.NativeRoot;}
        static Bounds Bound(RhodesBounds value)
        {Vector3 min=new Vector3(value.Min[0],value.Min[1],value.Min[2]),max=new Vector3(value.Max[0],value.Max[1],value.Max[2]);return new Bounds((min+max)*.5f,max-min);}
        static Material FindMaterial(GameObject native)
        {
            foreach(Renderer renderer in native.GetComponentsInChildren<Renderer>(true))
                if((renderer is MeshRenderer||renderer is SkinnedMeshRenderer)&&!Protected(renderer.transform)&&renderer.sharedMaterial!=null&&renderer.sharedMaterial.shader!=null&&renderer.sharedMaterial.shader.isSupported)return renderer.sharedMaterial;
            return null;
        }
        static bool Protected(Transform node)
        {
            for(;node!=null;node=node.parent)
            {string name=node.name.ToLowerInvariant();if(name.Contains("sensor")||name.Contains("fow")||name.Contains("underlay")||name.Contains("select")||name.Contains("hud")||name.Contains("blip"))return true;}
            return false;
        }
        static Dictionary<string,float> ReadDepths(string folder)
        {
            Dictionary<string,float> result=new Dictionary<string,float>(StringComparer.OrdinalIgnoreCase);
            result.Add("CIWS",4f);result.Add("REPAIR",6f);result.Add("DECK",12f);result.Add("SHARED_VT",10f);result.Add("SVT",10f);
            string file=Path.Combine(folder,"module_motion.ini");if(!File.Exists(file))return result;
            foreach(string raw in File.ReadAllLines(file))
            {
                string line=raw.Trim();if(line.Length==0||line.StartsWith("#")||line.StartsWith(";")||line.StartsWith("["))continue;
                int split=line.IndexOf('=');float depth;if(split<1||!float.TryParse(line.Substring(split+1).Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out depth)||float.IsNaN(depth)||float.IsInfinity(depth)||depth<=0f||depth>100f)
                    throw new InvalidDataException("Invalid V05 module motion depth");
                result[line.Substring(0,split).Trim().ToUpperInvariant()]=depth;
            }
            return result;
        }
        public static void ReleaseTemplates()
        {
            if(Templates!=null)UnityEngine.Object.DestroyImmediate(Templates);Templates=null;
            for(int i=Loaded.Count-1;i>=0;i--)Loaded[i].Dispose();Loaded.Clear();
            RhodesProbeLoader.ReleaseTemplateCache();
            RhodesTrackLoader.ReleaseTemplateCache();
            for(int i=Owned.Count-1;i>=0;i--)if(Owned[i]!=null)UnityEngine.Object.DestroyImmediate(Owned[i]);Owned.Clear();
        }
    }
}
