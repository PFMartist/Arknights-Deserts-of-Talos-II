using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // One custom info-panel sprite, routed through the existing HUD atlas
    // lifecycle. No Update, global UI search, native atlas edit or gameplay hook.
    public static class RhodesHudIcon
    {
        public const string SpriteName="EF_RHODES_Info";
        static readonly string[] Names={SpriteName,"EF_RHODES_Info_AV","EF_RHODES_Info_GN"};
        const string FieldName="m_UnitInfoPanelIconName";
        static string ReferenceName;
        static readonly Texture2D[] Icons=new Texture2D[3];
        static readonly Color32[][] IconPixels=new Color32[3][];
        static GameObject Templates;
        static bool Warned;
        static bool UpdateWarned;
        static bool CropReady;
        static int CropX,CropY,CropWidth=256,CropHeight=256;
        static readonly AtlasPositiveCache61 Cache=new AtlasPositiveCache61();
        static int ReferenceGeneration;
        static readonly List<UnityEngine.Object> Owned=new List<UnityEngine.Object>();

        public static void Configure(object customData,object originalData)
        {
            UnitHUDAttributes custom=customData as UnitHUDAttributes,original=originalData as UnitHUDAttributes;
            if(custom==null||original==null||object.ReferenceEquals(custom,original))
                throw new InvalidOperationException("Rhodes info icon requires an independent custom HUD");
            EnsureIcon();
            object previous=NativeRead.Get(custom,"m_IconSettings"),native=NativeRead.Get(original,"m_IconSettings");
            if(previous==null||native==null)throw new InvalidOperationException("Rhodes HUD icon settings missing");
            string nativeName=(string)NativeRead.Get(native,FieldName);
            object copy=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(previous,null);
            string reference=(string)NativeRead.Get(previous,FieldName);
            if(string.IsNullOrEmpty(reference)||reference==SpriteName)reference=nativeName;
            if(string.IsNullOrEmpty(reference))throw new InvalidOperationException("Rhodes native info reference sprite missing");
            if(ReferenceName!=reference){ReferenceName=reference;ReferenceGeneration++;}
            NativeRead.Set(copy,FieldName,SpriteName);NativeRead.Set(custom,"m_IconSettings",copy);
            if((string)NativeRead.Get(native,FieldName)!=nativeName)
                throw new InvalidOperationException("Rhodes info icon changed native donor HUD");
            Runtime.Log("RHODES_INFO_ICON_CONFIGURED sprite="+SpriteName+"; field="+FieldName+"; reference="+ReferenceName+
                "; privateHUDOnly=True; modelDerivedTopProjection=True; recurringUIScan=False; runtimeUIAcceptancePending=True");
        }

        // Call around the existing ThemeAtlas return value. Only the native
        // atlas containing the actual info reference is extended. Other atlas
        // objects are returned unchanged. An extended atlas is idempotent.
        public static UIAtlas MapAtlas(UIAtlas source)
        {return RhodesUnitIcons.MapAtlas(MapCarrierAtlas(source));}
        static UIAtlas MapCarrierAtlas(UIAtlas source)
        {
            if(source==null||string.IsNullOrEmpty(ReferenceName))return source;
            UIAtlas cached;if(Cache.TryGetValue(source,ReferenceGeneration,out cached)&&cached!=null)return cached;
            if(source.GetSprite(Names[2])!=null)return source;
            UISpriteData reference=source.GetSprite(ReferenceName);if(reference==null)return source;
            try
            {
                if(Cache.Count>=4&&!Cache.ContainsKey(source))throw new InvalidOperationException("Rhodes info atlas cache budget exceeded");
                UIAtlas result=BuildAtlas(source,reference);Cache.Store(source,result,ReferenceGeneration);return result;
            }
            catch(Exception error)
            {
                if(!Warned){Warned=true;Runtime.Log("WARNING RHODES_INFO_ICON "+error.GetType().Name+": "+error.Message+"; nativeAtlasUntouched=True");}
                return source;
            }
        }

        static void EnsureIcon()
        {
            for(int i=0;i<Names.Length;i++)
            {
                if(Icons[i]!=null)continue;
                string file=Path.Combine(CustomUnits.AssetDirectory,"rhodes_identity/"+Names[i]+".png");
                Texture2D image=new Texture2D(2,2,TextureFormat.RGBA32,false);image.hideFlags=HideFlags.HideAndDontSave;
                if(!image.LoadImage(File.ReadAllBytes(file))||image.width!=256||image.height!=256)
                {UnityEngine.Object.DestroyImmediate(image);throw new InvalidDataException("Rhodes info icon must be256x256 RGBA PNG");}
                Icons[i]=image;image.name=Names[i]+"Source";IconPixels[i]=image.GetPixels32();Owned.Add(image);
            }
            if(!CropReady)
            {
                int minX=256,minY=256,maxX=-1,maxY=-1;
                for(int i=0;i<Names.Length;i++)for(int y=0;y<256;y++)for(int x=0;x<256;x++)
                    if(IconPixels[i][y*256+x].a>=16){minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
                if(maxX<minX||maxY<minY)throw new InvalidDataException("Rhodes info icon has no visible geometry");
                CropX=Math.Max(0,minX-2);CropY=Math.Max(0,minY-2);
                CropWidth=Math.Min(255,maxX+2)-CropX+1;CropHeight=Math.Min(255,maxY+2)-CropY+1;CropReady=true;
            }
        }

        // Post-native InformationPanelController.UpdateDynamicDataForUnit(Entity).
        // This follows existing selected-unit events and native frame refreshes;
        // only the selected exact Entity is read, and only a changed name writes.
        public static void InfoUpdated(object controller,Entity entity)
        {
            OperatorPortraitIcons.InfoUpdated(controller,entity);
            if(controller==null||entity.Equals(Entity.None)||string.IsNullOrEmpty(ReferenceName))return;
            try
            {
                object selected;
                if(!NativeRead.TryGet(controller,"mSelectedEntity",out selected)||!(selected is Entity)||!((Entity)selected).Equals(entity))return;
                UnitView view;if(!UnitViewDiscovery.TryGet(entity,out view)||view==null||!view.EntityID.Equals(entity))return;
                UnitView.SpawnSettings settings=view.Settings;
                if(settings==null||settings.HUDAttributes==null||settings.HUDAttributes.IconSettings==null||
                    settings.HUDAttributes.IconSettings.UnitInfoPanelIconName!=SpriteName)return;
                object info=NativeRead.Get(controller,"mSettings");if(info==null)return;
                UISprite sprite=NativeRead.Get(info,"UnitIcon") as UISprite;if(sprite==null||sprite.atlas==null)return;
                string desired=SpriteName;
                RhodesDeploymentSnapshot state;
                if(RhodesDeploymentState.TryGet(entity,view.CommanderID,out state)&&
                    (state.DeckReady||state.SourceLevel>0)&&state.Branch>=1&&state.Branch<=2)desired=Names[state.Branch];
                UIAtlas atlas=EndfieldHudTheme.ThemeAtlas(sprite.atlas);
                if(atlas==null||atlas.GetSprite(desired)==null)return;
                if(sprite.atlas!=atlas)sprite.atlas=atlas;
                if(sprite.spriteName!=desired)sprite.spriteName=desired;
            }
            catch(Exception error)
            {
                if(!UpdateWarned){UpdateWarned=true;Runtime.Log("WARNING RHODES_INFO_ICON_UPDATE "+error.GetType().Name+": "+error.Message+"; nativePanelContinues=True");}
            }
        }

        static UIAtlas BuildAtlas(UIAtlas source,UISpriteData reference)
        {
            EnsureIcon();Texture native=source.texture;
            if(native==null||source.spriteMaterial==null)throw new InvalidDataException("Rhodes info atlas lacks texture/material");
            int iconWidth=reference.width,iconHeight=reference.height;
            if(iconWidth<=0||iconHeight<=0||iconWidth>1024||iconHeight>1024)throw new InvalidDataException("Unexpected native info sprite dimensions");
            int width=Math.Max(native.width,iconWidth+4),cursorX=2,cursorY=native.height+2,rowHeight=0;
            UISpriteData[] extras=new UISpriteData[Names.Length];
            for(int i=0;i<extras.Length;i++)
            {
                if(cursorX+iconWidth+2>width){cursorX=2;cursorY+=rowHeight+4;rowHeight=0;}
                UISpriteData entry=new UISpriteData();entry.name=Names[i];entry.x=cursorX;entry.y=cursorY;
                entry.width=iconWidth;entry.height=iconHeight;entry.paddingLeft=reference.paddingLeft;entry.paddingRight=reference.paddingRight;
                entry.paddingTop=reference.paddingTop;entry.paddingBottom=reference.paddingBottom;
                extras[i]=entry;cursorX+=iconWidth+4;rowHeight=Math.Max(rowHeight,iconHeight);
            }
            int height=cursorY+rowHeight+2;
            if(width>SystemInfo.maxTextureSize||height>SystemInfo.maxTextureSize)throw new InvalidDataException("Rhodes info extended atlas exceeds GPU texture limit");
            Color32[] original=ReadPixels(native),pixels=new Color32[width*height];
            for(int row=0;row<native.height;row++)Array.Copy(original,row*native.width,pixels,(row+height-native.height)*width,native.width);
            float scale,ox,oy;RhodesInfoIconLayout.Fit(iconWidth,iconHeight,CropWidth,CropHeight,out scale,out ox,out oy);
            bool pma=source.premultipliedAlpha;
            for(int tile=0;tile<extras.Length;tile++)for(int y=0;y<iconHeight;y++)for(int x=0;x<iconWidth;x++)
            {
                UISpriteData sprite=extras[tile];
                float sx=(x+.5f-ox)/scale,sy=(y+.5f-oy)/scale;byte alpha=0;
                if(sx>=0f&&sx<CropWidth&&sy>=0f&&sy<CropHeight)alpha=SampleAlpha(tile,(CropX+sx)/255f,(CropY+sy)/255f);
                byte white=pma?alpha:(byte)255;
                pixels[(height-sprite.y-iconHeight+y)*width+sprite.x+x]=new Color32(white,white,white,alpha);
            }
            // Exact source region preservation is independent of visual tests.
            for(int row=0;row<native.height;row++)for(int x=0;x<native.width;x++)
            {
                Color32 a=original[row*native.width+x],b=pixels[(row+height-native.height)*width+x];
                if(a.r!=b.r||a.g!=b.g||a.b!=b.b||a.a!=b.a)throw new InvalidDataException("Rhodes info atlas source region changed");
            }
            Texture2D texture=null;Material material=null;GameObject node=null;
            try
            {
                texture=new Texture2D(width,height,TextureFormat.RGBA32,false);texture.name="EF_RHODES_Atlas_"+native.name;
                texture.hideFlags=HideFlags.HideAndDontSave;texture.SetPixels32(pixels);texture.Apply(false,false);
                texture.filterMode=native.filterMode;texture.wrapMode=TextureWrapMode.Clamp;
                material=new Material(source.spriteMaterial);material.name="EF_RHODES_InfoMaterial_"+source.name;
                material.hideFlags=HideFlags.HideAndDontSave;material.mainTexture=texture;
                if(Templates==null)
                {Templates=new GameObject("EF_RHODES_HudIconTemplates");Templates.SetActive(false);Templates.hideFlags=HideFlags.HideAndDontSave;UnityEngine.Object.DontDestroyOnLoad(Templates);}
                node=new GameObject("EF_RHODES_InfoAtlas_"+source.name);node.transform.parent=Templates.transform;node.hideFlags=HideFlags.HideAndDontSave;
                UIAtlas result=node.AddComponent<UIAtlas>();result.spriteMaterial=material;result.pixelSize=source.pixelSize;
                List<UISpriteData> sprites=new List<UISpriteData>();foreach(UISpriteData item in source.spriteList)sprites.Add(Copy(item));
                sprites.AddRange(extras);result.spriteList=sprites;result.MarkSpriteListAsChanged();Owned.Add(texture);Owned.Add(material);
                Runtime.Log("RHODES_INFO_ATLAS_READY source="+source.name+"; nativeSprite="+ReferenceName+"; nativeInfoSize="+iconWidth+"x"+iconHeight+
                    "; addedForms=3; extendedSize="+width+"x"+height+"; PMA="+pma+"; infoSilhouetteRightAligned=True; nativeWidgetSizeAndAnchorsPreserved=True; sourceRegionPreserved=True; nativeAtlasUntouched=True; cachedNoRecurringScan=True");
                return result;
            }
            catch
            {
                if(node!=null)UnityEngine.Object.DestroyImmediate(node);
                if(material!=null)UnityEngine.Object.DestroyImmediate(material);
                if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }
        static byte SampleAlpha(int tile,float u,float v)
        {
            float px=u*255f,py=v*255f;int x=Mathf.Clamp((int)px,0,255),y=Mathf.Clamp((int)py,0,255);
            int x1=Math.Min(x+1,255),y1=Math.Min(y+1,255);float fx=px-x,fy=py-y;
            float a=IconPixels[tile][y*256+x].a*(1f-fx)+IconPixels[tile][y*256+x1].a*fx;
            float b=IconPixels[tile][y1*256+x].a*(1f-fx)+IconPixels[tile][y1*256+x1].a*fx;
            return (byte)Mathf.Clamp((int)(a*(1f-fy)+b*fy+.5f),0,255);
        }
        static Color32[] ReadPixels(Texture source)
        {
            Texture2D image=source as Texture2D;
            if(image!=null){try{return image.GetPixels32();}catch(UnityException){}}
            // Read only this texture asset. No camera/framebuffer capture.
            RenderTexture previous=RenderTexture.active,target=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32);
            Texture2D readable=null;
            try
            {
                Graphics.Blit(source,target);RenderTexture.active=target;
                readable=new Texture2D(source.width,source.height,TextureFormat.ARGB32,false);
                readable.ReadPixels(new Rect(0,0,source.width,source.height),0,0);readable.Apply();return readable.GetPixels32();
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);if(readable!=null)UnityEngine.Object.DestroyImmediate(readable);}
        }
        static UISpriteData Copy(UISpriteData source)
        {
            return new UISpriteData{name=source.name,x=source.x,y=source.y,width=source.width,height=source.height,
                borderLeft=source.borderLeft,borderRight=source.borderRight,borderTop=source.borderTop,borderBottom=source.borderBottom,
                paddingLeft=source.paddingLeft,paddingRight=source.paddingRight,paddingTop=source.paddingTop,paddingBottom=source.paddingBottom};
        }
        // Match teardown is not HUD teardown. The native UnitInterface, its
        // serialized sprite references and pooled visualization atlas caches
        // survive OnSessionEnded. Their bounded private atlases must survive
        // too; destroying them here leaves the next match with missing panels.
        // Keep the existing caller contract, releasing only session identities.
        public static void Release()
        {
            OperatorPortraitIcons.EndSession();
            EndfieldHudTheme.EndSession();
            UpdateWarned=false;
        }
    }
}
