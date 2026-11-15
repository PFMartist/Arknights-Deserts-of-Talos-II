using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Unity.Core.Data;
using BBI.Unity.Core.Rendering;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Scoped presentation override for the Rhodes flat-map test only. Native
    // SetRenderSettings creates our separate rig from a PRIVATE lighting asset.
    // The original rig objects/probes are parked intact, never rebuilt/deleted.
    public static class FlatLabDaylight
    {
        static readonly List<Entry> Entries=new List<Entry>();
        static readonly MethodInfo Memberwise=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        static bool Installed,Sensors,HaveGlobals;
        static Globals OriginalGlobals,DaylightGlobals;
        sealed class Entry
        {
            public SetRenderSettings Settings;
            public EnvironmentalLightingAsset Original,Private;
            public Transform OriginalRig,PrivateRig;
            public LightProbeGroup OriginalProbe,PrivateProbe;
            public string OriginalName,ParkedOriginalName,ParkedPrivateName;
            public bool OriginalActive;
        }
        struct Globals
        {
            public Color Key,ParticleAmbient;
            public Vector4 Direction;
        }
        // This Unity build exposes no Shader.GetGlobalColor/GetGlobalVector.
        // Derive the three values from the unique native owner exactly as
        // GenerateLightRigInstance does, rather than guessing GPU globals.
        static Globals NativeGlobals(EnvironmentalLightingAsset asset,Transform rig)
        {
            Light key=null;foreach(Light light in rig.GetComponentsInChildren<Light>(true))
                if(light!=null&&light.gameObject.tag==SetRenderSettings.kKeyLightTag)
                {if(key!=null)throw new InvalidOperationException("Flat daylight native key light is not unique");key=light;}
            if(key==null)throw new InvalidOperationException("Flat daylight native key light is absent");
            Globals value=new Globals();value.Key=asset.Data.ParticleLightColour;
            value.ParticleAmbient=asset.Data.ParticleAmbientLightColour;value.Direction=key.transform.rotation*(-Vector3.forward);return value;
        }
        static void WriteGlobals(Globals value)
        {
            Shader.SetGlobalColor("_KeyLightColor",value.Key);Shader.SetGlobalColor("_ParticleAmbientLightColor",value.ParticleAmbient);
            Shader.SetGlobalVector("_KeyLightDir",value.Direction);
        }
        public static void Install(string mapKey,IEnumerable<GameObject> sceneRoots,bool sensors)
        {
            if(Installed||!Runtime.RhodesCarrier||Runtime.HubRegion||Runtime.UnitsOnly||mapKey!=CustomFlatMap.MapKey)return;
            try
            {
                HashSet<SetRenderSettings> seen=new HashSet<SetRenderSettings>();
                List<SetRenderSettings> candidates=new List<SetRenderSettings>();
                foreach(GameObject root in sceneRoots)
                    if(root!=null)foreach(SetRenderSettings settings in root.GetComponentsInChildren<SetRenderSettings>(true))
                    {
                        if(settings==null||!settings.enabled||!settings.gameObject.activeInHierarchy||!seen.Add(settings))continue;
                        EnvironmentalLightingAsset original=settings.EnvironmentLighting;
                        Transform rig=NativeRead.Get(settings,"mLightRigInstance") as Transform;
                        if(original==null||original.Data==null||rig==null)continue;
                        candidates.Add(settings);
                    }
                if(candidates.Count!=1)
                {
                    Runtime.Log("FLAT_DAYLIGHT_SKIPPED nativeActiveLightRigOwners="+candidates.Count+"; uniqueOwnerRequired=True; nativePresentationRetained=True");return;
                }
                SetRenderSettings owner=candidates[0];Transform originalRig=NativeRead.Get(owner,"mLightRigInstance") as Transform;
                OriginalGlobals=NativeGlobals(owner.EnvironmentLighting,originalRig);HaveGlobals=true;
                InstallEntry(owner,owner.EnvironmentLighting,originalRig);
                DaylightGlobals=NativeGlobals(Entries[0].Private,Entries[0].PrivateRig);Installed=true;Sensors=false;
                if(sensors)SetSensors(true);
                Runtime.Log("FLAT_DAYLIGHT_INSTALLED nativeSettings="+Entries.Count+"; privateEnvironmentAsset=True; separateNativeGeneratedRig=True; originalRigInstancesPreserved=True; keyElevationDegrees=58; warmKeyIntensity=1.15; softFillIntensities=0.38,0.22; sensorNativeLightingRestored=True; fogSkyboxAmbientUIAndFOWUntouched=True; noPerFrameDiscovery=True; runtimePixelAcceptancePending=True");
            }
            catch(Exception error)
            {
                Reset();Runtime.Log("FLAT_DAYLIGHT_FALLBACK nativePresentationRestored=True; error="+error);
            }
        }
        static void InstallEntry(SetRenderSettings settings,EnvironmentalLightingAsset original,Transform rig)
        {
            Entry entry=new Entry();entry.Settings=settings;entry.Original=original;entry.OriginalRig=rig;
            entry.OriginalProbe=NativeRead.Get(settings,"m_SceneLightProbeRig") as LightProbeGroup;
            entry.OriginalName=rig.name;entry.OriginalActive=rig.gameObject.activeSelf;
            entry.ParkedOriginalName="EF_FlatLab_OriginalLighting_"+settings.GetInstanceID();
            entry.ParkedPrivateName="EF_FlatLab_DaylightLighting_"+settings.GetInstanceID();Entries.Add(entry);
            EnvironmentalLightingAsset copy=RhodesAssetClone59.CreateEmpty<EnvironmentalLightingAsset>();entry.Private=copy;
            copy.name="EF_FlatLab_Daylight_"+original.name;copy.hideFlags=HideFlags.HideAndDontSave;
            copy.Data=(EnvironmentalLightingAsset.EnvironmentalLighting)Memberwise.Invoke(original.Data,null);
            copy.Data.SceneShadowType=LightShadows.Soft;copy.Data.ShadowStrength=.58f;
            copy.Data.ParticleLightColour=new Color(1f,.95f,.88f,1f);copy.Data.ParticleAmbientLightColour=new Color(.32f,.38f,.44f,1f);
            copy.Data.LightRig=new EnvironmentalLightingAsset.EnvironmentalLighting.LightDescriptor[]
            {
                Light(new Color(1f,.95f,.87f,1f),1.15f,58f,-35f),
                Light(new Color(.76f,.85f,1f,1f),.38f,26f,145f),
                Light(new Color(.88f,.93f,1f,1f),.22f,82f,45f)
            };
            if(object.ReferenceEquals(copy.Data,original.Data)||object.ReferenceEquals(copy.Data.LightRig,original.Data.LightRig))
                throw new InvalidOperationException("Flat daylight lighting data is not private");
            rig.name=entry.ParkedOriginalName;rig.gameObject.SetActive(false);settings.EnvironmentLighting=copy;
            // GenerateLightRigInstance otherwise reparents the original scene
            // probe into the new rig. A separate default group is safe to own.
            NativeRead.Set(settings,"m_SceneLightProbeRig",null);
            try
            {
                NativeRead.Call(settings,"RebuildLightRig");entry.PrivateRig=NativeRead.Get(settings,"mLightRigInstance") as Transform;
                if(entry.PrivateRig==null||entry.PrivateRig==rig)throw new InvalidOperationException("Flat daylight did not create a separate native light rig");
                LightProbeGroup[] probes=entry.PrivateRig.GetComponentsInChildren<LightProbeGroup>(true);
                if(probes.Length!=1)throw new InvalidOperationException("Flat daylight requires one private generated probe group");
                entry.PrivateProbe=probes[0];
            }
            finally
            {
                NativeRead.Set(settings,"m_SceneLightProbeRig",entry.PrivateProbe!=null?entry.PrivateProbe:entry.OriginalProbe);
                if(entry.PrivateRig==null)
                    foreach(Transform node in settings.GetComponentsInChildren<Transform>(true))
                        if(node!=rig&&node.name==SetRenderSettings.kAutogeneratedLightRigName){entry.PrivateRig=node;break;}
            }
        }
        static EnvironmentalLightingAsset.EnvironmentalLighting.LightDescriptor Light(Color colour,float intensity,float elevation,float azimuth)
        {
            EnvironmentalLightingAsset.EnvironmentalLighting.LightDescriptor value=new EnvironmentalLightingAsset.EnvironmentalLighting.LightDescriptor();
            value.LightColour=colour;value.Intensity=intensity;value.Orientation=Quaternion.Euler(elevation,azimuth,0f);return value;
        }
        public static void SetSensors(bool sensors)
        {
            if(!Installed||Sensors==sensors)return;
            foreach(Entry entry in Entries)
            {
                if(entry.Settings==null)continue;
                if(entry.PrivateRig!=null){entry.PrivateRig.gameObject.SetActive(!sensors);entry.PrivateRig.name=sensors?entry.ParkedPrivateName:entry.OriginalName;}
                if(entry.OriginalRig!=null){entry.OriginalRig.gameObject.SetActive(sensors&&entry.OriginalActive);entry.OriginalRig.name=sensors?entry.OriginalName:entry.ParkedOriginalName;}
                entry.Settings.EnvironmentLighting=sensors?entry.Original:entry.Private;
                NativeRead.Set(entry.Settings,"mLightRigInstance",sensors?entry.OriginalRig:entry.PrivateRig);
                NativeRead.Set(entry.Settings,"m_SceneLightProbeRig",sensors?entry.OriginalProbe:entry.PrivateProbe);
            }
            WriteGlobals(sensors?OriginalGlobals:DaylightGlobals);Sensors=sensors;
            Runtime.Log("FLAT_DAYLIGHT_TRANSITION sensors="+sensors+"; nativeSensorRig="+sensors+"; cachedRigSwapOnly=True; fogBackgroundUIAndFOWUnchanged=True");
        }
        public static void Reset()
        {
            bool hadState=Entries.Count>0||HaveGlobals;
            foreach(Entry entry in Entries)
            {
                if(entry.PrivateRig!=null){entry.PrivateRig.gameObject.SetActive(false);entry.PrivateRig.name=entry.ParkedPrivateName;}
                if(entry.Settings!=null)
                {
                    if(entry.Settings.EnvironmentLighting==entry.Private||entry.Settings.EnvironmentLighting==entry.Original)
                    {
                        entry.Settings.EnvironmentLighting=entry.Original;
                        NativeRead.Set(entry.Settings,"mLightRigInstance",entry.OriginalRig);
                    }
                    NativeRead.Set(entry.Settings,"m_SceneLightProbeRig",entry.OriginalProbe);
                }
                if(entry.OriginalRig!=null){entry.OriginalRig.name=entry.OriginalName;entry.OriginalRig.gameObject.SetActive(entry.OriginalActive);}
                if(entry.PrivateRig!=null)UnityEngine.Object.Destroy(entry.PrivateRig.gameObject);
                if(entry.Private!=null)UnityEngine.Object.Destroy(entry.Private);
            }
            if(HaveGlobals)WriteGlobals(OriginalGlobals);
            Entries.Clear();Installed=Sensors=HaveGlobals=false;OriginalGlobals=new Globals();DaylightGlobals=new Globals();
            if(hadState)Runtime.Log("FLAT_DAYLIGHT_RESET originalAssetRigProbeAndShaderGlobalsRestored=True; ownedRigAndAssetReleased=True");
        }
    }
}
