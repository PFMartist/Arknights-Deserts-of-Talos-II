using System;
using BBI.Core;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class FactionUnits
    {
        static readonly string[] ArtilleryTacticalFields={"mHUDIconPrefab","mUnitIconName","m_EnableSensorBlip","mUnitSensorBlipName","m_IconBarMinWidthPixels","m_IconBarMaxWidthPixels","m_IconYOffset","m_UnderlayOffset","m_TacticalOverlayThresholdIndex"};
        static void PrepareArtilleryVoiceAndTactical(Context c)
        {
            UnitAssetContainer donor=FindSource("C_HAC_Upgrade01_MP");
            object audio=Read(Read(c.Custom,"m_Audio"),"m_Attributes"),sourceAudio=Read(Read(donor,"m_Audio"),"m_Attributes");
            object ownIcons=Read(Read(c.Hud,"m_Attributes"),"m_IconSettings"),sourceIcons=Read(Read(Read(donor,"m_HUD"),"m_Attributes"),"m_IconSettings");
            ConfigureArtilleryVoiceAndTacticalData(audio,ownIcons,sourceAudio,sourceIcons);
            PrepareArtillerySelectionTemplate(c,donor);
            Runtime.Log("RHODES_U09_AA_PRESENTATION_IDENTITY voiceAlias="+RhodesUnitVoiceAliases.ArtilleryVoiceToken+"; nativeVoice=armored_mib; tacticalMarker=MapIcon-Mid_alt_1; sensorAndTacticalDimensions=NativeAA; productionInfoFleetIdentityRetained=True; combatClassUnchanged=True");
        }
        // Native OnSpawned instantiates this private template per unit. Its
        // mesh/material references remain the immutable native AA resources.
        static void PrepareArtillerySelectionTemplate(Context c,UnitAssetContainer aa)
        {
            object ownHud=Read(c.Hud,"m_Attributes"),sourceHud=Read(Read(aa,"m_HUD"),"m_Attributes");
            if(object.ReferenceEquals(ownHud,sourceHud))throw new InvalidOperationException("Artillery selection HUD must be private");
            GameObject source=Read(sourceHud,"mUnderlayPrefab") as GameObject;
            if(source==null)throw new InvalidOperationException("Native AA selection underlay is missing");
            Bounds body=FactionUnitModels.BoundsFor(c.Spec.Key);GameObject clone=null;
            try
            {
                clone=(GameObject)UnityEngine.Object.Instantiate(source,Vector3.zero,Quaternion.identity);
                if(clone==null||object.ReferenceEquals(clone,source))throw new InvalidOperationException("Native AA underlay did not clone privately");
                clone.name="EF_U09_PrivateSelectionUnderlay";clone.hideFlags=HideFlags.HideAndDontSave;
                Bounds ring=new Bounds();bool known=false;
                foreach(Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
                {if(!known){ring=renderer.bounds;known=true;}else ring.Encapsulate(renderer.bounds);}
                if(!known)throw new InvalidOperationException("Native AA underlay has no renderer bounds");
                float diameter=Math.Max(ring.size.x,ring.size.z),scale=ArtillerySelectionScale(body.size.x,body.size.z,diameter);
                clone.transform.localScale=clone.transform.localScale*scale;clone.SetActive(false);UnityEngine.Object.DontDestroyOnLoad(clone);
                Write(ownHud,"mUnderlayPrefab",clone);c.OwnedPresentation.Add(clone);
                Runtime.Log("RHODES_U09_SELECTION_UNDERLAY privateNativeAATemplate=True; sourceDiameter="+diameter+"; desiredDiameter="+(Math.Max(body.size.x,body.size.z)*1.08f)+"; scale="+scale+"; sharedNativeMeshesMaterialsUnchanged=True; nativePerUnitInstantiation=True; hoverPaddingUnchanged=True; actualVisualAcceptancePending=True");
            }
            catch{if(clone!=null&&!object.ReferenceEquals(clone,source))UnityEngine.Object.DestroyImmediate(clone);throw;}
        }
        internal static float ArtillerySelectionScale(float width,float length,float sourceDiameter)
        {
            if(float.IsNaN(width)||float.IsInfinity(width)||width<=0f||float.IsNaN(length)||float.IsInfinity(length)||length<=0f||
                float.IsNaN(sourceDiameter)||float.IsInfinity(sourceDiameter)||sourceDiameter<=0f)
                throw new ArgumentOutOfRangeException("selection bounds","Finite positive unit and native selection dimensions required");
            float scale=Math.Max(width,length)*1.08f/sourceDiameter;
            if(float.IsNaN(scale)||float.IsInfinity(scale)||scale<=0f)throw new ArgumentOutOfRangeException("selection scale");
            return scale;
        }
        static void ConfigureArtilleryVoiceAndTacticalData(object audio,object icons,object aaAudio,object aaIcons)
        {
            object source=Read(aaAudio,"AudioHookups"),own=Read(audio,"AudioHookups");
            if((string)Read(source,"UnitCode")!="mib")throw new InvalidOperationException("Native AA voice donor changed");
            // A unique private token permits exact event-time aliasing for both
            // unit speech and non-positional OpsFleet completion messages.
            Write(own,"UnitCode",RhodesUnitVoiceAliases.ArtilleryVoiceToken);
            Write(own,"TargetSetClips",CopyData(Read(source,"TargetSetClips"),new System.Collections.Generic.Dictionary<object,object>(new IdentityComparer())));
            Write(audio,"AudioHookups",own);
            foreach(string field in ArtilleryTacticalFields)Write(icons,field,Read(aaIcons,field));
        }
        static bool KeepHarvesterAbility(string name,object attributes,object view)
        {
            if(name=="Ability_Common_DemoChargeLay_MP")
            {
                if((string)Read(Read(attributes,"m_UseWeapon"),"m_WeaponID")!="DEMOCHARGE"||(KeyCode)Read(view,"Hotkey")!=KeyCode.X)
                    throw new InvalidOperationException("Harvester native X demolition ability changed");
                return true;
            }
            if(name!="Ability_Common_DemoChargeLay_AI")return true;
            if((string)Read(Read(attributes,"m_UseWeapon"),"m_WeaponID")!="DEMOCHARGE_AI"||(KeyCode)Read(view,"Hotkey")!=KeyCode.Backspace)
                throw new InvalidOperationException("Harvester duplicate AI demolition donor changed");
            return false;
        }
        static void VerifyHarvesterCleanup(Context c)
        {
            int retained=0;
            foreach(ScriptableObject ability in (Array)Read(c.Custom,"m_Abilities"))
            {
                if(ability.name=="Ability_Common_DemoChargeLay_AI")throw new InvalidOperationException("Harvester AI demolition command remains exposed");
                if(ability.name=="Ability_Common_DemoChargeLay_MP")retained++;
            }
            if(retained!=1)throw new InvalidOperationException("Harvester must retain exactly one native X demolition command");
        }
    }
}
