using System;

namespace EndfieldDoK.MapUnitLab
{
    // Pure read-only choice; the native deployment state is authoritative.
    public static class RhodesVoicePolicy
    {
        public static bool UseSobanCarrier(RhodesDeploymentSnapshot state)
        {
            // SourceLevel commits at the END of first deployment. During F9
            // return it remains the outgoing form until the base-state commit.
            return state.Branch==2 && state.SourceLevel>=1 && state.SourceLevel<=2
                && state.Kind>=0 && state.Kind<=4;
        }
    }
}
