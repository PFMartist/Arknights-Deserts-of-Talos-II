namespace EndfieldDoK.MapUnitLab
{
    // Compatibility surface for the HUD and exact native command guard.
    // Allocation has no mod window/cooldown. Native per-level charging and
    // reserve ownership remain authoritative in PowerShuntProcessor.
    public sealed class RhodesPowerTiming
    {
        public const uint AdjustmentFrames=0,HoldFrames=0;
        public bool Accept(uint frame,bool changesTarget){return true;}
        public void Remaining(uint frame,out float adjustment,out float locked)
        {adjustment=locked=0f;}
    }
}
