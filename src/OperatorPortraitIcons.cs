using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Native RGBA heads at their original RGB brightness, shared consistently
    // across cargo, production, information and fleet; native atlas pixels stay intact.
    public static class OperatorPortraitIcons
    {
        public const string ProductionName="EF_LAEVAT_Portrait_Production",InfoName="EF_LAEVAT_Portrait_Info",FleetName="EF_LAEVAT_Portrait_Fleet",CargoName="EF_LAEVAT_Portrait_Cargo";
        static readonly string[] Fields={"mUnitProductionIconName","m_UnitInfoPanelIconName","mUnitFleetPanelIconName"};
        static string[] SpriteNames(OperatorUiIdentity identity)
        {return identity.PortraitSpriteNames;}
        sealed class PortraitEntry {internal OperatorUiIdentity Identity;internal string[] References;internal Texture2D Texture;internal Color32[] Pixels;}
        static readonly Dictionary<string,PortraitEntry> Entries=new Dictionary<string,PortraitEntry>(StringComparer.Ordinal);
        static readonly AtlasPositiveCache61 Mapped=new AtlasPositiveCache61();
        static readonly Dictionary<UIAtlas,UIAtlas> Cargo=new Dictionary<UIAtlas,UIAtlas>();
        static int RegistrationGeneration;
        static readonly Dictionary<UnitHUDAttributes,string> EnglishNames=new Dictionary<UnitHUDAttributes,string>();
        static readonly List<UnityEngine.Object> Owned=new List<UnityEngine.Object>();
        static readonly Dictionary<UISprite,bool> HiddenInfoBackgrounds=new Dictionary<UISprite,bool>();
        static GameObject Templates;
        static bool Warned;

        public static void Configure(object customHudData,object originalHudData)
        {Configure(customHudData,originalHudData,"EF_Laevatain_Test");}
        public static void Configure(object customHudData,object originalHudData,string typeKey)
        {
            UnitHUDAttributes custom=customHudData as UnitHUDAttributes,original=originalHudData as UnitHUDAttributes;
            if(custom==null||original==null||object.ReferenceEquals(custom,original))throw new InvalidOperationException("Operator portrait requires private HUD data");
            PortraitEntry entry=EnsurePortrait(OperatorUiIdentity.Require(typeKey));
            string[] references=ConfigureHudIcons(custom,original,entry.Identity);
            bool changed=entry.References==null;
            if(!changed)for(int i=0;i<references.Length;i++)if(references[i]!=entry.References[i]){changed=true;break;}
            entry.References=references;if(changed)RegistrationGeneration++;
            Runtime.Log("OPERATOR_PORTRAIT_CONFIGURED exactUnit="+typeKey+"; productionInformationAndFleet=True; nativeRGBAHeadCutout=True; originalSourceRGB=True; noDeadSymbolOrHatch=True; privateHUDOnly=True; nativeAtlasUnchanged=True");
        }
        // Pure managed HUD binding. Texture creation and atlas baking remain
        // separate, so every native consumer uses the same three-role names.
        internal static string[] ConfigureHudIcons(UnitHUDAttributes custom,UnitHUDAttributes original,OperatorUiIdentity identity)
        {
            if(custom==null||original==null||identity==null||object.ReferenceEquals(custom,original))throw new InvalidOperationException("Operator portrait requires private HUD data");
            object previous=NativeRead.Get(custom,"m_IconSettings"),native=NativeRead.Get(original,"m_IconSettings");
            object copy=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(previous,null);
            string[] references=new string[Fields.Length],names=SpriteNames(identity);
            for(int i=0;i<Fields.Length;i++)
            {
                string source=NativeRead.Get(native,Fields[i]) as string;
                if(string.IsNullOrEmpty(source))throw new InvalidOperationException("Operator donor portrait sprite reference absent");
                references[i]=source;NativeRead.Set(copy,Fields[i],names[i]);
            }
            NativeRead.Set(custom,"m_IconSettings",copy);ConfigureEnglishName(custom,identity.EnglishName);return references;
        }
        public static void ConfigureEnglishName(object customHudData,string englishName)
        {
            UnitHUDAttributes custom=customHudData as UnitHUDAttributes;
            if(custom==null||string.IsNullOrEmpty(englishName))throw new ArgumentException("Operator English identity requires private HUD and a name");
            EnglishNames[custom]=englishName;
        }
        public static string DisplayName(string typeKey)
        {OperatorUiIdentity identity;return OperatorUiIdentity.TryGet(typeKey,out identity)?identity.DisplayName:"干员";}
        public static string CargoSpriteName(string typeKey)
        {OperatorUiIdentity identity;return OperatorUiIdentity.TryGet(typeKey,out identity)?identity.CargoName:null;}
        static PortraitEntry EnsurePortrait(OperatorUiIdentity identity)
        {
            PortraitEntry entry;if(Entries.TryGetValue(identity.TypeKey,out entry))return entry;
            if(Entries.Count>=OperatorUiIdentity.Capacity)throw new InvalidOperationException("Operator portrait registry budget exceeded");
            Texture2D image=new Texture2D(2,2,TextureFormat.RGBA32,false);image.hideFlags=HideFlags.HideAndDontSave;
            if(!image.LoadImage(File.ReadAllBytes(Path.Combine(CustomUnits.AssetDirectory,"operator_identity/"+identity.PortraitFile)))||image.width!=256||image.height!=256)
            {UnityEngine.Object.DestroyImmediate(image);throw new InvalidDataException("Operator portrait must be256x256 PNG");}
            image.name=identity.Prefix+"_NativeRGBAPortrait";Owned.Add(image);EnsureTemplates();
            entry=new PortraitEntry{Identity=identity,Texture=image,Pixels=image.GetPixels32()};Entries.Add(identity.TypeKey,entry);return entry;
        }
        static void EnsureTemplates()
        {if(Templates==null){Templates=new GameObject("EF_OperatorPortraitTemplates");Templates.hideFlags=HideFlags.HideAndDontSave;Templates.SetActive(false);UnityEngine.Object.DontDestroyOnLoad(Templates);}}
        static bool Needs(UIAtlas source)
        {
            foreach(PortraitEntry entry in Entries.Values)if(entry.References!=null)
            {string[] names=SpriteNames(entry.Identity);for(int i=0;i<Fields.Length;i++)if(source.GetSprite(names[i])==null&&source.GetSprite(entry.References[i])!=null)return true;}
            return false;
        }
        public static UIAtlas MapAtlas(UIAtlas source)
        {
            if(source==null)return source;UIAtlas result;
            if(Mapped.TryGetValue(source,RegistrationGeneration,out result)&&result!=null)return result;
            if(!Needs(source))return source;
            try
            {
                if(!Mapped.ContainsKey(source)&&Mapped.Count>=8)throw new InvalidOperationException("Operator portrait atlas cache budget exceeded");
                List<string> references=new List<string>(),names=new List<string>();List<Color32[]> pixels=new List<Color32[]>();
                foreach(PortraitEntry entry in Entries.Values)if(entry.References!=null)
                {string[] target=SpriteNames(entry.Identity);for(int i=0;i<Fields.Length;i++)if(source.GetSprite(target[i])==null&&source.GetSprite(entry.References[i])!=null){references.Add(entry.References[i]);names.Add(target[i]);pixels.Add(entry.Pixels);}}
                result=AppendPatterns(source,references.ToArray(),names.ToArray(),pixels.ToArray());Mapped.Store(source,result,RegistrationGeneration);return result;
            }
            catch(Exception error){if(!Warned){Warned=true;Runtime.Log("WARNING OPERATOR_PORTRAIT_ATLAS "+error.GetType().Name+": "+error.Message);}return source;}
        }
        public static UIAtlas CargoAtlas(UIAtlas source)
        {
            if(source==null||source.spriteMaterial==null)throw new InvalidOperationException("Native cargo material absent");
            UIAtlas result;if(Cargo.TryGetValue(source,out result)&&result!=null)return result;
            if(Cargo.Count>=8||OperatorUiIdentity.All.Length>OperatorUiIdentity.Capacity)throw new InvalidOperationException("Operator cargo atlas budget exceeded");
            const int size=128;int width=size*OperatorUiIdentity.All.Length;Color32[] pixels=new Color32[width*size];List<UISpriteData> sprites=new List<UISpriteData>();
            for(int i=0;i<OperatorUiIdentity.All.Length;i++)
            {PortraitEntry entry=EnsurePortrait(OperatorUiIdentity.All[i]);UISpriteData sprite=new UISpriteData{name=entry.Identity.CargoName,x=i*size,y=0,width=size,height=size};PaintImage(pixels,width,size,sprite,source.premultipliedAlpha,entry.Pixels);sprites.Add(sprite);}
            result=Create(source,pixels,width,size,sprites,"Cargo");Cargo[source]=result;return result;
        }
        static void PaintImage(Color32[] output,int width,int height,UISpriteData sprite,bool pma,Color32[] image)
        {
            float scale=Math.Min(sprite.width,sprite.height)/256f,ox=(sprite.width-256f*scale)*.5f,oy=(sprite.height-256f*scale)*.5f;
            for(int y=0;y<sprite.height;y++)for(int x=0;x<sprite.width;x++)
            {
                float sx=(x+.5f-ox)/scale-.5f,sy=(y+.5f-oy)/scale-.5f;
                output[(height-sprite.y-sprite.height+y)*width+sprite.x+x]=Sample(sx,sy,pma,image);
            }
        }
        static Color32 Sample(float x,float y,bool pma,Color32[] image)
        {
            if(x<0f||x>255f||y<0f||y>255f)return new Color32(0,0,0,0);
            int x0=(int)x,y0=(int)y,x1=Math.Min(255,x0+1),y1=Math.Min(255,y0+1);float fx=x-x0,fy=y-y0;
            Color32 a=image[y0*256+x0],b=image[y0*256+x1],c=image[y1*256+x0],d=image[y1*256+x1];
            float wa=(1-fx)*(1-fy),wb=fx*(1-fy),wc=(1-fx)*fy,wd=fx*fy;
            float alpha=wa*a.a+wb*b.a+wc*c.a+wd*d.a;if(alpha<=0f)return new Color32(0,0,0,0);
            // Interpolate premultiplied colour to avoid dark transparent-edge
            // fringes, then emit the source atlas material's alpha convention.
            float divisor=pma?255f:alpha;
            return new Color32(Byte((wa*a.r*a.a+wb*b.r*b.a+wc*c.r*c.a+wd*d.r*d.a)/divisor),Byte((wa*a.g*a.a+wb*b.g*b.a+wc*c.g*c.a+wd*d.g*d.a)/divisor),Byte((wa*a.b*a.a+wb*b.b*b.a+wc*c.b*c.a+wd*d.b*d.a)/divisor),Byte(alpha));
        }
        static byte Byte(float value){return (byte)Mathf.Clamp(Mathf.RoundToInt(value),0,255);}
        internal static UIAtlas AppendPattern(UIAtlas source,string referenceName,string name,Color32[] pattern)
        {return AppendPatterns(source,new string[]{referenceName},new string[]{name},new Color32[][]{pattern});}
        internal static UIAtlas AppendPatterns(UIAtlas source,string[] references,string[] names,Color32[][] patterns)
        {
            EnsureTemplates();Texture texture=source.texture;
            if(texture==null||source.spriteMaterial==null||references.Length!=names.Length||names.Length!=patterns.Length)throw new InvalidDataException("Operator atlas input missing");
            int width=texture.width,height=texture.height,cursor=2,cursorY=height+2,rowHeight=0;List<UISpriteData> extras=new List<UISpriteData>();List<Color32[]> images=new List<Color32[]>();
            for(int i=0;i<names.Length;i++)
            {
                if(source.GetSprite(names[i])!=null)continue;UISpriteData reference=source.GetSprite(references[i]);
                if(reference==null||patterns[i]==null||patterns[i].Length!=256*256||reference.width<=0||reference.height<=0||reference.width>1024||reference.height>1024)throw new InvalidDataException("Operator atlas reference or256x256 pattern absent");
                width=Math.Max(width,reference.width+4);if(cursor+reference.width+2>width){cursor=2;cursorY+=rowHeight+4;rowHeight=0;}
                extras.Add(new UISpriteData{name=names[i],x=cursor,y=cursorY,width=reference.width,height=reference.height,paddingLeft=reference.paddingLeft,paddingRight=reference.paddingRight,paddingTop=reference.paddingTop,paddingBottom=reference.paddingBottom});images.Add(patterns[i]);
                cursor+=reference.width+4;rowHeight=Math.Max(rowHeight,reference.height);
            }
            if(extras.Count==0)return source;height=cursorY+rowHeight+2;
            if(width>SystemInfo.maxTextureSize||height>SystemInfo.maxTextureSize)throw new InvalidDataException("Operator atlas exceeds texture limit");
            Color32[] original=ReadPixels(texture),pixels=new Color32[width*height];
            for(int y=0;y<texture.height;y++)Array.Copy(original,y*texture.width,pixels,(y+height-texture.height)*width,texture.width);
            for(int i=0;i<extras.Count;i++)PaintImage(pixels,width,height,extras[i],source.premultipliedAlpha,images[i]);
            for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
            {Color32 a=original[y*texture.width+x],b=pixels[(y+height-texture.height)*width+x];if(a.r!=b.r||a.g!=b.g||a.b!=b.b||a.a!=b.a)throw new InvalidDataException("Native operator atlas pixels changed");}
            List<UISpriteData> sprites=new List<UISpriteData>();foreach(UISpriteData sprite in source.spriteList)sprites.Add(Copy(sprite));sprites.AddRange(extras);
            return Create(source,pixels,width,height,sprites,"RegisteredPatterns");
        }
        static UIAtlas Create(UIAtlas source,Color32[] pixels,int width,int height,List<UISpriteData> sprites,string suffix)
        {
            Texture2D texture=new Texture2D(width,height,TextureFormat.RGBA32,false);texture.name="EF_OperatorPortrait_"+suffix;texture.hideFlags=HideFlags.HideAndDontSave;
            texture.SetPixels32(pixels);texture.Apply(false,false);texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;
            Material material=new Material(source.spriteMaterial);material.name="EF_OperatorPortraitMaterial_"+suffix;material.hideFlags=HideFlags.HideAndDontSave;material.mainTexture=texture;
            GameObject node=new GameObject("EF_OperatorPortraitAtlas_"+suffix);node.hideFlags=HideFlags.HideAndDontSave;node.transform.parent=Templates.transform;
            UIAtlas atlas=node.AddComponent<UIAtlas>();atlas.spriteMaterial=material;atlas.pixelSize=source.pixelSize;atlas.spriteList=sprites;atlas.MarkSpriteListAsChanged();Owned.Add(material);Owned.Add(texture);return atlas;
        }
        static Color32[] ReadPixels(Texture texture)
        {
            Texture2D image=texture as Texture2D;if(image!=null)try{return image.GetPixels32();}catch(UnityException){}
            RenderTexture old=RenderTexture.active,temporary=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32);Texture2D read=null;
            try{Graphics.Blit(texture,temporary);RenderTexture.active=temporary;read=new Texture2D(texture.width,texture.height,TextureFormat.ARGB32,false);read.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);read.Apply();return read.GetPixels32();}
            finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(temporary);if(read!=null)UnityEngine.Object.DestroyImmediate(read);}
        }
        static UISpriteData Copy(UISpriteData s)
        {return new UISpriteData{name=s.name,x=s.x,y=s.y,width=s.width,height=s.height,borderLeft=s.borderLeft,borderRight=s.borderRight,borderTop=s.borderTop,borderBottom=s.borderBottom,paddingLeft=s.paddingLeft,paddingRight=s.paddingRight,paddingTop=s.paddingTop,paddingBottom=s.paddingBottom};}
        static void ShowInfoBackground(UISprite icon,bool show)
        {
            if(icon==null)return;
            bool original;
            if(!show)
            {
                if(!HiddenInfoBackgrounds.ContainsKey(icon))HiddenInfoBackgrounds.Add(icon,icon.enabled);
                icon.enabled=false;
            }
            else if(HiddenInfoBackgrounds.TryGetValue(icon,out original))
            {
                icon.enabled=original;
                HiddenInfoBackgrounds.Remove(icon);
            }
        }
        public static void InfoUpdated(object controller,Entity entity)
        {
            try
            {
                object selected;if(!NativeRead.TryGet(controller,"mSelectedEntity",out selected)||!(selected is Entity)||(Entity)selected!=entity)return;
                object info=NativeRead.Get(controller,"mSettings");
                UISprite background=NativeRead.Get(info,"UnitIcon") as UISprite;
                UnitView view;
                if(!UnitViewDiscovery.TryGet(entity,out view)||view==null||view.Settings==null||view.Settings.HUDAttributes==null)
                {ShowInfoBackground(background,true);return;}
                string description=null;
                var frame=ShipbreakersMain.CurrentSimFrame;
                UnitState state=frame==null?null:frame.FindObject<UnitState>(entity);
                if(state!=null&&state.EntityID.Equals(entity))
                    RhodesUnitIdentityPolicy.TryShortDescription(state.TypeID,view.Settings.HUDAttributes.UnitLocalizedShortDescriptionStringID,out description);
                // Retain registered future operator overrides, but known exact
                // unit identities no longer depend on a match-cleared HUD map.
                if(description!=null||EnglishNames.TryGetValue(view.Settings.HUDAttributes,out description))
                {
                    UILabel name=NativeRead.Get(info,"mUnitShortDescriptionValueLabel") as UILabel;
                    if(name!=null&&name.text!=description)name.text=description;
                }
                string wanted=view.Settings.HUDAttributes.IconSettings.UnitInfoPanelIconName;
                bool portrait=false;
                foreach(OperatorUiIdentity identity in OperatorUiIdentity.All)if(wanted==identity.InfoName){portrait=true;break;}
                // This one native sprite is the large vehicle schematic behind
                // the information labels. Small framed portraits live in their
                // own production/fleet/cargo widgets and keep their lifecycle.
                ShowInfoBackground(background,!portrait);
            }
            catch(Exception error){if(!Warned){Warned=true;Runtime.Log("WARNING OPERATOR_PORTRAIT_INFO "+error.GetType().Name+": "+error.Message);}}
        }
        public static void EndSession()
        {
            foreach(KeyValuePair<UISprite,bool> row in HiddenInfoBackgrounds)if(row.Key!=null)row.Key.enabled=row.Value;
            HiddenInfoBackgrounds.Clear();
            EnglishNames.Clear();OperatorSkillIcons.EndSession();
        }
        // Full HUD teardown only. Match retirement uses EndSession instead:
        // persistent native widgets can still hold these private atlases.
        public static void Release()
        {
            RhodesResearch.ReleaseResearchIconCache();
            EndSession();OperatorSkillIcons.Release();Mapped.Clear();Cargo.Clear();Entries.Clear();Warned=false;
            foreach(UnityEngine.Object item in Owned)if(item!=null)UnityEngine.Object.DestroyImmediate(item);Owned.Clear();
            if(Templates!=null)UnityEngine.Object.DestroyImmediate(Templates);Templates=null;
        }
    }
}
