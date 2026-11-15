using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;
using BBI.Game.Simulation;
using BBI.Unity.Core.World;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.FX;
using BBI.Unity.Game.Utility;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Pure presentation policy. This does not integrate or write temperature.
    internal static class RhodesThermalVisualProfile
    {
        internal const float HotColourGain=4f;
        internal static bool IsMaterial(string name)
        {return name=="V17SRC_RI44_HEAT_RADIATOR_STEEL"||name=="V17SRC_RI44_HEAT_RADIATOR_ARMOR"||name=="V17SRC_RI44_HEAT_GRILLE_PORT"||name=="V17SRC_RI44_HEAT_GRILLE_STARBOARD";}
        internal static bool IsMainRadiator(string name)
        {return name=="V17SRC_RI44_HEAT_RADIATOR_STEEL"||name=="V17SRC_RI44_HEAT_RADIATOR_ARMOR";}
        internal static float HotGlowScale(string name)
        {return !IsMaterial(name)?0f:IsMainRadiator(name)?6f:3f;}
        // Forward UNIT passes do not read GlowScale. Lift the red tint itself;
        // divide the deferred glow gain separately to avoid multiplying both.
        internal static Color HotColour(float alpha)
        {return new Color(HotColourGain,.075f*HotColourGain,.012f*HotColourGain,alpha);}
        internal static float MaterialGlowScale(float nominal){return nominal/HotColourGain;}
        // Native IsOverheating is the sole heat-state owner. No inferred heat
        // ratio, cooling afterglow or per-frame material fade is applied.
        internal static float Target(bool overheating){return overheating?1f:0f;}
    }

    // Three native pooled vent updaters and per-unit material variants. The
    // existing RhodesCarrierView owns Attach/Step/Restore; no new frame host.
    internal sealed class RhodesThermalVisuals
    {
        sealed class Surface
        {
            internal Material Material;
            internal Color Cold;
            internal float ColdDamageToggle,HotGlow;
        }
        sealed class RendererBinding
        {
            internal Renderer Renderer;
            internal Material[] Original;
        }
        static readonly string[] Anchors={"HP_RI44_THERMAL_RADIATOR","HP_RI44_THERMAL_GRILLE_PORT","HP_RI44_THERMAL_GRILLE_STARBOARD"};
        static readonly FieldInfo PowerState=typeof(UnitView).GetField("mCurrentPowerShuntState",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly MethodInfo Memberwise=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        static EffectDefinition[] Definitions;
        static Transform SteamTemplate;
        static GameObject TemplateHolder;
        static int Warnings;
        readonly List<RendererBinding> Renderers=new List<RendererBinding>();
        readonly List<Surface> Surfaces=new List<Surface>();
        readonly UnitFXInputs[] Inputs=new UnitFXInputs[3];
        readonly UnitFXUpdate[] Effects=new UnitFXUpdate[3];
        readonly Transform[] Mounts=new Transform[3];
        UnitView View;
        RhodesCarrierBinding Binding;
        bool Failed,Restored;
        float Applied=-1f;
        int StateReports;
        bool RenderAuditArmed;int RenderAuditAttempts,RenderAuditReports;

        // Exact private IDs only, including menu previews before any unit
        // attaches. Native FOW property blocks and all other materials remain.
        internal static void ConfigureMaterial(string name,Material material)
        {
            if(!RhodesThermalVisualProfile.IsMaterial(name)||material==null)return;
            if(material.HasProperty("_GlowScale"))material.SetFloat("_GlowScale",0f);
        }

        internal static bool Prepare(UnitViewAttributes own,UnitViewAttributes source,RhodesCarrierBinding binding)
        {
            try
            {
                if(own==null||source==null||object.ReferenceEquals(own,source)||binding==null||binding.GeometryRoot==null)return false;
                foreach(string name in Anchors)
                {Transform marker=binding.FindNode(name);if(marker==null||!marker.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("Authored thermal mount missing: "+name);}
                EffectDefinition donor=null;
                if(source.UnitEffectDefs!=null)foreach(EffectDefinition item in source.UnitEffectDefs)
                    if(item!=null&&item.FXType==FrillType.Overheating&&item.FXPrefab!=null){donor=item;break;}
                if(donor==null||Memberwise==null)throw new InvalidOperationException("Native carrier steam definition unavailable");
                if(SteamTemplate==null)SteamTemplate=BuildSteamTemplate(source,donor,binding.GeometryRoot.gameObject.layer);
                if(Definitions==null)
                {
                    Definitions=new EffectDefinition[3];
                    for(int i=0;i<3;i++)
                    {
                        EffectDefinition copy=(EffectDefinition)Memberwise.Invoke(donor,null);
                        copy.Name="EF_Rhodes_Thermal_"+Anchors[i];copy.FXPrefab=SteamTemplate;copy.BoneRef=new string[]{Anchors[i]};
                        copy.Sound="_UnSet_";copy.PolyGroup="";copy.RaycastDir=(RaycastDirection)0;
                        copy.FollowNLIPSScale=false;copy.InheritRaycastNormal=false;Definitions[i]=copy;
                    }
                }
                // Explicit per-instance UnitFXUpdate binding below deliberately
                // avoids UnitView's earlier cached BoneRef resolution.
                Runtime.Log("RHODES_THERMAL_VISUAL_TEMPLATE_READY vents=3; nativeSteamMaterialsAndLod=True; oldDonorPositionsDiscarded=True; nativePooledUpdater=True; sceneScans=0; visualAcceptancePending=True");
                return true;
            }
            catch(Exception error){Warn("prepare",error);return false;}
        }

        internal static RhodesThermalVisuals Attach(UnitView view,RhodesCarrierBinding binding)
        {
            RhodesThermalVisuals result=new RhodesThermalVisuals();
            try{result.Initialize(view,binding);return result;}
            catch(Exception error){result.Restore();Warn("attach",error);return null;}
        }
        void Initialize(UnitView view,RhodesCarrierBinding binding)
        {
            if(view==null||binding==null||binding.Geometry==null||Definitions==null||PowerState==null)
                throw new InvalidOperationException("Authored thermal presentation is not prepared");
            View=view;Binding=binding;
            Dictionary<Material,Material> copies=new Dictionary<Material,Material>();
            HashSet<string> covered=new HashSet<string>(StringComparer.Ordinal);
            foreach(Renderer renderer in binding.Geometry)
            {
                if(renderer==null)continue;
                Material[] original=renderer.sharedMaterials;Material[] changed=null;
                for(int i=0;i<original.Length;i++)
                {
                    Material material=original[i];if(material==null||!RhodesThermalVisualProfile.IsMaterial(material.name))continue;
                    covered.Add(material.name);
                    if(material.shader==null||material.shader.name!="UNIT (Specular setup)"||
                        !material.HasProperty("_Color")||!material.HasProperty("_GlowScale")||!material.HasProperty("_Surface")||!material.HasProperty("_DamageToggle"))
                        throw new InvalidOperationException("Thermal region requires audited native UNIT properties");
                    if(changed==null)changed=(Material[])original.Clone();
                    Material own;
                    if(!copies.TryGetValue(material,out own))
                    {
                        own=new Material(material);own.name=material.name+"_Entity_"+view.EntityID;own.hideFlags=HideFlags.HideAndDontSave;
                        Surfaces.Add(new Surface{Material=own,Cold=material.GetColor("_Color"),
                            ColdDamageToggle=material.GetFloat("_DamageToggle"),HotGlow=RhodesThermalVisualProfile.HotGlowScale(material.name)});copies.Add(material,own);
                        own.SetFloat("_GlowScale",0f);
                    }
                    changed[i]=own;
                }
                if(changed!=null){Renderers.Add(new RendererBinding{Renderer=renderer,Original=original});renderer.sharedMaterials=changed;}
            }
            if(covered.Count!=4)throw new InvalidOperationException("Four isolated authored thermal material identities are required for three heat groups");
            for(int i=0;i<3;i++)
            {
                Mounts[i]=binding.FindNode(Anchors[i]);
                if(Mounts[i]==null||!Mounts[i].IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("Instanced thermal mount absent");
                Inputs[i]=new UnitFXInputs();
                FXHelper.InternalFXDefinition definition=FXHelper.CreateInternalDefFromNewFXDef(Definitions[i],false,(RaycastDirection)0,false,false);
                Effects[i]=new UnitFXUpdate(view,definition,Inputs[i],SteamTemplate,new Transform[]{Mounts[i]});
            }
            Apply(0f);
            Runtime.Log("RHODES_THERMAL_VISUAL_ATTACHED entity="+view.EntityID+"; isolatedMaterials="+copies.Count+"; vents=3; nativePool=True; privatePerUnitMaterials=True; nativeFowPropertyBlocksRetained=True; fixedOverheatSwitch=True; nominalMainGlow=6; nominalSideGlow=3; hotColourGain=4; materialMainGlow=1.5; materialSideGlow=.75; nativeDamageGlowGateHandled=True; newFrameHosts=0; actualVisualAcceptancePending=True");
        }
        internal void Step(bool shown)
        {
            if(Failed||Restored)return;
            try
            {
                UnitState unit=View==null?null:View.NonInterpolatedTickState;
                if(View==null||Binding==null||Binding.GeometryRoot==null||unit==null||unit.UnitIsDead)
                {Restore();return;}
                PowerShuntState power=PowerState.GetValue(View) as PowerShuntState;
                float target=RhodesThermalVisualProfile.Target(shown&&power!=null&&power.IsOverheating);
                if(Apply(target)&&shown&&StateReports<8)
                {
                    StateReports++;
                    Runtime.Log("RHODES_THERMAL_VISUAL_STATE entity="+View.EntityID+"; nativeOverheating="+(power!=null&&power.IsOverheating)+
                        "; visualHot="+(target>0f)+"; materialMainGlow="+(1.5f*target)+"; hotColourGain=4; fixedOverheatSwitch=True; temperatureAndCoolingNotMapped=True; actualPixelsUnverified=True");
                }
                FXManager manager=FXManager.Manager;int lod=NativeUnitLodAdapter.CurrentLod(View);
                bool active=shown&&power!=null&&power.IsOverheating&&manager!=null&&manager.FrillTypeEnabled(FrillType.Overheating)&&lod>=0&&lod<=3;
                for(int i=0;i<3;i++)
                {
                    Inputs[i].UnitLOD=(LODGroupController.ModelLOD)Math.Max(0,Math.Min(3,lod));
                    Effects[i].ToggleFX(active&&Mounts[i]!=null&&Mounts[i].gameObject.activeInHierarchy,Inputs[i].UnitLOD,null);
                    if(Effects[i].IsActive)Effects[i].Update(0f);
                }
            }
            catch(Exception error){Failed=true;Restore();Warn("step",error);}
        }
        bool Apply(float heat)
        {
            if(Math.Abs(heat-Applied)<.001f)return false;Applied=heat;
            foreach(Surface surface in Surfaces)
                if(surface.Material!=null)
                {
                    // Native UNIT's deferred glow is Surface.R * GlowScale *
                    // albedo * (1-DamageToggle). The intake's cold toggle is 1,
                    // so changing colour/GlowScale alone left emission at zero.
                    // Change only these private thermal clones; the original
                    // PBR maps and native per-renderer FOW block stay intact.
                    surface.Material.SetColor("_Color",Color.Lerp(surface.Cold,RhodesThermalVisualProfile.HotColour(surface.Cold.a),heat));
                    surface.Material.SetFloat("_GlowScale",RhodesThermalVisualProfile.MaterialGlowScale(surface.HotGlow)*heat);
                    surface.Material.SetFloat("_DamageToggle",heat>0f?0f:surface.ColdDamageToggle);
                }
            if(heat>0f)ArmRenderAudit();else DisarmRenderAudit();
            return true;
        }
        void ArmRenderAudit()
        {
            if(!Runtime.DetailedDiagnosticsEnabled||RenderAuditArmed||RenderAuditReports>=4||Restored)return;
            RenderAuditAttempts=0;RenderAuditArmed=true;Camera.onPreRender+=AuditRenderedMaterials;
        }
        void DisarmRenderAudit()
        {if(!RenderAuditArmed)return;Camera.onPreRender-=AuditRenderedMaterials;RenderAuditArmed=false;}
        static string Components(Color value)
        {return value.r.ToString("G6",CultureInfo.InvariantCulture)+","+value.g.ToString("G6",CultureInfo.InvariantCulture)+","+value.b.ToString("G6",CultureInfo.InvariantCulture)+","+value.a.ToString("G6",CultureInfo.InvariantCulture);}
        // A bounded native render callback, only armed after a hot transition.
        // Read the real camera/material/property block; no Camera.main search,
        // render override, forced visibility, new Update or scene enumeration.
        void AuditRenderedMaterials(Camera camera)
        {
            if(!RenderAuditArmed)return;
            if(Restored||View==null||Applied<=0f||++RenderAuditAttempts>128){DisarmRenderAudit();return;}
            try
            {
                if(camera==null||camera.cameraType!=CameraType.Game||camera.targetTexture!=null)return;
                Plane[] planes=GeometryUtility.CalculateFrustumPlanes(camera);Renderer eligible=null;
                foreach(RendererBinding item in Renderers)
                {
                    Renderer renderer=item.Renderer;
                    if(renderer!=null&&renderer.enabled&&renderer.gameObject.activeInHierarchy&&
                        (camera.cullingMask&(1<<renderer.gameObject.layer))!=0&&GeometryUtility.TestPlanesAABB(planes,renderer.bounds))
                    {eligible=renderer;break;}
                }
                if(eligible==null)return;
                DisarmRenderAudit();RenderAuditReports++;
                Runtime.Log("RHODES_THERMAL_RENDER_CAMERA entity="+View.EntityID+"; camera="+camera.name+"; actualRenderingPath="+camera.actualRenderingPath+
                    "; cameraTarget=screen; frustumAndLayerEligible=True; nativeOverheat=True; materialStateReadOnly=True; pixelAcceptancePending=True");
                HashSet<Material> emitted=new HashSet<Material>();MaterialPropertyBlock block=new MaterialPropertyBlock();
                foreach(RendererBinding item in Renderers)
                {
                    Renderer renderer=item.Renderer;if(renderer==null||!renderer.enabled||!renderer.gameObject.activeInHierarchy||
                        (camera.cullingMask&(1<<renderer.gameObject.layer))==0||!GeometryUtility.TestPlanesAABB(planes,renderer.bounds))continue;
                    Material[] actual=renderer.sharedMaterials;
                    for(int slot=0;slot<actual.Length&&slot<item.Original.Length;slot++)
                    {
                        Material original=item.Original[slot],material=actual[slot];
                        if(original==null||!RhodesThermalVisualProfile.IsMaterial(original.name)||material==null||!emitted.Add(material))continue;
                        bool privateBound=false;foreach(Surface surface in Surfaces)if(object.ReferenceEquals(surface.Material,material)){privateBound=true;break;}
                        block.Clear();renderer.GetPropertyBlock(block);
                        Texture albedo=material.mainTexture,mask=material.GetTexture("_Surface");
                        Runtime.Log("RHODES_THERMAL_RENDER_MATERIAL entity="+View.EntityID+"; region="+original.name+"; renderer="+renderer.name+
                            "; rendererID="+renderer.GetInstanceID()+"; liveOwner="+renderer.transform.IsChildOf(View.transform)+"; privateBound="+privateBound+
                            "; rendererVisibleLastFrame="+renderer.isVisible+"; shader="+(material.shader==null?"none":material.shader.name)+
                            "; actualColour="+Components(material.GetColor("_Color"))+"; actualGlow="+material.GetFloat("_GlowScale")+
                            "; actualDamageToggle="+material.GetFloat("_DamageToggle")+"; albedo="+(albedo==null?"none":albedo.name)+
                            "; surface="+(mask==null?"none":mask.name)+"; rawBlockColour="+Components((Color)block.GetVector("_Color"))+
                            "; rawBlockBase="+Components((Color)block.GetVector("_BaseColor"))+"; rawBlockMayBeUnset=True; propertyBlockUnchanged=True; pixelsNotMeasured=True");
                    }
                }
            }
            catch(Exception error){DisarmRenderAudit();Warn("render_audit",error);}
        }
        internal void Restore()
        {
            if(Restored)return;Restored=true;
            DisarmRenderAudit();
            for(int i=0;i<3;i++)if(Effects[i]!=null){try{Effects[i].Reset();}catch(Exception error){Warn("fx_reset",error);}Effects[i]=null;}
            foreach(RendererBinding item in Renderers)if(item.Renderer!=null)
                try{item.Renderer.sharedMaterials=item.Original;}catch(Exception error){Warn("material_restore",error);}
            foreach(Surface surface in Surfaces)if(surface.Material!=null)
                try{UnityEngine.Object.Destroy(surface.Material);}catch(Exception error){Warn("material_release",error);}
            Renderers.Clear();Surfaces.Clear();View=null;Binding=null;
        }

        // The carrier template owner calls this only after every attached view
        // has restored. Remove only this private prefab's native pool entry;
        // never clear the common FX pool or another faction's effects.
        internal static void ReleaseTemplates()
        {
            Transform template=SteamTemplate;GameObject holder=TemplateHolder;
            Definitions=null;SteamTemplate=null;TemplateHolder=null;
            if(template!=null)
                try{RetirePrivatePool(template);}catch(Exception error){Warn("template_pool_release",error);}
            // FXManager may retire before our template. Its independent static
            // tracker must still forget this exact owned key, even for a Unity
            // wrapper whose native Transform was already destroyed.
            if(!object.ReferenceEquals(template,null))
                try{ForgetTemplateTracker(template);}catch(Exception error){Warn("template_tracker_release",error);}
            if(holder!=null)
                try{UnityEngine.Object.Destroy(holder);}catch(Exception error){Warn("template_release",error);}
            Warnings=0;
        }
        internal static void RetirePrivatePool(Transform template)
        {
            if(FXManager.Manager==null)return;
            MethodInfo getPool=typeof(FXElement).GetMethod("FXPool",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            if(getPool==null)throw new MissingMethodException("Native FX pool accessor absent");
            object pool;
            try{pool=getPool.Invoke(null,null);}
            catch(TargetInvocationException error){if(error.InnerException is KeyNotFoundException)return;throw;}
            if(pool==null)return;
            PropertyInfo poolsProperty=pool.GetType().GetProperty("prefabPools",BindingFlags.Instance|BindingFlags.Public);
            IDictionary pools=poolsProperty==null?null:poolsProperty.GetValue(pool,null) as IDictionary;
            if(pools==null)throw new InvalidOperationException("Native prefab-pool dictionary unavailable");
            if(pools.Contains(template))
            {
                object owned=pools[template];Type type=owned.GetType();
                MethodInfo stop=type.GetMethod("StopPreload",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                MethodInfo destruct=type.GetMethod("SelfDestruct",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                FieldInfo spawned=type.GetField("_spawned",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                MethodInfo despawn=pool.GetType().GetMethod("Despawn",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new Type[]{typeof(Transform),typeof(object)},null);
                if(stop==null||destruct==null||spawned==null||despawn==null)throw new InvalidOperationException("Native private-pool retirement contract changed");
                stop.Invoke(owned,null);
                List<Transform> active=new List<Transform>();
                foreach(object value in (IEnumerable)spawned.GetValue(owned)){Transform item=value as Transform;if(item!=null)active.Add(item);}
                foreach(Transform item in active)despawn.Invoke(pool,new object[]{item,item});
                pools.Remove(template);destruct.Invoke(owned,null);
            }
        }
        internal static void ForgetTemplateTracker(Transform template)
        {
            FieldInfo tracker=typeof(FXElement).GetField("sFXElementToCategoryTracker",BindingFlags.Static|BindingFlags.NonPublic);
            IDictionary categories=tracker==null?null:tracker.GetValue(null) as IDictionary;
            if(categories!=null&&categories.Contains(template))categories.Remove(template);
        }

        static Transform BuildSteamTemplate(UnitViewAttributes source,EffectDefinition donor,int layer)
        {
            GameObject holder=null;
            try
            {
                Transform prefab=donor.FXPrefab;
                if(prefab.name!="FX_PRFB_Overheat_Carrier_Steam"||donor.BoneRef==null||donor.BoneRef.Length!=1||source.ModelPrefab==null)
                    throw new InvalidOperationException("Exact native coalition steam donor contract changed");
                Transform bone=null;
                foreach(Transform candidate in source.ModelPrefab.GetComponentsInChildren<Transform>(true))
                    if(candidate.name==donor.BoneRef[0]){if(bone!=null)throw new InvalidOperationException("Native steam bone is ambiguous");bone=candidate;}
                if(bone==null)throw new InvalidOperationException("Native steam basis bone missing");
                FXElement element=prefab.GetComponent<FXElement>();FXDespawnTimerEvent timer=prefab.GetComponent<FXDespawnTimerEvent>();
                FXProxyVolumeDefinition proxy=prefab.GetComponent<FXProxyVolumeDefinition>();
                if(element==null||timer==null||proxy==null)throw new InvalidOperationException("Native steam lifecycle components unavailable");
                holder=new GameObject("EF_Rhodes_ThermalSteamTemplates");holder.SetActive(false);holder.hideFlags=HideFlags.HideAndDontSave;
                GameObject root=new GameObject("EF_Rhodes_NativeSteamVent");root.SetActive(false);root.hideFlags=HideFlags.HideAndDontSave;root.layer=layer;
                root.transform.parent=holder.transform;root.transform.localPosition=Vector3.zero;root.transform.localRotation=Quaternion.identity;root.transform.localScale=Vector3.one;
                FXElement ownElement=root.AddComponent<FXElement>();ownElement.OnParentDespawn=element.OnParentDespawn;ownElement.FXType=element.FXType;
                FXDespawnTimerEvent ownTimer=root.AddComponent<FXDespawnTimerEvent>();ownTimer.EventTrigger=timer.EventTrigger;
                CopyFields(timer,ownTimer,new string[]{"m_DespawnTimer","m_StopFXEmissionOnTimerStart"});
                FXProxyVolumeDefinition ownProxy=root.AddComponent<FXProxyVolumeDefinition>();
                CopyFields(proxy,ownProxy,new string[]{"m_OverrideGlobalLOD","m_ProxyVolumeSize","m_LODThresholds","m_ContinuousLODUpdates"});
                GameObject basis=new GameObject("NativeSteamBasis");basis.layer=layer;basis.transform.parent=root.transform;
                basis.transform.localPosition=Vector3.zero;basis.transform.localRotation=Quaternion.Inverse(source.ModelPrefab.transform.rotation)*bone.rotation;basis.transform.localScale=Vector3.one;
                foreach(string name in new string[]{"Steam_Front_Right","Steam_Front_Right_Distance"})
                {
                    Transform child=prefab.Find(name);if(child==null||child.GetComponent<ParticleSystem>()==null)throw new InvalidOperationException("Native steam near/far child absent");
                    GameObject copy=(GameObject)UnityEngine.Object.Instantiate(child.gameObject);copy.SetActive(false);copy.name=name;
                    copy.transform.parent=basis.transform;copy.transform.localPosition=Vector3.zero;copy.transform.localRotation=child.localRotation;copy.transform.localScale=child.localScale;
                    foreach(ParticleSystem system in copy.GetComponentsInChildren<ParticleSystem>(true)){system.Stop();system.Clear();}
                    copy.SetActive(true);
                }
                root.SetActive(true);UnityEngine.Object.DontDestroyOnLoad(holder);TemplateHolder=holder;return root.transform;
            }
            catch{if(holder!=null)UnityEngine.Object.DestroyImmediate(holder);throw;}
        }
        static void CopyFields(object source,object destination,string[] names)
        {
            foreach(string name in names)
            {
                FieldInfo field=source.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                if(field==null)throw new MissingFieldException(source.GetType().FullName,name);
                field.SetValue(destination,field.GetValue(source));
            }
        }
        static void Warn(string stage,Exception error)
        {if(Warnings++<4)Runtime.Log("RHODES_THERMAL_VISUAL_UNAVAILABLE stage="+stage+"; hullAndSimulationRetained=True; error="+error.GetType().Name+": "+error.Message);}
    }
}
