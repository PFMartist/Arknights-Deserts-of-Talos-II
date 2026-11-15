using System;
using System.Collections.Generic;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Rhodes DoK author: PFMartist. AI collaboration: GPT (Codex).
    // A small, static service plate inside the common chassis. Credits do not
    // replace any upstream attribution or assign copyright ownership to an AI.
    internal static class RhodesAuthorCredit58
    {
        // Metres in the exact shipped RHS2 root space. Six-axis triangle rays
        // place this inside the fixed common hull, away from moving modules.
        static readonly Vector3 PlateCentre=new Vector3(0f,20f,-180f);

        internal static void Attach(RhodesLoadedModel loaded,RhodesCarrierBinding binding,Material nativeMaterial,int layer)
        {
            if(loaded==null||binding==null||binding.GeometryRoot==null||nativeMaterial==null||loaded.LodGroup==null)
            {Runtime.Log("RHODES_AUTHOR_CREDIT58_OPTIONAL_SKIPPED missingOwnedModelOrMaterial=True; carrierRetained=True");return;}
            int ownedCount=loaded.Owned.Count;GameObject plate=null;LOD[] oldLevels=null;
            Renderer[] oldRenderers=loaded.Renderers,oldGeometry=binding.Geometry;
            try
            {
                oldLevels=loaded.LodGroup.GetLODs();
                if(oldLevels.Length==0)throw new InvalidOperationException("Author plate requires an existing LOD0");
                AttachCore(loaded,binding,nativeMaterial,layer,oldLevels,ref plate);
            }
            catch(Exception error)
            {
                // Decoration failure must never abort the carrier adapter.
                // Restore only our lists and assets; leave the authored rig alone.
                if(oldLevels!=null)try{loaded.LodGroup.SetLODs(oldLevels);}catch{}
                loaded.Renderers=oldRenderers;binding.Geometry=oldGeometry;
                if(plate!=null)try{UnityEngine.Object.DestroyImmediate(plate);}catch{}
                for(int i=loaded.Owned.Count-1;i>=ownedCount;i--)
                    if(loaded.Owned[i]!=null)try{UnityEngine.Object.DestroyImmediate(loaded.Owned[i]);}catch{}
                loaded.Owned.RemoveRange(ownedCount,loaded.Owned.Count-ownedCount);
                Runtime.Log("RHODES_AUTHOR_CREDIT58_OPTIONAL_SKIPPED carrierRetained=True; error="+error.GetType().Name+": "+error.Message);
            }
        }

        static void AttachCore(RhodesLoadedModel loaded,RhodesCarrierBinding binding,Material nativeMaterial,int layer,LOD[] oldLevels,ref GameObject plate)
        {
            Texture2D texture=CreateTexture(loaded);
            Texture2D neutral=new Texture2D(1,1,TextureFormat.RGBA32,false);
            loaded.Owned.Add(neutral);
            neutral.name="RhodesCredit58_Neutral";neutral.hideFlags=HideFlags.HideAndDontSave;
            neutral.SetPixel(0,0,Color.clear);neutral.Apply(false,true);
            Material material=new Material(nativeMaterial);loaded.Owned.Add(material);
            material.name="RhodesCredit58_NativeUnit";material.hideFlags=HideFlags.HideAndDontSave;
            material.mainTexture=texture;material.mainTextureScale=Vector2.one;material.mainTextureOffset=Vector2.zero;
            foreach(string property in new string[]{"_MainTex","_BaseMap","_Diffuse"})if(material.HasProperty(property))material.SetTexture(property,texture);
            foreach(string property in new string[]{"_Color","_BaseColor","_TrimColor"})if(material.HasProperty(property))material.SetColor(property,Color.white);
            foreach(string property in new string[]{"_BumpMap","_OcclusionMap","_MetallicGlossMap","_SpecGlossMap","_EmissionMap","_DetailAlbedoMap","_DetailNormalMap","_ParallaxMap"})if(material.HasProperty(property))material.SetTexture(property,null);
            foreach(string property in new string[]{"_Surface","_DetailMask","_OverlayMask"})if(material.HasProperty(property))material.SetTexture(property,neutral);
            foreach(string property in new string[]{"_BumpScale","_Parallax","_Glossiness","_Metallic"})if(material.HasProperty(property))material.SetFloat(property,0f);
            if(material.HasProperty("_SpecColor"))material.SetColor("_SpecColor",Color.black);
            foreach(string keyword in new string[]{"_NORMALMAP","_METALLICGLOSSMAP","_SPECGLOSSMAP","_DETAIL_MULX2","_PARALLAXMAP","_EMISSION"})material.DisableKeyword(keyword);

            Mesh mesh=new Mesh();loaded.Owned.Add(mesh);mesh.name="RhodesCredit58_InteriorPlate";mesh.hideFlags=HideFlags.HideAndDontSave;
            mesh.vertices=new Vector3[]{new Vector3(-4f,-1f,0f),new Vector3(4f,-1f,0f),new Vector3(4f,1f,0f),new Vector3(-4f,1f,0f)};
            mesh.uv=new Vector2[]{new Vector2(0f,0f),new Vector2(1f,0f),new Vector2(1f,1f),new Vector2(0f,1f)};
            mesh.normals=new Vector3[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward};
            mesh.triangles=new int[]{0,1,2,0,2,3};mesh.RecalculateBounds();
            plate=new GameObject("Credits.PFMartist.GPT-Codex");plate.layer=layer;
            plate.transform.parent=binding.GeometryRoot;plate.transform.localPosition=PlateCentre;
            plate.transform.localRotation=Quaternion.identity;plate.transform.localScale=Vector3.one;
            plate.AddComponent<MeshFilter>().sharedMesh=mesh;
            MeshRenderer renderer=plate.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            List<Renderer> geometry=new List<Renderer>(loaded.Renderers);geometry.Add(renderer);
            LOD[] levels=(LOD[])oldLevels.Clone();
            List<Renderer> near=new List<Renderer>(levels[0].renderers);near.Add(renderer);levels[0].renderers=near.ToArray();
            loaded.LodGroup.SetLODs(levels);
            loaded.Renderers=geometry.ToArray();binding.Geometry=loaded.Renderers;
            Runtime.Log("RHODES_AUTHOR_CREDIT58 author=PFMartist; aiCollaboration=GPT_Codex; interiorCommonHull=True; lod0Only=True; vertices=4; triangles=2; updateLoop=False");
        }

        static Texture2D CreateTexture(RhodesLoadedModel loaded)
        {
            const int width=256,height=64;
            Color32[] pixels=new Color32[width*height];
            Color32 background=new Color32(31,34,36,255),ink=new Color32(207,201,176,255);
            for(int i=0;i<pixels.Length;i++)pixels[i]=background;
            for(int x=3;x<width-3;x++){pixels[3*width+x]=ink;pixels[(height-4)*width+x]=ink;}
            for(int y=3;y<height-3;y++){pixels[y*width+3]=ink;pixels[y*width+width-4]=ink;}
            Draw(pixels,width,height,"PFMartist",49,11,3,ink);
            Draw(pixels,width,height,"GPT / Codex",63,39,2,ink);
            Texture2D result=new Texture2D(width,height,TextureFormat.RGBA32,false);
            loaded.Owned.Add(result);
            result.name="RhodesCredit58_PFMartist_GPT_Codex";result.hideFlags=HideFlags.HideAndDontSave;
            result.wrapMode=TextureWrapMode.Clamp;result.filterMode=FilterMode.Bilinear;
            result.SetPixels32(pixels);result.Apply(false,true);return result;
        }

        static void Draw(Color32[] pixels,int width,int height,string text,int left,int top,int scale,Color32 ink)
        {
            for(int i=0;i<text.Length;i++)
            {
                string[] rows=Glyph(text[i]).Split('/');
                for(int y=0;y<7;y++)for(int x=0;x<5;x++)if(rows[y][x]=='1')
                    for(int dy=0;dy<scale;dy++)for(int dx=0;dx<scale;dx++)
                    {
                        int px=left+i*6*scale+x*scale+dx,py=height-1-(top+y*scale+dy);
                        if(px>=0&&px<width&&py>=0&&py<height)pixels[py*width+px]=ink;
                    }
            }
        }

        static string Glyph(char c)
        {
            switch(c)
            {
                case 'P':return "11110/10001/10001/11110/10000/10000/10000";
                case 'F':return "11111/10000/10000/11110/10000/10000/10000";
                case 'M':return "10001/11011/10101/10101/10001/10001/10001";
                case 'a':return "00000/00000/01110/00001/01111/10001/01111";
                case 'r':return "00000/00000/10110/11001/10000/10000/10000";
                case 't':return "01000/01000/11110/01000/01000/01001/00110";
                case 'i':return "00100/00000/01100/00100/00100/00100/01110";
                case 's':return "00000/00000/01111/10000/01110/00001/11110";
                case 'G':return "01110/10001/10000/10111/10001/10001/01110";
                case 'T':return "11111/00100/00100/00100/00100/00100/00100";
                case 'C':return "01110/10001/10000/10000/10000/10001/01110";
                case 'o':return "00000/00000/01110/10001/10001/10001/01110";
                case 'd':return "00001/00001/01101/10011/10001/10001/01111";
                case 'e':return "00000/00000/01110/10001/11111/10000/01110";
                case 'x':return "00000/00000/10001/01010/00100/01010/10001";
                case '/':return "00001/00001/00010/00100/01000/10000/10000";
                case ' ':return "00000/00000/00000/00000/00000/00000/00000";
                default:throw new ArgumentException("Unsupported author plate glyph");
            }
        }
    }
}
