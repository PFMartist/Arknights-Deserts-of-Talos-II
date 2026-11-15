using System;
using EndfieldDoK.MapUnitLab.ModCore;

namespace EndfieldDoK.MapUnitLab
{
    // Called only behind the existing native-shutdown and SimGate barrier.
    // Registry templates are deliberately absent: native definitions keep their
    // leases until a real registry-generation replacement releases each factory.
    internal static class MatchRuntimeLifetime
    {
        static readonly ServiceReleaseBatch Match=new ServiceReleaseBatch("match",new ServiceReleaseBatch.Entry[]{
            new ServiceReleaseBatch.Entry("power_ui",RhodesPowerUI.EndSession),
            new ServiceReleaseBatch.Entry("flight",RhodesTiltrotorFlightControl.Reset),
            new ServiceReleaseBatch.Entry("operator_transport",RhodesOperatorTransport.Reset),
            new ServiceReleaseBatch.Entry("concealment",RhodesTiltrotorConcealment.Reset),
            new ServiceReleaseBatch.Entry("laevatain",LaevatainFlameBridge.Reset),
            new ServiceReleaseBatch.Entry("gilberta",GilbertaGravityBridge.Reset),
            new ServiceReleaseBatch.Entry("ardelia",ArdeliaHealingBridge.Reset),
            new ServiceReleaseBatch.Entry("research",RhodesResearch.Reset),
            new ServiceReleaseBatch.Entry("deployment",RhodesDeploymentState.Reset),
            new ServiceReleaseBatch.Entry("power_gate",RhodesPowerGate.Reset),
            new ServiceReleaseBatch.Entry("fire_gate",HumanoidFireGate.Reset),
            new ServiceReleaseBatch.Entry("orbital",PerlicaOrbitalBridge.Reset),
            new ServiceReleaseBatch.Entry("casualties",HumanoidCasualtyProbe.Reset),
            new ServiceReleaseBatch.Entry("weapon_events",HumanoidWeaponEventBridge.UnbindAll),
            new ServiceReleaseBatch.Entry("corpses",HumanoidCorpse.ResetAll),
            new ServiceReleaseBatch.Entry("vehicle_wreck_pool",RhodesVehicleWrecks.EndMatch),
            new ServiceReleaseBatch.Entry("attack_audio",HumanoidAttackAudio.ResetAll),
            new ServiceReleaseBatch.Entry("unit_voice",HumanoidUnitVoice.ResetAll),
            new ServiceReleaseBatch.Entry("rifle_fx",HumanoidRifleFireFx.ResetAll),
            new ServiceReleaseBatch.Entry("hud_icons",RhodesHudIcon.Release),
            new ServiceReleaseBatch.Entry("mode_voice",RhodesModeVoice.Reset),
            new ServiceReleaseBatch.Entry("sensors",EndfieldSensorTheme.Release),
            new ServiceReleaseBatch.Entry("camera",SmallUnitCamera.Reset),
            new ServiceReleaseBatch.Entry("operator_reticles",RhodesOperatorReticleLifetime.ResetSession),
            new ServiceReleaseBatch.Entry("optional_faults",OptionalPresentationServices.EndMatch)
        });
        static readonly ServiceReleaseBatch Views=new ServiceReleaseBatch("view",new ServiceReleaseBatch.Entry[]{
            new ServiceReleaseBatch.Entry("carrier_views",RhodesCarrierUnits.Reset),
            new ServiceReleaseBatch.Entry("humanoid_selection",HumanoidSelection.Reset),
            new ServiceReleaseBatch.Entry("humanoid_playback",HumanoidModel.Reset),
            new ServiceReleaseBatch.Entry("round_events",BvmNativeState.Unbind),
            new ServiceReleaseBatch.Entry("view_registry",UnitViewDiscovery.Reset)
        });
        static readonly ServiceReleaseBatch.Failure Reporter=Report;
        static readonly ServiceReleaseBatch Retirement=new ServiceReleaseBatch("retirement",new ServiceReleaseBatch.Entry[]{
            new ServiceReleaseBatch.Entry("match",EndMatch),new ServiceReleaseBatch.Entry("views",ReleaseViews)});
        static void Report(string scope,string service,Exception error)
        {Runtime.Log("LIFETIME_RELEASE_FAILED scope="+scope+"; service="+service+"; remainingServicesStillReleased=True; error="+error);}
        internal static void EndMatch(){Match.Run(Reporter);}
        internal static void ReleaseViews(){Views.Run(Reporter);}
        internal static void RetireAll(){Retirement.Run(Reporter);}
    }
}
