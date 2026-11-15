using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Globalization;
using UnityEngine.Rendering;
using BBI.Unity.Core.Data;
using BBI.Unity.Core.Rendering;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Scoped dusk-lighting preset for the Rhodes carrier terrain lab only. Native
    // SetRenderSettings creates our separate rig from a PRIVATE lighting asset.
    // The original rig objects/probes are parked intact, never rebuilt/deleted.
    public static class RhodesTestLighting
    {
        static readonly List<Entry> Entries=new List<Entry>();
        static readonly MethodInfo Memberwise=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        static bool Installed,Sensors,HaveGlobals,HaveAmbient;
        static Profile ActiveProfile;
        static AmbientState OriginalAmbient;
        static Globals OriginalGlobals,DuskGlobals;
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
        // Install-time configuration only. No per-frame discovery, relighting or
        // RenderSettings writes. This class replaces the old FlatLabDaylight calls.
        sealed class Profile
        {
            public bool Enabled=true,AmbientEnabled=true;
            public float KeyElevation=24f,KeyAzimuth=-35f,KeyIntensity=.72f;
            public float FillIntensity=.16f,TopFillIntensity=.08f,ShadowStrength=.52f,AmbientIntensity=.55f;
            public Color KeyColour=new Color(1f,.84f,.67f,1f);
            public Color FillColour=new Color(.66f,.77f,1f,1f);
            public Color TopFillColour=new Color(.76f,.83f,1f,1f);
            public Color Sky=new Color(.18f,.22f,.29f,1f),Equator=new Color(.105f,.12f,.15f,1f),Ground=new Color(.055f,.05f,.045f,1f);
        }
        struct AmbientState
        {
            public AmbientMode Mode;public Color Flat,Sky,Equator,Ground;
            public float Intensity;public SphericalHarmonicsL2 Probe;
        }
        static AmbientState CaptureAmbient()
        {
            AmbientState value=new AmbientState();value.Mode=RenderSettings.ambientMode;
            value.Flat=RenderSettings.ambientLight;value.Sky=RenderSettings.ambientSkyColor;
            value.Equator=RenderSettings.ambientEquatorColor;value.Ground=RenderSettings.ambientGroundColor;
            value.Intensity=RenderSettings.ambientIntensity;value.Probe=RenderSettings.ambientProbe;return value;
        }
        static void RestoreAmbient()
        {
            if(!HaveAmbient)return;
            RenderSettings.ambientMode=OriginalAmbient.Mode;RenderSettings.ambientLight=OriginalAmbient.Flat;
            RenderSettings.ambientSkyColor=OriginalAmbient.Sky;RenderSettings.ambientEquatorColor=OriginalAmbient.Equator;
            RenderSettings.ambientGroundColor=OriginalAmbient.Ground;RenderSettings.ambientIntensity=OriginalAmbient.Intensity;
            RenderSettings.ambientProbe=OriginalAmbient.Probe;
        }
        static void ApplyAmbient()
        {
            if(ActiveProfile==null||!ActiveProfile.AmbientEnabled)return;
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=ActiveProfile.Sky;
            RenderSettings.ambientEquatorColor=ActiveProfile.Equator;RenderSettings.ambientGroundColor=ActiveProfile.Ground;
            RenderSettings.ambientIntensity=ActiveProfile.AmbientIntensity;
        }
        static Profile ReadProfile()
        {
            Profile result=new Profile();string file=Path.Combine(CustomUnits.AssetDirectory,"rhodes_test_lighting.ini");
            if(!File.Exists(file))return result;
            FileInfo info=new FileInfo(file);if(info.Length>8192)throw new InvalidDataException("Rhodes test lighting profile exceeds8KiB");
            HashSet<string> seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(string raw in File.ReadAllLines(file))
            {
                string line=raw.Trim();if(line.Length==0||line.StartsWith("#",StringComparison.Ordinal)||line.StartsWith(";",StringComparison.Ordinal))continue;
                int equals=line.IndexOf('=');if(equals<=0)throw new InvalidDataException("Invalid Rhodes test lighting profile line");
                string key=line.Substring(0,equals).Trim().ToLowerInvariant(),value=line.Substring(equals+1).Trim();
                if(!seen.Add(key))throw new InvalidDataException("Duplicate Rhodes test lighting key: "+key);
                switch(key)
                {
                    case "enabled":result.Enabled=Boolean(value);break;
                    case "ambient_enabled":result.AmbientEnabled=Boolean(value);break;
                    case "key_elevation":result.KeyElevation=Number(value,10f,55f);break;
                    case "key_azimuth":result.KeyAzimuth=Number(value,-180f,180f);break;
                    case "key_intensity":result.KeyIntensity=Number(value,.25f,1.2f);break;
                    case "fill_intensity":result.FillIntensity=Number(value,0f,.5f);break;
                    case "top_fill_intensity":result.TopFillIntensity=Number(value,0f,.35f);break;
                    case "shadow_strength":result.ShadowStrength=Number(value,0f,.8f);break;
                    case "ambient_intensity":result.AmbientIntensity=Number(value,0f,1f);break;
                    case "key_colour":result.KeyColour=Colour(value);break;
                    case "fill_colour":result.FillColour=Colour(value);break;
                    case "top_fill_colour":result.TopFillColour=Colour(value);break;
                    case "ambient_sky":result.Sky=Colour(value);break;
                    case "ambient_equator":result.Equator=Colour(value);break;
                    case "ambient_ground":result.Ground=Colour(value);break;
                    default:throw new InvalidDataException("Unknown Rhodes test lighting key: "+key);
                }
            }
            return result;
        }
        static bool Boolean(string value)
        {bool result;if(!bool.TryParse(value,out result))throw new InvalidDataException("Invalid lighting boolean");return result;}
        static float Number(string value,float min,float max)
        {float result;if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out result)||float.IsNaN(result)||float.IsInfinity(result)||result<min||result>max)throw new InvalidDataException("Lighting value outside reviewed range");return result;}
        static Color Colour(string value)
        {string[] parts=value.Split(',');if(parts.Length!=3)throw new InvalidDataException("Lighting colour requires RGB triplet");return new Color(Number(parts[0],0f,1f),Number(parts[1],0f,1f),Number(parts[2],0f,1f),1f);}
        // This Unity build exposes no Shader.GetGlobalColor/GetGlobalVector.
        // Derive the three values from the unique native owner exactly as
        // GenerateLightRigInstance does, rather than guessing GPU globals.
        static Globals NativeGlobals(EnvironmentalLightingAsset asset,Transform rig)
        {
            Light key=null;foreach(Light light in rig.GetComponentsInChildren<Light>(true))
                if(light!=null&&light.gameObject.tag==SetRenderSettings.kKeyLightTag)
                {if(key!=null)throw new InvalidOperationException("Rhodes test lighting native key light is not unique");key=light;}
            if(key==null)throw new InvalidOperationException("Rhodes test lighting native key light is absent");
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
                ActiveProfile=ReadProfile();
                if(!ActiveProfile.Enabled){ActiveProfile=null;Runtime.Log("RHODES_TEST_LIGHTING_DISABLED nativePresentationRetained=True");return;}
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
                    Runtime.Log("RHODES_TEST_LIGHTING_SKIPPED nativeActiveLightRigOwners="+candidates.Count+"; uniqueOwnerRequired=True; nativePresentationRetained=True");return;
                }
                SetRenderSettings owner=candidates[0];Transform originalRig=NativeRead.Get(owner,"mLightRigInstance") as Transform;
                OriginalGlobals=NativeGlobals(owner.EnvironmentLighting,originalRig);HaveGlobals=true;
                OriginalAmbient=CaptureAmbient();HaveAmbient=true;
                InstallEntry(owner,owner.EnvironmentLighting,originalRig);
                DuskGlobals=NativeGlobals(Entries[0].Private,Entries[0].PrivateRig);Installed=true;Sensors=!sensors;
                SetSensors(sensors);
                Runtime.Log("RHODES_TEST_LIGHTING_INSTALLED nativeSettings="+Entries.Count+"; preset=dusk_v01; privateEnvironmentAsset=True; separateNativeGeneratedRig=True; originalRigInstancesPreserved=True; keyElevationDegrees="+ActiveProfile.KeyElevation+"; warmKeyIntensity="+ActiveProfile.KeyIntensity+"; ambientOverride="+ActiveProfile.AmbientEnabled+"; ambientIntensity="+ActiveProfile.AmbientIntensity+"; sensorNativeLightingAndAmbientRestored=True; fogSkyboxReflectionBackgroundUIAndFOWUntouched=True; noPerFrameDiscovery=True; runtimePixelAcceptancePending=True");
            }
            catch(Exception error)
            {
                Reset();Runtime.Log("RHODES_TEST_LIGHTING_FALLBACK nativePresentationRestored=True; error="+error);
            }
        }
        static void InstallEntry(SetRenderSettings settings,EnvironmentalLightingAsset original,Transform rig)
        {
            Entry entry=new Entry();entry.Settings=settings;entry.Original=original;entry.OriginalRig=rig;
            entry.OriginalProbe=NativeRead.Get(settings,"m_SceneLightProbeRig") as LightProbeGroup;
            entry.OriginalName=rig.name;entry.OriginalActive=rig.gameObject.activeSelf;
            entry.ParkedOriginalName="EF_FlatLab_OriginalLighting_"+settings.GetInstanceID();
            entry.ParkedPrivateName="EF_FlatLab_DuskLighting_"+settings.GetInstanceID();Entries.Add(entry);
            EnvironmentalLightingAsset copy=RhodesAssetClone59.CreateEmpty<EnvironmentalLightingAsset>();entry.Private=copy;
            copy.name="EF_FlatLab_Dusk_"+original.name;copy.hideFlags=HideFlags.HideAndDontSave;
            copy.Data=(EnvironmentalLightingAsset.EnvironmentalLighting)Memberwise.Invoke(original.Data,null);
            copy.Data.SceneShadowType=LightShadows.Soft;copy.Data.ShadowStrength=ActiveProfile.ShadowStrength;
            copy.Data.ParticleLightColour=ActiveProfile.KeyColour;
            copy.Data.ParticleAmbientLightColour=ActiveProfile.Equator;
            copy.Data.LightRig=new EnvironmentalLightingAsset.EnvironmentalLighting.LightDescriptor[]
            {
                Light(ActiveProfile.KeyColour,ActiveProfile.KeyIntensity,ActiveProfile.KeyElevation,ActiveProfile.KeyAzimuth),
                Light(ActiveProfile.FillColour,ActiveProfile.FillIntensity,35f,145f),
                Light(ActiveProfile.TopFillColour,ActiveProfile.TopFillIntensity,75f,45f)
            };
            if(object.ReferenceEquals(copy.Data,original.Data)||object.ReferenceEquals(copy.Data.LightRig,original.Data.LightRig))
                throw new InvalidOperationException("Rhodes test lighting lighting data is not private");
            rig.name=entry.ParkedOriginalName;rig.gameObject.SetActive(false);settings.EnvironmentLighting=copy;
            // GenerateLightRigInstance otherwise reparents the original scene
            // probe into the new rig. A separate default group is safe to own.
            NativeRead.Set(settings,"m_SceneLightProbeRig",null);
            try
            {
                NativeRead.Call(settings,"RebuildLightRig");entry.PrivateRig=NativeRead.Get(settings,"mLightRigInstance") as Transform;
                if(entry.PrivateRig==null||entry.PrivateRig==rig)throw new InvalidOperationException("Rhodes test lighting did not create a separate native light rig");
                LightProbeGroup[] probes=entry.PrivateRig.GetComponentsInChildren<LightProbeGroup>(true);
                if(probes.Length!=1)throw new InvalidOperationException("Rhodes test lighting requires one private generated probe group");
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
            WriteGlobals(sensors?OriginalGlobals:DuskGlobals);
            if(sensors)RestoreAmbient();else ApplyAmbient();Sensors=sensors;
            Runtime.Log("RHODES_TEST_LIGHTING_TRANSITION sensors="+sensors+"; nativeSensorRig="+sensors+"; cachedRigSwapOnly=True; nativeAmbientRestored="+sensors+"; backgroundFogSkyboxUIAndFOWUnchanged=True");
        }
        public static void Reset()
        {
            bool hadState=Entries.Count>0||HaveGlobals||HaveAmbient;
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
            RestoreAmbient();
            Entries.Clear();Installed=Sensors=HaveGlobals=HaveAmbient=false;OriginalGlobals=new Globals();DuskGlobals=new Globals();
            OriginalAmbient=new AmbientState();ActiveProfile=null;
            if(hadState)Runtime.Log("RHODES_TEST_LIGHTING_RESET originalAssetRigProbeAmbientAndShaderGlobalsRestored=True; ownedRigAndAssetReleased=True");
        }
    }
}
