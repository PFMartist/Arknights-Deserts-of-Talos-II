using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Game.Data.Queries;

namespace EndfieldDoK.MapUnitLab
{
    // Native skirmish artifacts are extracted at the commander's ExtractionZone,
    // not at the carrier. Preserve the native collectible, docking, countdown,
    // score and victory chain; add only U02 to a private runtime query copy.
    internal static class RhodesSurveyArtifacts
    {
        internal const string SalvageAbility="Ability_C_DeploySalvagers_Tech";
        const string ZoneKey="ExtractionZone";
        static readonly MethodInfo CloneMethod=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly string[] NativeCollectors={"C_Baserunner","G_Baserunner","C_Baserunner_MP","G_Baserunner_MP","C_Harvester_MP","G_Harvester_MP","C_Sob_Baserunner_MP","K_Baserunner_MP"};
        static EntityTypeCollection Registry;
        static EntityTypeAttributes Definition;
        static ExtractionZoneAttributes Original,Custom;
        static QueryContainerBase OriginalSnapshot;

        internal static void Register(EntityTypeCollection registry)
        {
            if(registry==null)throw new ArgumentNullException("registry");
            if(object.ReferenceEquals(registry,Registry)){Verify(registry);return;}
            Reset();
            EntityTypeAttributes definition=registry.GetEntityType(ZoneKey);
            ExtractionZoneAttributes original=definition==null?null:definition.Get<ExtractionZoneAttributes>();
            if(original==null||original.QueryData==null)throw new InvalidOperationException("Native artifact extraction-zone data is unavailable");
            QueryContainerBase source=original.QueryData;
            if(!source.UseEntityTypeQuery||source.QEntityTypeNames==null||source.QEntityTypeNames.Contains(FactionUnits.SurveyKey))
                throw new InvalidOperationException("Unexpected native extraction-zone collector query");
            foreach(string key in NativeCollectors)if(!source.QEntityTypeNames.Contains(key))
                throw new InvalidOperationException("Native extraction collector missing: "+key);
            QueryContainerBase query=CopyQuery(source);query.QEntityTypeNames.Add(FactionUnits.SurveyKey);
            ExtractionZoneAttributes custom=(ExtractionZoneAttributes)CloneMethod.Invoke(original,null);
            FieldInfo field=FindField(custom.GetType(),"m_Query");field.SetValue(custom,query);
            if(object.ReferenceEquals(custom.QueryData,source)||!object.ReferenceEquals(custom.QueryData,query)||!custom.RadiusMeters.Equals(original.RadiusMeters))
                throw new InvalidOperationException("Private artifact extraction query could not be constructed");
            if(!definition.Replace(original,custom))throw new InvalidOperationException("Cannot register private artifact extraction query");
            Registry=registry;Definition=definition;Original=original;Custom=custom;OriginalSnapshot=CopyQuery(source);
            try{Verify(registry);}catch{Reset();throw;}
        }

        internal static void Verify(EntityTypeCollection registry)
        {
            if(Registry==null||!object.ReferenceEquals(registry,Registry)||Definition==null||Original==null||Custom==null||
                !object.ReferenceEquals(registry.GetEntityType(ZoneKey),Definition)||!object.ReferenceEquals(Definition.Get<ExtractionZoneAttributes>(),Custom))
                throw new InvalidOperationException("Rhodes artifact extraction registry generation changed");
            QueryContainerBase source=Original.QueryData,query=Custom.QueryData;
            if(object.ReferenceEquals(Original,Custom)||object.ReferenceEquals(source,query)||!SameQuery(source,OriginalSnapshot)||
                !Custom.RadiusMeters.Equals(Original.RadiusMeters)||!SameFlagsAndOtherFilters(query,source)||
                query.QEntityTypeNames==null||query.QEntityTypeNames.Count!=source.QEntityTypeNames.Count+1)
                throw new InvalidOperationException("Rhodes artifact extraction altered native query data");
            for(int i=0;i<source.QEntityTypeNames.Count;i++)if(query.QEntityTypeNames[i]!=source.QEntityTypeNames[i])
                throw new InvalidOperationException("Rhodes artifact extraction lost or reordered a native collector");
            if(query.QEntityTypeNames[query.QEntityTypeNames.Count-1]!=FactionUnits.SurveyKey||
                Aliases(query.QTags,source.QTags)||Aliases(query.QEntityTypeNames,source.QEntityTypeNames)||
                Aliases(query.RequiredComponentIDs,source.RequiredComponentIDs)||Aliases(query.RequiredCommanderIDs,source.RequiredCommanderIDs))
                throw new InvalidOperationException("Rhodes artifact extraction query is not isolated");
        }

        // Restore only our owned datum. Never overwrite a newer owner's change.
        // FactionUnits calls this after its live-view guard, before asset teardown.
        internal static void Reset()
        {
            if(Definition!=null&&Custom!=null&&object.ReferenceEquals(Definition.Get<ExtractionZoneAttributes>(),Custom))
                if(!Definition.Replace(Custom,Original))throw new InvalidOperationException("Cannot restore native artifact extraction query");
            Registry=null;Definition=null;Original=null;Custom=null;OriginalSnapshot=null;
        }

        static QueryContainerBase CopyQuery(QueryContainerBase source)
        {
            QueryContainerBase copy=(QueryContainerBase)CloneMethod.Invoke(source,null);
            copy.QTags=source.QTags==null?null:new List<string>(source.QTags);
            copy.QEntityTypeNames=source.QEntityTypeNames==null?null:new List<string>(source.QEntityTypeNames);
            copy.RequiredComponentIDs=source.RequiredComponentIDs==null?null:(int[])source.RequiredComponentIDs.Clone();
            copy.RequiredCommanderIDs=source.RequiredCommanderIDs==null?null:(int[])source.RequiredCommanderIDs.Clone();
            return copy;
        }
        static bool SameQuery(QueryContainerBase a,QueryContainerBase b)
        {return a!=null&&b!=null&&SameFlagsAndOtherFilters(a,b)&&Same(a.QEntityTypeNames,b.QEntityTypeNames);}
        static bool SameFlagsAndOtherFilters(QueryContainerBase a,QueryContainerBase b)
        {
            return a.UseTagQuery==b.UseTagQuery&&a.TagCheckType==b.TagCheckType&&a.UseEntityTypeQuery==b.UseEntityTypeQuery&&
                a.UseComponentTypeQuery==b.UseComponentTypeQuery&&a.UseCommanderQuery==b.UseCommanderQuery&&
                Same(a.QTags,b.QTags)&&Same(a.RequiredComponentIDs,b.RequiredComponentIDs)&&Same(a.RequiredCommanderIDs,b.RequiredCommanderIDs);
        }
        static bool Same<T>(IList<T> a,IList<T> b)
        {
            if(a==null||b==null)return a==null&&b==null;
            if(a.Count!=b.Count)return false;
            for(int i=0;i<a.Count;i++)if(!EqualityComparer<T>.Default.Equals(a[i],b[i]))return false;
            return true;
        }
        static bool Aliases(object a,object b){return a!=null&&object.ReferenceEquals(a,b);}
        static FieldInfo FindField(Type type,string name)
        {
            for(Type t=type;t!=null;t=t.BaseType)
            {FieldInfo field=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(field!=null)return field;}
            throw new MissingFieldException(type.FullName,name);
        }
    }
}
