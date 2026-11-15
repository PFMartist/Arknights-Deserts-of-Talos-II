using System;
using System.Reflection;
using BBI.Unity.Core.World;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.FX;
using BBI.Unity.Game.Utility;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Visual policy only. Research/deployment epochs remain simulation-owned.
    internal static class RhodesModuleWarningProfile
    {
        internal const float DegreesPerSecond=180f;
        internal static bool Active(RhodesDeploymentSnapshot state,int tick,float sub)
        {
            if(state.Branch!=1&&state.Branch!=2)return false;
            if(state.Kind==1&&state.StartTick>=0&&tick>=state.StartTick&&
                RhodesDeploymentProfile.AgeTicks(state.StartTick,tick,sub)<RhodesDeploymentProfile.DurationTicks(state.Branch,1))return true;
            return state.Branch==2&&state.TargetLevel>=2&&!state.AftReady&&state.AftStartTick>=0&&tick>=state.AftStartTick&&
                RhodesDeploymentProfile.AgeTicks(state.AftStartTick,tick,sub)<RhodesDeploymentProfile.GunneryAftTicks;
        }
        internal static float Yaw(int tick,float sub,int corner)
        {
            if(float.IsNaN(sub)||float.IsInfinity(sub))sub=0f;
            // Modulo before float conversion: long matches retain smooth motion.
            return ((tick%16)+RhodesDeploymentProfile.Clamp(sub))*22.5f+corner*90f;
        }
    }

    // Four FIXED hull-rim mounts. One native pooled updater, driven by the
    // existing module presentation host; no added MonoBehaviour/Update/Light.
    internal sealed class RhodesModuleWarningLights
    {
        static readonly string[] Names={"EF_Rhodes_Warning_FL","EF_Rhodes_Warning_FR","EF_Rhodes_Warning_AL","EF_Rhodes_Warning_AR"};
        internal static Vector3 FrontLeft {get{return new Vector3(-54f,104.516f,90f);}}
        internal static Vector3 FrontRight {get{return new Vector3(86f,104.215f,90f);}}
        internal static Vector3 AftLeft {get{return new Vector3(-54f,104.516f,-101f);}}
        internal static Vector3 AftRight {get{return new Vector3(86f,104.215f,-101f);}}
        static readonly MethodInfo Memberwise=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly FieldInfo TimerField=typeof(FXDespawnTimerEvent).GetField("m_DespawnTimer",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        static readonly FieldInfo StopField=typeof(FXDespawnTimerEvent).GetField("m_StopFXEmissionOnTimerStart",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        static EffectDefinition Definition;
        static bool Warned;
        readonly UnitView View;
        readonly Transform[] Mounts;
        readonly UnitFXInputs Inputs;
        readonly UnitFXUpdate Effects;
        bool Live;

        internal static void Prepare(UnitViewAttributes view,EffectDefinition signalDonor,RhodesCarrierBinding binding)
        {
            try
            {
                Transform fixedHull=binding.FindNode("ROOT_Rhodes");
                if(fixedHull==null||!fixedHull.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("Fixed module rim root is absent");
                if(Definition==null)
                {
                    Transform beam=null;
                    foreach(EffectDefinition item in view.UnitEffectDefs)
                        if(item!=null&&item.Name=="EF_Rhodes_NativeLight_V07_LIGHT_WORK_A"&&item.FXPrefab!=null)beam=item.FXPrefab.Find("MainBeam 1");
                    if(beam==null)throw new InvalidOperationException("Retained native beam donor is absent");
                    Transform template=BuildTemplate(beam,signalDonor.FXPrefab,binding.GeometryRoot.gameObject.layer);
                    Definition=(EffectDefinition)Memberwise.Invoke(signalDonor,null);
                    Definition.Name="EF_Rhodes_ModuleConversionWarning";Definition.FXPrefab=template;
                    Definition.BoneRef=Names;Definition.ActiveAtLOD=(LODGroupController.ModelLODSelection)7;
                    Definition.Sound="_UnSet_";Definition.PolyGroup="";Definition.RaycastDir=(RaycastDirection)0;
                    Definition.FollowNLIPSScale=false;Definition.InheritRaycastNormal=false;
                }
                Vector3[] positions={FrontLeft,FrontRight,AftLeft,AftRight};
                binding.ModuleWarningMarkers=new Transform[4];
                for(int i=0;i<4;i++)
                {
                    Transform marker=fixedHull.Find(Names[i]);
                    if(marker==null){GameObject go=new GameObject(Names[i]);go.layer=binding.GeometryRoot.gameObject.layer;marker=go.transform;marker.parent=fixedHull;}
                    marker.localPosition=positions[i];marker.localRotation=Quaternion.identity;marker.localScale=Vector3.one;
                    binding.ModuleWarningMarkers[i]=marker;
                }
                Runtime.Log("RHODES_MODULE_WARNINGS_PREPARED fixedHullRimCorners=4; followsMovingDeck=False; amberOpposedSweeps=True; rotationDegreesPerSecond=180; deploymentOnly=True; nativePooledUpdater=True; newLights=0; newMaterials=0; newTextures=0; newFrameCallbacks=0; visualAcceptance=False");
            }
            catch(Exception error){if(!Warned){Warned=true;Runtime.Log("RHODES_MODULE_WARNINGS_UNAVAILABLE otherLightingRetained=True; error="+error.Message);}}
        }
        internal static RhodesModuleWarningLights Attach(UnitView view,RhodesCarrierBinding binding)
        {
            if(Definition==null||binding.ModuleWarningMarkers==null||binding.ModuleWarningMarkers.Length!=4)return null;
            foreach(Transform mount in binding.ModuleWarningMarkers)if(mount==null)return null;
            return new RhodesModuleWarningLights(view,binding.ModuleWarningMarkers);
        }
        RhodesModuleWarningLights(UnitView view,Transform[] mounts)
        {
            View=view;Mounts=mounts;Inputs=new UnitFXInputs();
            FXHelper.InternalFXDefinition definition=FXHelper.CreateInternalDefFromNewFXDef(Definition,false,(RaycastDirection)0,false,false);
            Effects=new UnitFXUpdate(view,definition,Inputs,Definition.FXPrefab,mounts);
        }
        internal void Step(RhodesDeploymentSnapshot state,int tick,float sub,bool shown)
        {
            if(!shown||!RhodesModuleWarningProfile.Active(state,tick,sub)){Hide();return;}
            FXManager manager=FXManager.Manager;
            if(manager==null||!manager.FrillTypeEnabled(FrillType.Always)){Hide();return;}
            int lod=NativeUnitLodAdapter.CurrentLod(View);
            if(lod<0||lod>=3){Hide();return;}
            for(int i=0;i<Mounts.Length;i++)Mounts[i].localRotation=Quaternion.AngleAxis(RhodesModuleWarningProfile.Yaw(tick,sub,i),Vector3.up);
            Inputs.UnitLOD=(LODGroupController.ModelLOD)lod;
            Effects.ToggleFX(true,Inputs.UnitLOD,null);
            Live=true;
            if(Effects.IsActive)Effects.Update(0f);
        }
        internal void Hide(){if(!Live)return;Effects.Reset();Live=false;}

        static Transform BuildTemplate(Transform nativeBeam,Transform nativeSignals,int layer)
        {
            MeshFilter sourceFilter=nativeBeam.GetComponent<MeshFilter>();Renderer sourceRenderer=nativeBeam.GetComponent<Renderer>();
            if(sourceFilter==null||sourceFilter.sharedMesh==null||sourceFilter.sharedMesh.name!="FX_GEO_Resource_Beam_01"||
                sourceRenderer==null||sourceRenderer.sharedMaterial==null||sourceRenderer.sharedMaterial.name!="FX_MAT_Unit_Headlight_Geo_Carrier"||
                sourceRenderer.sharedMaterial.shader==null||!sourceRenderer.sharedMaterial.shader.isSupported)
                throw new InvalidOperationException("Native warning beam mesh/material contract changed");
            FXDespawnTimerEvent sourceTimer=nativeSignals.GetComponent<FXDespawnTimerEvent>();
            if(sourceTimer==null||TimerField==null||StopField==null||sourceTimer.EventTrigger!=FXTriggerEventFlags.Ungrouped||
                (float)TimerField.GetValue(sourceTimer)!=0f||!(bool)StopField.GetValue(sourceTimer))throw new InvalidOperationException("Native warning despawn contract changed");
            GameObject holder=new GameObject("EF_Rhodes_ModuleWarningTemplateHolder");holder.SetActive(false);holder.hideFlags=HideFlags.HideAndDontSave;
            GameObject template=new GameObject("EF_Rhodes_ModuleWarningTemplate");template.SetActive(false);template.layer=layer;
            template.hideFlags=HideFlags.HideAndDontSave;template.transform.parent=holder.transform;
            Mesh tinted=null;
            try
            {
                FXElement element=template.AddComponent<FXElement>();element.OnParentDespawn=FXElement.ParentDespawnType.Despawn;element.FXType=FXElement.FXCategory.UnitLightsLarge;
                FXDespawnTimerEvent timer=template.AddComponent<FXDespawnTimerEvent>();timer.EventTrigger=sourceTimer.EventTrigger;
                TimerField.SetValue(timer,0f);StopField.SetValue(timer,true);
                // One private tinted mesh shared by all eight cones. The native
                // UVs, normals, texture falloff and shared material stay intact.
                tinted=(Mesh)UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);tinted.name="EF_Rhodes_WarningBeam_Amber";tinted.hideFlags=HideFlags.HideAndDontSave;
                Color[] colors=tinted.colors;
                bool hasColors=colors.Length==tinted.vertexCount;
                if(!hasColors&&colors.Length!=0)throw new InvalidOperationException("Native warning beam vertex colors are malformed");
                if(!hasColors)colors=new Color[tinted.vertexCount]; // Native mesh has no color channel: implicit white.
                for(int i=0;i<colors.Length;i++){float intensity=hasColors?Mathf.Max(colors[i].r,Mathf.Max(colors[i].g,colors[i].b)):1f;colors[i]=new Color(intensity,intensity*.42f,intensity*.035f,hasColors?colors[i].a:1f);}
                tinted.colors=colors;
                for(int i=0;i<2;i++)
                {
                    GameObject cone=new GameObject("AmberSweep_"+i);cone.layer=layer;cone.transform.parent=template.transform;
                    cone.transform.localPosition=Vector3.zero;cone.transform.localRotation=Quaternion.AngleAxis(i*180f,Vector3.up)*Quaternion.FromToRotation(Vector3.down,Vector3.forward);
                    cone.transform.localScale=new Vector3(1.1f,3.2f,.75f);
                    cone.AddComponent<MeshFilter>().sharedMesh=tinted;MeshRenderer renderer=cone.AddComponent<MeshRenderer>();renderer.sharedMaterial=sourceRenderer.sharedMaterial;
                    renderer.receiveShadows=false;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                Transform sourceGlow=nativeSignals.Find("Rear_Light 1/Glow_Soft");
                if(sourceGlow==null||sourceGlow.childCount!=0||sourceGlow.GetComponents<MonoBehaviour>().Length!=0)throw new InvalidOperationException("Native warning glow leaf differs");
                GameObject glow=(GameObject)UnityEngine.Object.Instantiate(sourceGlow.gameObject);glow.SetActive(false);glow.layer=layer;glow.name="AmberLampGlow";
                glow.transform.parent=template.transform;glow.transform.localPosition=Vector3.zero;glow.transform.localRotation=Quaternion.identity;glow.transform.localScale=Vector3.one;
                ParticleSystem ps=glow.GetComponent<ParticleSystem>();ParticleSystemRenderer pr=glow.GetComponent<ParticleSystemRenderer>();
                if(ps==null||pr==null||pr.sharedMaterial==null||pr.sharedMaterial.name!="FX_MAT_Flash_02_LessSoft")throw new InvalidOperationException("Native warning glow material differs");
                ps.Stop();ps.Clear();ps.startColor=new Color(1f,.42f,.035f,.8f);ps.startSize=5f;ps.startLifetime=.18f;ps.startSpeed=0f;ps.startDelay=0f;
                ps.emissionRate=12f;ps.maxParticles=4;ps.loop=true;ps.playOnAwake=true;ps.simulationSpace=ParticleSystemSimulationSpace.Local;
                glow.SetActive(true);template.SetActive(true);UnityEngine.Object.DontDestroyOnLoad(holder);
                return template.transform;
            }
            catch{UnityEngine.Object.DestroyImmediate(holder);if(tinted!=null)UnityEngine.Object.DestroyImmediate(tinted);throw;}
        }
    }
}
