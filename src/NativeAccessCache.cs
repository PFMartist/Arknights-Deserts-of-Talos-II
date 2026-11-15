using System;
using System.Collections.Generic;
using System.Reflection;

namespace EndfieldDoK.MapUnitLab.ModCore
{
    // Cache immutable reflection metadata only. Current values, instances,
    // entity references and lookup results never enter these tables.
    // Immutable copy-on-write dictionary snapshots make warm reads lock-free.
    // Locks protect cold publication, not user/game getter or method execution.
    internal static class NativeAccessCache
    {
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
        const int MaximumNamesPerType=256;
        static readonly object Gate=new object();
        static volatile Dictionary<Type,TypeEntries> Types=new Dictionary<Type,TypeEntries>();

        sealed class TypeEntries
        {
            public volatile Dictionary<string,MemberEntry> Reads=new Dictionary<string,MemberEntry>(StringComparer.Ordinal);
            public volatile Dictionary<string,MemberEntry> Writes=new Dictionary<string,MemberEntry>(StringComparer.Ordinal);
            public volatile Dictionary<string,MethodEntry[]> Calls=new Dictionary<string,MethodEntry[]>(StringComparer.Ordinal);
        }
        sealed class MemberEntry
        {
            public FieldInfo Field;
            public PropertyInfo Property;
            public bool Missing { get { return Field==null&&Property==null; } }
        }
        sealed class MethodEntry
        {
            public MethodInfo Method;
            public Type[] Parameters;
        }

        static TypeEntries Entries(Type type)
        {
            TypeEntries entries;
            if(Types.TryGetValue(type,out entries))return entries;
            lock(Gate){
                if(Types.TryGetValue(type,out entries))return entries;
                entries=new TypeEntries();
                Dictionary<Type,TypeEntries> next=new Dictionary<Type,TypeEntries>(Types);
                next.Add(type,entries);Types=next;
                return entries;
            }
        }
        static MemberEntry FindMember(Type type,string name,bool writing)
        {
            // Match the old helper's per-level field-before-property ordering.
            // A read-only property is skipped only for writes, allowing a base
            // field/property with the same name to be used exactly as before.
            for(Type current=type;current!=null;current=current.BaseType){
                FieldInfo field=current.GetField(name,Flags);
                if(field!=null)return new MemberEntry{Field=field};
                PropertyInfo property=current.GetProperty(name,Flags);
                if(property!=null&&(!writing||property.CanWrite))return new MemberEntry{Property=property};
            }
            return new MemberEntry();
        }
        static MemberEntry Member(Type type,string name,bool writing)
        {
            // Reflection itself supplies the legacy exception for null names.
            if(name==null)return FindMember(type,name,writing);
            TypeEntries entries=Entries(type);
            Dictionary<string,MemberEntry> cache=writing?entries.Writes:entries.Reads;
            MemberEntry result;
            if(cache.TryGetValue(name,out result))return result;
            lock(Gate){
                cache=writing?entries.Writes:entries.Reads;
                if(cache.TryGetValue(name,out result))return result;
                result=FindMember(type,name,writing);
                // Caller-controlled miss names cannot grow a type cache forever.
                // Fixed game metadata normally remains far below this limit.
                if(cache.Count<MaximumNamesPerType){
                    Dictionary<string,MemberEntry> next=new Dictionary<string,MemberEntry>(cache,StringComparer.Ordinal);
                    next.Add(name,result);
                    if(writing)entries.Writes=next;else entries.Reads=next;
                }
                return result;
            }
        }
        static MethodEntry[] FindMethods(Type type,string name)
        {
            List<MethodEntry> methods=new List<MethodEntry>();
            for(Type current=type;current!=null;current=current.BaseType){
                foreach(MethodInfo method in current.GetMethods(Flags)){
                    if(method.Name!=name||method.IsGenericMethodDefinition)continue;
                    ParameterInfo[] parameters=method.GetParameters();
                    Type[] types=new Type[parameters.Length];
                    for(int index=0;index<types.Length;index++)types[index]=parameters[index].ParameterType;
                    methods.Add(new MethodEntry{Method=method,Parameters=types});
                }
            }
            return methods.ToArray();
        }
        static MethodEntry[] Methods(Type type,string name)
        {
            // No CLR method has an empty/null name. Normalizing this key does
            // not alter the legacy MissingMethodException text at the caller.
            string key=name??String.Empty;
            TypeEntries entries=Entries(type);
            Dictionary<string,MethodEntry[]> cache=entries.Calls;MethodEntry[] result;
            if(cache.TryGetValue(key,out result))return result;
            lock(Gate){
                cache=entries.Calls;
                if(cache.TryGetValue(key,out result))return result;
                result=FindMethods(type,name);
                if(cache.Count<MaximumNamesPerType){
                    Dictionary<string,MethodEntry[]> next=new Dictionary<string,MethodEntry[]>(cache,StringComparer.Ordinal);
                    next.Add(key,result);entries.Calls=next;
                }
                return result;
            }
        }
        static object Read(MemberEntry member,object instance)
        {return member.Field!=null?member.Field.GetValue(instance):member.Property.GetValue(instance,null);}

        public static object Get(object value,string name)
        {
            if(value==null)throw new ArgumentNullException("value",name);
            Type type=value as Type??value.GetType();object instance=value is Type?null:value;
            MemberEntry member=Member(type,name,false);
            if(member.Missing)throw new MissingMemberException(type.FullName,name);
            return Read(member,instance);
        }
        public static bool TryGet(object value,string name,out object result)
        {
            // Optional absent members are cached without constructing an
            // exception. Exceptions from a real property getter retain the
            // same reflection wrapping and are not mistaken for absence.
            if(value==null)throw new ArgumentNullException("value",name);
            Type type=value as Type??value.GetType();object instance=value is Type?null:value;
            MemberEntry member=Member(type,name,false);
            if(member.Missing){result=null;return false;}
            try{result=Read(member,instance);return true;}
            catch(MissingMemberException){result=null;return false;}
        }
        public static void Set(object value,string name,object result)
        {
            // Deliberately preserve the old Set(null,...) NullReferenceException.
            Type type=value as Type??value.GetType();object instance=value is Type?null:value;
            MemberEntry member=Member(type,name,true);
            if(member.Missing)throw new MissingMemberException(type.FullName,name);
            if(member.Field!=null)member.Field.SetValue(instance,result);
            else member.Property.SetValue(instance,result,null);
        }
        public static object Call(object value,string name,params object[] args)
        {
            if(value==null)throw new ArgumentNullException("value",name);
            Type type=value as Type??value.GetType();object instance=value is Type?null:value;
            int count=args.Length; // Preserve the legacy failure for a null args array.
            foreach(MethodEntry entry in Methods(type,name)){
                if(entry.Parameters.Length!=count)continue;
                bool match=true;
                for(int index=0;index<count;index++)
                    if(args[index]!=null&&!entry.Parameters[index].IsInstanceOfType(args[index]))match=false;
                if(match)return entry.Method.Invoke(instance,args);
            }
            throw new MissingMethodException(type.FullName,name+" ("+count+" arguments)");
        }
    }
}
