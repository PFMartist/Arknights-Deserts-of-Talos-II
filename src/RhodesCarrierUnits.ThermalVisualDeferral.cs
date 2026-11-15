using System;
using System.Collections.Generic;
using BBI.Unity.Core.World;
using BBI.Unity.Game.Data;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesCarrierUnits
    {
        // Heat simulation, native gauge and announcements remain active. Only
        // inherited donor-coordinate thermal FX wait for the next authored hull.
        static void PrepareDeferredThermalVisuals(Context c)
        {
            object sourceAsset=Read(c.Source,"m_View");
            UnitViewAttributes source=Read(sourceAsset,"m_Attributes") as UnitViewAttributes;
            UnitViewAttributes own=Read(c.View,"m_Attributes") as UnitViewAttributes;
            if(source==null||own==null||object.ReferenceEquals(c.View,sourceAsset)||object.ReferenceEquals(own,source))
                throw new InvalidOperationException("Deferred Rhodes thermal FX require private view attributes");
            int removed;
            own.UnitEffectDefs=FilterDeferredThermalEffects(own.UnitEffectDefs,out removed);
            RhodesCarrierBinding binding=own.ModelPrefab==null?null:own.ModelPrefab.GetComponent<RhodesCarrierBinding>();
            bool prepared=RhodesThermalVisuals.Prepare(own,source,binding);
            Runtime.Log("RHODES_THERMAL_VISUALS_DEFERRED inheritedDefinitionsRemoved="+removed+
                "; nativeHeatGaugeVoiceRetained=True; donorViewUnchanged=True; customHeatMaterialsAndVentsPrepared="+prepared+"; wrongDonorPositionsDisabled=True");
        }

        // Pure managed-data helper; no prefab, shared definition, material,
        // trigger flag, simulation state or source array is modified.
        internal static EffectDefinition[] FilterDeferredThermalEffects(EffectDefinition[] source,out int removed)
        {
            removed=0;if(source==null)return null;
            List<EffectDefinition> retained=new List<EffectDefinition>(source.Length);
            foreach(EffectDefinition definition in source)
            {
                if(definition!=null&&(definition.FXType==FrillType.Overheating||definition.FXType==FrillType.OverheatingDamage))
                    removed++;
                else retained.Add(definition);
            }
            return retained.ToArray();
        }
    }
}
