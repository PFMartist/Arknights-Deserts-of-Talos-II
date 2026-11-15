using System;
using System.Collections.Generic;
using BBI.Unity.Core.World;
using BBI.Unity.Game.Data;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Build one private single-nozzle native Engine prefab per aircraft type.
    // UnitView owns all triggers, FX pooling, quality/LOD, visibility and death
    // cleanup. BoneRef follows the authored nozzle transform, including F01's
    // thrust-vector animation. There is no added per-frame controller.
    public static class RhodesAirEffects
    {
        static GameObject Templates;
        static readonly List<GameObject> Owned=new List<GameObject>();
        static readonly string[] FighterNozzles={"L0::HP_F01_VTOL_RearThrust_L","L0::HP_F01_VTOL_RearThrust_R"};
        static readonly string[] BomberNozzles={"L0::HP_B01_Exhaust_L_0","L0::HP_B01_Exhaust_L_1","L0::HP_B01_Exhaust_R_0","L0::HP_B01_Exhaust_R_1"};
        static readonly string[] SingleNozzleParts={"FX_SYS_Round_SourceGlow_L","FX_SYS_EllipseGlow_00_L","FX_SYS_EllipseGlow_01_L","FX_SYS_EllipseGlow_02_L","FX_SYS_Trail_L"};

        public static string[] NozzlesFor(string key)
        {
            if(key==RhodesCarrierUnits.AircraftKey)return (string[])FighterNozzles.Clone();
            if(key==RhodesCarrierUnits.HeavyBomberKey)return (string[])BomberNozzles.Clone();
            return new string[0]; // U08 never receives fixed-wing engine flames.
        }

        public static bool KeepNativePart(string name)
        {foreach(string part in SingleNozzleParts)if(name==part)return true;return false;}

        public static void Configure(string key,UnitViewAttributes target,UnitViewAttributes source,RhodesLoadedModel model)
        {
            string[] nozzles=NozzlesFor(key);
            if(nozzles.Length==0)return;
            if(target==null||source==null||model==null||object.ReferenceEquals(target,source))
                throw new InvalidOperationException("Air-engine effects require the private aircraft view");
            foreach(string name in nozzles)
            {
                Transform node=null;int count=0;
                for(int i=0;i<model.Package.Nodes.Length;i++)if(model.Package.Nodes[i].Name==name){node=model.Nodes[i];count++;}
                if(count!=1||node==null||!node.IsChildOf(model.Root.transform))
                    throw new InvalidOperationException("Exact authored engine nozzle missing or duplicated: "+name);
                // No guessed offsets, donor-tail coordinates, or hidden scale.
                // These RHS markers face local +Z; the native plume trails -Z.
                if((node.localScale-Vector3.one).sqrMagnitude>.000001f)
                    throw new InvalidOperationException("Authored nozzle unexpectedly scaled: "+name);
            }
            EffectDefinition original=null;
            if(source.UnitEffectDefs!=null)foreach(EffectDefinition definition in source.UnitEffectDefs)
                if(definition!=null&&definition.FXType==FrillType.Engine&&definition.FXPrefab!=null)
                {if(original!=null)throw new InvalidOperationException("Ambiguous native aircraft engine FX");original=definition;}
            if(original==null)throw new InvalidOperationException("Native aircraft engine frill absent: "+key);
            if(Templates==null)
            {
                Templates=new GameObject("EF_Rhodes_AirEngine_Templates");Templates.SetActive(false);
                Templates.hideFlags=HideFlags.HideAndDontSave;UnityEngine.Object.DontDestroyOnLoad(Templates);
            }
            GameObject single=(GameObject)UnityEngine.Object.Instantiate(original.FXPrefab.gameObject);
            single.name=key+"_SingleNozzle_NativeEngineFX";single.transform.parent=Templates.transform;
            single.transform.localPosition=Vector3.zero;single.transform.localRotation=Quaternion.identity;single.transform.localScale=Vector3.one;
            Owned.Add(single);
            Transform glow=null;
            for(int i=0;i<single.transform.childCount;i++)
            {Transform child=single.transform.GetChild(i);if(child.name==SingleNozzleParts[0]){if(glow!=null)throw new InvalidOperationException("Duplicate native source glow");glow=child;}}
            if(glow==null)throw new InvalidOperationException("Native single-engine source-glow origin absent");
            Vector3 nativeOrigin=glow.localPosition;int retained=0;
            for(int i=single.transform.childCount-1;i>=0;i--)
            {
                Transform child=single.transform.GetChild(i);
                if(!KeepNativePart(child.name)){UnityEngine.Object.DestroyImmediate(child.gameObject);continue;}
                child.localPosition-=nativeOrigin;retained++;
            }
            if(retained!=SingleNozzleParts.Length||single.GetComponentsInChildren<ParticleSystem>(true).Length<4)
                throw new InvalidOperationException("Native single-nozzle engine composition differs");
            EffectDefinition effect=new EffectDefinition();
            effect.Name="Rhodes authored-nozzle engine";effect.FXPrefab=single.transform;
            effect.ActiveAtLOD=original.ActiveAtLOD;effect.Sound=original.Sound;effect.PolyGroup=original.PolyGroup;
            NativeRead.Set(effect,"m_FrillType",original.FXType);
            effect.BoneRef=nozzles;effect.RaycastDir=original.RaycastDir;
            effect.FollowNLIPSScale=original.FollowNLIPSScale;effect.InheritRaycastNormal=original.InheritRaycastNormal;
            target.UnitEffectDefs=new EffectDefinition[]{effect};
            Runtime.Log("RHODES_AIR_ENGINE_FX_PREPARED key="+key+"; nativePrefab="+original.FXPrefab.name+
                "; frill="+effect.FXType+"; activeAtLOD="+effect.ActiveAtLOD+"; nozzles="+String.Join(",",nozzles)+
                "; singleNozzleParts="+retained+"; oldTwinOffsetsRemoved=True; emitterOffset=0,0,0; emitterRotation=0,0,0,1; emitterScale=1,1,1; nativeTimingVisibilityPoolAndQuality=True; U08Excluded=True; runtimeVisualAcceptancePending=True");
        }

        public static void Reset()
        {
            foreach(GameObject item in Owned)if(item!=null)UnityEngine.Object.DestroyImmediate(item);
            Owned.Clear();if(Templates!=null)UnityEngine.Object.DestroyImmediate(Templates);Templates=null;
        }
    }
}
