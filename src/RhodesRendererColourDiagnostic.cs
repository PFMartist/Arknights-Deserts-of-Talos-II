using System;
using System.Collections.Generic;
using System.Globalization;
using BBI.Core.ComponentModel;
using BBI.Unity.Game.World;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Once per at most four carrier spawns. Reads actual Renderer copies after
    // the existing forced native property-block application; no visual writes.
    internal static class RhodesRendererColourDiagnostic
    {
        static readonly HashSet<Entity> Reported=new HashSet<Entity>();
        static readonly string[] Targets={"V17SRC_03_Mechanical_Graphite","V17SRC_04_Edges_SatinSteel","V17SRC_V17_Baked_LiftPlatforms_Paint","V17SRC_V17_Baked_SharedRecovery_OfficialSVG","V17SRC_V17_AV_FlightDeck_PBR","V17SRC_02_Deck_WarmGrey","V17SRC_V06_07_Glazing_DeepBlue","V06_VP03_ArmoredServiceBelt"};
        internal static void Record(UnitView view,RhodesCarrierBinding binding)
        {
            if(view==null||binding==null||binding.Geometry==null||view.EntityID.Equals(Entity.None)||Reported.Count>=4||!Reported.Add(view.EntityID))return;
            try
            {
                Renderer[] cached=NativeRead.Get(view,"mGeoRenderObjects") as Renderer[];MaterialPropertyBlock native=NativeRead.Get(view,"mGeometryRendererPropertyBlock") as MaterialPropertyBlock;
                if(native==null)return;MaterialPropertyBlock actual=new MaterialPropertyBlock();HashSet<string> emitted=new HashSet<string>(StringComparer.Ordinal);int reports=0;
                foreach(Renderer renderer in binding.Geometry)
                {
                    if(renderer==null)continue;Material material=renderer.sharedMaterial;if(material==null)continue;string target=null;
                    foreach(string name in Targets)if(material.name.IndexOf(name,StringComparison.Ordinal)>=0){target=name;break;}
                    if(target==null||!emitted.Add(target))continue;
                    actual.Clear();renderer.GetPropertyBlock(actual);Vector4 baseActual=actual.GetVector("_BaseColor"),trimActual=actual.GetVector("_TrimColor");bool inCache=false;
                    if(cached!=null)foreach(Renderer item in cached)if(object.ReferenceEquals(item,renderer)){inCache=true;break;}
                    Texture surface=material.HasProperty("_Surface")?material.GetTexture("_Surface"):null;
                    Texture overlay=material.HasProperty("_OverlayMask")?material.GetTexture("_OverlayMask"):null;
                    Runtime.Log("RHODES_ACTUAL_RENDERER_COLOURS entity="+view.EntityID+"; material="+target+"; renderer="+renderer.name+"; rendererID="+renderer.GetInstanceID()+
                        "; childOfLiveView="+renderer.transform.IsChildOf(view.transform)+"; presentInNativeGeoCache="+inCache+"; nativeCacheCount="+(cached==null?0:cached.Length)+
                        "; actualBase="+Text(baseActual)+"; actualTrim="+Text(trimActual)+"; nativeBase="+Text(native.GetVector("_BaseColor"))+"; nativeTrim="+Text(native.GetVector("_TrimColor"))+
                        "; materialBase="+Colour(material,"_BaseColor")+"; materialTrim="+Colour(material,"_TrimColor")+"; materialSpec="+Colour(material,"_SpecColor")+
                        "; surface="+(surface==null?"none":surface.name)+"; shader="+(material.shader==null?"none":material.shader.name)+"; activeColorSpace="+QualitySettings.activeColorSpace+
                        "; customTeamColours="+RhodesTeamColours.CustomColoursEnabled+"; sharedMaterialOverlay="+(overlay==null?"none":overlay.name)+"; overlaySize="+(overlay==null?"none":overlay.width+"x"+overlay.height)+
                        "; afterExistingNativeForce=True; readOnly=True; oncePerSpawnBound4=True");
                    if(++reports>=8)break;
                }
            }
            catch(Exception error){Runtime.Log("RHODES_ACTUAL_RENDERER_COLOURS_UNAVAILABLE entity="+view.EntityID+"; readOnly=True; "+error.GetType().Name+": "+error.Message);}
        }
        static string Colour(Material material,string name){return material.HasProperty(name)?Text((Vector4)material.GetColor(name)):"absent";}
        static string Text(Vector4 value){return value.x.ToString("F6",CultureInfo.InvariantCulture)+","+value.y.ToString("F6",CultureInfo.InvariantCulture)+","+value.z.ToString("F6",CultureInfo.InvariantCulture)+","+value.w.ToString("F6",CultureInfo.InvariantCulture);}
    }
}
