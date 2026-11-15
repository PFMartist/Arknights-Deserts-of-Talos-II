using System;
using System.Collections.Generic;
using System.Globalization;
using BBI.Core.ComponentModel;
using BBI.Game.Data;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.HUD;
using BBI.Unity.Game.UI;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Native UNIT team paint is an Overlay blend. Mid-grey is its identity;
    // it preserves the authored albedo rather than painting it Coalition orange.
    // The panel lease changes only its default input; native shared assets
    // stay intact. Local matches retain accepted native customization bytes.
    public sealed class RhodesDefaultColours : IDisposable
    {
        const string Field="m_DefaultColoursCoalition";
        readonly object Panel;
        UnitHUDInteractionAttributes.PlayerUnitColours Original,Replacement;
        bool Applied;
        NGUIEventHandler DefaultButton;
        int DefaultReports;
        static readonly HashSet<Entity> ReportedViews=new HashSet<Entity>();
        static UnitColors LastRequested,LastAccepted,LastResolved;
        static bool LastCustomization;

        public RhodesDefaultColours(object panel)
        {if(panel==null)throw new ArgumentNullException("panel");Panel=panel;}

        public static UnitHUDInteractionAttributes.PlayerUnitColours CreateAuthoredDefaults()
        {
            Color neutral=new Color(.5f,.5f,.5f,1f);
            return new UnitHUDInteractionAttributes.PlayerUnitColours(neutral,neutral);
        }
        public static UnitColors ResolveSkirmish(UnitColors requested,bool hasCustomization)
        {
            if(hasCustomization)return requested;
            // Native UnitColors stores RGBA8. This is the nearest persistable
            // neutral Overlay colour (the live preview can use exact 0.5).
            const int neutral=unchecked((int)0x808080FF);
            return new UnitColors(neutral,neutral);
        }

        public static UnitColors ResolveSkirmish(UnitColors requested,CustomizationSettings customization)
        {
            // The native default button does not clear UseCustomizationValues.
            // Accept stores its colours and sets the flag true, just like an
            // explicit palette. Use that accepted native source for the human
            // slot, even when an already-open lobby description is older.
            // No colour-value heuristic: every accepted RGBA byte is retained.
            LastRequested=requested;
            LastCustomization=customization!=null&&customization.UseCustomizationValues;
            LastAccepted=customization==null?requested:customization.UnitColors;
            LastResolved=LastCustomization
                ?customization.UnitColors:ResolveSkirmish(requested,false);
            return LastResolved;
        }

        public static string Describe(UnitColors colours)
        {return colours.Base.ToString("X8")+"/"+colours.Trim.ToString("X8");}

        public static void ResetSpawnReports(){ReportedViews.Clear();}

        public static void ReportSpawnColours(UnitView view)
        {
            if(!RhodesSkirmishFaction.IsMatchActive||view==null||ReportedViews.Count>=4||ReportedViews.Contains(view.EntityID))return;
            try
            {
            // This runs at the existing carrier bind observation. Read native
            // cached spawn/property-block state once; never change rendering.
            object spawn=NativeRead.Get(view,"mSpawnSettings");
            UnitHUDInteractionAttributes.PlayerUnitColours colours=NativeRead.Get(spawn,"CommanderColours") as UnitHUDInteractionAttributes.PlayerUnitColours;
            MaterialPropertyBlock block=NativeRead.Get(view,"mGeometryRendererPropertyBlock") as MaterialPropertyBlock;
            if(colours==null||block==null)return;
            ReportedViews.Add(view.EntityID);
            Runtime.Log("RHODES_NATIVE_SPAWN_COLOURS entity="+view.EntityID+
                "; requested="+Describe(LastRequested)+"; accepted="+Describe(LastAccepted)+
                "; useCustomization="+LastCustomization+"; resolved="+Describe(LastResolved)+
                "; spawnBase="+Describe((Vector4)colours.BaseColour)+"; spawnTrim="+Describe((Vector4)colours.TrimColour)+
                "; propertyBase="+Describe(block.GetVector("_BaseColor"))+"; propertyTrim="+Describe(block.GetVector("_TrimColor"))+
                "; readOnly=True; nativeSpawnCacheAndPropertyBlock=True");
            }
            catch(Exception error)
            {
                ReportedViews.Add(view.EntityID);
                Runtime.Log("RHODES_NATIVE_SPAWN_COLOURS_UNAVAILABLE entity="+view.EntityID+"; readOnly=True; "+error.Message);
            }
        }

        static string Describe(Vector4 colour)
        {return colour.x.ToString("F6",CultureInfo.InvariantCulture)+","+colour.y.ToString("F6",CultureInfo.InvariantCulture)+","+colour.z.ToString("F6",CultureInfo.InvariantCulture)+","+colour.w.ToString("F6",CultureInfo.InvariantCulture);}

        public void Apply()
        {
            if(Applied)return;
            UnitHUDInteractionAttributes.PlayerUnitColours original=NativeRead.Get(Panel,Field) as UnitHUDInteractionAttributes.PlayerUnitColours;
            if(original==null)throw new InvalidOperationException("Native customization default-colour instance is missing");
            UnitHUDInteractionAttributes.PlayerUnitColours replacement=CreateAuthoredDefaults();
            NativeRead.Set(Panel,Field,replacement);
            Original=original;Replacement=replacement;Applied=true;
            PlayerCustomizationPanel panel=Panel as PlayerCustomizationPanel;
            if(DefaultButton==null&&panel!=null)
            {
                UIButton button=NativeRead.Get(panel,"m_DefaultColourButton") as UIButton;
                if(button!=null)DefaultButton=button.GetComponent<NGUIEventHandler>();
                if(DefaultButton!=null)DefaultButton.ClickEvent+=AfterNativeDefaultClick;
            }
        }

        void AfterNativeDefaultClick(NGUIEventHandler handler)
        {
            if(!Applied||DefaultReports>=4)return;
            DefaultReports++;
            try
            {
            UnitColors current=(UnitColors)NativeRead.Get(Panel,"UnitColors");
            Runtime.Log("RHODES_NATIVE_DEFAULT_CLICK panelColours="+Describe(current)+
                "; selectedFaction="+NativeRead.Get(Panel,"mSelectedFactionID")+
                "; privateNeutralDefault="+object.ReferenceEquals(NativeRead.Get(Panel,Field),Replacement)+
                "; nativeDefaultMethodAlreadyRan=True; acceptStillRequired=True");
            }
            catch(Exception error){Runtime.Log("RHODES_NATIVE_DEFAULT_CLICK_UNAVAILABLE readOnly=True; "+error.Message);}
        }

        public void Restore()
        {
            if(!Applied)return;
            try
            {
                // Do not overwrite a newer native defaults generation if the
                // panel was reinitialized while this page was being closed.
                if(object.ReferenceEquals(NativeRead.Get(Panel,Field),Replacement))NativeRead.Set(Panel,Field,Original);
            }
            finally{Applied=false;Original=null;Replacement=null;}
        }

        public void Dispose()
        {
            try{Restore();}
            finally{if(DefaultButton!=null)DefaultButton.ClickEvent-=AfterNativeDefaultClick;DefaultButton=null;}
        }
    }
}
