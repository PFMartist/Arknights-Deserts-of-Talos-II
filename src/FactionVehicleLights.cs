using System;
using System.Collections.Generic;
using BBI.Unity.Core.World;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Measured from the installed RHS2 lens faces after handedness conversion.
    // Each lens bank gets one visible cone/glow pair, not one light per bulb.
    // Templates persist across matches; native scene FX pools own instances.
    internal static class FactionVehicleLights
    {
        const string Marker="EF_Vehicle_HeadlightAnchor";
        static GameObject Holder;
        static readonly Dictionary<string,EffectDefinition> Templates=new Dictionary<string,EffectDefinition>(StringComparer.Ordinal);

        internal static bool Mounts(string key,out Vector3 left,out Vector3 right)
        {
            float x,y,z;
            switch(key)
            {
                case FactionUnits.HarvesterKey:x=4.55f;y=6.839f;z=9.19f;break;
                case FactionUnits.SurveyKey:
                case FactionUnits.EngineeringKey:x=4.8f;y=11.8f;z=22.30f;break;
                case FactionUnits.EscortKey:x=2.064f;y=2.686f;z=6.37f;break;
                case FactionUnits.SuppressionKey:
                case FactionUnits.AirDefenceKey:
                case FactionUnits.ArtilleryKey:x=3.9f;y=5.25f;z=14.33f;break;
                case FactionUnits.RailgunKey:x=4.9f;y=4.1f;z=12.74f;break;
                default:left=right=Vector3.zero;return false;
            }
            left=new Vector3(-x,y,z);right=new Vector3(x,y,z);return true;
        }

        internal static void Append(UnitViewAttributes target,UnitViewAttributes donor)
        {
            FactionModelBinding binding=target==null||target.ModelPrefab==null?null:target.ModelPrefab.GetComponent<FactionModelBinding>();
            if(binding==null||binding.Air)return;
            Vector3 left,right;if(!Mounts(binding.UnitKey,out left,out right))return;
            GameObject anchor=null;
            try
            {
                if(object.ReferenceEquals(target,donor)||binding.GeometryRoot==null)
                    throw new InvalidOperationException("Vehicle headlights require private geometry");
                Vector3 scale=binding.GeometryRoot.lossyScale;
                if(Mathf.Abs(scale.x-1f)>.001f||Mathf.Abs(scale.y-1f)>.001f||Mathf.Abs(scale.z-1f)>.001f)
                    throw new InvalidOperationException("Vehicle headlight geometry must be metric");
                if(target.UnitEffectDefs!=null)foreach(EffectDefinition item in target.UnitEffectDefs)
                    if(item!=null&&item.Name=="EF_Vehicle_Headlights_"+binding.UnitKey)return;
                if(binding.GeometryRoot.Find(Marker)!=null)throw new InvalidOperationException("Vehicle headlight anchor already exists");
                if(Holder==null)
                {
                    Holder=new GameObject("EF_Vehicle_NativeLightTemplates");Holder.SetActive(false);
                    Holder.hideFlags=HideFlags.HideAndDontSave;UnityEngine.Object.DontDestroyOnLoad(Holder);
                }
                EffectDefinition definition;
                if(!Templates.TryGetValue(binding.UnitKey,out definition))
                {
                    definition=RhodesNativeLightFactory.VehicleDefinition(donor,Holder.transform,binding.UnitKey,Marker,left,right);
                    Templates.Add(binding.UnitKey,definition);
                }
                // Created before native UnitView initialization/bone caching.
                // It follows the same geometry root as native body pitch/roll.
                anchor=new GameObject(Marker);anchor.layer=binding.GeometryRoot.gameObject.layer;
                anchor.transform.parent=binding.GeometryRoot;anchor.transform.localPosition=Vector3.zero;
                anchor.transform.localRotation=Quaternion.identity;anchor.transform.localScale=Vector3.one;
                List<EffectDefinition> effects=new List<EffectDefinition>();
                if(target.UnitEffectDefs!=null)effects.AddRange(target.UnitEffectDefs);
                effects.Add(definition);target.UnitEffectDefs=effects.ToArray();
                Runtime.Log("RHODES_VEHICLE_HEADLIGHTS_READY key="+binding.UnitKey+"; groups=1; spot=1; cones=2; glows=4; category=UnitLightsSmall; nativeMapLODQualityFowPool=True; customUpdate=0; visualAcceptancePending=True");
            }
            catch(Exception error)
            {
                if(anchor!=null)UnityEngine.Object.DestroyImmediate(anchor);
                // A presentation-only issue must not abort faction registration.
                Runtime.Log("RHODES_VEHICLE_HEADLIGHTS_DISABLED key="+binding.UnitKey+"; reason="+error.Message);
            }
        }
    }
}
