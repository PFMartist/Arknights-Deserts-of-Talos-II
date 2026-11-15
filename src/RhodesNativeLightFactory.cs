using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Unity.Core.World;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.FX;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Registration only. Native UnitView/UnitFXUpdate and the FX pool own all
    // live instances, visibility, unit LOD, quality, proxy LOD and despawn.
    internal static class RhodesNativeLightFactory
    {
        internal const int DefinitionCount=4;
        const float FrontBeamDownwardDegrees=15f;
        const string SoftCookieName="FX_TEX_LightCookie_Coalition_Battlecruiser_01";
        static readonly string[] Markers={"V07_LIGHT_WORK_A","V07_LIGHT_WORK_B",RhodesTrackLampMounts.AnchorAName,RhodesTrackLampMounts.AnchorBName};
        static readonly MethodInfo Memberwise=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly Transform[] Templates=new Transform[DefinitionCount];
        static GameObject Holder;
        static Texture SoftCookie;

        internal static void Append(UnitViewAttributes destination,UnitViewAttributes carrier,UnitViewAttributes escort,RhodesCarrierBinding binding)
        {
            if(destination==null||binding==null||!binding.IsV05||binding.GeometryRoot==null)
                throw new InvalidOperationException("Native Rhodes lights require their owned V05 geometry root");
            if(Memberwise==null)throw new InvalidOperationException("Native EffectDefinition clone contract changed");
            RhodesTrackLampMounts.Ensure(binding.GeometryRoot);
            for(int index=0;index<Markers.Length;index++)CheckMarker(binding,index);

            EffectDefinition large=FindDonor(carrier,"FX_PRFB_C_Carrier_FX_Lights_01_Headlights",FXElement.FXCategory.UnitLightsLarge);
            EffectDefinition small=FindDonor(escort,"FX_PRFB_C_Escort_FX_Lights_01_Headlights",FXElement.FXCategory.UnitLightsSmall);
            EnsureTemplates(large.FXPrefab,small.FXPrefab);
            List<EffectDefinition> definitions=new List<EffectDefinition>();
            if(destination.UnitEffectDefs!=null)definitions.AddRange(destination.UnitEffectDefs);
            for(int index=0;index<Markers.Length;index++)
            {
                EffectDefinition definition=(EffectDefinition)Memberwise.Invoke(index<2?large:small,null);
                definition.Name="EF_Rhodes_NativeLight_"+Markers[index];
                definition.FXPrefab=Templates[index];
                definition.BoneRef=new string[]{Markers[index]};
                definitions.Add(definition);
            }
            destination.UnitEffectDefs=definitions.ToArray();
            Runtime.Log("RHODES_NATIVE_LIGHT_DEFINITIONS_PREPARED front=2; frontDonor=C_Carrier_MP; frontRange=500/400; frontIntensity=8; frontAngle=75.1/99.2; frontNativeBeamLength=297.5; track=2; trackDonor=C_Escort_MP; trackRange=30; trackIntensity=4.69; trackAngle=101.6; smallCategory=19; largeCategory=20; nativeCookieMaterialsParticles=True; nativePoolProxyQualityFowLifecycle=True; customUpdateCallbacks=0; newTextures=0; actualVisualAcceptance=False");
        }

        internal static int VerifySuffix(UnitViewAttributes view,int offset)
        {
            if(view==null||view.UnitEffectDefs==null||offset<0||view.UnitEffectDefs.Length<offset+DefinitionCount)
                throw new InvalidOperationException("Rhodes native light definitions are incomplete");
            RhodesCarrierBinding binding=view.ModelPrefab==null?null:view.ModelPrefab.GetComponent<RhodesCarrierBinding>();
            for(int index=0;index<DefinitionCount;index++)
            {
                EffectDefinition definition=view.UnitEffectDefs[offset+index];
                if(definition==null||definition.Name!="EF_Rhodes_NativeLight_"+Markers[index]||definition.FXPrefab!=Templates[index]||definition.FXPrefab==null||
                    definition.BoneRef==null||definition.BoneRef.Length!=1||definition.BoneRef[0]!=Markers[index]||definition.FXType!=FrillType.Lights||
                    (int)definition.ActiveAtLOD!=(index<2?15:7)||definition.Sound!=(index<2?"_UnSet_":"")||definition.PolyGroup!=""||
                    (int)definition.RaycastDir!=0||definition.FollowNLIPSScale!=(index>=2)||definition.InheritRaycastNormal)
                    throw new InvalidOperationException("Rhodes native light definition/marker contract changed");
                CheckMarker(binding,index);
                CheckTemplate(Templates[index],index);
            }
            return DefinitionCount;
        }

        // One native Small FX group per vehicle, like C_Escort: one shadowless
        // ground spot, two cones and four lens glows. No per-unit light driver.
        internal static EffectDefinition VehicleDefinition(UnitViewAttributes view,Transform holder,string key,string marker,Vector3 left,Vector3 right)
        {
            EffectDefinition donor=FindDonor(view,"FX_PRFB_C_Escort_FX_Lights_01_Headlights",FXElement.FXCategory.UnitLightsSmall);
            Transform source=donor.FXPrefab;
            GameObject template=new GameObject("EF_Vehicle_Headlights_"+key);
            template.SetActive(false);template.hideFlags=HideFlags.HideAndDontSave;template.layer=source.gameObject.layer;
            template.transform.parent=holder;template.transform.localPosition=Vector3.zero;
            template.transform.localRotation=Quaternion.identity;template.transform.localScale=Vector3.one;
            try
            {
                FXElement original=source.GetComponent<FXElement>();FXElement element=template.AddComponent<FXElement>();
                element.FXType=original.FXType;element.OnParentDespawn=original.OnParentDespawn;
                CopyTimer(source,template);
                FXProxyVolumeDefinition proxy=source.GetComponent<FXProxyVolumeDefinition>();
                if(proxy==null)throw new InvalidOperationException("Vehicle native light proxy missing");
                CopyFields(proxy,template.AddComponent<FXProxyVolumeDefinition>(),new string[]{"m_OverrideGlobalLOD","m_ProxyVolumeSize","m_LODThresholds","m_ContinuousLODUpdates"});
                CopyRootParticlePolicy(source,template);
                Transform spot=CloneLeaf(source,"FX_LIGHT_Front",template.transform);
                Light light=spot.GetComponent<Light>();
                if(light==null||light.type!=LightType.Spot||light.shadows!=LightShadows.None||light.cookie==null||
                    light.cookie.name!="FX_TEX_LightCookie_Railgun_Glow_01")
                    throw new InvalidOperationException("Vehicle native small spot/cookie missing");
                spot.localPosition=(left+right)*.5f; // Native single-spot budget for the lamp pair.
                Vector3 direction=spot.localRotation*Vector3.forward;
                for(int side=0;side<2;side++)
                {
                    Vector3 mount=side==0?left:right;
                    Transform beam=CloneLeaf(source,side==0?"MainBeam 5":"MainBeam 4",template.transform);
                    MeshFilter mesh=beam.GetComponent<MeshFilter>();Renderer renderer=beam.GetComponent<Renderer>();
                    if(mesh==null||mesh.sharedMesh==null||mesh.sharedMesh.name!="FX_GEO_Resource_Beam_01"||
                        renderer==null||renderer.sharedMaterial==null||renderer.sharedMaterial.name!="FX_MAT_Unit_Headlight_Geo_Escort")
                        throw new InvalidOperationException("Vehicle native cone mesh/material missing");
                    beam.localPosition=mount;beam.localRotation=Quaternion.FromToRotation(Vector3.down,direction);
                    for(int glowIndex=1;glowIndex<=2;glowIndex++)
                    {
                        Transform glow=CloneLeaf(source,"Low_Front_"+(side==0?"Left":"Right")+"_Center "+glowIndex,template.transform);
                        ParticleSystem particles=glow.GetComponent<ParticleSystem>();
                        if(particles==null)throw new InvalidOperationException("Vehicle native lens glow missing");
                        glow.localPosition=mount;particles.Stop();particles.Clear();
                    }
                }
                template.SetActive(true);
                if(template.activeInHierarchy||template.GetComponentsInChildren<Light>(true).Length!=1||
                    template.GetComponentsInChildren<ParticleSystem>(true).Length!=4||
                    template.GetComponentsInChildren<MeshFilter>(true).Length!=2||
                    Mathf.Abs(light.range-30f)>.001f||Mathf.Abs(light.intensity-4.69f)>.001f||Mathf.Abs(light.spotAngle-101.6f)>.001f)
                    throw new InvalidOperationException("Vehicle native headlight budget/photometry changed");
                EffectDefinition definition=(EffectDefinition)Memberwise.Invoke(donor,null);
                definition.Name="EF_Vehicle_Headlights_"+key;definition.FXPrefab=template.transform;
                definition.BoneRef=new string[]{marker};
                return definition;
            }
            catch{UnityEngine.Object.DestroyImmediate(template);throw;}
        }

        static EffectDefinition FindDonor(UnitViewAttributes view,string prefabName,FXElement.FXCategory category)
        {
            EffectDefinition result=null;
            if(view!=null&&view.UnitEffectDefs!=null)foreach(EffectDefinition definition in view.UnitEffectDefs)
                if(definition!=null&&definition.FXPrefab!=null&&definition.FXPrefab.name==prefabName&&definition.FXType==FrillType.Lights)
                {
                    if(result!=null)throw new InvalidOperationException("Native light donor is ambiguous: "+prefabName);
                    result=definition;
                }
            FXElement element=result==null?null:result.FXPrefab.GetComponent<FXElement>();
            bool large=category==FXElement.FXCategory.UnitLightsLarge;
            if(result==null||element==null||element.FXType!=category||(int)result.ActiveAtLOD!=(large?15:7)||result.Sound!=(large?"_UnSet_":"")||result.PolyGroup!=""||
                (int)result.RaycastDir!=0||result.FollowNLIPSScale==large||result.InheritRaycastNormal)
                throw new InvalidOperationException("Native light donor contract changed: "+prefabName);
            return result;
        }

        static void CheckMarker(RhodesCarrierBinding binding,int index)
        {
            if(binding==null||binding.GeometryRoot==null)throw new InvalidOperationException("Rhodes native light binding missing");
            Transform marker=index<2?binding.FindNode(Markers[index]):binding.GeometryRoot.Find(Markers[index]);
            if(marker==null||!marker.IsChildOf(binding.GeometryRoot)||!Metric(marker.lossyScale))
                throw new InvalidOperationException("Rhodes native light marker outside owned metric geometry: "+Markers[index]);
            if(index<2)
            {
                Transform target=binding.FindNode(Markers[index].Replace("V07_LIGHT_WORK_","V07_LIGHT_TARGET_WORK_"));
                if(target==null||!target.IsChildOf(binding.GeometryRoot)||Vector3.Dot(marker.up,(target.position-marker.position).normalized)<.999f||
                    Vector3.Dot(marker.up,binding.GeometryRoot.forward)<.999f)
                    throw new InvalidOperationException("Rhodes front native light must face straight forward: "+Markers[index]);
            }
            else
            {
                float[] ray=RhodesTrackLampMounts.GetMountRay(index-2);
                Vector3 expectedPosition=new Vector3(ray[0],ray[1],ray[2]);
                Vector3 expectedDirection=new Vector3(ray[3],ray[4],ray[5]);
                if((marker.localPosition-expectedPosition).sqrMagnitude>.0001f||Vector3.Dot(marker.localRotation*Vector3.up,expectedDirection)<.999f)
                    throw new InvalidOperationException("Rhodes track native light differs from the retained lens face: "+Markers[index]);
            }
        }

        static bool Metric(Vector3 scale)
        {return Mathf.Abs(scale.x-1f)<.001f&&Mathf.Abs(scale.y-1f)<.001f&&Mathf.Abs(scale.z-1f)<.001f;}

        static void EnsureTemplates(Transform carrier,Transform escort)
        {
            if(Holder!=null)return;
            Transform softCookieSource=carrier.Find("FX_LIGHT_Front 1");
            Light softCookieLight=softCookieSource==null?null:softCookieSource.GetComponent<Light>();
            Texture cookie=softCookieLight==null?null:softCookieLight.cookie;
            if(cookie==null||cookie.name!=SoftCookieName)
                throw new InvalidOperationException("Native single-lobe headlight cookie is missing");
            GameObject holder=new GameObject("EF_Rhodes_NativeLightTemplates");
            holder.SetActive(false);holder.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                SoftCookie=cookie;
                Templates[0]=Build(carrier,holder.transform,0,"FX_LIGHT_Front","MainBeam 1",new string[]{"Headlight_Right_Glow_01"},cookie);
                Templates[1]=Build(carrier,holder.transform,1,"FX_LIGHT_Front 1","MainBeam",new string[]{"Headlight_Left_Glow_01"},cookie);
                Templates[2]=Build(escort,holder.transform,2,"FX_LIGHT_Front","MainBeam 4",new string[]{"Low_Front_Right_Center 1","Low_Front_Right_Center 2"},cookie);
                Templates[3]=Build(escort,holder.transform,3,"FX_LIGHT_Front","MainBeam 5",new string[]{"Low_Front_Left_Center 1","Low_Front_Left_Center 2"},cookie);
                UnityEngine.Object.DontDestroyOnLoad(holder);Holder=holder;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(holder);
                for(int index=0;index<Templates.Length;index++)Templates[index]=null;
                SoftCookie=null;
                throw;
            }
            // The persistent templates stay live for every scene-local FX pool.
            // Never destroy a pool key during a match reset. Their own activeSelf
            // is true, so PrefabPool.SpawnNew produces an active first clone even
            // though the template is hidden beneath its inactive factory holder.
        }

        static Transform Build(Transform donor,Transform holder,int index,string spotName,string beamName,string[] glowNames,Texture cookie)
        {
            GameObject template=new GameObject("EF_Rhodes_NativeLightTemplate_"+Markers[index]);
            template.SetActive(false);template.hideFlags=HideFlags.HideAndDontSave;
            template.layer=donor.gameObject.layer;template.transform.parent=holder;
            template.transform.localPosition=Vector3.zero;template.transform.localRotation=Quaternion.identity;template.transform.localScale=Vector3.one;

            FXElement sourceElement=donor.GetComponent<FXElement>();
            FXElement element=template.AddComponent<FXElement>();
            element.FXType=sourceElement.FXType;element.OnParentDespawn=sourceElement.OnParentDespawn;
            CopyTimer(donor,template);
            FXProxyVolumeDefinition sourceProxy=donor.GetComponent<FXProxyVolumeDefinition>();
            if(sourceProxy==null)throw new InvalidOperationException("Native light proxy is missing");
            FXProxyVolumeDefinition proxy=template.AddComponent<FXProxyVolumeDefinition>();
            CopyFields(sourceProxy,proxy,new string[]{"m_OverrideGlobalLOD","m_ProxyVolumeSize","m_LODThresholds","m_ContinuousLODUpdates"});
            CopyRootParticlePolicy(donor,template);

            Transform spot=CloneLeaf(donor,spotName,template.transform);
            Light light=spot.GetComponent<Light>();
            if(light==null||light.type!=LightType.Spot||light.shadows!=LightShadows.None||light.cookie==null)
                throw new InvalidOperationException("Native shadowless spot/cookie is missing: "+spotName);
            if(index<2)light.cookie=cookie;
            spot.localPosition=Vector3.zero;
            spot.localRotation=Quaternion.FromToRotation(Vector3.forward,Vector3.up);spot.localScale=Vector3.one;

            Transform beam=CloneLeaf(donor,beamName,template.transform);
            MeshFilter filter=beam.GetComponent<MeshFilter>();
            Renderer renderer=beam.GetComponent<Renderer>();
            string materialName=index<2?"FX_MAT_Unit_Headlight_Geo_Carrier":"FX_MAT_Unit_Headlight_Geo_Escort";
            if(filter==null||filter.sharedMesh==null||filter.sharedMesh.name!="FX_GEO_Resource_Beam_01"||renderer==null||renderer.sharedMaterial==null||renderer.sharedMaterial.name!=materialName)
                throw new InvalidOperationException("Native cone mesh/material identity changed: "+beamName);
            Bounds bounds=filter.sharedMesh.bounds;
            if(Mathf.Abs(bounds.min.y+5f)>.001f||Mathf.Abs(bounds.max.y)>.001f)
                throw new InvalidOperationException("Native cone emission axis/bounds changed");
            beam.localPosition=spot.localPosition;beam.localRotation=Quaternion.FromToRotation(Vector3.down,Vector3.up);
            // Front marker +Y faces forward and +Z faces down in the hull frame.
            if(index<2)beam.localRotation=Quaternion.AngleAxis(FrontBeamDownwardDegrees,Vector3.right)*beam.localRotation;
            // Keep each original cone's dimensions, mesh, material and shader.
            foreach(string glowName in glowNames)
            {
                Transform glow=CloneLeaf(donor,glowName,template.transform);
                ParticleSystem particles=glow.GetComponent<ParticleSystem>();
                ParticleSystemRenderer glowRenderer=glow.GetComponent<ParticleSystemRenderer>();
                if(particles==null||glowRenderer==null||glowRenderer.sharedMaterial==null)
                    throw new InvalidOperationException("Native light glow particles are missing: "+glowName);
                glow.localPosition=spot.localPosition;
                particles.Stop();particles.Clear();
            }
            template.SetActive(true);CheckTemplate(template.transform,index);
            return template.transform;
        }

        static void CopyTimer(Transform donor,GameObject template)
        {
            FXDespawnTimerEvent source=donor.GetComponent<FXDespawnTimerEvent>();
            if(source==null||source.EventTrigger!=FXTriggerEventFlags.Ungrouped||
                (float)Field(typeof(FXDespawnTimerEvent),"m_DespawnTimer").GetValue(source)!=0f||
                !(bool)Field(typeof(FXDespawnTimerEvent),"m_StopFXEmissionOnTimerStart").GetValue(source))
                throw new InvalidOperationException("Native light ungroup/despawn timer contract changed");
            FXDespawnTimerEvent timer=template.AddComponent<FXDespawnTimerEvent>();
            timer.EventTrigger=source.EventTrigger;
            CopyFields(source,timer,new string[]{"m_DespawnTimer","m_StopFXEmissionOnTimerStart"});
            timer.enabled=source.enabled;
        }

        static void CopyRootParticlePolicy(Transform donor,GameObject template)
        {
            FXLODSubcomponent sourceLOD=donor.GetComponent<FXLODSubcomponent>();
            if(sourceLOD!=null)CopyFields(sourceLOD,template.AddComponent<FXLODSubcomponent>(),new string[]{"m_ActivateAt"});
            FXLODParticleNLIPSScale sourceScale=donor.GetComponent<FXLODParticleNLIPSScale>();
            if(sourceScale!=null)
            {
                if(sourceLOD==null)throw new InvalidOperationException("Native light particle scale has no LOD subcomponent");
                CopyFields(sourceScale,template.AddComponent<FXLODParticleNLIPSScale>(),new string[]{"m_ScaleDuringLODs","m_AdditionalScaleFactor","m_ApplyToChildParticleSystems"});
            }
        }

        static Transform CloneLeaf(Transform donor,string name,Transform parent)
        {
            Transform source=donor.Find(name);
            if(source==null||source.childCount!=0)throw new InvalidOperationException("Native light optical leaf missing: "+name);
            foreach(MonoBehaviour script in source.GetComponents<MonoBehaviour>())
                if(!(script is FXLODSubcomponent))throw new InvalidOperationException("Unexpected native optical script: "+script.GetType().FullName);
            GameObject child=(GameObject)UnityEngine.Object.Instantiate(source.gameObject);
            child.SetActive(false);child.name=source.name;child.hideFlags=HideFlags.HideAndDontSave;
            child.transform.parent=parent;child.transform.localPosition=source.localPosition;
            child.transform.localRotation=source.localRotation;child.transform.localScale=source.localScale;
            child.SetActive(source.gameObject.activeSelf);
            return child.transform;
        }

        static FieldInfo Field(Type type,string name)
        {
            FieldInfo field=type.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(field==null)throw new InvalidOperationException("Native lighting serialized field missing: "+type.Name+"."+name);
            return field;
        }

        static void CopyFields(object source,object destination,string[] names)
        {
            foreach(string name in names)
            {
                FieldInfo field=Field(source.GetType(),name);
                object value=field.GetValue(source);
                if(value!=null&&!field.FieldType.IsValueType&&field.FieldType!=typeof(string))value=Memberwise.Invoke(value,null);
                field.SetValue(destination,value);
            }
        }

        static void CheckTemplate(Transform template,int index)
        {
            FXElement element=template==null?null:template.GetComponent<FXElement>();
            FXElement.FXCategory category=index<2?FXElement.FXCategory.UnitLightsLarge:FXElement.FXCategory.UnitLightsSmall;
            if(template==null||!template.gameObject.activeSelf||template.gameObject.activeInHierarchy||template.parent==null||template.parent.gameObject.activeSelf||
                element==null||element.FXType!=category||template.GetComponent<FXProxyVolumeDefinition>()==null||template.GetComponent<FXDespawnTimerEvent>()==null||SoftCookie==null)
                throw new InvalidOperationException("Native light template pool/category/proxy/despawn contract changed");
            Light[] lights=template.GetComponentsInChildren<Light>(true);
            int particleCount=template.GetComponentsInChildren<ParticleSystem>(true).Length;
            if(lights.Length!=1||particleCount!=(index<2?1:2)||lights[0].shadows!=LightShadows.None||lights[0].cookie==null||
                (index<2&&(lights[0].cookie!=SoftCookie||lights[0].cookie.name!=SoftCookieName))||
                Vector3.Dot(lights[0].transform.localRotation*Vector3.forward,Vector3.up)<.999f||
                Mathf.Abs(lights[0].range-(index==0?500f:index==1?400f:30f))>.001f||
                Mathf.Abs(lights[0].intensity-(index<2?8f:4.69f))>.001f||
                Mathf.Abs(lights[0].spotAngle-(index==0?75.1f:index==1?99.2f:101.6f))>.001f)
                throw new InvalidOperationException("Native light optical count/axis/photometry contract changed");
        }
    }
}
