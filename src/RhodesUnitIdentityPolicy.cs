using System;
using BBI.Core.ComponentModel;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;

namespace EndfieldDoK.MapUnitLab
{
    // Presentation only. Native veterancy still owns XP, ranks, buffs and its
    // name-pool/RNG state; do not expose the donor crew name in a custom unit.
    public static class RhodesUnitIdentityPolicy
    {
        static bool TooltipWarned;
        internal static bool TryShortDescription(string typeKey,string hudDescription,out string description)
        {
            description=null;
            OperatorUiIdentity identity;
            if(OperatorUiIdentity.TryGet(typeKey,out identity))
            {description=identity.EnglishName;return true;}
            if(typeKey==HumanoidUnits.PerlicaKey)
            {description="Perlica";return true;}
            if(!FactionUnits.IsFactionKey(typeKey)&&typeKey!=RhodesCarrierUnits.UnitKey&&
                typeKey!=RhodesCarrierUnits.AircraftKey&&typeKey!=RhodesCarrierUnits.HeavyBomberKey&&
                typeKey!=HumanoidUnits.GuardSquadKey)return false;
            if(string.IsNullOrEmpty(hudDescription))return false;
            // The private HUD already contains the current equipment's role.
            // Reuse it instead of inventing a second unit-name/text registry.
            description=hudDescription;return true;
        }
        // The patch calls this only while assigning the completed tooltip's
        // Name field. Rank names, XP totals and buff descriptions stay native.
        public static string ResolveNativeTooltipName(string original,Entity entity)
        {
            try
            {
                var frame=ShipbreakersMain.CurrentSimFrame;
                UnitState state=frame==null?null:frame.FindObject<UnitState>(entity);
                if(state==null||!state.EntityID.Equals(entity))return original;
                UnitHUDAttributes hud=ShipbreakersMain.GetEntityTypeAttributes<UnitHUDAttributes>(state.TypeID);
                string name;
                return TryShortDescription(state.TypeID,hud==null?null:hud.UnitLocalizedNameStringID,out name)?name:original;
            }
            catch(Exception error)
            {
                if(!TooltipWarned){TooltipWarned=true;Runtime.Log("WARNING RHODES_UNIT_TOOLTIP_NAME "+error.GetType().Name+": "+error.Message+"; nativeTooltipContinues=True");}
                return original;
            }
        }
    }
}
