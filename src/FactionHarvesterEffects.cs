using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Unity.Core.World;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Reuse the native per-unit salvage event -> UnitFXController path. The
    // private view owns only five definitions and two authored emission aliases;
    // no additional scene scan, subscription, simulation write or Update exists.
    internal static class FactionHarvesterEffects
    {
        internal const string PdaMarker="PIVOT_H03_HP_Resource_Collect";
        internal const string CutterMarker="PIVOT_U01_HP_Resource_Cutter";
        internal const string PdaAlias="EF_U01_Resource_PDA";
        internal const string CutterAlias="EF_U01_Resource_Cutter";
        static readonly MethodInfo CloneMethod=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);

        internal static EffectDefinition[] Configure(UnitViewAttributes target,UnitViewAttributes source,FactionModelBinding binding)
        {
            GameObject pda=null,cutter=null;
            try
            {
                if(target==null||source==null||object.ReferenceEquals(target,source)||binding==null||binding.UnitKey!=FactionUnits.HarvesterKey)
                    throw new InvalidOperationException("U01 resource effects require a private harvester view");
                // Validate all authored levels but emit one native effect set.
                // Unity's LODGroup switches mesh renderers, not these empties.
                Transform pdaMount=null,cutterMount=null;
                for(int lod=0;lod<3;lod++)
                {
                    Transform p=Marker(binding,lod,PdaMarker),c=Marker(binding,lod,CutterMarker);
                    if(lod==0){pdaMount=p;cutterMount=c;}
                }
                EffectDefinition[] effects=CloneDefinitions(source.UnitEffectDefs,PdaAlias,CutterAlias);
                pda=Alias(PdaAlias,pdaMount);cutter=Alias(CutterAlias,cutterMount);
                Runtime.Log("RHODES_U01_NATIVE_RESOURCE_FX_READY definitions=5; CU=2; RU=2; wreckLaser=1; pda="+PdaMarker+"; cutter="+CutterMarker+
                    "; nativeSalvageEvents=True; nativeVisibilityLODAndPooling=True; newUpdateMethods=0; sourceDefinitionsImmutable=True; actualVisualAcceptancePending=True");
                return effects;
            }
            catch(Exception error)
            {
                if(pda!=null)UnityEngine.Object.DestroyImmediate(pda);
                if(cutter!=null)UnityEngine.Object.DestroyImmediate(cutter);
                // Match the previous no-resource-FX U01 presentation if only
                // this optional binding fails. Never abort every faction unit.
                Runtime.Log("RHODES_U01_RESOURCE_FX_DISABLED scope=U01Only; sourceDefinitionsImmutable=True; reason="+error.Message);
                return new EffectDefinition[0];
            }
        }

        internal static EffectDefinition[] CloneDefinitions(EffectDefinition[] source,string pda,string cutter)
        {
            if(source==null||string.IsNullOrEmpty(pda)||string.IsNullOrEmpty(cutter)||pda==cutter)
                throw new InvalidOperationException("U01 resource effect source/alias missing");
            List<EffectDefinition> result=new List<EffectDefinition>(5);int[] counts=new int[3];
            foreach(EffectDefinition item in source)
            {
                if(item==null)continue;
                int kind=ResourceKind(item.FXType);if(kind<0)continue;
                if(item.BoneRef==null||item.BoneRef.Length!=1||item.BoneRef[0]!="FX_PDA_Muzzle"||object.ReferenceEquals(item.FXPrefab,null))
                    throw new InvalidOperationException("Native U01 resource effect input changed");
                // Preserve every base/private serialized field, including the
                // native trigger enum, prefab, LOD, sound and raycast policy.
                // BoneRef is the sole mutable array in the inspected schema.
                EffectDefinition copy=(EffectDefinition)CloneMethod.Invoke(item,null);
                copy.BoneRef=new string[]{kind==2?cutter:pda};
                if(copy.FXType!=item.FXType)throw new InvalidOperationException("U01 resource trigger clone differs");
                result.Add(copy);counts[kind]++;
            }
            if(counts[0]!=2||counts[1]!=2||counts[2]!=1||result.Count!=5)
                throw new InvalidOperationException("Native U01 resource effect inventory changed");
            return result.ToArray();
        }
        internal static int ResourceKind(FrillType type)
        {
            if(type==FrillType.SalvageR1Range)return 0;
            if(type==FrillType.SalvageR2Range)return 1;
            if(type==FrillType.SalvageR3Range)return 2;
            return -1;
        }
        static Transform Marker(FactionModelBinding binding,int lod,string marker)
        {
            string name="L"+lod+"::"+marker;Transform found=null;
            for(int i=0;i<binding.Names.Length;i++)if(binding.Names[i]==name)
            {
                if(found!=null)throw new InvalidOperationException("Duplicate U01 resource marker "+name);
                found=binding.Nodes[i];
            }
            if(found==null||!found.IsChildOf(binding.GeometryRoot)||found.parent==null||found.parent.name!="L"+lod+"::PIVOT_H03_Collector_Head")
                throw new InvalidOperationException("U01 resource marker parent differs "+name);
            Vector3 scale=found.localScale;
            if(Math.Abs(scale.x-1f)>.00001f||Math.Abs(scale.y-1f)>.00001f||Math.Abs(scale.z-1f)>.00001f)
                throw new InvalidOperationException("U01 resource marker scale differs "+name);
            return found;
        }
        static GameObject Alias(string name,Transform mount)
        {
            GameObject result=new GameObject(name);result.layer=mount.gameObject.layer;
            result.transform.parent=mount;result.transform.localPosition=Vector3.zero;
            result.transform.localRotation=Quaternion.identity;result.transform.localScale=Vector3.one;
            return result;
        }
    }
}
