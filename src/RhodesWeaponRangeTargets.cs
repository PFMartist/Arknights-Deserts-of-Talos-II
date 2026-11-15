using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Two PRIVATE clones for a manual range. No native unit is disarmed,
    // renamed, retuned or removed from the native registry.
    public static class RhodesWeaponRangeTargets
    {
        public const string ArmourKey="EF_Rhodes_SupportCruiserRangeTarget_MP";
        public const string AirKey="EF_Rhodes_AirRangeTarget_MP";
        public const string ArmourDonorKey="C_SupportCruiser_MP",AirDonorKey="C_Interceptor_MP";
        public const int ArmourHitpoints=120000,AirHitpoints=24000;
        static EntityTypeCollection Registry;
        static readonly List<Context> Contexts=new List<Context>();
        public static bool Ready {get;private set;}
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        static readonly MethodInfo CloneMethod=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        sealed class Context
        {
            public UnitAssetContainer Source,Custom;
            public ScriptableObject Core,Movement,View;
            public bool Air;
            public readonly Dictionary<object,object> Map=new Dictionary<object,object>(new IdentityComparer());
            public readonly List<ScriptableObject> Owned=new List<ScriptableObject>();
            public readonly List<SourceRecord> Sources=new List<SourceRecord>();
        }
        sealed class SourceRecord {public ScriptableObject Asset;public string Fingerprint;public EntityTypeAttributes Definition;}
        sealed class IdentityComparer:IEqualityComparer<object>
        {public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);}public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}}
        public static void Register()
        {
            EntityTypeCollection registry=(EntityTypeCollection)NativeRead.Get(typeof(ShipbreakersMain),"sEntityTypes");
            if(registry==null)throw new InvalidOperationException("Rhodes range requires the native registry");
            if(object.ReferenceEquals(Registry,registry)){if(!Ready)throw new InvalidOperationException("Rhodes range registration previously failed");VerifyRegistered();return;}
            Release();Registry=registry;Ready=false;
            Prepare(ArmourDonorKey,ArmourKey,false,ArmourHitpoints);
            Prepare(AirDonorKey,AirKey,true,AirHitpoints);
            VerifyRegistered();Ready=true;
        }
        static void Prepare(string donorName,string key,bool air,int hitpoints)
        {
            if(Registry.GetEntityType(key)!=null)throw new InvalidOperationException("Rhodes range type collision "+key);
            EntityTypeAttributes registered=Registry.GetEntityType(donorName);UnitAssetContainer source=null;
            if(registered==null)throw new InvalidOperationException("Rhodes range donor not registered "+donorName);
            foreach(UnitAssetContainer candidate in Resources.FindObjectsOfTypeAll(typeof(UnitAssetContainer)))
                if(candidate!=null&&candidate.name==donorName&&object.ReferenceEquals(Read(Read(candidate,"m_Attributes"),"m_Attributes"),registered.Get<UnitAttributes>())){source=candidate;break;}
            if(source==null)throw new InvalidOperationException("Rhodes range donor container absent "+donorName);
            Context c=new Context{Source=source,Air=air};Contexts.Add(c);
            c.Custom=(UnitAssetContainer)CloneAsset(c,source,key);
            foreach(FieldInfo field in Fields(source.GetType())){
                if(field.Name=="mDataCache")continue;ScriptableObject component=field.GetValue(source) as ScriptableObject;
                if(component!=null)CloneAsset(c,component,key+"_"+field.Name.TrimStart('m','_'));
            }
            c.Core=(ScriptableObject)c.Map[Read(source,"m_Attributes")];
            c.Movement=(ScriptableObject)c.Map[Read(source,"m_Movement")];c.View=(ScriptableObject)c.Map[Read(source,"m_View")];
            ScriptableObject nav=Read(c.Core,"m_NavMesh") as ScriptableObject;if(nav!=null)CloneAsset(c,nav,key+"_Nav");
            ScriptableObject experience=Read(source,"m_Experience") as ScriptableObject;
            if(experience!=null){ScriptableObject ev=Read(experience,"m_View") as ScriptableObject;if(ev!=null)CloneAsset(c,ev,key+"_ExperienceView");}
            foreach(ScriptableObject owned in c.Owned)RemapAsset(c,owned);
            ClearArray(c.Custom,"m_Weapons");ClearArray(c.Custom,"m_Abilities");
            ClearArray(c.Core,"m_WeaponLoadout");ClearArray(Read(c.Core,"m_Attributes"),"m_WeaponLoadout");
            Write(c.Custom,"m_Harvester",null);Write(c.Custom,"m_UnitHangar",null);
            object storage=Read(c.Custom,"m_Storage");
            if(storage!=null){ClearArray(storage,"m_InventoryLoadout");object sd=Read(storage,"m_Attributes");ClearArray(sd,"m_InventoryLoadout");Write(sd,"m_LinkToPlayerBank",false);Write(sd,"m_IsResourceController",false);}
            object core=Read(c.Core,"m_Attributes");
            // Retain donor class, armour and native presentation. Only native
            // maximum HP is enlarged; actual spawned HP/damage remain native.
            Write(core,"m_MaxHealth",hitpoints);Write(core,"m_RepairPickupRange",0);
            Write(core,"m_DamageReceivedMultiplier",1f);Write(core,"m_AccuracyReceivedMultiplier",1f);
            Write(core,"m_Selectable",true);Write(core,"m_Controllable",false);Write(core,"m_Targetable",true);
            Write(core,"m_NonAutoTargetable",false);Write(core,"m_StartsInHangar",false);Write(core,"m_WorldHeightOffset",0f);
            Write(core,"m_AggroRange",0);Write(core,"m_AlertRange",0);Write(core,"m_LeashRange",0);
            Write(core,"m_SensorRadius",0f);Write(core,"m_ContactRadius",0f);
            // Native air view physics retains its desired 250m altitude. Do not
            // also set WorldHeightOffset=250; that would double the elevation.
            Write(Read(Read(c.Movement,"m_Simulation"),"m_Dynamics"),"m_PermanentlyImmobile",true);
            UnitViewAttributes view=(UnitViewAttributes)Read(c.View,"Data");view.TurretDefinitions=new UnitViewAttributes.TurretAttributes[0];
            object hud=Read(Read(c.Custom,"m_HUD"),"m_Attributes");
            Write(hud,"mUnitLocalizedNameStringID",air?"无人空中靶标":"支援巡洋舰靶标");
            Write(hud,"mUnitLocalizedShortDescriptionStringID",hitpoints+"生命值；固定靶标，无攻击与维修能力");
            foreach(ScriptableObject owned in c.Owned)if(!object.ReferenceEquals(owned,c.Custom)&&!object.ReferenceEquals(owned,c.Core))CallIfPresent(owned,"OnEnable");
            Write(c.Custom,"mDataCache",null);CallRequired(c.Custom,"OnEnable");
            Verify(c);
            EntityTypeAttributes definition=new EntityTypeAttributes(key);
            foreach(object datum in (IEnumerable)c.Custom)if(datum!=null&&!definition.Add(datum))throw new InvalidOperationException("Duplicate Rhodes range datum "+key);
            Registry.Add(definition);
            Runtime.Log("RHODES_RANGE_TARGET_REGISTERED key="+key+"; nativeDonor="+donorName+"; privateClone=True; HP="+hitpoints+"; weapons=0; abilities=0; nativeAirPhysics="+air+"; stationarySim=True; airAltitudeStillRequiresRuntimeCheck="+air+"; nativeDamage=True; FOWNotBypassed=True; noHostileCarrier=True");
        }
        public static void VerifyRegistered()
        {
            if(Contexts.Count!=2)throw new InvalidOperationException("Rhodes range must register one support-cruiser target type and one air target type");
            int ground=0,air=0;
            foreach(Context c in Contexts){Verify(c);EntityTypeAttributes definition=Registry.GetEntityType(c.Custom.name);
                if(definition==null||!object.ReferenceEquals(definition.Get<UnitAttributes>(),Read(c.Core,"m_Attributes")))throw new InvalidOperationException("Rhodes range registry identity mismatch");
                if(c.Air)air++;else ground++;}
            if(ground!=1||air!=1)throw new InvalidOperationException("Rhodes range donor-role registration count mismatch");
        }
        static void Verify(Context c)
        {
            foreach(SourceRecord source in c.Sources)if(Fingerprint(source.Asset)!=source.Fingerprint||!object.ReferenceEquals(Registry.GetEntityType(source.Asset.name),source.Definition))throw new InvalidOperationException("Native donor changed for Rhodes range "+source.Asset.name);
            HashSet<object> sourceObjects=new HashSet<object>(new IdentityComparer()),seen=new HashSet<object>(new IdentityComparer());
            foreach(SourceRecord source in c.Sources)foreach(FieldInfo field in Fields(source.Asset.GetType()))if(field.Name!="mDataCache")Gather(field.GetValue(source.Asset),sourceObjects);
            foreach(ScriptableObject owned in c.Owned)foreach(FieldInfo field in Fields(owned.GetType()))if(field.Name!="mDataCache")Check(field.GetValue(owned),sourceObjects,seen,owned.name+"."+field.Name);
            UnitAttributes core=(UnitAttributes)Read(c.Core,"m_Attributes");
            UnitAttributes native=(UnitAttributes)Read(Read(c.Source,"m_Attributes"),"m_Attributes");
            if(c.Source.name!=(c.Air?AirDonorKey:ArmourDonorKey)||c.Custom.name!=(c.Air?AirKey:ArmourKey)||core.Class!=native.Class||core.Armour!=native.Armour)
                throw new InvalidOperationException("Rhodes range donor identity/class/native armour mismatch");
            if(core.MaxHealth!=(c.Air?AirHitpoints:ArmourHitpoints)||core.WeaponLoadout.Length!=0||core.NonAutoTargetable||core.Controllable||!core.Targetable)throw new InvalidOperationException("Rhodes range core flags/cache mismatch");
            if((c.Air&&(core.Class&UnitClass.Air)==0)||(!c.Air&&(core.Class&UnitClass.Ground)==0))throw new InvalidOperationException("Rhodes range donor target class mismatch");
            if(((Array)Read(c.Custom,"m_Weapons")).Length!=0||((Array)Read(c.Custom,"m_Abilities")).Length!=0)throw new InvalidOperationException("Rhodes range retained a weapon or ability");
            if(Read(c.Custom,"m_Harvester")!=null||Read(c.Custom,"m_UnitHangar")!=null||Convert.ToInt32(Read(core,"m_RepairPickupRange"))!=0)
                throw new InvalidOperationException("Rhodes range retained harvesting, hangar or repair-pickup capability");
            object storage=Read(c.Custom,"m_Storage");
            if(storage!=null){object sd=Read(storage,"m_Attributes");
                if(((Array)Read(storage,"m_InventoryLoadout")).Length!=0||((Array)Read(sd,"m_InventoryLoadout")).Length!=0||(bool)Read(sd,"m_LinkToPlayerBank")||(bool)Read(sd,"m_IsResourceController"))
                    throw new InvalidOperationException("Rhodes range retained resource or inventory capability");}
            if(((UnitViewAttributes)Read(c.View,"Data")).ModelPrefab!=((UnitViewAttributes)Read(Read(c.Source,"m_View"),"Data")).ModelPrefab)
                throw new InvalidOperationException("Rhodes range native read-only presentation changed");
            if(!((UnitMovementAttributes)Read(c.Movement,"m_Simulation")).Dynamics.PermanentlyImmobile)throw new InvalidOperationException("Rhodes range movement lock missing");
        }
        static void Release()
        {
            foreach(Context c in Contexts){foreach(UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))
                if(view!=null&&view.gameObject.activeInHierarchy&&object.ReferenceEquals(view.UnitAttrs,Read(c.Core,"m_Attributes")))throw new InvalidOperationException("Cannot release live Rhodes range target assets");
                for(int i=c.Owned.Count-1;i>=0;i--)if(c.Owned[i]!=null)UnityEngine.Object.DestroyImmediate(c.Owned[i]);}
            Contexts.Clear();Ready=false;
        }
        static ScriptableObject CloneAsset(Context c,ScriptableObject source,string name)
        {
            if(source==null)throw new InvalidOperationException("Rhodes source asset absent: "+name);object prior;if(c.Map.TryGetValue(source,out prior))return (ScriptableObject)prior;
            c.Sources.Add(new SourceRecord{Asset=source,Fingerprint=Fingerprint(source),Definition=Registry.GetEntityType(source.name)});
            ScriptableObject clone=RhodesAssetClone59.CreateEmpty(source.GetType());c.Map.Add(source,clone);
            Dictionary<object,object> copied=new Dictionary<object,object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(source.GetType()))if(field.Name!="mDataCache")field.SetValue(clone,CopyData(field.GetValue(source),copied));
            clone.name=name;clone.hideFlags=HideFlags.HideAndDontSave;c.Owned.Add(clone);return clone;
        }
        static object CopyData(object value,Dictionary<object,object> seen)
        {
            if(value==null)return null;Type type=value.GetType();if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type))return value;
            object previous;if(seen.TryGetValue(value,out previous))return previous;Array array=value as Array;
            if(array!=null){if(array.Rank!=1)throw new InvalidOperationException("Unexpected multidimensional carrier data");Array clone=(Array)array.Clone();seen.Add(value,clone);for(int index=0;index<array.Length;index++)clone.SetValue(CopyData(array.GetValue(index),seen),index);return clone;}
            if(type.Namespace==null||!type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))return value;
            object result=CloneMethod.Invoke(value,null);seen.Add(value,result);foreach(FieldInfo field in Fields(type))field.SetValue(result,CopyData(field.GetValue(value),seen));return result;
        }
        static void RemapAsset(Context c,ScriptableObject asset)
        {HashSet<object> seen=new HashSet<object>(new IdentityComparer());foreach(FieldInfo field in Fields(asset.GetType()))if(field.Name!="mDataCache")field.SetValue(asset,Remap(c,field.GetValue(asset),seen));}
        static object Remap(Context c,object value,HashSet<object> seen)
        {
            if(value==null)return null;object mapped;if(c.Map.TryGetValue(value,out mapped))return mapped;
            Type type=value.GetType();if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type)||!seen.Add(value))return value;
            Array array=value as Array;if(array!=null){for(int index=0;index<array.Length;index++)array.SetValue(Remap(c,array.GetValue(index),seen),index);return array;}
            if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))foreach(FieldInfo field in Fields(type))field.SetValue(value,Remap(c,field.GetValue(value),seen));return value;
        }
        static bool Mutable(object value)
        {if(value==null)return false;Type type=value.GetType();return !type.IsValueType&&type!=typeof(string)&&!typeof(UnityEngine.Object).IsAssignableFrom(type)&&(value is Array||(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal)));}
        static void Gather(object value,HashSet<object> result)
        {if(!Mutable(value)||!result.Add(value))return;Array array=value as Array;if(array!=null){foreach(object item in array)Gather(item,result);return;}foreach(FieldInfo field in Fields(value.GetType()))Gather(field.GetValue(value),result);}
        static void Check(object value,HashSet<object> native,HashSet<object> seen,string path)
        {if(!Mutable(value)||!seen.Add(value))return;if(native.Contains(value))throw new InvalidOperationException("Rhodes mutable data aliases native source: "+value.GetType().FullName+"; ownedPath="+path);Array array=value as Array;if(array!=null){for(int index=0;index<array.Length;index++)Check(array.GetValue(index),native,seen,path+"["+index+"]");return;}foreach(FieldInfo field in Fields(value.GetType()))Check(field.GetValue(value),native,seen,path+"."+field.Name);}
        static string Fingerprint(ScriptableObject asset)
        {StringBuilder text=new StringBuilder();HashSet<object> seen=new HashSet<object>(new IdentityComparer());foreach(FieldInfo field in Fields(asset.GetType())){text.Append(field.DeclaringType.FullName).Append('.').Append(field.Name).Append('=');AppendValue(text,field.GetValue(asset),seen);}using(SHA256 hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","");}
        static void AppendValue(StringBuilder text,object value,HashSet<object> seen)
        {
            if(value==null){text.Append("null;");return;}Type type=value.GetType();text.Append(type.FullName).Append(':');UnityEngine.Object unity=value as UnityEngine.Object;
            if(!object.ReferenceEquals(unity,null)){text.Append(unity.GetInstanceID()).Append('/').Append(unity.name).Append(';');return;}
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)){text.Append(Convert.ToString(value,CultureInfo.InvariantCulture)).Append(';');return;}
            if(type.IsValueType){foreach(FieldInfo field in Fields(type)){text.Append(field.Name).Append('=');AppendValue(text,field.GetValue(value),seen);}return;}
            if(!seen.Add(value)){text.Append("seen;");return;}Array array=value as Array;if(array!=null){text.Append(array.Length).Append('[');foreach(object item in array)AppendValue(text,item,seen);text.Append(']');return;}
            if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))foreach(FieldInfo field in Fields(type)){text.Append(field.Name).Append('=');AppendValue(text,field.GetValue(value),seen);}else text.Append(RuntimeHelpers.GetHashCode(value));
        }
        static List<FieldInfo> Fields(Type type)
        {List<FieldInfo> result=new List<FieldInfo>();for(Type current=type;current!=null&&current!=typeof(object)&&current!=typeof(UnityEngine.Object)&&current!=typeof(ScriptableObject);current=current.BaseType)foreach(FieldInfo field in current.GetFields(Flags))if(!field.IsStatic)result.Add(field);result.Sort(delegate(FieldInfo a,FieldInfo b){return string.CompareOrdinal(a.DeclaringType.FullName+"."+a.Name,b.DeclaringType.FullName+"."+b.Name);});return result;}
        static object Read(object value,string name)
        {if(value==null)throw new ArgumentNullException(name);for(Type type=value.GetType();type!=null;type=type.BaseType){FieldInfo field=type.GetField(name,Flags);if(field!=null)return field.GetValue(value);PropertyInfo property=type.GetProperty(name,Flags);if(property!=null)return property.GetValue(value,null);}throw new MissingMemberException(value.GetType().FullName,name);}
        static void Write(object value,string name,object data)
        {for(Type type=value.GetType();type!=null;type=type.BaseType){FieldInfo field=type.GetField(name,Flags);if(field!=null){field.SetValue(value,data);return;}}throw new MissingFieldException(value.GetType().FullName,name);}
        static void ClearArray(object value,string name)
        {foreach(FieldInfo field in Fields(value.GetType()))if(field.Name==name){if(!field.FieldType.IsArray)throw new InvalidDataException("Expected carrier array: "+name);field.SetValue(value,Array.CreateInstance(field.FieldType.GetElementType(),0));return;}throw new MissingFieldException(value.GetType().FullName,name);}
        static bool CallIfPresent(object value,string name)
        {for(Type type=value.GetType();type!=null;type=type.BaseType){MethodInfo method=type.GetMethod(name,Flags,null,Type.EmptyTypes,null);if(method!=null){method.Invoke(value,null);return true;}}return false;}
        static void CallRequired(object value,string name){if(!CallIfPresent(value,name))throw new MissingMethodException(value.GetType().FullName,name);}
    }
}
