using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Unity.Core.World;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.FX;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // ISOLATED CANDIDATE. Registration only: native UnitView/UnitFXUpdate own
    // visibility, FOW, LOD, movement, pooling, and despawn. No custom MonoBehaviour,
    // Update, extra presentation Step, scene search, camera search, or Light.
    internal static class RhodesBeaconFactory
    {
        // The beacon is a child of the authored probe pivot, so the existing
        // probe animation carries it through every hull configuration.
        static readonly string[] Markers={"EF_Rhodes_Signal_Probe"};
        internal const string ProbeParentName="PIVOT_ProbeSway";
        internal const float PulseScale=.32f;
        internal static Vector3 ProbePosition {get{return new Vector3(0f,19.4f,0f);}}
        static readonly MethodInfo Memberwise=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly FieldInfo Frill=typeof(EffectDefinition).GetField("m_FrillType",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly FieldInfo DespawnTimer=typeof(FXDespawnTimerEvent).GetField("m_DespawnTimer",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        static readonly FieldInfo StopEmission=typeof(FXDespawnTimerEvent).GetField("m_StopFXEmissionOnTimerStart",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        static Transform TemplateRoot,Signal;
        static bool Warned;

        internal static void Append(UnitViewAttributes destination,UnitViewAttributes gaalsien,RhodesCarrierBinding binding,bool respectMapUnitLights=false)
        {
            if(destination==null||binding==null||binding.GeometryRoot==null)return;
            try
            {
            PrepareMarkers(binding);
            List<string> present=new List<string>();
            for(int index=0;index<Markers.Length;index++)
            {
                string name=Markers[index];Transform marker=Marker(binding,index);
                // Missing markers are deliberately skipped; no donor hull/root fallback.
                if(marker==null)continue;
                if(!marker.IsChildOf(binding.GeometryRoot)||!Metric(marker.lossyScale))
                    throw new InvalidOperationException("Beacon marker must be inside the owned metric root: "+name);
                if(Vector3.Dot(marker.up,binding.GeometryRoot.up)<.999f)
                    throw new InvalidOperationException("Beacon marker local+Y must point up: "+name);
                present.Add(name);
            }
            if(present.Count!=Markers.Length)throw new InvalidOperationException("Required independent signal mounts are unavailable");
                EffectDefinition donor=FindDonor(gaalsien);
                if(Memberwise==null||Frill==null||Frill.FieldType!=typeof(FrillType))throw new InvalidOperationException("Native EffectDefinition clone contract changed");
                // Templates stay inactive in the hierarchy beneath a persistent
                // inactive holder, with activeSelf=true for native first spawn.
                // SpawnPool accepts unknown transforms and makes PrefabPool lazily.
                // Keep one template until application exit, matching
                // the persistent Rhodes prefab/definition cache. Scene-local
                // native pools may rebuild against the same live template keys.
                // Never destroy a live pool key.
                if(Signal==null)Signal=Build(donor.FXPrefab,"WARM_WHITE",new Color(1f,.94f,.8f,1f),binding.GeometryRoot.gameObject.layer);
                List<EffectDefinition> definitions=new List<EffectDefinition>();
                if(destination.UnitEffectDefs!=null)definitions.AddRange(destination.UnitEffectDefs);
                foreach(string name in present)
                {
                    EffectDefinition definition=(EffectDefinition)Memberwise.Invoke(donor,null);
                    definition.Name="EF_Rhodes_Beacon_"+name;
                    definition.FXPrefab=Signal;
                    definition.BoneRef=new string[]{name};
                    definition.ActiveAtLOD=(LODGroupController.ModelLODSelection)7;
                    definition.Sound="_UnSet_";definition.PolyGroup="";
                    definition.RaycastDir=(RaycastDirection)0;
                    definition.FollowNLIPSScale=false;definition.InheritRaycastNormal=false;
                    // Preserve native day/night-independent beacons: Always=110.
                    // Native FrillTypeEnabled and FXElement.Spawn still enforce
                    // FXMasterEnabled; UnitView still owns FOW/visibility/LOD.
                    // Lights=1213 is an explicit optional map policy, not default.
                    if(respectMapUnitLights)Frill.SetValue(definition,FrillType.Lights);
                    if(definition.FXType!=(respectMapUnitLights?FrillType.Lights:FrillType.Always))throw new InvalidOperationException("Beacon frill gate assignment failed");
                    definitions.Add(definition);
                }
                destination.UnitEffectDefs=definitions.ToArray();
                RhodesModuleWarningLights.Prepare(destination,donor,binding);
                Runtime.Log("RHODES_BEACON_DEFINITIONS_PREPARED count="+present.Count+"; mounts=ProbeTop; authoredMovingParent="+ProbeParentName+"; tint=WarmWhite; nativePulseRate=0.2; nativeLifetimeSeconds=0.20/0.22; nativePooling=True; nativeUnitFXLifecycle=True; newLights=0; newTextures=0; newMaterials=0; newUpdateCallbacks=0; actualVisualAcceptance=False");
            }
            catch(Exception error)
            {if(!Warned){Warned=true;Runtime.Log("RHODES_BEACONS_UNAVAILABLE geometryAndOtherEffectsRetained=True; error="+error.Message);}}
        }

        static void PrepareMarkers(RhodesCarrierBinding binding)
        {
            Transform probe=binding.FindNode(ProbeParentName);
            if(probe==null||!probe.IsChildOf(binding.GeometryRoot))
                throw new InvalidOperationException("Exact authored probe signal parent is absent");
            binding.SignalProbeMarker=MakeMarker(probe,Markers[0],ProbePosition,binding.GeometryRoot.gameObject.layer);
        }
        static Transform MakeMarker(Transform parent,string name,Vector3 position,int layer)
        {
            if(parent.Find(name)!=null)throw new InvalidOperationException("Duplicate private signal marker: "+name);
            GameObject marker=new GameObject(name);marker.layer=layer;marker.transform.parent=parent;
            marker.transform.localPosition=position;marker.transform.localRotation=Quaternion.identity;marker.transform.localScale=Vector3.one;
            // Deliberately outside binding.Nodes: it is not an authored animation
            // channel and must not change the captured model pose array lengths.
            return marker.transform;
        }
        static Transform Marker(RhodesCarrierBinding binding,int index)
        {
            return binding==null||index!=0?null:binding.SignalProbeMarker;
        }

        internal static void VerifySuffix(UnitViewAttributes view,int offset)
        {
            if(view==null||view.UnitEffectDefs==null||offset<0||view.UnitEffectDefs.Length<offset||view.UnitEffectDefs.Length>offset+Markers.Length)
                throw new InvalidOperationException("Rhodes beacon suffix count changed");
            HashSet<string> used=new HashSet<string>(StringComparer.Ordinal);
            RhodesCarrierBinding binding=view.ModelPrefab==null?null:view.ModelPrefab.GetComponent<RhodesCarrierBinding>();
            for(int i=offset;i<view.UnitEffectDefs.Length;i++)
            {
                EffectDefinition d=view.UnitEffectDefs[i];
                if(d==null||d.BoneRef==null||d.BoneRef.Length!=1||Array.IndexOf(Markers,d.BoneRef[0])<0||!used.Add(d.BoneRef[0])||
                    d.Name!="EF_Rhodes_Beacon_"+d.BoneRef[0]||d.FXPrefab!=Signal||d.FXPrefab==null||
                    (d.FXType!=FrillType.Always&&d.FXType!=FrillType.Lights)||(int)d.ActiveAtLOD!=7||d.Sound!="_UnSet_"||d.PolyGroup!=""||
                    (int)d.RaycastDir!=0||d.FollowNLIPSScale||d.InheritRaycastNormal||binding==null||Marker(binding,Array.IndexOf(Markers,d.BoneRef[0]))==null)
                    throw new InvalidOperationException("Rhodes beacon suffix ownership/marker/native-effect contract changed");
            }
        }

        static bool Metric(Vector3 s)
        {return Mathf.Abs(s.x-1f)<.001f&&Mathf.Abs(s.y-1f)<.001f&&Mathf.Abs(s.z-1f)<.001f;}
        static EffectDefinition FindDonor(UnitViewAttributes gaalsien)
        {
            EffectDefinition result=null;
            if(gaalsien!=null&&gaalsien.UnitEffectDefs!=null)foreach(EffectDefinition d in gaalsien.UnitEffectDefs)
                if(d!=null&&d.Name=="FX - Lights"&&d.FXType==FrillType.Always)
                {if(result!=null)throw new InvalidOperationException("Gaalsien beacon donor is ambiguous");result=d;}
            if(result==null||result.FXPrefab==null||result.FXPrefab.name!="FX_PRFB_G_Carrier_FX_Lights_01")
                throw new InvalidOperationException("Exact native Gaalsien light prefab is unavailable");
            return result;
        }
        static Transform Build(Transform donor,string suffix,Color colour,int layer)
        {
            if(TemplateRoot==null)
            {
                GameObject holder=new GameObject("EF_Rhodes_BeaconTemplates");
                holder.SetActive(false);holder.hideFlags=HideFlags.HideAndDontSave;holder.layer=layer;
                UnityEngine.Object.DontDestroyOnLoad(holder);TemplateRoot=holder.transform;
            }
            GameObject template=new GameObject("EF_Rhodes_BeaconTemplate_"+suffix);
            template.SetActive(false);template.hideFlags=HideFlags.HideAndDontSave;template.layer=layer;
            template.transform.parent=TemplateRoot;
            template.transform.localPosition=Vector3.zero;template.transform.localRotation=Quaternion.identity;template.transform.localScale=Vector3.one;
            try
            {
                // Add native lifecycle only. Never instantiate the donor's root,
                // other particle systems, NLIPS group, point light, or scripts.
                FXElement element=template.AddComponent<FXElement>();
                element.OnParentDespawn=FXElement.ParentDespawnType.Despawn;
                element.FXType=FXElement.FXCategory.UnitLightsLarge;
                FXDespawnTimerEvent nativeTimer=donor.GetComponent<FXDespawnTimerEvent>();
                if(nativeTimer==null||DespawnTimer==null||DespawnTimer.FieldType!=typeof(float)||StopEmission==null||StopEmission.FieldType!=typeof(bool)||
                    nativeTimer.EventTrigger!=FXTriggerEventFlags.Ungrouped||(float)DespawnTimer.GetValue(nativeTimer)!=0f||!(bool)StopEmission.GetValue(nativeTimer))
                    throw new InvalidOperationException("Native beacon detach/despawn timer contract changed");
                FXDespawnTimerEvent timer=template.AddComponent<FXDespawnTimerEvent>();
                timer.EventTrigger=nativeTimer.EventTrigger;
                DespawnTimer.SetValue(timer,DespawnTimer.GetValue(nativeTimer));
                StopEmission.SetValue(timer,StopEmission.GetValue(nativeTimer));
                timer.enabled=nativeTimer.enabled;
                CopyParticle(donor,"BlinkLight01/blink_Glow_Soft_Blink",template.transform,colour,.20f,40f,PulseScale,"FX_MAT_Flash_02_LessSoft");
                CopyParticle(donor,"BlinkLight01/Glow_Center_Blink",template.transform,colour,.22f,17f,PulseScale,"FX_MAT_Flash_02_Hard");
                // PrefabPool.SpawnNew preserves the source activeSelf state.
                // The holder suppresses template playback while fresh pooled
                // clones activate, run FXElement.Awake, and playOnAwake.
                template.SetActive(true);
                return template.transform;
            }
            catch{UnityEngine.Object.DestroyImmediate(template);throw;}
        }
        static void CopyParticle(Transform donor,string path,Transform parent,Color colour,float lifetime,float size,float scale,string expectedMaterial)
        {
            Transform source=donor.Find(path);
            if(source==null||source.childCount!=0||source.GetComponents<MonoBehaviour>().Length!=0||source.GetComponents<Light>().Length!=0)
                throw new InvalidOperationException("Native beacon leaf contract changed: "+path);
            ParticleSystem native=source.GetComponent<ParticleSystem>();
            ParticleSystemRenderer renderer=source.GetComponent<ParticleSystemRenderer>();
            if(native==null||renderer==null||renderer.sharedMaterial==null||renderer.sharedMaterial.name!=expectedMaterial||renderer.sharedMaterial.shader==null||!renderer.sharedMaterial.shader.isSupported)
                throw new InvalidOperationException("Native beacon particle/material missing: "+path);
            if(!native.loop||!native.playOnAwake||Mathf.Abs(native.emissionRate-.2f)>.0001f||Mathf.Abs(native.startLifetime-lifetime)>.0001f||Mathf.Abs(native.startSize-size)>.001f)
                throw new InvalidOperationException("Native beacon period/lifetime/size contract changed: "+path);
            GameObject child=(GameObject)UnityEngine.Object.Instantiate(source.gameObject);
            child.SetActive(false);child.name=source.name;child.hideFlags=HideFlags.HideAndDontSave;child.layer=parent.gameObject.layer;
            child.transform.parent=parent;child.transform.localPosition=source.localPosition*scale;
            child.transform.localRotation=source.localRotation;child.transform.localScale=Vector3.one;
            ParticleSystem ps=child.GetComponent<ParticleSystem>();
            Color nativeColour=ps.startColor;colour.a=nativeColour.a;ps.startColor=colour;ps.startSize=size*scale;ps.maxParticles=3;
            ps.Stop();ps.Clear();child.SetActive(true);
            // Emission/color-over-lifetime gradients, duration9, loop, prewarm,
            // billboard rendering, shared textures and native shader stay intact.
        }
    }
}
