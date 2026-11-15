using System;
using BBI.Game.Data;
using BBI.Unity.Game.Data;

namespace EndfieldDoK.MapUnitLab
{
    // Private U08 trial: native planar Hover dynamics with Air classification,
    // native aircraft presentation, weapons, sortie and carrier storage kept.
    // This does not add a 3D simulation or change ground/air targetability.
    internal static class RhodesTiltrotorFlightConfig
    {
        internal const float CruiseSpeedMetresPerSecond=220f;
        internal const float CruiseAltitudeMetres=120f;
        internal const float MinimumFlightAltitudeMetres=80f,MaximumFlightAltitudeMetres=180f;

        internal static void Prepare(UnitAssetContainer own,UnitAssetContainer source)
        {
            if(own==null||source==null||own.name!=FactionUnits.TiltStrikeKey||object.ReferenceEquals(own,source))
                throw new InvalidOperationException("Tiltrotor flight tuning requires its private U08 container");
            object movement=NativeRead.Get(own,"m_Movement"),nativeMovement=NativeRead.Get(source,"m_Movement");
            if(movement==null||object.ReferenceEquals(movement,nativeMovement))
                throw new InvalidOperationException("Tiltrotor movement aliases native interceptor");
            object sim=NativeRead.Get(movement,"m_Simulation"),dynamics=NativeRead.Get(sim,"m_Dynamics"),maneuvers=NativeRead.Get(sim,"m_Maneuvers");
            NativeRead.Set(sim,"m_DriveType",UnitDriveType.Hover);
            // Native UnitMovementAttributesAsset.OnEnable synchronizes both
            // owner fields as well. Explicit values make pre-cache checks safe.
            NativeRead.Set(dynamics,"mOwnerDriveType",UnitDriveType.Hover);
            NativeRead.Set(maneuvers,"mOwnerDriveType",UnitDriveType.Hover);
            ConfigureCruise(dynamics);
            NativeRead.Set(dynamics,"m_MaxSpeedTurnRadius",70f);
            NativeRead.Set(dynamics,"m_DriftType",0f);
            NativeRead.Set(dynamics,"m_AccelerationTimeRandomVariancePercentage",0f);
            NativeRead.Set(dynamics,"m_MaxSpeedTurnRadiusRandomVariancePercentage",0f);
            NativeRead.Set(dynamics,"m_DriftTypeRandomVariancePercentage",0f);
            NativeRead.Set(dynamics,"m_DriftOvershootFactorRandomVariancePercentage",0f);
            NativeRead.Set(dynamics,"m_MinDriftSlipSpeedRandomVariancePercentage",0f);
            NativeRead.Set(maneuvers,"m_ArrivalDistance",12f);
            NativeRead.Set(maneuvers,"m_DockingArrivalDistance",12f);
            NativeRead.Set(maneuvers,"m_FormationSpacingDistance",28f);
            NativeRead.Set(maneuvers,"m_FormationSectionGapAhead",36f);
            object hover=NativeRead.Get(sim,"m_Hover");
            if(hover==null){
                // Serializable native data type is internal in this build.
                // Its parameterless ctor is pure managed defaults (audited IL).
                Type type=typeof(UnitDriveType).Assembly.GetType("BBI.Game.Data.HoverDynamicsAttributesData",true);
                hover=Activator.CreateInstance(type,true);NativeRead.Set(sim,"m_Hover",hover);
            }
            NativeRead.Set(hover,"m_TurnAcceleration",2.5f);
            NativeRead.Set(hover,"m_TurnMaxLookAheadTime",1f);
            NativeRead.Set(hover,"m_NoTurnDistance",15f);
            NativeRead.Set(hover,"m_FaceTargetInCombatOnly",false);
            NativeRead.Set(hover,"m_TurnHeuristic",HoverTurnHeuristic.Fast);
            object combat=NativeRead.Get(sim,"m_Combat");
            NativeRead.Set(combat,"m_ActiveCombatApproachMode",CombatApproachType.Hold);
            NativeRead.Set(combat,"m_PassiveCombatApproachMode",CombatApproachType.Hold);
            // Keep direct Player-created targets functional, but do not put
            // this aircraft's weapons into native idle/opportunity acquisition.
            // Explicit attack-move temporarily opts in per instance at runtime.
            foreach(object weapon in (Array)NativeRead.Get(own,"m_Weapons"))
            {
                object attributes=NativeRead.Get(weapon,"m_Simulation");
                NativeRead.Set(attributes,"m_ExcludeFromAutoTargetAcquisition",true);
                NativeRead.Set(attributes,"m_ExcludeFromAutoFire",false);
            }
            object physics=NativeRead.Get(movement,"m_ViewPhysics"),air=NativeRead.Get(physics,"Air");
            object nativeAir=NativeRead.Get(NativeRead.Get(nativeMovement,"m_ViewPhysics"),"Air");
            if(air==null||object.ReferenceEquals(air,nativeAir))
                throw new InvalidOperationException("Tiltrotor air presentation aliases native interceptor");
            NativeRead.Set(air,"HasAirUnitPhysics",true);
            NativeRead.Set(air,"DesiredAltitudeAboveGround",CruiseAltitudeMetres);
            NativeRead.Set(air,"MinAltitudeAboveGround",MinimumFlightAltitudeMetres);
            NativeRead.Set(air,"MaxAltitudeAboveGround",MaximumFlightAltitudeMetres);
            NativeRead.Set(air,"AltitudeLookAheadDistance",450f);
            NativeRead.Set(air,"AltitudeChangeFactor",1.8f);
            NativeRead.Set(air,"PitchChangeFactor",1.4f);
            NativeRead.Set(air,"MaxRoll",12f);
            NativeRead.Set(air,"RollLerpFactor",3f);
        }

        static void ConfigureCruise(object dynamics)
        {
            NativeRead.Set(dynamics,"m_MaxSpeed",CruiseSpeedMetresPerSecond);
            NativeRead.Set(dynamics,"m_MinCruiseSpeed",0f);
            NativeRead.Set(dynamics,"m_AccelerationTime",2.8f);
            NativeRead.Set(dynamics,"m_BrakingTime",3.2f);
        }

        // Call after native movement OnEnable/cache refresh. CanStop is not
        // configurable on Air drive; original getter derives it from Hover.
        internal static void VerifyConfigured(UnitAssetContainer own)
        {
            object movement=NativeRead.Get(own,"m_Movement");
            UnitMovementAttributes sim=(UnitMovementAttributes)NativeRead.Get(movement,"m_Simulation");
            if(sim.DriveType!=UnitDriveType.Hover||sim.Dynamics.DriveType!=UnitDriveType.Hover||!sim.Maneuvers.CanStop)
                throw new InvalidOperationException("U08 native hover/stop configuration was not retained");
            float cruise=Convert.ToSingle(NativeRead.Get(NativeRead.Get(sim,"m_Dynamics"),"m_MaxSpeed"));
            if(Math.Abs(cruise-CruiseSpeedMetresPerSecond)>.001f)
                throw new InvalidOperationException("U08 cruise speed differs from its private flight configuration");
            object air=NativeRead.Get(NativeRead.Get(movement,"m_ViewPhysics"),"Air");
            if(!(bool)NativeRead.Get(air,"HasAirUnitPhysics"))throw new InvalidOperationException("U08 lost native aircraft presentation");
            // Asset associations live in serialized bindings. The nested
            // m_Attributes cache exposes immutable runtime WeaponBinding data.
            object core=NativeRead.Get(own,"m_Attributes");
            Array loadout=(Array)NativeRead.Get(core,"m_WeaponLoadout");
            foreach(object weapon in (Array)NativeRead.Get(own,"m_Weapons"))
            {
                object attributes=NativeRead.Get(weapon,"m_Simulation");
                bool manualSkill=false;
                foreach(object binding in loadout)
                    if(object.ReferenceEquals(NativeRead.Get(binding,"m_Weapon"),weapon)&&
                        RhodesEmbarkedOperatorSkills.IsProxyWeaponID((string)NativeRead.Get(binding,"m_WeaponID")))manualSkill=true;
                if(!(bool)NativeRead.Get(attributes,"m_ExcludeFromAutoTargetAcquisition")||
                    (bool)NativeRead.Get(attributes,"m_ExcludeFromAutoFire")!=manualSkill)
                    throw new InvalidOperationException("U08 manual target acquisition policy lost");
            }
            RhodesTiltrotorFlightControl.ConfigureStopKey(own);
            Runtime.Log("RHODES_TILTROTOR_FLIGHT_CONFIGURED drive=Hover; classRetained=Air; nativeCanStop=True; maximumSpeedTrial="+CruiseSpeedMetresPerSecond+"; minimumCruiseSpeed=0; desiredVisualAltitudeAGL=120; minVisualAltitudeAGL=80; maxVisualAltitudeAGL=180; nativeFlightPresentationRetained=True; nativeXYSimulation=True; nativeHangarWeaponsAndSortieRetained=True; embarkedSkillWeaponsManualOnly=True; runtimeAcceptancePending=True");
        }
    }
}
