using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Model-derived private unit sprites, appended at native sprite dimensions.
    // Registration follows the completed eleven-unit/aircraft registry. There
    // is no scene scan, recurring tick, shared HUD mutation or atlas replacement.
    public static class RhodesUnitIcons
    {
        sealed class Entry
        {
            internal string Name,Reference,File;
            internal Texture2D Texture;
            internal Color32[] Pixels;
            internal int X,Y,Width,Height;
        }
        sealed class Binding
        {
            internal UnitHUDAttributes Hud,Donor;
            internal object Copy;
            internal AbilityViewAttributes Production;
            internal Entry ProductionIcon,InformationIcon;
            internal string DonorProduction,DonorInformation,DonorFleet;
        }
        static readonly string[] Keys={"RI_U01_HARVESTER","RI_U02_FIELD_SURVEY","RI_U03_ENGINEERING","RI_U04_LIGHT_ESCORT","RI_U05_SUPPRESSION","RI_U06_RAILGUN","RI_U07_AIR_DEFENCE","RI_U08_TILT_STRIKE","RI_U09_ARTILLERY","EF_Rhodes_Interceptor_MP","EF_Rhodes_HeavyBomber_MP"};
        static readonly string[] Donors={"C_Harvester_MP","C_Baserunner_MP","C_Baserunner_MP","C_Escort_MP","C_HAC_MP","C_Railgun_MP","C_HAC_Upgrade01_MP","C_Interceptor_MP","C_ArtilleryCruiser_MP","C_Interceptor_MP","C_Bomber_MP"};
        static readonly List<Entry> Entries=new List<Entry>();
        static readonly AtlasPositiveCache61 Cache=new AtlasPositiveCache61();
        static readonly List<UnityEngine.Object> Owned=new List<UnityEngine.Object>();
        static readonly MethodInfo Clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        static EntityTypeCollection ConfiguredRegistry;
        static GameObject Templates;
        static bool Warned;

        public static void ConfigureRegistered()
        {
            EntityTypeCollection registry=NativeRead.Get(typeof(ShipbreakersMain),"sEntityTypes") as EntityTypeCollection;
            if(registry==null)throw new InvalidOperationException("Unit icons require the completed native registry");
            List<Binding> bindings=new List<Binding>();
            for(int i=0;i<Keys.Length;i++)
            {
                EntityTypeAttributes own=registry.GetEntityType(Keys[i]),donor=registry.GetEntityType(Donors[i]);
                UnitHUDAttributes hud=own==null?null:own.Get<UnitHUDAttributes>(),native=donor==null?null:donor.Get<UnitHUDAttributes>();
                if(hud==null||native==null||object.ReferenceEquals(hud,native)||hud.IconSettings==null||native.IconSettings==null)
                    throw new InvalidOperationException("Private unit HUD missing at completed icon registration: "+Keys[i]);
                string production=i<9?FactionUnits.FindSpec(Keys[i]).ProductionKey:i==9?RhodesCarrierUnits.AircraftProductionKey:RhodesCarrierUnits.HeavyBomberProductionKey;
                EntityTypeAttributes ability=registry.GetEntityType(production);
                AbilityViewAttributes view=ability==null?null:ability.Get<AbilityViewAttributes>();
                if(view==null || (string)NativeRead.Get(view,"Name")!=production)
                    throw new InvalidOperationException("Private production icon view missing: "+production);
                Entry p=GetEntry(Keys[i],"Production",native.IconSettings.UnitProductionIconName);
                Entry n=GetEntry(Keys[i],"Info",native.IconSettings.UnitInfoPanelIconName);
                object settings=NativeRead.Get(hud,"m_IconSettings"),copy=Clone.Invoke(settings,null);
                NativeRead.Set(copy,"mUnitProductionIconName",p.Name);NativeRead.Set(copy,"m_UnitInfoPanelIconName",n.Name);NativeRead.Set(copy,"mUnitFleetPanelIconName",p.Name);
                bindings.Add(new Binding{Hud=hud,Donor=native,Copy=copy,Production=view,ProductionIcon=p,InformationIcon=n,
                    DonorProduction=native.IconSettings.UnitProductionIconName,DonorInformation=native.IconSettings.UnitInfoPanelIconName,DonorFleet=(string)NativeRead.Get(native.IconSettings,"mUnitFleetPanelIconName")});
            }
            // Validate the complete eleven-unit set before changing any HUD.
            foreach(Binding b in bindings)
            {
                NativeRead.Set(b.Hud,"m_IconSettings",b.Copy);NativeRead.Set(b.Production,"IconName",b.ProductionIcon.Name);
                if(b.Hud.IconSettings.UnitProductionIconName!=b.ProductionIcon.Name || b.Hud.IconSettings.UnitInfoPanelIconName!=b.InformationIcon.Name ||
                    (string)NativeRead.Get(b.Hud.IconSettings,"mUnitFleetPanelIconName")!=b.ProductionIcon.Name || (string)NativeRead.Get(b.Production,"IconName")!=b.ProductionIcon.Name ||
                    b.Donor.IconSettings.UnitProductionIconName!=b.DonorProduction || b.Donor.IconSettings.UnitInfoPanelIconName!=b.DonorInformation || (string)NativeRead.Get(b.Donor.IconSettings,"mUnitFleetPanelIconName")!=b.DonorFleet)
                    throw new InvalidOperationException("Unit icon binding changed a donor or failed its private target");
            }
            bool newRegistry=!object.ReferenceEquals(ConfiguredRegistry,registry);ConfiguredRegistry=registry;
            if(newRegistry)Runtime.Log("RHODES_UNIT_ICONS_REGISTERED units=11; privateHUDAndProductionViews=11; sprites=22; productionInfoFleetIcons=True; afterAllAircraftRegistered=True; donorHUDUnchanged=True; carrierFormsAndOperatorIconsUnchanged=True; nativeSpriteDimensions=True; runtimeAcceptancePending=True");
        }
        static Entry GetEntry(string key,string role,string reference)
        {
            if(string.IsNullOrEmpty(reference))throw new InvalidDataException("Native icon reference absent: "+key+"/"+role);
            string name="EF_UNIT_"+key+"_"+role;
            foreach(Entry e in Entries)if(e.Name==name){if(e.Reference!=reference)throw new InvalidDataException("Native icon reference changed across registries");return e;}
            if(Entries.Count>=22)throw new InvalidDataException("Unit icon catalog budget exceeded");
            Entry entry=new Entry{Name=name,Reference=reference,File=key+"_"+role+".png"};
            Texture2D image=new Texture2D(2,2,TextureFormat.RGBA32,false);image.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                if(!image.LoadImage(File.ReadAllBytes(Path.Combine(CustomUnits.AssetDirectory,"unit_identity/"+entry.File)))||image.width!=256||image.height!=256)
                    throw new InvalidDataException("Unit identity must be a256x256 RGBA PNG: "+entry.File);
                entry.Texture=image;entry.Pixels=image.GetPixels32();int minX=256,minY=256,maxX=-1,maxY=-1;
                for(int y=0;y<256;y++)for(int x=0;x<256;x++)if(entry.Pixels[y*256+x].a>=16){minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
                if(maxX<minX||maxY<minY)throw new InvalidDataException("Unit icon has no visible geometry");
                entry.X=Math.Max(0,minX-1);entry.Y=Math.Max(0,minY-1);entry.Width=Math.Min(255,maxX+1)-entry.X+1;entry.Height=Math.Min(255,maxY+1)-entry.Y+1;
                Owned.Add(image);Entries.Add(entry);return entry;
            }
            catch{UnityEngine.Object.DestroyImmediate(image);throw;}
        }
        static bool Needs(UIAtlas atlas)
        {foreach(Entry e in Entries)if(atlas.GetSprite(e.Name)==null&&atlas.GetSprite(e.Reference)!=null)return true;return false;}
        public static UIAtlas MapAtlas(UIAtlas source)
        {
            if(source==null||Entries.Count!=22)return source;
            UIAtlas cached;if(Cache.TryGetValue(source,Entries.Count,out cached)&&cached!=null)return cached;
            if(!Needs(source))return source;
            try
            {
                if(Cache.Count>=8&&!Cache.ContainsKey(source))throw new InvalidOperationException("Unit icon atlas cache budget exceeded");
                UIAtlas result=Build(source);Cache.Store(source,result,Entries.Count);return result;
            }
            catch(Exception error){if(!Warned){Warned=true;Runtime.Log("WARNING RHODES_UNIT_ICON_ATLAS "+error.GetType().Name+": "+error.Message+"; nativeAtlasUnchanged=True");}return source;}
        }
        static UIAtlas Build(UIAtlas source)
        {
            Texture native=source.texture;if(native==null||source.spriteMaterial==null)throw new InvalidDataException("Unit icon source atlas has no texture/material");
            List<Entry> entries=new List<Entry>();List<UISpriteData> extras=new List<UISpriteData>();int width=native.width,x=2,y=native.height+2,row=0;
            foreach(Entry e in Entries)
            {
                UISpriteData reference=source.GetSprite(e.Reference);if(reference==null||source.GetSprite(e.Name)!=null)continue;
                if(reference.width<1||reference.height<1||reference.width>1024||reference.height>1024)throw new InvalidDataException("Unexpected native unit sprite dimensions");
                width=Math.Max(width,reference.width+4);if(x+reference.width+2>width){x=2;y+=row+4;row=0;}
                extras.Add(new UISpriteData{name=e.Name,x=x,y=y,width=reference.width,height=reference.height,paddingLeft=reference.paddingLeft,paddingRight=reference.paddingRight,paddingTop=reference.paddingTop,paddingBottom=reference.paddingBottom});
                entries.Add(e);x+=reference.width+4;row=Math.Max(row,reference.height);
            }
            int height=y+row+2;if(width>SystemInfo.maxTextureSize||height>SystemInfo.maxTextureSize)throw new InvalidDataException("Unit icon atlas exceeds texture limit");
            Color32[] original=ReadPixels(native),pixels=new Color32[width*height];
            for(int r=0;r<native.height;r++)Array.Copy(original,r*native.width,pixels,(r+height-native.height)*width,native.width);
            for(int i=0;i<entries.Count;i++)Paint(pixels,width,height,extras[i],entries[i],source.premultipliedAlpha);
            for(int r=0;r<native.height;r++)for(int c=0;c<native.width;c++)
            {Color32 a=original[r*native.width+c],b=pixels[(r+height-native.height)*width+c];if(a.r!=b.r||a.g!=b.g||a.b!=b.b||a.a!=b.a)throw new InvalidDataException("Native unit atlas source region changed");}
            Texture2D texture=null;Material material=null;GameObject node=null;
            try
            {
                texture=new Texture2D(width,height,TextureFormat.RGBA32,false);texture.name="EF_UNIT_AtlasTexture";texture.hideFlags=HideFlags.HideAndDontSave;texture.SetPixels32(pixels);texture.Apply(false,false);texture.filterMode=native.filterMode;texture.wrapMode=TextureWrapMode.Clamp;
                material=new Material(source.spriteMaterial);material.name="EF_UNIT_AtlasMaterial";material.hideFlags=HideFlags.HideAndDontSave;material.mainTexture=texture;
                if(Templates==null){Templates=new GameObject("EF_UNIT_IconTemplates");Templates.SetActive(false);Templates.hideFlags=HideFlags.HideAndDontSave;UnityEngine.Object.DontDestroyOnLoad(Templates);}
                node=new GameObject("EF_UNIT_Atlas");node.hideFlags=HideFlags.HideAndDontSave;node.transform.parent=Templates.transform;UIAtlas result=node.AddComponent<UIAtlas>();result.spriteMaterial=material;result.pixelSize=source.pixelSize;
                List<UISpriteData> sprites=new List<UISpriteData>();foreach(UISpriteData s in source.spriteList)sprites.Add(new UISpriteData{name=s.name,x=s.x,y=s.y,width=s.width,height=s.height,borderLeft=s.borderLeft,borderRight=s.borderRight,borderTop=s.borderTop,borderBottom=s.borderBottom,paddingLeft=s.paddingLeft,paddingRight=s.paddingRight,paddingTop=s.paddingTop,paddingBottom=s.paddingBottom});
                sprites.AddRange(extras);result.spriteList=sprites;result.MarkSpriteListAsChanged();Owned.Add(texture);Owned.Add(material);
                Runtime.Log("RHODES_UNIT_ICON_ATLAS_READY added="+extras.Count+"; nativeSpriteDimensionsAndPadding=True; nativePixelsPreserved=True; cropFitWithoutWidgetResize=True; boundedUILifetimeCache=True; runtimeVisualAcceptancePending=True");return result;
            }
            catch{if(node!=null)UnityEngine.Object.DestroyImmediate(node);if(material!=null)UnityEngine.Object.DestroyImmediate(material);if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);throw;}
        }
        static void Paint(Color32[] pixels,int width,int height,UISpriteData sprite,Entry entry,bool pma)
        {
            float padding=Math.Min(2f,Math.Min(sprite.width,sprite.height)*.05f),scale=Math.Min((sprite.width-padding*2)/entry.Width,(sprite.height-padding*2)/entry.Height);
            float ox=(sprite.width-entry.Width*scale)*.5f,oy=(sprite.height-entry.Height*scale)*.5f;
            if(entry.Name.EndsWith("_Info",StringComparison.Ordinal))RhodesInfoIconLayout.Fit(sprite.width,sprite.height,entry.Width,entry.Height,out scale,out ox,out oy);
            for(int y=0;y<sprite.height;y++)for(int x=0;x<sprite.width;x++)
            {
                float sx=(x+.5f-ox)/scale,sy=(y+.5f-oy)/scale;Color32 pixel=new Color32(0,0,0,0);
                if(sx>=0&&sx<entry.Width&&sy>=0&&sy<entry.Height)
                {int ix=Math.Min(255,entry.X+(int)sx),iy=Math.Min(255,entry.Y+(int)sy);pixel=entry.Pixels[iy*256+ix];if(pma){pixel.r=(byte)(pixel.r*pixel.a/255);pixel.g=(byte)(pixel.g*pixel.a/255);pixel.b=(byte)(pixel.b*pixel.a/255);}}
                pixels[(height-sprite.y-sprite.height+y)*width+sprite.x+x]=pixel;
            }
        }
        static Color32[] ReadPixels(Texture texture)
        {
            Texture2D image=texture as Texture2D;if(image!=null)try{return image.GetPixels32();}catch(UnityException){}
            RenderTexture old=RenderTexture.active,temp=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32);Texture2D read=null;
            try{Graphics.Blit(texture,temp);RenderTexture.active=temp;read=new Texture2D(texture.width,texture.height,TextureFormat.ARGB32,false);read.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);read.Apply();return read.GetPixels32();}
            finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(temp);if(read!=null)UnityEngine.Object.DestroyImmediate(read);}
        }
    }
}
