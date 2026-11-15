using BBI.Unity.Game.Data;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesCarrierUnits
    {
        // Read-only access to the existing private contexts. This does not
        // instantiate, register, rebuild a datum cache or alter a native donor.
        internal static UnitAssetContainer WreckContainer(string key,bool source)
        {
            Context c=key==UnitKey?Current:key==AircraftKey?AviationContext:key==HeavyBomberKey?HeavyBomberContext:null;
            return c==null?null:source?c.Source:c.Custom;
        }
    }
}
