using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using BBI.Core.ComponentModel;
using BBI.Unity.Game.World;
using EndfieldDoK.MapUnitLab;
using UnityEngine;

namespace EndfieldDoK.ModelIntake
{
    // Shared by battle and the front-end preview. This reads only the explicitly
    // supplied package and its adjacent private assets; no registry, commander,
    // simulation, lifecycle hook, Update, scene scan or material-per-unit clone.
    public sealed class RhodesTeamColourProfile
    {
        internal readonly Dictionary<string,RhodesTeamColourRule> Materials=new Dictionary<string,RhodesTeamColourRule>(StringComparer.Ordinal);
        internal readonly Dictionary<string,Texture2D> Textures=new Dictionary<string,Texture2D>(StringComparer.Ordinal);
        internal readonly Dictionary<Material,Texture> OriginalMasks=new Dictionary<Material,Texture>();
        internal readonly Dictionary<Material,Material> TrackMaterials=new Dictionary<Material,Material>();
        internal string Directory;
        internal bool AuthoredOnly,AuthoredOverlayReported;
        internal int Pixels;
        internal int MaximumMasks=4,MaximumTotalPixels=2097152;
    }
    internal sealed class RhodesTeamColourRule
    {
        internal string File,Hash;
        internal int Width,Height;
    }
    public static class RhodesTeamColours
    {
        // Bounded, read-only diagnostic at the existing unit bind event. Four
        // U01 spawns per process, at most two hopper materials each; no Update,
        // timer, scene scan, pixel buffer readback or recurring log traffic.
        internal const int HarvesterColourCaptureLimit=4;
        static readonly HashSet<Entity> CapturedHarvesters=new HashSet<Entity>();
        static readonly MethodInfo PropertyBlockGetTexture=typeof(MaterialPropertyBlock).GetMethod("GetTexture",new Type[]{typeof(string)});
        // Temporary authored-colour test. Restoring true re-enables matching
        // RITC profiles; a changed model still needs its exact-hash rebind.
        // No profile/texture asset is deleted.
        public static readonly bool CustomColoursEnabled=false;
        const int MaximumMaskPixels=1048576;
        static readonly string[] UnitKeys={"RI_U01_HARVESTER","RI_U02_FIELD_SURVEY","RI_U03_ENGINEERING","RI_U04_LIGHT_ESCORT","RI_U05_SUPPRESSION","RI_U06_RAILGUN","RI_U07_AIR_DEFENCE","RI_U08_TILT_STRIKE","RI_U09_ARTILLERY"};
        static readonly string[] AirWingKeys={"EF_Rhodes_Interceptor_MP","EF_Rhodes_HeavyBomber_MP"};

        internal static void RecordHarvesterColours(UnitView view,string unitKey,Renderer[] geometry,Renderer donor)
        {
            if(unitKey!="RI_U01_HARVESTER"||view==null||geometry==null||view.EntityID.Equals(Entity.None)||CapturedHarvesters.Count>=HarvesterColourCaptureLimit||!CapturedHarvesters.Add(view.EntityID))return;
            try
            {
                MaterialPropertyBlock block=new MaterialPropertyBlock();HashSet<string> names=new HashSet<string>(StringComparer.Ordinal);int reports=0;
                foreach(Renderer renderer in geometry)
                {
                    if(renderer==null)continue;Material material=renderer.sharedMaterial;
                    if(material==null||!material.name.StartsWith("V17SRC_U01_U01_PBR_Hopper",StringComparison.Ordinal)||!names.Add(material.name))continue;
                    block.Clear();renderer.GetPropertyBlock(block);
                    Texture sharedOverlay=material.HasProperty("_OverlayMask")?material.GetTexture("_OverlayMask"):null;
                    Texture blockOverlay=PropertyBlockGetTexture==null?null:PropertyBlockGetTexture.Invoke(block,new object[]{"_OverlayMask"}) as Texture;
                    Texture effectiveOverlay=blockOverlay==null?sharedOverlay:blockOverlay;Texture2D pixel=effectiveOverlay as Texture2D;
                    string overlayPixel="not_1x1_or_not_read";
                    if(pixel!=null&&pixel.width==1&&pixel.height==1)
                    {try{overlayPixel=ColourText(pixel.GetPixel(0,0));}catch(Exception){overlayPixel="not_cpu_readable";}}
                    string overlaySource=PropertyBlockGetTexture==null?"shared_only_mpb_texture_api_unavailable":blockOverlay==null?"shared_no_mpb_texture_override":"mpb_texture_override";
                    Runtime.Log("U01_ACTUAL_RENDERER_COLOURS entity="+view.EntityID+"; material="+material.name+"; renderer="+renderer.name+
                        "; shader="+(material.shader==null?"none":material.shader.name)+"; sharedColor="+MaterialColour(material,"_Color")+
                        "; sharedBase="+MaterialColour(material,"_BaseColor")+"; sharedTrim="+MaterialColour(material,"_TrimColor")+"; sharedSpec="+MaterialColour(material,"_SpecColor")+
                        "; actualBlockBase="+VectorText(block.GetVector("_BaseColor"))+"; actualBlockTrim="+VectorText(block.GetVector("_TrimColor"))+
                        "; blockColorVector="+VectorText(block.GetVector("_Color"))+"; blockSpecVector="+VectorText(block.GetVector("_SpecColor"))+
                        "; albedo="+TextureText(material.mainTexture)+"; surface="+TextureText(material.HasProperty("_Surface")?material.GetTexture("_Surface"):null)+
                        "; sharedOverlay="+TextureText(sharedOverlay)+"; blockTextureGetterExposed="+(PropertyBlockGetTexture!=null)+"; blockOverlay="+TextureText(blockOverlay)+"; overlayPixelSource="+overlaySource+"; resolvedOverlayPixel="+overlayPixel+
                        "; reflection="+renderer.reflectionProbeUsage+"; useLightProbes="+renderer.useLightProbes+
                        "; donorReflection="+(donor==null?"missing":donor.reflectionProbeUsage.ToString())+"; donorUseLightProbes="+(donor!=null&&donor.useLightProbes)+
                        "; activeColorSpace="+QualitySettings.activeColorSpace+"; customColours="+CustomColoursEnabled+
                        "; afterExistingNativeForce=True; readOnly=True; maxFourUnitsTwoLinesPerProcess=True; blueCauseNotEstablished=True");
                    if(++reports>=2)break;
                }
            }
            catch(Exception error){Runtime.Log("U01_ACTUAL_RENDERER_COLOURS_UNAVAILABLE entity="+view.EntityID+"; readOnly=True; "+error.GetType().Name+": "+error.Message);}
        }
        static string MaterialColour(Material material,string property)
        {return material.HasProperty(property)?ColourText(material.GetColor(property)):"absent";}
        static string ColourText(Color value){return VectorText((Vector4)value);}
        static string VectorText(Vector4 value)
        {return value.x.ToString("F6",CultureInfo.InvariantCulture)+","+value.y.ToString("F6",CultureInfo.InvariantCulture)+","+value.z.ToString("F6",CultureInfo.InvariantCulture)+","+value.w.ToString("F6",CultureInfo.InvariantCulture);}
        static string TextureText(Texture texture){return texture==null?"none":texture.name+"@"+texture.width+"x"+texture.height;}

        public static RhodesTeamColourProfile Prepare(string packageFile,RhodesLoadedModel owner)
        {
            if(owner==null)throw new ArgumentNullException("owner");
            try
            {
                DirectoryInfo folder=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(packageFile)));
                string key=folder.Name;DirectoryInfo assetRoot=null;
                if(key=="rhodes_v05" && Path.GetFileName(packageFile)=="rhodes_v05.bin")assetRoot=folder.Parent;
                else if(Path.GetFileName(packageFile)=="model.bin" && folder.Parent!=null && folder.Parent.Name=="faction_v01")
                {foreach(string unitKey in UnitKeys)if(key==unitKey){assetRoot=folder.Parent.Parent;break;}}
                else if(Path.GetFileName(packageFile)=="model.bin" && folder.Parent!=null && folder.Parent.Name=="air_wing_v01")
                {foreach(string unitKey in AirWingKeys)if(key==unitKey){assetRoot=folder.Parent.Parent;break;}}
                if(assetRoot==null)return null; // Humanoids and all unrelated packages are excluded.
                if(!CustomColoursEnabled)
                {
                    owner.Warnings.Add("Rhodes custom team colours disabled: "+key+"; authored albedo retained; private material overlay validation follows; native HUD/owner/FOW colours unchanged");
                    return new RhodesTeamColourProfile{AuthoredOnly=true};
                }
                string directory=Path.Combine(assetRoot.FullName,"team_colours"),file=Path.Combine(directory,"profiles.ritc");
                if(!File.Exists(file)){owner.Warnings.Add("Native team colour profile absent; authored albedo retained: "+key);return null;}
                FileInfo metadata=new FileInfo(file);if(metadata.Length<6||metadata.Length>131072)throw new InvalidDataException("Invalid team-colour metadata budget");
                string[] lines=File.ReadAllLines(file);if(lines.Length<2||lines.Length>256||lines[0]!="RITC1")throw new InvalidDataException("Unsupported team-colour profile format");
                RhodesTeamColourProfile profile=new RhodesTeamColourProfile();profile.Directory=directory;string expectedModel=null;
                // Only the carrier's explicitly named/hash-bound profile may
                // load its authored PBR paint masks. Small-unit limits stay.
                bool carrierProfile=key=="rhodes_v05";
                if(carrierProfile){profile.MaximumMasks=16;profile.MaximumTotalPixels=33554432;}
                int maximumDimension=carrierProfile?4096:2048,maximumPixels=carrierProfile?4194304:MaximumMaskPixels;
                foreach(string line in lines)
                {
                    if(line.Length==0||line[0]=='#'||line=="RITC1")continue;
                    string[] values=line.Split('|');if(values.Length!=7)throw new InvalidDataException("Invalid team-colour profile row");
                    if(values[0]!=key)continue;
                    if(!ValidHash(values[1])||!ValidHash(values[4])||values[3]!=Path.GetFileName(values[3])||Path.GetExtension(values[3])!=".png")throw new InvalidDataException("Unsafe team-colour mask reference");
                    if(expectedModel!=null&&expectedModel!=values[1])throw new InvalidDataException("Inconsistent team-colour source model");expectedModel=values[1];
                    int width,height;
                    if(!int.TryParse(values[5],out width)||!int.TryParse(values[6],out height)||width<1||height<1||width>maximumDimension||height>maximumDimension||(long)width*height>maximumPixels)
                        throw new InvalidDataException("Team-colour mask dimensions exceed budget");
                    bool materialExists=false;foreach(RhodesMaterial material in owner.Package.Materials)if(material.Name==values[2]){materialExists=true;break;}
                    if(!materialExists||profile.Materials.ContainsKey(values[2]))throw new InvalidDataException("Unknown or duplicate team-colour material");
                    profile.Materials.Add(values[2],new RhodesTeamColourRule{File=values[3],Hash=values[4],Width=width,Height=height});
                }
                if(profile.Materials.Count==0)return null;
                if(Hash(packageFile)!=expectedModel)throw new InvalidDataException("Team-colour profile does not match this exact model/UV package");
                owner.Warnings.Add("Native team colours: "+key+"; exact masked materials="+profile.Materials.Count+"; owner colours/FOW/NLIPS remain native property-block data");
                return profile;
            }
            catch(Exception error){owner.Warnings.Add("Team-colour profile skipped; authored appearance retained: "+error.Message);return null;}
        }

        // Bind only a pre-existing private material at model construction. The
        // native UnitView/PlayerCustomizationUnitView supplies _BaseColor and
        // _TrimColor per instance, including all LODs and FTK skin renderers.
        public static void BindMaterial(RhodesMaterial description,Material material,RhodesLoadedModel owner)
        {
            RhodesTeamColourProfile profile=owner.TeamColours;if(profile==null)return;
            if(profile.AuthoredOnly)
            {
                // ModelBuilder clears the donor mask BEFORE this final binding
                // call. Require that actual private material state, rather than
                // merely skipping RITC and accidentally retaining donor paint.
                if(material.HasProperty("_OverlayMask"))
                {
                    Texture2D clear=material.GetTexture("_OverlayMask") as Texture2D;
                    if(clear==null||clear.name!="EF_RHS2_ClearOverlay"||clear.width!=1||clear.height!=1)
                        throw new InvalidDataException("Authored colours require the private clear overlay: "+description.Name);
                    Color pixel=clear.GetPixel(0,0);
                    if(pixel.r!=0f||pixel.g!=0f||pixel.b!=0f||pixel.a!=0f)
                        throw new InvalidDataException("Authored-colour overlay is not transparent black: "+description.Name);
                    if(!profile.AuthoredOverlayReported)
                    {
                        owner.Warnings.Add("Rhodes authored-colour material verified: "+description.Name+"; customColours=False; actualOverlay=EF_RHS2_ClearOverlay; size=1x1; RGBA=0,0,0,0; paletteIgnoredByZeroMask=True; PBR/specular settings preserved");
                        profile.AuthoredOverlayReported=true;
                    }
                }
                return;
            }
            RhodesTeamColourRule rule;if(!profile.Materials.TryGetValue(description.Name,out rule))return;
            if(material.shader==null||material.shader.name!="UNIT (Specular setup)"||!material.HasProperty("_OverlayMask")||!material.HasProperty("_BaseColor")||!material.HasProperty("_TrimColor"))
                throw new InvalidDataException("Exact native team-colour shader contract required: "+description.Name);
            Texture2D texture;
            if(!profile.Textures.TryGetValue(rule.File,out texture))
            {
                if(profile.Textures.Count>=profile.MaximumMasks||profile.Pixels+(long)rule.Width*rule.Height>profile.MaximumTotalPixels)throw new InvalidDataException("Aggregate team-colour mask budget exceeded");
                string file=Path.Combine(profile.Directory,rule.File);FileInfo info=new FileInfo(file);
                if(!info.Exists||info.Length<24||info.Length>4194304||Hash(file)!=rule.Hash)throw new InvalidDataException("Team-colour mask missing or hash mismatch: "+rule.File);
                RhodesResourceContent content=RhodesResourceContent.Read(file,4194304L);byte[] bytes=content.Bytes;
                if(bytes[0]!=137||bytes[1]!=80||bytes[2]!=78||bytes[3]!=71||BigEndian32(bytes,16)!=rule.Width||BigEndian32(bytes,20)!=rule.Height)
                    throw new InvalidDataException("Team-colour PNG header/dimensions mismatch: "+rule.File);
                // Mask channels are linear data. Albedo remains its authored sRGB
                // texture; B=0 and unused alpha=255 were checked offline.
                texture=RhodesSharedResources.Png(content,"team-mask",true,false,rule.Width,rule.Height,"EF_NativeTeamMask_"+rule.File,owner);
                profile.Pixels+=rule.Width*rule.Height;profile.Textures.Add(rule.File,texture);
            }
            if(!profile.OriginalMasks.ContainsKey(material))profile.OriginalMasks.Add(material,material.GetTexture("_OverlayMask"));
            material.SetTexture("_OverlayMask",texture);
            // Overlay uses the same native UV0/_MainTex_ST as albedo. Do not
            // reinterpret the second UV set or turn B into a damage mask.
        }
        // FTK skins can reuse a body-panel material index for steel tread plates.
        // Keep their authored overlay on one model-template material copy, shared
        // by every track LOD and every unit cloned from that template. Not a tick.
        public static Material UnpaintedTrackMaterial(Material material,RhodesLoadedModel owner)
        {
            RhodesTeamColourProfile profile=owner.TeamColours;if(profile==null||profile.AuthoredOnly)return material;
            Texture original;if(!profile.OriginalMasks.TryGetValue(material,out original))return material;
            Material copy;if(profile.TrackMaterials.TryGetValue(material,out copy))return copy;
            copy=new Material(material);copy.name=material.name+"_UnpaintedTrack";copy.hideFlags=HideFlags.HideAndDontSave;copy.SetTexture("_OverlayMask",original);
            owner.Owned.Add(copy);profile.TrackMaterials.Add(material,copy);return copy;
        }
        public static Material[] UnpaintedTrackMaterials(Material[] materials,RhodesLoadedModel owner)
        {
            if(owner.TeamColours==null||owner.TeamColours.AuthoredOnly)return materials;
            Material[] result=new Material[materials.Length];for(int i=0;i<materials.Length;i++)result[i]=UnpaintedTrackMaterial(materials[i],owner);return result;
        }
        static bool ValidHash(string value)
        {if(value==null||value.Length!=64)return false;foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))return false;return true;}
        static string Hash(string file)
        {using(SHA256 hash=SHA256.Create())using(FileStream stream=File.OpenRead(file))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static int BigEndian32(byte[] bytes,int offset)
        {return bytes[offset]<<24|bytes[offset+1]<<16|bytes[offset+2]<<8|bytes[offset+3];}
    }
}
