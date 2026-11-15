using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace EndfieldDoK.ModelIntake
{
    // Optional, exact-model/PBR-bound native scalar F0 and RGB tint conversion.
    // No authored texture is rewritten. Work occurs once at model construction.
    internal static class RhodesSpecularCorrection
    {
        const string FileName="specular_correction.rhspec";
        static readonly string[] Materials={"V17SRC_03_Mechanical_Graphite","V17SRC_04_Edges_SatinSteel"};
        internal static void Apply(string folder,string modelFile,string validatedModelHash,string profileFile,RhodesPbrProfile profile)
        {
            string file=Path.Combine(folder,FileName);if(!File.Exists(file))return;
            DirectoryInfo directory=new DirectoryInfo(folder);string folderName=directory.Name;
            bool sixWheel=folderName=="RI_U02_FIELD_SURVEY"||folderName=="RI_U03_ENGINEERING";
            bool approvedSixWheel=sixWheel&&directory.Parent!=null&&directory.Parent.Name=="faction_v01"&&
                Path.GetFileName(modelFile)=="model.bin"&&string.Equals(Path.GetFullPath(modelFile),
                    Path.GetFullPath(Path.Combine(folder,"model.bin")),StringComparison.OrdinalIgnoreCase);
            if(folderName!="rhodes_v05"&&folderName!="RI_U01_HARVESTER"&&folderName!="RI_U04_LIGHT_ESCORT"&&!approvedSixWheel)
                throw new InvalidDataException("Specular correction has no approved model intake");
            FileInfo info=new FileInfo(file);if(info.Length<150||info.Length>65536)throw new InvalidDataException("Specular correction metadata budget");
            string[] lines=File.ReadAllLines(file);
            if(lines.Length>=5&&lines[0]=="RHSPEC2")
            {ApplyVersion2(folder,validatedModelHash,profileFile,profile,lines,QualitySettings.activeColorSpace==ColorSpace.Gamma);return;}
            if(Path.GetFileName(modelFile)!="rhodes_v05.bin"||folderName!="rhodes_v05")throw new InvalidDataException("Legacy specular correction is carrier-only");
            if(lines.Length!=6||lines[0]!="RHSPEC1"||lines[1]!=validatedModelHash||lines[2]!=Hash(profileFile)||lines[3]!="Gamma")
                throw new InvalidDataException("Specular correction exact model/profile/Gamma contract");
            if(QualitySettings.activeColorSpace!=ColorSpace.Gamma)throw new InvalidDataException("Specular correction requires the audited native Gamma project");
            Dictionary<string,string[]> replacements=new Dictionary<string,string[]>(StringComparer.Ordinal);Dictionary<string,Color> tints=new Dictionary<string,Color>(StringComparer.Ordinal);
            for(int i=4;i<lines.Length;i++)
            {
                string[] v=lines[i].Split('|');if(v.Length!=14)throw new InvalidDataException("Specular correction row format");
                bool allowed=false;foreach(string name in Materials)if(v[0]==name)allowed=true;
                string[] original;if(!allowed||replacements.ContainsKey(v[0])||!profile.Entries.TryGetValue(v[0],out original)||original[2]!=v[1])
                    throw new InvalidDataException("Specular correction exact private material/source mapping");
                int width,height;if(!int.TryParse(v[5],out width)||!int.TryParse(v[6],out height)||width<1||height<1||width>4096||height>4096||(long)width*height>4194304)
                    throw new InvalidDataException("Specular correction PNG dimensions budget");
                string old=RequireHash(folder,v[1],v[2]),target=RequireHash(folder,v[3],v[4]);
                RequirePng(old,width,height);RequirePng(target,width,height);RequireHash(folder,v[10],v[11]);RequireHash(folder,v[12],v[13]);
                if(!v[3].StartsWith("textures/hotfix_f0_",StringComparison.Ordinal)||Path.GetExtension(v[3])!=".png")throw new InvalidDataException("Specular correction target path");
                Color tint=new Color(Number(v[7]),Number(v[8]),Number(v[9]),1f);
                string[] copy=(string[])original.Clone();copy[2]=v[3];replacements.Add(v[0],copy);tints.Add(v[0],tint);
            }
            if(replacements.Count!=2)throw new InvalidDataException("Specular correction requires both complete private targets");
            foreach(KeyValuePair<string,string[]> pair in replacements){profile.Entries[pair.Key]=pair.Value;profile.SpecularTints.Add(pair.Key,tints[pair.Key]);}
        }
        static void ApplyVersion2(string folder,string validatedModelHash,string profileFile,RhodesPbrProfile profile,string[] lines,bool nativeGamma)
        {
            if(lines.Length<5||lines.Length>134||lines[0]!="RHSPEC2"||lines[1]!=validatedModelHash||lines[2]!=Hash(profileFile)||lines[3]!="Gamma"||!nativeGamma)
                throw new InvalidDataException("Specular v2 exact model/profile/Gamma contract");
            Dictionary<string,string[]> replacements=new Dictionary<string,string[]>(StringComparer.Ordinal);
            Dictionary<string,Color> tints=new Dictionary<string,Color>(StringComparer.Ordinal);
            Dictionary<string,string> occlusion=new Dictionary<string,string>(StringComparer.Ordinal);
            for(int i=4;i<lines.Length;i++)
            {
                string[] v=lines[i].Split('|');string[] original;
                if(v.Length!=12||!v[0].StartsWith("V17SRC_",StringComparison.Ordinal)||replacements.ContainsKey(v[0])||!profile.Entries.TryGetValue(v[0],out original)||original[2]!=v[1])
                    throw new InvalidDataException("Specular v2 exact material/source mapping");
                int width,height;
                if(!int.TryParse(v[5],out width)||!int.TryParse(v[6],out height)||width<1||height<1||width>4096||height>4096||(long)width*height>4194304)
                    throw new InvalidDataException("Specular v2 PNG dimensions budget");
                RequireHash(folder,v[1],v[2]);string target=RequireHash(folder,v[3],v[4]);RequirePng(target,width,height);
                if(v[3]!=v[1]&&(!v[3].StartsWith("textures/hotfix_f0_",StringComparison.Ordinal)||Path.GetExtension(v[3])!=".png"))throw new InvalidDataException("Specular v2 target path");
                Color tint=new Color(Factor(v[7]),Factor(v[8]),Factor(v[9]),1f);
                if((v[10].Length==0)!=(v[11].Length==0))throw new InvalidDataException("Specular v2 incomplete AO pair");
                if(v[10].Length>0)
                {
                    if(!v[10].StartsWith("textures/hotfix_ao_",StringComparison.Ordinal)||Path.GetExtension(v[10])!=".png")throw new InvalidDataException("Specular v2 AO path");
                    string ao=RequireHash(folder,v[10],v[11]);RequirePng(ao,width,height);occlusion.Add(v[0],v[10]);
                }
                string[] copy=(string[])original.Clone();copy[2]=v[3];replacements.Add(v[0],copy);tints.Add(v[0],tint);
            }
            if(replacements.Count==0)throw new InvalidDataException("Specular v2 empty mapping");
            foreach(KeyValuePair<string,string[]> pair in replacements){profile.Entries[pair.Key]=pair.Value;profile.SpecularTints.Add(pair.Key,tints[pair.Key]);}
            foreach(KeyValuePair<string,string> pair in occlusion)profile.OcclusionTextures.Add(pair.Key,pair.Value);
        }
        static float Factor(string text)
        {float value;if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||float.IsNaN(value)||float.IsInfinity(value)||value<0f||value>16f)throw new InvalidDataException("Specular v2 tint factor range");return value;}
        static float Number(string text)
        {float value;if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||float.IsNaN(value)||float.IsInfinity(value)||value<0f||value>1f)throw new InvalidDataException("Specular tint range");return value;}
        static string RequireHash(string folder,string relative,string expected)
        {
            if(expected.Length!=64)throw new InvalidDataException("Specular correction SHA256 length");foreach(char c in expected)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))throw new InvalidDataException("Specular correction SHA256 encoding");
            string path=RhodesModelPackage.SafeAssetPath(folder,relative);if(Hash(path)!=expected)throw new InvalidDataException("Specular correction source/target bytes changed: "+relative);return path;
        }
        static void RequirePng(string file,int width,int height)
        {
            FileInfo info=new FileInfo(file);if(info.Length<24||info.Length>4194304)throw new InvalidDataException("Specular correction PNG file budget");
            using(FileStream stream=File.OpenRead(file)){byte[] b=new byte[24];if(stream.Read(b,0,24)!=24||b[0]!=137||b[1]!=80||b[2]!=78||b[3]!=71||Big(b,16)!=width||Big(b,20)!=height)throw new InvalidDataException("Specular correction PNG header");}
        }
        static int Big(byte[] bytes,int at){return bytes[at]<<24|bytes[at+1]<<16|bytes[at+2]<<8|bytes[at+3];}
        static string Hash(string file)
        {using(FileStream stream=File.OpenRead(file))using(SHA256 algorithm=SHA256.Create())return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-",string.Empty).ToLowerInvariant();}
    }
}
