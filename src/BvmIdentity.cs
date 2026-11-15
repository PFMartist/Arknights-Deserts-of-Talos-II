using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // A custom-only presentation adapter. Shared native atlases, simulation
    // attributes, selection/collision geometry and NLIPS are never modified.
    public static class BvmIdentity
    {
        static readonly string[] Fields={"mUnitIconName","mUnitSensorBlipName","mUnitProductionIconName","mUnitFleetPanelIconName","m_UnitInfoPanelIconName"};
        static readonly string[] Names={"EF_BVM_Unit","EF_BVM_Sensor","EF_BVM_Production","EF_BVM_Fleet","EF_BVM_Info"};
        static readonly int[] Defaults={48,24,96,64,128};
        static readonly Dictionary<UIAtlas,UIAtlas> Atlases=new Dictionary<UIAtlas,UIAtlas>();
        static readonly Dictionary<UISprite,SpriteRoute> Routes=new Dictionary<UISprite,SpriteRoute>();
        static readonly List<UnityEngine.Object> Owned=new List<UnityEngine.Object>();
        static readonly List<UISprite> Retired=new List<UISprite>();
        static string[] ReferenceNames,OriginalNames;
        static UnitHUDAttributes CustomHUD;
        static GameObject Templates;
        static Texture2D Sheet;
        static Color32[] SheetPixels;
        static float NextScan;
        static bool MetricsReady;
        static Bounds BodyBounds;
        static float BodyTop,Diameter;
        static int RouteReports;

        sealed class SpriteRoute { public UIAtlas Original,Clone; }

        public static void ApplyHud(object customHudData,object originalHudData)
        {
            UnitHUDAttributes custom=customHudData as UnitHUDAttributes,original=originalHudData as UnitHUDAttributes;
            if(custom==null||original==null||object.ReferenceEquals(custom,original))throw new InvalidOperationException("BVM identity needs an independent custom HUD");
            if(CustomHUD!=null&&!object.ReferenceEquals(CustomHUD,custom))Reset();
            EnsureTemplates();LoadSheet();CustomHUD=custom;
            object previous=NativeRead.Get(custom,"m_IconSettings"),native=NativeRead.Get(original,"m_IconSettings");
            if(previous==null||native==null)throw new InvalidOperationException("Native icon settings unavailable");
            object icons=CloneConfig(previous),hover=CloneConfig(NativeRead.Get(custom,"m_HoverSettings"));
            ReferenceNames=new string[Names.Length];OriginalNames=new string[Names.Length];
            for(int i=0;i<Names.Length;i++)
            {
                ReferenceNames[i]=(string)NativeRead.Get(previous,Fields[i]);OriginalNames[i]=(string)NativeRead.Get(native,Fields[i]);
                Set(icons,Fields[i],Names[i]);
            }
            // Native ResizeIconProgressBars clamps the projected unit height to
            // these exact fields; a small physical tank should not get a LAV bar.
            Set(icons,"m_IconBarMinWidthPixels",28);Set(icons,"m_IconBarMaxWidthPixels",64);
            Set(custom,"m_IconSettings",icons);if(hover!=null)Set(custom,"m_HoverSettings",hover);
            GameObject sourcePrefab=NativeRead.Get(icons,"mHUDIconPrefab") as GameObject;
            if(sourcePrefab!=null)
            {
                GameObject prefab=(GameObject)UnityEngine.Object.Instantiate(sourcePrefab);prefab.name="EF_BVM_"+sourcePrefab.name;
                prefab.transform.parent=Templates.transform;prefab.hideFlags=HideFlags.HideAndDontSave;
                foreach(UISprite sprite in prefab.GetComponentsInChildren<UISprite>(true))
                    if(sprite.atlas!=null)sprite.atlas=CloneAtlas(sprite.atlas);
                Set(icons,"mHUDIconPrefab",prefab);
            }
            for(int i=0;i<Names.Length;i++)if((string)NativeRead.Get(native,Fields[i])!=OriginalNames[i])throw new InvalidOperationException("Native icon names changed");
            Runtime.Log("BVM_IDENTITY_HUD_READY names=5; source=deterministicBVMTopProjection; AIImage=False; independentHUD=True; globalAtlasReplacementUntouched=True; healthBarPixels=28..64; actualUIAcceptancePending=True");
        }

        public static void ApplyView(UnitViewAttributes custom,UnitViewAttributes original)
        {
            if(custom==null||original==null||object.ReferenceEquals(custom,original)||custom.ModelPrefab==null||custom.ModelPrefab==original.ModelPrefab)
                throw new InvalidOperationException("BVM identity needs an independent prepared model");
            BvmRigBinding rig=FindRig(custom.ModelPrefab);
            if(rig==null||rig.ModelRoot==null)throw new InvalidOperationException("BVM identity rig unavailable");
            Measure(rig);ApplyMetricHud(custom.ModelPrefab.GetComponent<UnitView>());
        }

        public static void Bind(UnitView view)
        {
            if(view==null||!IsCustom(view))return;
            BvmIdentityView binding=view.GetComponent<BvmIdentityView>();
            if(binding==null)binding=view.gameObject.AddComponent<BvmIdentityView>();
            binding.Initialize(view);
        }

        static BvmRigBinding FindRig(GameObject node)
        {
            if(node==null)return null;BvmRigBinding[] found=node.GetComponentsInChildren<BvmRigBinding>(true);return found.Length==0?null:found[0];
        }
        static bool IsCustom(UnitView view)
        {
            if(view==null||FindRig(view.gameObject)==null)return false;
            UnitView.SpawnSettings settings=view.Settings;
            return settings!=null&&settings.HUDAttributes!=null&&settings.HUDAttributes.IconSettings!=null&&
                IsName(settings.HUDAttributes.IconSettings.UnitIconName);
        }

        static void Measure(BvmRigBinding rig)
        {
            HashSet<Mesh> meshes=new HashSet<Mesh>();List<float> heights=new List<float>();bool ready=false;
            foreach(SkinnedMeshRenderer renderer in rig.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh=renderer.sharedMesh;if(mesh==null||!mesh.name.StartsWith("EF_BVM_Chunk",StringComparison.Ordinal)||!meshes.Add(mesh))continue;
                int kind=BvmRigBinding.Static;
                if(renderer.bones!=null&&renderer.bones.Length==1&&rig.Bones!=null)
                    for(int n=0;n<rig.Bones.Length;n++)if(rig.Bones[n]==renderer.bones[0]){kind=rig.RigKinds[n];break;}
                // Barrel length is excluded from the footprint, while the hull,
                // tracks and turret remain represented by their actual vertices.
                if(kind==BvmRigBinding.Pitch||kind==BvmRigBinding.Recoil)continue;
                foreach(Vector3 point in mesh.vertices)
                {
                    if(!ready){BodyBounds=new Bounds(point,Vector3.zero);ready=true;}else BodyBounds.Encapsulate(point);
                    heights.Add(point.y);
                }
            }
            if(!ready||BodyBounds.size.x<=0f||BodyBounds.size.z<=0f||heights.Count==0)throw new InvalidOperationException("BVM identity cannot measure body geometry");
            heights.Sort();BodyTop=heights[Math.Min(heights.Count-1,(int)(heights.Count*.995f))];
            Diameter=Math.Max(BodyBounds.size.x,BodyBounds.size.z)*1.08f;MetricsReady=true;
            Runtime.Log("BVM_IDENTITY_GEOMETRY bodyBounds="+BodyBounds+"; bodyTopPercentile995="+BodyTop+"; selectionRingDiameter="+Diameter+"; barrelExcluded=True; actualVerticesUsed=True; collisionGeometryUnchanged=True");
        }

        static void ApplyMetricHud(UnitView template)
        {
            if(CustomHUD==null||!MetricsReady)return;
            object icons=NativeRead.Get(CustomHUD,"m_IconSettings");Set(icons,"m_IconYOffset",BodyTop+.65f);Set(icons,"m_UnderlayOffset",.06f);
            object hover=NativeRead.Get(CustomHUD,"m_HoverSettings");
            BoxCollider collider=template==null?null:template.SelectionCollider;
            if(hover!=null&&collider!=null)
            {
                // Native hover box uses collider half-size + these offsets.
                // Changing offsets only changes its decoration, never hit testing.
                Set(hover,"m_WorldOffsetWidth",BodyBounds.size.x*.5f+.25f-collider.size.x*.5f);
                Set(hover,"m_WorldOffsetLength",BodyBounds.size.z*.5f+.35f-collider.size.z*.5f);
                Set(hover,"m_World2DPaddingHorizontal",.12f);Set(hover,"m_World2DPaddingVertical",.12f);
            }
        }

        public static void MainTick()
        {
            if(CustomHUD==null||Time.realtimeSinceStartup<NextScan)return;NextScan=Time.realtimeSinceStartup+.5f;
            Retired.Clear();
            foreach(KeyValuePair<UISprite,SpriteRoute> pair in Routes)
            {
                UISprite sprite=pair.Key;
                if(sprite==null){Retired.Add(sprite);continue;}
                if(!sprite.gameObject.activeInHierarchy||!IsName(sprite.spriteName))
                {
                    if(sprite.atlas==pair.Value.Clone)sprite.atlas=pair.Value.Original;
                    Retired.Add(sprite);
                }
            }
            foreach(UISprite sprite in Retired)Routes.Remove(sprite);
            foreach(UISprite sprite in Resources.FindObjectsOfTypeAll(typeof(UISprite)))
            {
                if(sprite==null||!sprite.gameObject.activeInHierarchy||!IsName(sprite.spriteName)||sprite.atlas==null)continue;
                SpriteRoute route;
                if(Routes.TryGetValue(sprite,out route)&&sprite.atlas==route.Clone)continue;
                UIAtlas original=sprite.atlas;
                // A custom HUD prefab may already reference one of our clones.
                UIAtlas native=NativeAtlas(original);UIAtlas clone=CloneAtlas(native);
                Routes[sprite]=new SpriteRoute{Original=native,Clone=clone};sprite.atlas=clone;sprite.MakePixelPerfect();
                if(RouteReports<12){Runtime.Log("BVM_ICON_ATLAS_ROUTED sprite="+sprite.spriteName+"; nativeAtlas="+native.name+"; clone="+clone.name+"; originalSpritesRetained=True");RouteReports++;}
            }
        }

        static UIAtlas NativeAtlas(UIAtlas atlas)
        {
            foreach(KeyValuePair<UIAtlas,UIAtlas> pair in Atlases)if(pair.Value==atlas)return pair.Key;
            return atlas;
        }
        static bool IsName(string name){if(name==null)return false;foreach(string item in Names)if(item==name)return true;return false;}
        static void EnsureTemplates()
        {
            if(Templates!=null)return;Templates=new GameObject("EF_BVM_IdentityTemplates");Templates.hideFlags=HideFlags.HideAndDontSave;
            Templates.SetActive(false);UnityEngine.Object.DontDestroyOnLoad(Templates);
        }
        static void LoadSheet()
        {
            if(Sheet!=null)return;string file=Path.Combine(CustomUnits.AssetDirectory,"bvm_identity/bvm_identity_icons.png");
            Sheet=new Texture2D(2,2,TextureFormat.RGBA32,false);Sheet.hideFlags=HideFlags.HideAndDontSave;
            if(!Sheet.LoadImage(File.ReadAllBytes(file))||Sheet.width!=1280||Sheet.height!=256)throw new InvalidDataException("BVM icon sheet contract mismatch");
            SheetPixels=Sheet.GetPixels32();Owned.Add(Sheet);
        }

        static UIAtlas CloneAtlas(UIAtlas native)
        {
            // Theme the base before adding this unit's private icon sprites.
            // The custom extension retains its coordinates and all added icons.
            native=EndfieldHudTheme.ThemeAtlas(native);
            UIAtlas result;if(Atlases.TryGetValue(native,out result))return result;
            EnsureTemplates();LoadSheet();Texture source=native.texture;
            if(source==null||native.spriteMaterial==null)throw new InvalidOperationException("Native icon atlas has no texture/material: "+native.name);
            int width=source.width,height=source.height,cursorX=2,cursorY=height+2,rowHeight=0;
            UISpriteData[] extras=new UISpriteData[Names.Length];
            for(int i=0;i<Names.Length;i++)
            {
                UISpriteData reference=native.GetSprite(ReferenceNames[i]);if(reference==null)reference=native.GetSprite(OriginalNames[i]);
                int w=reference==null?Defaults[i]:reference.width,h=reference==null?Defaults[i]:reference.height;
                if(w<=0||h<=0||w>1024||h>1024)throw new InvalidOperationException("Unexpected native icon dimensions");
                width=Math.Max(width,w+4);
                if(cursorX+w+2>width){cursorX=2;cursorY+=rowHeight+4;rowHeight=0;}
                UISpriteData data=new UISpriteData();data.name=Names[i];data.x=cursorX;data.y=cursorY;data.width=w;data.height=h;
                if(reference!=null){data.paddingLeft=reference.paddingLeft;data.paddingRight=reference.paddingRight;data.paddingTop=reference.paddingTop;data.paddingBottom=reference.paddingBottom;}
                extras[i]=data;cursorX+=w+4;rowHeight=Math.Max(rowHeight,h);
            }
            int newHeight=cursorY+rowHeight+2;
            if(width>SystemInfo.maxTextureSize||newHeight>SystemInfo.maxTextureSize)throw new InvalidOperationException("BVM extended atlas exceeds GPU texture limit");
            Color32[] original=ReadAtlasPixels(source),pixels=new Color32[width*newHeight];
            bool pixelsPresent=false;foreach(Color32 pixel in original)if(pixel.a!=0){pixelsPresent=true;break;}
            if(!pixelsPresent)throw new InvalidDataException("Native atlas readback returned no nontransparent pixels: "+native.name);
            // UISpriteData y is top-origin; retain every original sprite's exact
            // coordinates by keeping the original texture aligned to the top.
            for(int row=0;row<height;row++)Array.Copy(original,row*source.width,pixels,(row+newHeight-height)*width,source.width);
            bool pma=native.premultipliedAlpha;
            for(int i=0;i<extras.Length;i++)FillIcon(pixels,width,newHeight,extras[i],i,pma);
            Texture2D texture=new Texture2D(width,newHeight,TextureFormat.RGBA32,false);texture.name="EF_BVM_Atlas_"+native.name;texture.hideFlags=HideFlags.HideAndDontSave;
            texture.SetPixels32(pixels);texture.Apply(false,false);texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=source.filterMode;Owned.Add(texture);
            Material material=new Material(native.spriteMaterial);material.name="EF_BVM_AtlasMaterial_"+native.name;material.hideFlags=HideFlags.HideAndDontSave;material.mainTexture=texture;Owned.Add(material);
            GameObject node=new GameObject("EF_BVM_Atlas_"+native.name);node.transform.parent=Templates.transform;node.hideFlags=HideFlags.HideAndDontSave;
            result=node.AddComponent<UIAtlas>();result.spriteMaterial=material;result.pixelSize=native.pixelSize;
            List<UISpriteData> sprites=new List<UISpriteData>();foreach(UISpriteData data in native.spriteList)sprites.Add(CopySprite(data));
            sprites.AddRange(extras);result.spriteList=sprites;result.MarkSpriteListAsChanged();Atlases.Add(native,result);
            // Byte-for-byte source region check is independent of UI rendering.
            for(int row=0;row<height;row++)for(int col=0;col<source.width;col++)
            {
                Color32 a=original[row*source.width+col],b=pixels[(row+newHeight-height)*width+col];
                if(a.r!=b.r||a.g!=b.g||a.b!=b.b||a.a!=b.a)throw new InvalidOperationException("Native atlas source region changed");
            }
            Runtime.Log("BVM_IDENTITY_ATLAS_READY native="+native.name+"; nativeSprites="+native.spriteList.Count+"; addedSprites=5; size="+width+"x"+newHeight+"; sourcePixelRegionPreserved=True; nativeAtlasObjectUntouched=True; PMA="+pma);
            return result;
        }

        static Color32[] ReadAtlasPixels(Texture source)
        {
            Texture2D texture=source as Texture2D;
            if(texture!=null)
            {
                try{return texture.GetPixels32();}catch(UnityException){ }
            }
            // Read the atlas ASSET only, never a camera, framebuffer or screen.
            RenderTexture prior=RenderTexture.active,target=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32);
            Texture2D readable=null;
            try
            {
                Graphics.Blit(source,target);RenderTexture.active=target;readable=new Texture2D(source.width,source.height,TextureFormat.ARGB32,false);
                readable.ReadPixels(new Rect(0,0,source.width,source.height),0,0);readable.Apply();return readable.GetPixels32();
            }
            finally{RenderTexture.active=prior;RenderTexture.ReleaseTemporary(target);if(readable!=null)UnityEngine.Object.DestroyImmediate(readable);}
        }
        static void FillIcon(Color32[] pixels,int width,int height,UISpriteData sprite,int tile,bool pma)
        {
            float side=Math.Min(sprite.width,sprite.height),offsetX=(sprite.width-side)*.5f,offsetY=(sprite.height-side)*.5f;
            for(int y=0;y<sprite.height;y++)for(int x=0;x<sprite.width;x++)
            {
                float u=(x+.5f-offsetX)/side,v=(y+.5f-offsetY)/side;byte alpha=0;
                if(u>=0f&&u<=1f&&v>=0f&&v<=1f)
                {
                    int px=Mathf.Clamp((int)(u*256f),0,255),py=Mathf.Clamp((int)(v*256f),0,255);
                    alpha=SheetPixels[py*1280+tile*256+px].a;
                }
                byte white=pma?alpha:(byte)255;pixels[(height-sprite.y-sprite.height+y)*width+sprite.x+x]=new Color32(white,white,white,alpha);
            }
        }
        static UISpriteData CopySprite(UISpriteData source)
        {
            UISpriteData target=new UISpriteData();target.name=source.name;target.x=source.x;target.y=source.y;target.width=source.width;target.height=source.height;
            target.borderLeft=source.borderLeft;target.borderRight=source.borderRight;target.borderTop=source.borderTop;target.borderBottom=source.borderBottom;
            target.paddingLeft=source.paddingLeft;target.paddingRight=source.paddingRight;target.paddingTop=source.paddingTop;target.paddingBottom=source.paddingBottom;return target;
        }
        static object CloneConfig(object source){return source==null?null:typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(source,null);}
        static void Set(object target,string name,object value)
        {
            for(Type type=target.GetType();type!=null&&type!=typeof(object);type=type.BaseType)
            {
                FieldInfo field=type.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly);
                if(field!=null){field.SetValue(target,value);return;}
            }
            throw new MissingFieldException(target.GetType().FullName,name);
        }
        public static void Reset()
        {
            foreach(KeyValuePair<UISprite,SpriteRoute> pair in Routes)
                if(pair.Key!=null&&pair.Key.atlas==pair.Value.Clone)pair.Key.atlas=pair.Value.Original;
            Routes.Clear();Atlases.Clear();if(Templates!=null)UnityEngine.Object.DestroyImmediate(Templates);Templates=null;
            for(int i=Owned.Count-1;i>=0;i--)if(Owned[i]!=null)UnityEngine.Object.DestroyImmediate(Owned[i]);Owned.Clear();
            Sheet=null;SheetPixels=null;CustomHUD=null;ReferenceNames=OriginalNames=null;MetricsReady=false;NextScan=0f;RouteReports=0;
        }

        public sealed class BvmIdentityView : MonoBehaviour
        {
            UnitView View;
            BvmRigBinding Rig;
            GameObject NativeRing,OwnRing;
            Renderer[] NativeRenderers=new Renderer[0];
            bool[] NativeEnabled=new bool[0];
            Mesh RingMesh;
            MeshRenderer RingRenderer;
            Material RingMaterial;
            Texture2D WhiteTexture;
            float NextReport,RenderDiameter,RenderBand,MetresPerPixel;
            int Reports;
            bool LastVisible,HaveVisibleState;
            bool SpriteShader;
            const float GroundLift=.12f;

            public void Initialize(UnitView view)
            {
                if(View!=null&&View!=view){RestoreNative();DestroyOwn();}
                View=view;Rig=FindRig(view.gameObject);
                if(!MetricsReady&&Rig!=null){Measure(Rig);ApplyMetricHud(view);}
            }

            void LateUpdate()
            {
                if(View==null||Rig==null||Rig.ModelRoot==null||!IsCustom(View))
                {RestoreNative();if(RingRenderer!=null)RingRenderer.enabled=false;return;}
                GameObject current=NativeRead.Get(View,"mUnderlayObject") as GameObject;
                if(current!=NativeRing)
                {
                    RestoreNative();NativeRing=current;
                    if(NativeRing!=null)
                    {
                        NativeRenderers=NativeRing.GetComponentsInChildren<Renderer>(true);NativeEnabled=new bool[NativeRenderers.Length];
                        for(int i=0;i<NativeRenderers.Length;i++)NativeEnabled[i]=NativeRenderers[i]!=null&&NativeRenderers[i].enabled;
                    }
                }
                if(OwnRing==null)CreateOwn();
                // Keep native active/selection/FOW decisions intact. Only replace
                // the renderer on this specific custom instance, never its asset.
                for(int i=0;i<NativeRenderers.Length;i++)if(NativeRenderers[i]!=null)NativeRenderers[i].enabled=false;
                Vector3 center=Rig.ModelRoot.TransformPoint(new Vector3(BodyBounds.center.x,0f,BodyBounds.center.z));
                center.y=Rig.ModelRoot.position.y+GroundLift;OwnRing.transform.position=center;
                // This mesh is explicitly authored in XZ. Its normal and rotation
                // are independent of the native prefab's root/child orientation.
                OwnRing.transform.rotation=Quaternion.AngleAxis(Rig.ModelRoot.eulerAngles.y,Vector3.up);
                Vector3 scale=OwnRing.transform.parent==null?Vector3.one:OwnRing.transform.parent.lossyScale;
                OwnRing.transform.localScale=new Vector3(1f/Math.Max(.00001f,Math.Abs(scale.x)),1f/Math.Max(.00001f,Math.Abs(scale.y)),1f/Math.Max(.00001f,Math.Abs(scale.z)));
                object fade=NativeRead.Get(View,"mFOWFade");
                float visibility=fade==null?0f:Mathf.Clamp01(Convert.ToSingle(NativeRead.Get(fade,"Value")));
                bool nativeActive=NativeRing==null||View.UnderlayActive;
                bool shown=View.Selected&&nativeActive&&View.gameObject.activeInHierarchy&&visibility>.001f;
                FitProjectedBand();
                Color colour=NativeSelectionColour();colour.a=(SpriteShader?.70f:1f)*visibility;
                RingMaterial.color=colour;RingRenderer.enabled=shown;
                bool changed=!HaveVisibleState||shown!=LastVisible;
                if(changed||(Reports<10&&Time.realtimeSinceStartup>=NextReport))
                {
                    ReportState(shown,nativeActive,visibility,colour);HaveVisibleState=true;LastVisible=shown;Reports++;NextReport=Time.realtimeSinceStartup+.5f;
                }
            }

            Color NativeSelectionColour()
            {
                UnitHUDInteractionAttributes.IconColourings colours=NativeRead.Get(View,"mIconColours") as UnitHUDInteractionAttributes.IconColourings;
                if(colours!=null)
                {
                    Color colour=colours.SelectedColour;
                    if(Math.Max(colour.r,Math.Max(colour.g,colour.b))>.05f)return colour;
                }
                return new Color(.30f,1f,.48f,1f);
            }

            void CreateOwn()
            {
                Shader shader=Shader.Find("Sprites/Default");
                SpriteShader=shader!=null&&shader.isSupported;
                if(!SpriteShader){shader=Shader.Find("Unlit/Color");if(shader==null||!shader.isSupported)throw new InvalidOperationException("No supported shader for custom BVM selection ring");}
                RenderDiameter=Diameter;RenderBand=BandWidth(Diameter);
                Vector3[] vertices;int[] indices;Vector2[] uv;BuildRingBuffers(RenderDiameter,RenderBand,out vertices,out indices,out uv);
                RingMesh=new Mesh();RingMesh.name="EF_BVM_SelectionRing_XZ_BothWindings";RingMesh.hideFlags=HideFlags.HideAndDontSave;
                RingMesh.vertices=vertices;RingMesh.uv=uv;RingMesh.triangles=indices;
                Vector3[] normals=new Vector3[vertices.Length];Color[] colours=new Color[vertices.Length];
                for(int i=0;i<vertices.Length;i++){normals[i]=Vector3.up;colours[i]=Color.white;}
                RingMesh.normals=normals;RingMesh.colors=colours;RingMesh.RecalculateBounds();Bounds bounds=RingMesh.bounds;bounds.Expand(.05f);RingMesh.bounds=bounds;
                WhiteTexture=new Texture2D(1,1,TextureFormat.ARGB32,false);WhiteTexture.name="EF_BVM_RingWhite";WhiteTexture.hideFlags=HideFlags.HideAndDontSave;
                WhiteTexture.SetPixel(0,0,Color.white);WhiteTexture.Apply();
                RingMaterial=new Material(shader);RingMaterial.name="EF_BVM_SelectionRing_Instance_"+View.GetInstanceID();RingMaterial.hideFlags=HideFlags.HideAndDontSave;
                if(RingMaterial.HasProperty("_MainTex"))RingMaterial.mainTexture=WhiteTexture;
                RingMaterial.color=NativeSelectionColour();RingMaterial.renderQueue=3200;
                // Where a shader exposes these switches, make them explicit.
                // Standard Sprites/Default hardcodes Cull Off / ZWrite Off;
                // double-winding geometry also works with the unlit fallback.
                if(RingMaterial.HasProperty("_Cull"))RingMaterial.SetInt("_Cull",0);
                if(RingMaterial.HasProperty("_ZWrite"))RingMaterial.SetInt("_ZWrite",0);
                if(RingMaterial.HasProperty("_ZTest"))RingMaterial.SetInt("_ZTest",4);
                OwnRing=new GameObject("EF_BVM_SelectionRing_ExplicitXZ");OwnRing.layer=LayerMask.NameToLayer("Default");OwnRing.transform.parent=Rig.ModelRoot;
                OwnRing.transform.localPosition=Vector3.zero;OwnRing.transform.localRotation=Quaternion.identity;OwnRing.transform.localScale=Vector3.one;
                OwnRing.AddComponent<MeshFilter>().sharedMesh=RingMesh;RingRenderer=OwnRing.AddComponent<MeshRenderer>();RingRenderer.enabled=false;
                RingRenderer.sharedMaterial=RingMaterial;RingRenderer.receiveShadows=false;RingRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                Runtime.Log("BVM_SELECTION_RING_CREATED source=proceduralExplicitXZ; shader="+shader.name+"; shaderSupported="+shader.isSupported+
                    "; vertices="+vertices.Length+"; triangles="+(indices.Length/3)+"; geometryBothWindings=True; groundLift="+GroundLift+"; bandWidth="+BandWidth(Diameter)+
                    "; diameter="+Diameter+"; meshBounds="+RingMesh.bounds+"; queue=3200; layer=Default; nativeUnderlayTransformUntouched=True; nativeSharedAssetsUntouched=True; SelectionColliderUnchanged=True; actualPixelAcceptancePending=True");
            }

            void FitProjectedBand()
            {
                object hud=View.Settings.HUDSystem, camera=hud==null?null:NativeRead.Get(hud,"ActiveUserCamera");
                MetresPerPixel=camera==null?0f:Convert.ToSingle(NativeRead.Get(camera,"MetersPerPixel"));
                if(float.IsNaN(MetresPerPixel)||float.IsInfinity(MetresPerPixel)||MetresPerPixel<0f)MetresPerPixel=0f;
                // At distant tactical scales this remains an eight-pixel UI
                // selection marker with a >=1 pixel band. No unit NLIPS changes.
                float diameter=Math.Max(Diameter,MetresPerPixel*8f),band=Math.Max(BandWidth(Diameter),MetresPerPixel*1.15f);
                if(Math.Abs(diameter-RenderDiameter)<=RenderDiameter*.08f&&Math.Abs(band-RenderBand)<=RenderBand*.08f)return;
                Vector3[] points;int[] indices;Vector2[] uv;BuildRingBuffers(diameter,band,out points,out indices,out uv);
                RingMesh.vertices=points;RingMesh.uv=uv;RingMesh.triangles=indices;RingMesh.RecalculateBounds();Bounds bounds=RingMesh.bounds;bounds.Expand(.05f);RingMesh.bounds=bounds;
                RenderDiameter=diameter;RenderBand=band;
            }

            static float BandWidth(float diameter){return Math.Min(.30f,Math.Max(.20f,diameter*.030f));}
            // Pure managed buffer construction, also invoked by the offline
            // fixture. No Unity object, camera, frame-buffer or game is required.
            static void BuildRingBuffers(float diameter,float bandWidth,out Vector3[] vertices,out int[] indices,out Vector2[] uv)
            {
                if(float.IsNaN(diameter)||float.IsInfinity(diameter)||diameter<=.5f)throw new ArgumentException("Invalid ring diameter");
                List<Vector3> points=new List<Vector3>();List<int> triangles=new List<int>();
                float outer=diameter*.5f,inner=outer-bandWidth;
                if(bandWidth<=0f||inner<=0f)throw new ArgumentException("Invalid ring band width");
                for(int sector=0;sector<4;sector++)
                    for(int segment=0;segment<24;segment++)
                    {
                        double start=(sector*90.0+4.5+segment*81.0/24.0)*Math.PI/180.0;
                        double end=(sector*90.0+4.5+(segment+1)*81.0/24.0)*Math.PI/180.0;
                        AddDoubleQuad(points,triangles,Polar(outer,start),Polar(outer,end),Polar(inner,end),Polar(inner,start));
                    }
                // Four short radial bars retain a conventional RTS selection
                // language without baking icons or creating a filled disc.
                for(int i=0;i<4;i++)
                {
                    double angle=i*Math.PI*.5;Vector3 outward=Polar(1f,angle),side=Polar(1f,angle+Math.PI*.5);
                    Vector3 near=outward*(outer+.06f),far=outward*(outer+.36f),half=side*.085f;
                    AddDoubleQuad(points,triangles,near-half,near+half,far+half,far-half);
                }
                vertices=points.ToArray();indices=triangles.ToArray();uv=new Vector2[vertices.Length];
                for(int i=0;i<uv.Length;i++)uv[i]=new Vector2(.5f,.5f);
            }
            static Vector3 Polar(float radius,double radians){return new Vector3((float)Math.Sin(radians)*radius,0f,(float)Math.Cos(radians)*radius);}
            static void AddDoubleQuad(List<Vector3> vertices,List<int> triangles,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {
                int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);
                int[] both={0,1,2,0,2,3,2,1,0,3,2,0};foreach(int index in both)triangles.Add(start+index);
            }

            void ReportState(bool shown,bool nativeActive,float visibility,Color colour)
            {
                string cameras="";int masks=0;
                foreach(Camera camera in Camera.allCameras)
                {
                    if(camera==null||!camera.enabled||!camera.gameObject.activeInHierarchy)continue;
                    bool includes=(camera.cullingMask&(1<<OwnRing.layer))!=0;if(includes)masks++;
                    cameras+=camera.name+":"+(includes?"layerIncluded":"layerExcluded")+" ";
                }
                Runtime.Log("BVM_SELECTION_RING_RENDER_STATE selected="+View.Selected+"; nativeUnderlayActive="+nativeActive+"; FOWFade="+visibility+
                    "; sensors="+UserCamera.IsSensorsModeActive+"; rendererEnabled="+RingRenderer.enabled+"; active="+OwnRing.activeInHierarchy+"; visibleToAnyCamera="+RingRenderer.isVisible+
                    "; cameraMasksIncludingLayer="+masks+"; cameras="+cameras+"; worldPosition="+OwnRing.transform.position+"; worldBounds="+RingRenderer.bounds+
                    "; ownMeshXZ=True; bothTriangleWindings=True; worldNormalDot="+Vector3.Dot(OwnRing.transform.up,Vector3.up)+"; groundLift="+GroundLift+
                    "; colour="+colour+"; material="+RingMaterial.name+"; shader="+RingMaterial.shader.name+"; shaderSupported="+RingMaterial.shader.isSupported+"; layer="+OwnRing.layer+"; renderDiameter="+RenderDiameter+"; bandMetres="+RenderBand+"; metresPerPixel="+MetresPerPixel+"; bandPixels="+(MetresPerPixel>.000001f?RenderBand/MetresPerPixel:0f)+"; queue="+RingMaterial.renderQueue+
                    "; cull="+(SpriteShader?"Off(shader)+bothGeometryWindings":"bothGeometryWindings")+"; ZWrite="+(SpriteShader?"Off(shader)":"nativeUnlitFallback")+
                    "; ZTest=LEqual; nativeInstanceRenderersHidden="+NativeRenderers.Length+"; pixelAcceptancePending=True");
                if(shown&&masks==0)Runtime.Log("BVM_SELECTION_RING_CAMERA_MASK_WARNING noActiveCameraIncludesDefaultLayer; pixelAcceptancePending=True");
            }
            void RestoreNative()
            {
                for(int i=0;i<NativeRenderers.Length;i++)if(NativeRenderers[i]!=null)NativeRenderers[i].enabled=NativeEnabled[i];
                NativeRing=null;NativeRenderers=new Renderer[0];NativeEnabled=new bool[0];HaveVisibleState=false;
            }
            void DestroyOwn()
            {
                if(OwnRing!=null)UnityEngine.Object.DestroyImmediate(OwnRing);OwnRing=null;RingRenderer=null;
                if(RingMesh!=null)UnityEngine.Object.DestroyImmediate(RingMesh);RingMesh=null;
                if(RingMaterial!=null)UnityEngine.Object.DestroyImmediate(RingMaterial);RingMaterial=null;
                if(WhiteTexture!=null)UnityEngine.Object.DestroyImmediate(WhiteTexture);WhiteTexture=null;
            }
            void OnDisable(){RestoreNative();if(RingRenderer!=null)RingRenderer.enabled=false;}
            void OnDestroy(){RestoreNative();DestroyOwn();View=null;Rig=null;}
        }
    }
}
