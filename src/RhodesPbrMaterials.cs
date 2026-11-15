using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace EndfieldDoK.ModelIntake
{
    // Explicit offline conversion of glTF metallic/roughness to the game's
    // audited UNIT Surface (R=glow,G=smoothness,B=specular intensity). Original
    // PNGs remain in the package. These textures belong to one model template.
    internal sealed class RhodesPbrProfile
    {
        internal string ModelSha256;
        internal readonly Dictionary<string,string[]> Entries=new Dictionary<string,string[]>(StringComparer.Ordinal);
        internal readonly Dictionary<string,Color> SpecularTints=new Dictionary<string,Color>(StringComparer.Ordinal);
        internal readonly Dictionary<string,string> OcclusionTextures=new Dictionary<string,string>(StringComparer.Ordinal);
    }
    internal static class RhodesPbrMaterials
    {
        internal const long MaximumPixels=134217728L;
        // V18 adds distinct baked material atlases and small uniform surfaces.
        // The aggregate pixel budget remains unchanged; runtime maps are capped
        // offline so texture count growth cannot increase the pixel allowance.
        internal const int MaximumTextures=128;
        internal static RhodesPbrProfile Read(string folder,string modelFile)
        {return Read(folder,modelFile,null);}
        internal static RhodesPbrProfile Read(string folder,string modelFile,string decodedModelHash)
        {
            RhodesPbrProfile result=ReadProfile(folder,modelFile,decodedModelHash);if(result==null)return null;
            RhodesSpecularCorrection.Apply(folder,modelFile,result.ModelSha256,Path.Combine(folder,"materials.rhpbr"),result);
            return result;
        }
        // Pure metadata parsing shared by production intake and offline reader
        // validation. Unity colour-space validation remains in Apply above.
        internal static RhodesPbrProfile ReadProfile(string folder,string modelFile,string decodedModelHash)
        {
            string path=Path.Combine(folder,"materials.rhpbr");if(!File.Exists(path))return null;
            FileInfo info=new FileInfo(path);if(info.Length<70||info.Length>65536)throw new InvalidDataException("PBR profile exceeds metadata budget");
            string[] lines=File.ReadAllLines(path);if(lines.Length<3||lines.Length>130||lines[0]!="RHPBR1"||lines[1]!=(string.IsNullOrEmpty(decodedModelHash)?Hash(modelFile):decodedModelHash))throw new InvalidDataException("PBR profile/model pair mismatch");
            RhodesPbrProfile result=new RhodesPbrProfile();result.ModelSha256=lines[1];
            for(int i=2;i<lines.Length;i++)
            {
                if(lines[i].Length==0)continue;string[] v=lines[i].Split('|');
                if(v.Length!=10||!v[0].StartsWith("V17SRC_",StringComparison.Ordinal)||result.Entries.ContainsKey(v[0]))throw new InvalidDataException("Invalid PBR material identity");
                RhodesModelPackage.SafeAssetPath(folder,v[1]);RhodesModelPackage.SafeAssetPath(folder,v[2]);RhodesModelPackage.SafeAssetPath(folder,v[6]);
                for(int k=3;k<=5;k++)Value(v[k]);for(int k=7;k<=9;k++)Value(v[k]);result.Entries.Add(v[0],v);
            }
            return result;
        }
        static float Value(string text)
        {
            float value;if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||float.IsNaN(value)||float.IsInfinity(value)||value<0f||value>16f)throw new InvalidDataException("Invalid PBR factor");return value;
        }
        static string Hash(string file)
        {using(FileStream stream=File.OpenRead(file))using(SHA256 hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static Texture2D Texture(string file,string folder,RhodesLoadedModel owner,Dictionary<string,Texture2D> textures)
        {
            if(string.IsNullOrEmpty(file))return null;string path=RhodesModelPackage.SafeAssetPath(folder,file);Texture2D texture;
            if(textures.TryGetValue(path,out texture))return texture;
            FileInfo info=new FileInfo(path);if(!info.Exists||info.Length<24||info.Length>33554432L)throw new InvalidDataException("Invalid PBR texture size");
            RhodesResourceContent content=RhodesResourceContent.Read(path,33554432L);byte[] bytes=content.Bytes;if(bytes[0]!=137||bytes[1]!=80||bytes[2]!=78||bytes[3]!=71)throw new InvalidDataException("PBR textures must be PNG");
            int width=Big(bytes,16),height=Big(bytes,20);
            if(width<1||height<1||width>4096||height>4096||textures.Count>=MaximumTextures||owner.AlbedoPixels+(long)width*height>MaximumPixels)throw new InvalidDataException("PBR texture count/pixel budget exceeded before decode");
            string name=Path.GetFileName(file).StartsWith("hotfix_f0_",StringComparison.Ordinal)?"EF_V17_Specular_"+Path.GetFileNameWithoutExtension(file):"EF_V17_PBR_"+textures.Count;
            texture=RhodesSharedResources.Png(content,"pbr",true,false,width,height,name,owner);
            owner.AlbedoPixels+=(long)width*height;textures.Add(path,texture);return texture;
        }
        static int Big(byte[] bytes,int at){return bytes[at]<<24|bytes[at+1]<<16|bytes[at+2]<<8|bytes[at+3];}
        internal static bool RequiresTangents(RhodesMaterial description,RhodesPbrProfile profile)
        {string[] entry;return profile!=null&&profile.Entries.TryGetValue(description.Name,out entry)&&entry[1].Length>0;}
        internal static void Bind(RhodesMaterial description,Material material,string folder,RhodesLoadedModel owner,Dictionary<string,Texture2D> textures)
        {
            if(owner.PbrMaterials==null)return;string[] v;if(!owner.PbrMaterials.Entries.TryGetValue(description.Name,out v))return;
            if(v[1].Length>0)
            {
                if(!material.HasProperty("_BumpMap"))throw new InvalidDataException("PBR normal shader binding missing: "+description.Name);
                material.SetTexture("_BumpMap",Texture(v[1],folder,owner,textures));material.SetFloat("_BumpScale",Value(v[3]));material.EnableKeyword("_NORMALMAP");
            }
            if(material.shader!=null&&material.shader.name=="UNIT (Specular setup)")
            {
                if(!material.HasProperty("_Surface"))throw new InvalidDataException("Native Surface shader binding missing");
                Color specularTint;bool corrected=owner.PbrMaterials.SpecularTints.TryGetValue(description.Name,out specularTint);
                material.SetTexture("_Surface",Texture(v[2],folder,owner,textures));material.SetFloat("_Glossiness",1f);material.SetColor("_SpecColor",corrected?specularTint:Color.white);
                string occlusion;
                if(owner.PbrMaterials.OcclusionTextures.TryGetValue(description.Name,out occlusion))
                {
                    if(!material.HasProperty("_OcclusionMap")||!material.HasProperty("_OcclusionStrength"))throw new InvalidDataException("Native occlusion shader binding missing");
                    material.SetTexture("_OcclusionMap",Texture(occlusion,folder,owner,textures));material.SetFloat("_OcclusionStrength",1f);
                }
            }
            else
            {
                if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",1f-Value(v[5]));
                if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",Value(v[4]));
            }
            if(v[6].Length>0&&material.HasProperty("_EmissionMap")){material.SetTexture("_EmissionMap",Texture(v[6],folder,owner,textures));material.EnableKeyword("_EMISSION");}
        }
        // Unity 5.2 has no Mesh.RecalculateTangents. Build the conventional
        // orthonormal UV0 tangent basis before uploading immutable RHS meshes.
        internal static Vector4[] Tangents(Vector3[] positions,Vector3[] normals,Vector2[] uv,int[] indices)
        {
            Vector3[] first=new Vector3[positions.Length],second=new Vector3[positions.Length];
            for(int i=0;i<indices.Length;i+=3)
            {
                int a=indices[i],b=indices[i+1],c=indices[i+2];Vector3 e1=positions[b]-positions[a],e2=positions[c]-positions[a];Vector2 u1=uv[b]-uv[a],u2=uv[c]-uv[a];float det=u1.x*u2.y-u2.x*u1.y;
                if(Math.Abs(det)<1e-12f)continue;Vector3 t=(e1*u2.y-e2*u1.y)/det,s=(e2*u1.x-e1*u2.x)/det;
                first[a]+=t;first[b]+=t;first[c]+=t;second[a]+=s;second[b]+=s;second[c]+=s;
            }
            Vector4[] result=new Vector4[positions.Length];
            for(int i=0;i<result.Length;i++)
            {
                Vector3 n=normals[i],t=first[i]-n*Vector3.Dot(n,first[i]);
                if(t.sqrMagnitude<1e-12f)t=Vector3.Cross(n,Math.Abs(n.y)<.9f?Vector3.up:Vector3.right);t.Normalize();
                result[i]=new Vector4(t.x,t.y,t.z,Vector3.Dot(Vector3.Cross(n,t),second[i])<0f?-1f:1f);
            }
            return result;
        }
    }
}
