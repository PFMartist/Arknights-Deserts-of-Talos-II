using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // BuildFlatWorld only. Native textures are read-only donors; the floor owns
    // its private clone/material. No scene, sensor, lighting or simulation edit.
    public static class FlatSandMaterial
    {
        public const float TileWorldMetres=32f;
        const string DonorMaterial="terrain_north-chunked";
        const string DonorShader="MixMap_Terrain_ColourMap";
        const string DonorAlbedo="TEX_Tile_sand_rough_03_dif";
        const string DonorNormal="TEX_Tile_sand_rough_03_nrm";

        public static Material Create(float worldSideMetres,ICollection<UnityEngine.Object> ownedAssets)
        {
            if(ownedAssets==null)throw new ArgumentNullException("ownedAssets");
            if(float.IsNaN(worldSideMetres)||float.IsInfinity(worldSideMetres)||worldSideMetres<=0f||worldSideMetres>16000f)throw new ArgumentOutOfRangeException("worldSideMetres");
            Shader shader=Shader.Find("Diffuse")??Shader.Find("Standard");
            if(shader==null)throw new InvalidOperationException("A lit non-unit floor shader could not be found");
            Texture2D albedo=null;Material material=null;string source;
            try
            {
                Texture2D native=FindNativeAlbedo();
                if(native!=null)
                {
                    albedo=UnityEngine.Object.Instantiate(native) as Texture2D;
                    if(albedo==null||albedo==native)throw new InvalidOperationException("Native sand albedo was not independently cloned");
                    source="native MP_16_WorldArt/sharedassets1.assets:256 via terrain_north-chunked._Splat4";
                }
                else
                {
                    albedo=MakeFallback();
                    source="deterministic procedural fallback; exact loaded donor unavailable";
                }
                albedo.name="EF_FlatSand_Albedo_Private";albedo.hideFlags=HideFlags.HideAndDontSave;
                albedo.wrapMode=TextureWrapMode.Repeat;albedo.filterMode=FilterMode.Trilinear;albedo.anisoLevel=4;albedo.mipMapBias=0f;
                material=new Material(shader);material.name="EF_FlatSand_Private";material.hideFlags=HideFlags.HideAndDontSave;
                material.mainTexture=albedo;material.mainTextureScale=new Vector2(worldSideMetres/TileWorldMetres,worldSideMetres/TileWorldMetres);
                material.mainTextureOffset=Vector2.zero;material.color=new Color(.86f,.85f,.82f,1f);
                if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",.04f);
                if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",0f);
                // A flat four-vertex floor has no tangent basis. Use albedo-only
                // lighting rather than inventing normals or reusing terrain UVs.
                ownedAssets.Add(albedo);ownedAssets.Add(material);
                Runtime.Log("FLAT SAND MATERIAL: source="+source+"; albedo="+albedo.width+"x"+albedo.height+"; mips="+albedo.mipmapCount+"; shader="+shader.name+"; metresPerTile="+TileWorldMetres+"; privateTextureClone="+(native!=null)+"; trilinear=True; aniso=4; sourceAssetsModified=False; terrainBlendMasksInherited=False; normalMapBound=False; meshCollisionNavigationSensorAndDaylightUnchanged=True; runtimeVisualAcceptancePending=True");
                return material;
            }
            catch
            {
                if(material!=null)UnityEngine.Object.DestroyImmediate(material);
                if(albedo!=null)UnityEngine.Object.DestroyImmediate(albedo);
                throw;
            }
        }

        static Texture2D FindNativeAlbedo()
        {
            // Source scene renderers have already been pruned. Their native
            // material asset remains loaded; one exact lookup at world creation.
            foreach(Material candidate in Resources.FindObjectsOfTypeAll(typeof(Material)))
            {
                if(candidate==null||(candidate.name!=DonorMaterial&&candidate.name!=DonorMaterial+" (Instance)")||candidate.shader==null||candidate.shader.name!=DonorShader||!candidate.HasProperty("_Splat4")||!candidate.HasProperty("_Normal4"))continue;
                Texture2D albedo=candidate.GetTexture("_Splat4") as Texture2D;
                Texture2D normal=candidate.GetTexture("_Normal4") as Texture2D;
                if(albedo!=null&&albedo.name==DonorAlbedo&&albedo.width==1024&&albedo.height==1024&&albedo.mipmapCount==11&&albedo.format==TextureFormat.DXT5&&normal!=null&&normal.name==DonorNormal)return albedo;
            }
            return null;
        }

        static Texture2D MakeFallback()
        {
            // 256-square seamless integer-frequency ripples + bounded grain.
            // An explicitly labelled fallback, not a claim of native terrain.
            const int size=256;Texture2D texture=new Texture2D(size,size,TextureFormat.RGBA32,true);
            try
            {
                Color32[] pixels=new Color32[size*size];
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    double u=(double)x/size,v=(double)y/size;
                    double wave=Math.Sin((u*5+v*2)*Math.PI*2+Math.Sin(v*Math.PI*4)*.55);
                    double broad=Math.Sin((u+v*2)*Math.PI*2)*.018;
                    uint hash=unchecked((uint)(x*374761393+y*668265263+325799));hash=(hash^(hash>>13))*1274126177u;hash^=hash>>16;
                    double grain=((hash&255)/255.0-.5)*.035;
                    float light=(float)(wave*.025+broad+grain);
                    pixels[y*size+x]=new Color(.68f+light,.57f+light,.41f+light,1f);
                }
                texture.SetPixels32(pixels);texture.Apply(true,true);return texture;
            }
            catch{UnityEngine.Object.DestroyImmediate(texture);throw;}
        }
    }
}
