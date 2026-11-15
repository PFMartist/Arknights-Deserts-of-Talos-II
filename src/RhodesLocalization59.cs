using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BBI.Core.Data;
using BBI.Unity.Game.Localize;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Register the literal display text found in our own completed definitions.
    // Unknown/native keys still run through the complete native lookup path.
    public static class RhodesLocalization59
    {
        const int MaximumEntries=4096,MaximumVisitedObjects=32768,MaximumDepth=32;
        const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        static volatile Dictionary<string,LocalizedEntry> Entries=new Dictionary<string,LocalizedEntry>(StringComparer.Ordinal);
        sealed class Identity:IEqualityComparer<object>
        {public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);}public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}}

        public static void CaptureRegistered(EntityTypeCollection registry)
        {
            try
            {
                int definitions;Dictionary<string,LocalizedEntry> next=BuildCatalog(registry,out definitions);
                // Publish once, after all fields have been copied/configured. A
                // warm GetEntry lookup does not allocate, scan definitions or lock.
                Entries=next;
                Runtime.Log("RHODES_LOCALIZATION59_READY definitions="+definitions+"; literalEntries="+next.Count+"; ownRegisteredFieldsOnly=True; nativeLookupAndWarningsPreserved=True; recurringScan=False");
            }
            catch(Exception error)
            {
                // Text decoration cannot abort successful unit registration.
                // Unknown text will continue to use the original lookup/warning.
                Entries=new Dictionary<string,LocalizedEntry>(StringComparer.Ordinal);
                Runtime.Log("RHODES_LOCALIZATION59_OPTIONAL_UNAVAILABLE nativeLookupRetained=True; error="+error.GetType().Name+": "+error.Message);
            }
        }
        internal static Dictionary<string,LocalizedEntry> BuildCatalog(EntityTypeCollection registry,out int definitions)
        {
            if(registry==null)throw new ArgumentNullException("registry");
            Dictionary<string,LocalizedEntry> next=new Dictionary<string,LocalizedEntry>(StringComparer.Ordinal);
            HashSet<object> seen=new HashSet<object>(new Identity());definitions=0;
            foreach(string key in registry.GetAllEntityTypeNames())
            {
                if(!OwnsDefinition(key))continue;
                EntityTypeAttributes definition=registry.GetEntityType(key);if(definition==null)continue;
                definitions++;
                foreach(object data in definition.GetAll<object>())Gather(data,next,seen,0);
            }
            return next;
        }

        public static LocalizedEntry Resolve(string key)
        {
            LocalizedEntry entry;Dictionary<string,LocalizedEntry> snapshot=Entries;
            return key!=null&&snapshot.TryGetValue(key,out entry)?entry:null;
        }

        internal static bool OwnsDefinition(string key)
        {
            return key!=null&&(key.StartsWith("RI_",StringComparison.Ordinal)||key.StartsWith("EF_",StringComparison.Ordinal)||
                key.StartsWith("Ability_RI_",StringComparison.Ordinal)||key.StartsWith("Ability_EF_",StringComparison.Ordinal)||
                key.StartsWith("Research_RI_",StringComparison.Ordinal)||key.StartsWith("Research_EF_",StringComparison.Ordinal));
        }
        internal static bool IsLiteralDisplayText(string text)
        {
            if(string.IsNullOrEmpty(text)||text.Length>8192)return false;
            for(int i=0;i<text.Length;i++)if(text[i]>='\u3400'&&text[i]<='\u9fff')return true;
            return false;
        }
        static bool IsDisplayField(string name)
        {return name.IndexOf("Localized",StringComparison.OrdinalIgnoreCase)>=0||name.EndsWith("LocID",StringComparison.OrdinalIgnoreCase)||name=="m_TechTreeName"||name=="m_TierName"||name=="m_FactionName";}
        static void Gather(object value,Dictionary<string,LocalizedEntry> output,HashSet<object> seen,int depth)
        {
            if(value==null)return;
            Type type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(Delegate).IsAssignableFrom(type))return;
            if(depth>MaximumDepth||seen.Count>=MaximumVisitedObjects)throw new InvalidOperationException("Owned localization graph exceeds finite registration budget");
            if(!type.IsValueType&&!seen.Add(value))return;
            Array array=value as Array;if(array!=null){foreach(object item in array)Gather(item,output,seen,depth+1);return;}
            ScriptableObject asset=value as ScriptableObject;
            if(!object.ReferenceEquals(asset,null))
            {
                // Referenced power/tech assets contain additional localized
                // level descriptions. Do not traverse their native Unity base.
                object data;
                if(NativeRead.TryGet(asset,"m_Attributes",out data))Gather(data,output,seen,depth+1);
                return;
            }
            if(typeof(UnityEngine.Object).IsAssignableFrom(type)||type.Namespace==null||!type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))return;
            for(Type current=type;current!=null&&current!=typeof(object);current=current.BaseType)
            foreach(FieldInfo field in current.GetFields(Fields))
            {
                if(field.IsStatic)continue;
                object data=field.GetValue(value);
                string text=data as string;
                if(field.FieldType==typeof(string)&&IsDisplayField(field.Name)&&IsLiteralDisplayText(text))
                {
                    if(!output.ContainsKey(text))
                    {
                        if(output.Count>=MaximumEntries)throw new InvalidOperationException("Owned localization entry budget exceeded");
                        // Same fields/identity as the original literal fallback,
                        // prepared once instead of warning and allocating per UI call.
                        output.Add(text,new LocalizedEntry(0,text,text));
                    }
                }
                else if(field.FieldType!=typeof(string))Gather(data,output,seen,depth+1);
            }
        }
    }
}
