// Independent stationary humanoid range target. Registration only; no spawn,
// move, fire, health regeneration, respawn or global-native-data modification.
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
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static class HumanoidRangeTarget
    {
        public const string UnitKey = "EF_Humanoid_RangeTarget";
        public const int TargetHitpoints = 50000;
        public static bool Ready { get; private set; }
        public static string GetSourceKey() { return Current == null ? null : Current.Source.name; }
        static EntityTypeCollection Registry;
        static Context Current;
        static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        sealed class Context
        {
            public UnitAssetContainer Source, Custom;
            public ScriptableObject Core, Movement, View;
            public GameObject OriginalModel;
            public readonly Dictionary<object, object> AssetMap = new Dictionary<object, object>(new IdentityComparer());
            public readonly List<ScriptableObject> Owned = new List<ScriptableObject>();
            public readonly List<SourceRecord> Sources = new List<SourceRecord>();
        }
        sealed class SourceRecord
        {
            public ScriptableObject Asset;
            public string Fingerprint;
            public EntityTypeAttributes Definition;
        }
        sealed class IdentityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return object.ReferenceEquals(a, b); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }

        public static void Register()
        {
            EntityTypeCollection current = (EntityTypeCollection)NativeRead.Get(typeof(ShipbreakersMain), "sEntityTypes");
            if (current == null) throw new InvalidOperationException("Range target needs native registry");
            if (object.ReferenceEquals(current, Registry))
            {
                if (!Ready) throw new InvalidOperationException("Range target registration failed before; reload required");
                VerifyRegistered(); return;
            }
            ReleaseStale(); Registry = current; Ready = false;
            UnitAssetContainer source = null;
            foreach (string key in new string[] { "G_Harvester_MP", "C_Harvester_MP" })
            {
                EntityTypeAttributes native = current.GetEntityType(key);
                UnitAttributes attributes = native == null ? null : native.Get<UnitAttributes>();
                if (attributes == null) continue;
                foreach (UnitAssetContainer candidate in Resources.FindObjectsOfTypeAll(typeof(UnitAssetContainer)))
                    if (candidate != null && candidate.name == key &&
                        object.ReferenceEquals(Read(Read(candidate, "m_Attributes"), "m_Attributes"), attributes))
                    { if (((UnitViewAttributes)Read(Read(candidate, "m_View"), "Data")).ModelPrefab != null) { source = candidate; break; } }
                if (source != null) break;
            }
            if (source == null) throw new InvalidOperationException("Range target native mining donor is not loaded");
            Context c = new Context(); Current = c; c.Source = source;
            c.Custom = (UnitAssetContainer)CloneAsset(c, source, UnitKey);
            foreach (FieldInfo field in Fields(source.GetType()))
            {
                if (field.Name == "mDataCache" || field.Name == "m_Abilities" || field.Name == "m_Weapons") continue;
                object value = field.GetValue(source);
                ScriptableObject component = value as ScriptableObject;
                if (component != null) CloneAsset(c, component, UnitKey + "_" + field.Name.TrimStart('m', '_'));
                Array array = value as Array;
                if (array != null && array.Rank == 1) foreach (object item in array)
                {
                    ScriptableObject extra = item as ScriptableObject;
                    if (extra != null) CloneAsset(c, extra, UnitKey + "_Extra_" + c.Owned.Count);
                }
            }
            c.Core = (ScriptableObject)c.AssetMap[Read(source, "m_Attributes")];
            c.Movement = (ScriptableObject)c.AssetMap[Read(source, "m_Movement")];
            c.View = (ScriptableObject)c.AssetMap[Read(source, "m_View")];
            ScriptableObject sourceNav = Read(c.Core, "m_NavMesh") as ScriptableObject;
            if (sourceNav == null) throw new InvalidOperationException("Range target navigation donor absent");
            CloneAsset(c, sourceNav, UnitKey + "_Nav");
            ScriptableObject experience = Read(source, "m_Experience") as ScriptableObject;
            ScriptableObject experienceView = experience == null ? null : Read(experience, "m_View") as ScriptableObject;
            if (experienceView != null) CloneAsset(c, experienceView, UnitKey + "_ExperienceView");
            ClearArray(c.Custom, "m_Weapons"); ClearArray(c.Custom, "m_Abilities");
            ClearArray(c.Core, "m_WeaponLoadout"); ClearArray(Read(c.Core, "m_Attributes"), "m_WeaponLoadout");
            foreach (ScriptableObject owned in c.Owned) RemapAsset(c, owned);
            // A stationary unarmed target neither mines nor carries resources.
            // Container.OnEnable rebuilds Storage from asset bindings; clear
            // both layers so it cannot reintroduce donor inventory datums.
            PrepareEmptyStorage(Read(c.Custom, "m_Storage"));
            Write(c.Custom, "m_Harvester", null);
            object core = Read(c.Core, "m_Attributes");
            Write(core, "m_MaxHealth", TargetHitpoints); Write(core, "m_Armour", 0);
            Write(core, "m_DamageReceivedMultiplier", 1f); Write(core, "m_AccuracyReceivedMultiplier", 1f);
            Write(core, "m_Selectable", true); Write(core, "m_Controllable", false);
            Write(core, "m_Targetable", true); Write(core, "m_NonAutoTargetable", true);
            Write(core, "m_AggroRange", 0); Write(core, "m_LeashRange", 0); Write(core, "m_AlertRange", 0);
            Write(core, "m_SensorRadius", 0f); Write(core, "m_ContactRadius", 0f);
            Write(core, "m_StartsInHangar", false);
            Write(Read(Read(c.Movement, "m_Simulation"), "m_Dynamics"), "m_PermanentlyImmobile", true);
            UnitViewAttributes view = (UnitViewAttributes)Read(c.View, "Data");
            c.OriginalModel = ((UnitViewAttributes)Read(Read(source, "m_View"), "Data")).ModelPrefab;
            ClearArray(view, "TurretDefinitions");
            object hud = Read(Read(c.Custom, "m_HUD"), "m_Attributes");
            Write(hud, "mUnitLocalizedNameStringID", "高耐久靶标");
            Write(hud, "mUnitLocalizedShortDescriptionStringID", "高生命值、无装甲的固定靶标");
            Write(hud, "mUnitLocalizedLongDescriptionStringID", "用于观察攻击动作与伤害效果。右键下达攻击命令；靶标保持静止且不会反击。");
            foreach (ScriptableObject owned in c.Owned)
                if (!object.ReferenceEquals(owned, c.Custom) && !object.ReferenceEquals(owned, c.Core)) CallIfPresent(owned, "OnEnable");
            Write(c.Custom, "mDataCache", null); CallRequired(c.Custom, "OnEnable");
            VerifyPrepared();
            if (Registry.GetEntityType(UnitKey) != null) throw new InvalidOperationException("Range target type key collision");
            EntityTypeAttributes prepared = BuildDefinition(c.Custom);
            foreach (AbilityAttributes ability in prepared.GetAll<AbilityAttributes>())
                throw new InvalidOperationException("Range target retained an unexpected ability: " + ability.Name);
            Registry.Add(prepared); VerifyRegistered(); Ready = true;
            Runtime.Log("HUMANOID_RANGE_TARGET_REGISTERED key=" + UnitKey + "; donor=" + source.name +
                "; HP=50000; armour=0; targetable=True; nonAutoTargetable=True; controllable=False; permanentlyImmobile=True" +
                "; weapons=0; abilities=0; inventoryCount=0; resourceController=False; playerBankLink=False; harvestingComponent=False; globalDonorUnchanged=True; nativeModelReadOnlyShared=True; gameplayAcceptancePending=True");
        }

        public static void VerifyRegistered()
        {
            VerifyPrepared(); Context c = Current;
            EntityTypeAttributes definition = Registry.GetEntityType(UnitKey);
            if (definition == null || !object.ReferenceEquals(definition.Get<UnitAttributes>(), Read(c.Core, "m_Attributes")) ||
                !object.ReferenceEquals(definition.Get<UnitMovementAttributes>(), Read(c.Movement, "m_Simulation")))
                throw new InvalidOperationException("Range target registry does not use owned data");
        }
        static void VerifyPrepared()
        {
            Context c = Current; if (c == null || Registry == null) throw new InvalidOperationException("Range target not prepared");
            List<ScriptableObject> sources = new List<ScriptableObject>();
            foreach (SourceRecord source in c.Sources)
            {
                if (Fingerprint(source.Asset) != source.Fingerprint || !object.ReferenceEquals(Registry.GetEntityType(source.Asset.name), source.Definition))
                    throw new InvalidOperationException("Range target changed native source " + source.Asset.name);
                sources.Add(source.Asset);
            }
            VerifyEmptyStorage(Read(c.Custom, "m_Storage"));
            if (Read(c.Custom, "m_Harvester") != null) throw new InvalidOperationException("Range target must not retain mining behavior without resource storage");
            AssertNoSharedMutable(sources, c.Owned);
            UnitAttributes core = (UnitAttributes)Read(c.Core, "m_Attributes");
            UnitMovementAttributes movement = (UnitMovementAttributes)Read(c.Movement, "m_Simulation");
            if (core.MaxHealth != TargetHitpoints || core.Armour != 0 || !core.Targetable || !core.NonAutoTargetable || core.Controllable ||
                core.WeaponLoadout == null || core.WeaponLoadout.Length != 0 || !movement.Dynamics.PermanentlyImmobile || movement.Dynamics.MaxSpeed <= Fixed64.Zero)
                throw new InvalidOperationException("Range target health/flags/movement/loadout verification failed");
            if (((Array)Read(c.Custom, "m_Abilities")).Length != 0 || ((Array)Read(c.Custom, "m_Weapons")).Length != 0)
                throw new InvalidOperationException("Range target inherited combat capabilities");
            UnitViewAttributes view = (UnitViewAttributes)Read(c.View, "Data");
            if (c.OriginalModel == null || view.ModelPrefab != c.OriginalModel || ((Array)Read(view, "TurretDefinitions")).Length != 0)
                throw new InvalidOperationException("Range target native read-only model or empty turret definitions changed");
        }
        static void ReleaseStale()
        {
            Context c = Current; if (c == null) return;
            foreach (UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))
                if (view != null && view.gameObject.activeInHierarchy && c.Core != null && object.ReferenceEquals(view.UnitAttrs, Read(c.Core, "m_Attributes")))
                    throw new InvalidOperationException("Range target assets still used by active view");
            for (int i = c.Owned.Count - 1; i >= 0; i--) if (c.Owned[i] != null) UnityEngine.Object.DestroyImmediate(c.Owned[i]);
            Current = null;
        }
        static void PrepareEmptyStorage(object storage)
        {
            if (storage == null) return;
            object data = Read(storage, "m_Attributes");
            ClearArray(storage, "m_InventoryLoadout"); ClearArray(data, "m_InventoryLoadout");
            Write(data, "m_LinkToPlayerBank", false); Write(data, "m_IsResourceController", false);
        }
        static void VerifyEmptyStorage(object storage)
        {
            if (storage == null) return;
            object data = Read(storage, "m_Attributes");
            Array assets = Read(storage, "m_InventoryLoadout") as Array, runtime = Read(data, "m_InventoryLoadout") as Array;
            if (assets == null || assets.Length != 0 || runtime == null || runtime.Length != 0 ||
                (bool)Read(data, "m_LinkToPlayerBank") || (bool)Read(data, "m_IsResourceController"))
                throw new InvalidOperationException("Range target inherited donor resource storage after native cache rebuild");
        }
        static void ClearArray(object target, string name)
        {
            foreach (FieldInfo field in Fields(target.GetType())) if (field.Name == name)
            {
                if (!field.FieldType.IsArray) throw new InvalidDataException("Expected range-target array " + name);
                field.SetValue(target, Array.CreateInstance(field.FieldType.GetElementType(), 0)); return;
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }
        static EntityTypeAttributes BuildDefinition(ScriptableObject asset)
        {
            EntityTypeAttributes definition = new EntityTypeAttributes(asset.name);
            // AssetContainer implements IEnumerable. MultiAssetBase and
            // AssetBase instead expose multiple or single datums through Data.
            object data = null;
            IEnumerable many = asset as IEnumerable;
            if (many == null) { data = Read(asset, "Data"); many = data as IEnumerable; }
            if (many != null)
            {
                foreach (object value in many)
                    if (value != null && !definition.Add(value)) throw new InvalidOperationException("Duplicate humanoid attribute type: " + value.GetType().FullName);
            }
            else if (data == null || !definition.Add(data))
                throw new InvalidOperationException("Humanoid asset has no registry datum: " + asset.name);
            return definition;
        }

        static ScriptableObject CloneAsset(Context c, ScriptableObject source, string name)
        {
            if (source == null) throw new InvalidOperationException("Required humanoid asset absent: " + name);
            object previous; if (c.AssetMap.TryGetValue(source, out previous)) return (ScriptableObject)previous;
            c.Sources.Add(new SourceRecord { Asset = source, Fingerprint = Fingerprint(source), Definition = Registry.GetEntityType(source.name) });
            ScriptableObject clone = RhodesAssetClone59.CreateEmpty(source.GetType()); c.AssetMap.Add(source, clone);
            Dictionary<object, object> copied = new Dictionary<object, object>(new IdentityComparer());
            foreach (FieldInfo field in Fields(source.GetType())) if (field.Name != "mDataCache") field.SetValue(clone, CopyData(field.GetValue(source), copied));
            Own(c, clone, name); return clone;
        }
        static void Own(Context c, ScriptableObject asset, string name) { asset.name = name; asset.hideFlags = HideFlags.HideAndDontSave; c.Owned.Add(asset); }
        static object CopyData(object value, Dictionary<object, object> seen)
        {
            if (value == null) return null; Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(UnityEngine.Object).IsAssignableFrom(type)) return value;
            object previous; if (seen.TryGetValue(value, out previous)) return previous;
            Array array = value as Array;
            if (array != null)
            { if (array.Rank != 1) throw new InvalidOperationException("Unexpected humanoid multidimensional array"); Array clone = (Array)array.Clone(); seen.Add(value, clone); for (int i = 0; i < array.Length; i++) clone.SetValue(CopyData(array.GetValue(i), seen), i); return clone; }
            if (type.Namespace == null || !type.Namespace.StartsWith("BBI.", StringComparison.Ordinal)) return value;
            object result = CloneMethod.Invoke(value, null); seen.Add(value, result);
            foreach (FieldInfo field in Fields(type)) field.SetValue(result, CopyData(field.GetValue(value), seen)); return result;
        }
        static void RemapAsset(Context c, ScriptableObject asset)
        { HashSet<object> seen = new HashSet<object>(new IdentityComparer()); foreach (FieldInfo field in Fields(asset.GetType())) if (field.Name != "mDataCache") field.SetValue(asset, Remap(c, field.GetValue(asset), seen)); }
        static object Remap(Context c, object value, HashSet<object> seen)
        {
            if (value == null) return null; object mapped; if (c.AssetMap.TryGetValue(value, out mapped)) return mapped;
            Type type = value.GetType(); if (type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(UnityEngine.Object).IsAssignableFrom(type) || !seen.Add(value)) return value;
            Array array = value as Array; if (array != null) { for (int i = 0; i < array.Length; i++) array.SetValue(Remap(c, array.GetValue(i), seen), i); return array; }
            if (type.Namespace != null && type.Namespace.StartsWith("BBI.", StringComparison.Ordinal)) foreach (FieldInfo field in Fields(type)) field.SetValue(value, Remap(c, field.GetValue(value), seen));
            return value;
        }
        static bool Mutable(object value)
        { if (value == null) return false; Type type = value.GetType(); return !type.IsValueType && type != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(type) && (value is Array || (type.Namespace != null && type.Namespace.StartsWith("BBI.", StringComparison.Ordinal))); }
        static void Gather(object value, HashSet<object> result)
        { if (!Mutable(value) || !result.Add(value)) return; Array array = value as Array; if (array != null) { foreach (object item in array) Gather(item, result); return; } foreach (FieldInfo field in Fields(value.GetType())) Gather(field.GetValue(value), result); }
        static void Check(object value, HashSet<object> sources, HashSet<object> seen)
        { if (!Mutable(value) || !seen.Add(value)) return; if (sources.Contains(value)) throw new InvalidOperationException("Humanoid mutable data aliases another source: " + value.GetType().FullName); Array array = value as Array; if (array != null) { foreach (object item in array) Check(item, sources, seen); return; } foreach (FieldInfo field in Fields(value.GetType())) Check(field.GetValue(value), sources, seen); }
        static void AssertNoSharedMutable(List<ScriptableObject> sources, List<ScriptableObject> targets)
        { HashSet<object> original = new HashSet<object>(new IdentityComparer()), seen = new HashSet<object>(new IdentityComparer()); foreach (ScriptableObject source in sources) foreach (FieldInfo field in Fields(source.GetType())) Gather(field.GetValue(source), original); foreach (ScriptableObject target in targets) foreach (FieldInfo field in Fields(target.GetType())) Check(field.GetValue(target), original, seen); }
        static string Fingerprint(ScriptableObject asset)
        { StringBuilder text = new StringBuilder(); HashSet<object> seen = new HashSet<object>(new IdentityComparer()); foreach (FieldInfo field in Fields(asset.GetType())) { text.Append(field.DeclaringType.FullName).Append('.').Append(field.Name).Append('='); AppendValue(text, field.GetValue(asset), seen); } using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", ""); }
        static void AppendValue(StringBuilder text, object value, HashSet<object> seen)
        {
            if (value == null) { text.Append("null;"); return; } Type type = value.GetType(); text.Append(type.FullName).Append(':'); UnityEngine.Object unity = value as UnityEngine.Object;
            if (!object.ReferenceEquals(unity, null)) { text.Append(unity.GetInstanceID()).Append('/').Append(unity.name).Append(';'); return; }
            if (type.IsPrimitive || type.IsEnum || type == typeof(string)) { text.Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(';'); return; }
            if (type.IsValueType) { foreach (FieldInfo field in Fields(type)) { text.Append(field.Name).Append('='); AppendValue(text, field.GetValue(value), seen); } return; }
            if (!seen.Add(value)) { text.Append("seen;"); return; } Array array = value as Array;
            if (array != null) { text.Append(array.Length).Append('['); foreach (object item in array) AppendValue(text, item, seen); text.Append(']'); return; }
            if (type.Namespace != null && type.Namespace.StartsWith("BBI.", StringComparison.Ordinal)) foreach (FieldInfo field in Fields(type)) { text.Append(field.Name).Append('='); AppendValue(text, field.GetValue(value), seen); }
            else text.Append(RuntimeHelpers.GetHashCode(value));
        }
        static List<FieldInfo> Fields(Type type)
        { List<FieldInfo> result = new List<FieldInfo>(); for (Type current = type; current != null && current != typeof(object) && current != typeof(UnityEngine.Object) && current != typeof(ScriptableObject); current = current.BaseType) foreach (FieldInfo field in current.GetFields(Flags)) if (!field.IsStatic) result.Add(field); result.Sort(delegate(FieldInfo a, FieldInfo b) { return string.CompareOrdinal(a.DeclaringType.FullName + "." + a.Name, b.DeclaringType.FullName + "." + b.Name); }); return result; }
        static object Read(object value, string name)
        { if (value == null) throw new ArgumentNullException(name); for (Type type = value.GetType(); type != null; type = type.BaseType) { FieldInfo field = type.GetField(name, Flags); if (field != null) return field.GetValue(value); PropertyInfo property = type.GetProperty(name, Flags); if (property != null) return property.GetValue(value, null); } throw new MissingMemberException(value.GetType().FullName, name); }
        static void Write(object value, string name, object data)
        { for (Type type = value.GetType(); type != null; type = type.BaseType) { FieldInfo field = type.GetField(name, Flags); if (field != null) { field.SetValue(value, data); return; } } throw new MissingFieldException(value.GetType().FullName, name); }
        static bool CallIfPresent(object value, string name)
        { for (Type type = value.GetType(); type != null; type = type.BaseType) { MethodInfo method = type.GetMethod(name, Flags, null, Type.EmptyTypes, null); if (method != null) { method.Invoke(value, null); return true; } } return false; }
        static void CallRequired(object value, string name) { if (!CallIfPresent(value, name)) throw new MissingMethodException(value.GetType().FullName, name); }
        static Type NativeType(string name) { return typeof(WeaponAttributesAsset).Assembly.GetType("BBI.Unity.Game.Data." + name, true); }
    }
}
