using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Each entry owns a private ability view and original native skill motif.
    // Fixed catalog and atlas caches keep the additional work initialization-only.
    public static class OperatorSkillIcons
    {
        public const string SpriteName="EF_LAEVAT_ComboPattern";
        sealed class Entry {internal OperatorUiIdentity Identity;internal object View;internal string Reference;internal Texture2D Pattern;internal Color32[] Pixels;}
        static readonly Dictionary<string,Entry> Entries=new Dictionary<string,Entry>(StringComparer.Ordinal);
        static readonly Dictionary<UIAtlas,UIAtlas> Cache=new Dictionary<UIAtlas,UIAtlas>();
        static bool Warned;
        public static void Configure(object privateAbilityView)
        {Configure(privateAbilityView,"EF_Laevatain_Test");}
        public static void Configure(object privateAbilityView,string typeKey)
        {
            if(privateAbilityView==null)throw new ArgumentNullException("privateAbilityView");
            OperatorUiIdentity identity=OperatorUiIdentity.Require(typeKey);Entry entry;
            if(!Entries.TryGetValue(typeKey,out entry))
            {if(Entries.Count>=OperatorUiIdentity.Capacity)throw new InvalidOperationException("Operator skill registry budget exceeded");entry=new Entry{Identity=identity};Entries.Add(typeKey,entry);}
            string prior=NativeRead.Get(privateAbilityView,"IconName") as string;
            if(object.ReferenceEquals(entry.View,privateAbilityView)&&prior==identity.SkillName)return;
            if(prior==identity.SkillName){entry.View=privateAbilityView;if(entry.Reference!=null)return;throw new InvalidDataException("Operator skill reference lost between private generations");}
            if(string.IsNullOrEmpty(prior))throw new InvalidDataException("Operator native ability icon reference missing");
            EnsurePattern(entry);entry.Reference=prior;entry.View=privateAbilityView;NativeRead.Set(privateAbilityView,"IconName",identity.SkillName);
            Runtime.Log("OPERATOR_SKILL_ICON_CONFIGURED exactAbility="+identity.AbilityKey+"; originalSkillMotif=True; whitePatternTransparentBackground=True; sharedNativeAbilityUnchanged=True");
        }
        static void EnsurePattern(Entry entry)
        {
            if(entry.Pattern!=null)return;Texture2D image=new Texture2D(2,2,TextureFormat.RGBA32,false);image.hideFlags=HideFlags.HideAndDontSave;
            if(!image.LoadImage(File.ReadAllBytes(Path.Combine(CustomUnits.AssetDirectory,"operator_identity/"+entry.Identity.SkillFile)))||image.width!=256||image.height!=256)
            {UnityEngine.Object.DestroyImmediate(image);throw new InvalidDataException("Operator skill pattern must be256x256 PNG");}
            image.name=entry.Identity.Prefix+"_NativeSkillPattern";entry.Pattern=image;entry.Pixels=image.GetPixels32();
        }
        static bool Needs(UIAtlas source)
        {foreach(Entry entry in Entries.Values)if(entry.Reference!=null&&source.GetSprite(entry.Identity.SkillName)==null&&source.GetSprite(entry.Reference)!=null)return true;return false;}
        public static UIAtlas MapAtlas(UIAtlas source)
        {
            if(source==null||!Needs(source))return source;UIAtlas atlas;
            if(Cache.TryGetValue(source,out atlas)&&atlas!=null&&!Needs(atlas))return atlas;
            try
            {
                if(!Cache.ContainsKey(source)&&Cache.Count>=8)throw new InvalidOperationException("Operator skill atlas cache budget exceeded");
                List<string> references=new List<string>(),names=new List<string>();List<Color32[]> pixels=new List<Color32[]>();
                foreach(Entry entry in Entries.Values)if(entry.Reference!=null&&source.GetSprite(entry.Identity.SkillName)==null&&source.GetSprite(entry.Reference)!=null)
                {EnsurePattern(entry);references.Add(entry.Reference);names.Add(entry.Identity.SkillName);pixels.Add(entry.Pixels);}
                atlas=OperatorPortraitIcons.AppendPatterns(source,references.ToArray(),names.ToArray(),pixels.ToArray());Cache[source]=atlas;return atlas;
            }
            catch(Exception error){if(!Warned){Warned=true;Runtime.Log("WARNING OPERATOR_SKILL_ICON_ATLAS "+error.GetType().Name+": "+error.Message);}return source;}
        }
        public static void EndSession()
        {foreach(Entry entry in Entries.Values)entry.View=null;}
        public static void Release()
        {Cache.Clear();foreach(Entry entry in Entries.Values)if(entry.Pattern!=null)UnityEngine.Object.DestroyImmediate(entry.Pattern);Entries.Clear();Warned=false;}
    }
}
