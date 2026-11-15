using System;
using System.Collections.Generic;
using System.IO;
using BBI.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesResearch
    {
        const string EscortResearchIcon="EF_UNIT_RI_U04_LIGHT_ESCORT_Production";
        const string SuppressionResearchIcon="EF_UNIT_RI_U05_SUPPRESSION_Production";
        const string RailgunResearchIcon="EF_UNIT_RI_U06_RAILGUN_Production";
        const string AirDefenceResearchIcon="EF_UNIT_RI_U07_AIR_DEFENCE_Production";
        const string TiltResearchIcon="EF_UNIT_RI_U08_TILT_STRIKE_Production";
        const string ArtilleryResearchIcon="EF_UNIT_RI_U09_ARTILLERY_Production";
        const string EngineeringResearchIcon="EF_UNIT_"+FactionUnits.EngineeringKey+"_Production";
        const string SurveyResearchIcon="EF_UNIT_"+FactionUnits.SurveyKey+"_Production";
        const string InterceptorResearchIcon="EF_UNIT_"+RhodesCarrierUnits.AircraftKey+"_Production";
        const string BomberResearchIcon="EF_UNIT_"+RhodesCarrierUnits.HeavyBomberKey+"_Production";
        const string CarrierResearchIcon="EF_RHODES_Info",AviationResearchIcon="EF_RHODES_Info_AV",GunneryResearchIcon="EF_RHODES_Info_GN";
        const string CarrierProductionReference="Icon-Command-Production-C_Carrier";
        static readonly string[] CarrierResearchIcons={CarrierResearchIcon,AviationResearchIcon,GunneryResearchIcon};
        static readonly string[] UnitResearchIcons={EscortResearchIcon,SuppressionResearchIcon,RailgunResearchIcon,AirDefenceResearchIcon,TiltResearchIcon,ArtilleryResearchIcon,EngineeringResearchIcon,SurveyResearchIcon,InterceptorResearchIcon,BomberResearchIcon};
        static readonly Dictionary<UIAtlas,UIAtlas> ResearchAtlases=new Dictionary<UIAtlas,UIAtlas>();
        static Color32[][] ResearchCarrierPixels;
        static bool ResearchAtlasWarned;

        // One explicit identity for every private item. Group armour uses U05,
        // the first actual target; fleet logistics use the base Rhodes hull.
        static string ResearchIconFor(string key)
        {
            switch(key)
            {
                case LightAssembly:case EscortBoost:case EscortDamage1:case EscortDamage2:case EscortArmour1:case EscortArmour2:return EscortResearchIcon;
                case SuppressionAssembly:case TrackedArmour1:case TrackedArmour2:case TrackedArmour3:return SuppressionResearchIcon;
                case RailgunAssembly:case RailgunDamage:return RailgunResearchIcon;
                case AirDefenceAssembly:case AirDefenceReload:return AirDefenceResearchIcon;
                case TiltAssembly:case AdvancedOriginiumSuppressant:return TiltResearchIcon;
                case ArtilleryAssembly:case PredictiveFireControl:return ArtilleryResearchIcon;
                case Aviation1:return AviationResearchIcon;
                case InterceptorService:return InterceptorResearchIcon;
                case BomberProtection:return BomberResearchIcon;
                case EngineeringRepair:return EngineeringResearchIcon;
                case SurveyProbeDurability:return SurveyResearchIcon;
                case Gunnery1:case Gunnery2:case Gunnery3:return GunneryResearchIcon;
                case Population1:case Population2:case Population3:case Population4:case Population5:
                case ProductionLine2:case ReactorCapacity:case ReactorCapacity2:case ReactorCapacity3:case ReactorCapacity4:case ReactorCapacity5:return CarrierResearchIcon;
                default:throw new InvalidOperationException("Private research icon identity missing: "+key);
            }
        }
        static void ApplyResearchIcon(object data,string key)
        {Set(data,"m_IconSpriteName",ResearchIconFor(key));}
        static void VerifyResearchIcons(TechTreeAttributes tree)
        {
            foreach(string key in Keys)
            {
                ResearchItemAttributes item=Registry.GetEntityType(key).Get<ResearchItemAttributes>();
                if((string)NativeRead.Get(item,"m_IconSpriteName")!=ResearchIconFor(key))
                    throw new InvalidOperationException("Private research icon identity changed: "+key);
            }
            string[][] chains=CapabilityEngineeringChains();
            for(int i=0;i<tree.TechTrees.Length;i++)
            {
                string expected=i==0?CarrierResearchIcon:ResearchIconFor(chains[i-1][0]);
                if((string)NativeRead.Get(tree.TechTrees[i],"m_IconSpriteName")!=expected)
                    throw new InvalidOperationException("Private research branch icon identity changed: "+i);
            }
        }

        // Research buttons use Atlas-ControlStrip, whereas the existing Rhodes
        // info sprites are normally appended only to InfoUnitImages. Reuse the
        // same three authored PNGs at the native production sprite dimensions.
        // Unit production sprites are already added by RhodesUnitIcons earlier
        // in ThemeAtlas, after ConfigureRegistered completed all eleven units.
        public static UIAtlas MapResearchAtlas(UIAtlas source)
        {
            if(source==null||!Ready||source.GetSprite(CarrierProductionReference)==null)return source;
            bool missing=false;foreach(string name in CarrierResearchIcons)if(source.GetSprite(name)==null)missing=true;
            if(!missing)return source;
            UIAtlas cached;if(ResearchAtlases.TryGetValue(source,out cached)&&cached!=null)return cached;
            try
            {
                foreach(string name in UnitResearchIcons)if(source.GetSprite(name)==null)
                    throw new InvalidDataException("Research atlas reached before registered unit production sprites: "+name);
                if(ResearchAtlases.Count>=8)throw new InvalidOperationException("Research icon atlas cache budget exceeded");
                EnsureResearchCarrierPixels();
                UIAtlas result=OperatorPortraitIcons.AppendPatterns(source,
                    new string[]{CarrierProductionReference,CarrierProductionReference,CarrierProductionReference},CarrierResearchIcons,ResearchCarrierPixels);
                foreach(string key in Keys)if(result.GetSprite(ResearchIconFor(key))==null)
                    throw new InvalidDataException("Research atlas cannot resolve private item: "+key);
                ResearchAtlases[source]=result;
                Runtime.Log("RHODES_RESEARCH_ICON_ATLAS_READY items="+Keys.Length+"; branches="+(CapabilityEngineeringChains().Length+1)+
                    "; existingUnitProductionSprites="+UnitResearchIcons.Length+"; existingRhodesForms="+CarrierResearchIcons.Length+"; allItemNamesResolve=True; nativeProductionDimensions=True; nativeAtlasUntouched=True; boundedUILifetimeCache=True; visualAcceptancePending=True");
                return result;
            }
            catch(Exception error)
            {
                if(!ResearchAtlasWarned){ResearchAtlasWarned=true;Runtime.Log("WARNING RHODES_RESEARCH_ICON_ATLAS "+error.GetType().Name+": "+error.Message);}
                return source;
            }
        }
        static void EnsureResearchCarrierPixels()
        {
            if(ResearchCarrierPixels!=null)return;
            Color32[][] images=new Color32[CarrierResearchIcons.Length][];
            for(int i=0;i<images.Length;i++)
            {
                Texture2D image=new Texture2D(2,2,TextureFormat.RGBA32,false);image.hideFlags=HideFlags.HideAndDontSave;
                try
                {
                    string file=Path.Combine(CustomUnits.AssetDirectory,"rhodes_identity/"+CarrierResearchIcons[i]+".png");
                    if(!image.LoadImage(File.ReadAllBytes(file))||image.width!=256||image.height!=256)
                        throw new InvalidDataException("Research Rhodes identity must be the existing256x256 PNG: "+file);
                    images[i]=image.GetPixels32();
                }
                finally{UnityEngine.Object.DestroyImmediate(image);}
            }
            ResearchCarrierPixels=images;
        }
        internal static void ReleaseResearchIconCache()
        {ResearchAtlases.Clear();ResearchCarrierPixels=null;ResearchAtlasWarned=false;}
        // Match Reset deliberately retains these bounded UI-lifetime caches.
        // AppendPatterns owns the generated atlas/material/texture through the
        // existing OperatorPortraitIcons full-HUD Release path. Loaded source
        // textures are destroyed immediately after their managed pixels copy.
    }
}
