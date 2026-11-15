using System;
using EndfieldDoK.MapUnitLab.ModCore;

namespace EndfieldDoK.MapUnitLab
{
    internal static class OptionalPresentationServices
    {
        static readonly ServiceFaultDomain Domain=new ServiceFaultDomain(Report);
        static readonly ServiceFaultDomain.Work Hud=UpdateHud,Sensors=UpdateSensors,Camera=UpdateCamera,Selection=UpdateSelection,Audio=UpdateAudio,Reticles=UpdateReticles;
        static void Report(string service,Exception error)
        {Runtime.Log("OPTIONAL_PRESENTATION_DISABLED service="+service+"; authorityContinues=True; retry=next_match; error="+error);}
        static void UpdateHud(object main){EndfieldHudTheme.BindGameHud(main);}
        static void UpdateSensors(object main){EndfieldSensorTheme.MainTick(main);}
        static void UpdateCamera(object main){SmallUnitCamera.MainTick(main);}
        static void UpdateSelection(object unused){HumanoidSelection.MainTick();}
        static void UpdateAudio(object unused){HumanoidAttackAudio.MainTick();}
        static void UpdateReticles(object unused){RhodesOperatorReticleLifetime.MainTick();}
        internal static void TickHud(object main)
        {Domain.Run("hud",Hud,main);Domain.Run("sensors",Sensors,main);Domain.Run("camera",Camera,main);Domain.Run("operator_reticles",Reticles,null);}
        internal static void TickHumanoid()
        {Domain.Run("selection",Selection,null);Domain.Run("attack_audio",Audio,null);}
        internal static void TickSelection(){Domain.Run("selection",Selection,null);}
        internal static void TickAudio(){Domain.Run("attack_audio",Audio,null);}
        internal static void EndMatch(){Domain.EndMatch();}
    }
}
