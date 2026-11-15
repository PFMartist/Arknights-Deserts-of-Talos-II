using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Game.Data;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesCarrierUnits
    {
        static void PrepareTiltrotorBay(Context carrier,object sourceData,object hangar)
        {
            object source=null;
            foreach(object bay in (Array)Read(sourceData,"m_StorableEntities"))if((string)Read(bay,"m_EntityType")==AircraftDonorKey)source=bay;
            if(source==null)throw new InvalidOperationException("Native aircraft bay template missing for side runway");
            object own=CopyData(source,new Dictionary<object,object>(new IdentityComparer()));
            Write(own,"m_Name",RhodesTiltrotorProfile.BayName);Write(own,"m_EntityType",FactionUnits.TiltStrikeKey);
            Write(own,"m_Class",UnitClass.Air|UnitClass.Large|UnitClass.Strikecraft);Write(own,"m_UsesStrictClassMatching",true);
            Write(own,"m_MaxCount",RhodesTiltrotorProfile.Capacity);Write(own,"m_SlotCount",1);
            Write(own,"m_UndockPresCtrlBone",RhodesTiltrotorProfile.LaunchBone);Write(own,"m_DockPresCtrlBone",RhodesTiltrotorProfile.DockBone);
            Write(own,"m_UndockTotalSecondsPerSlot",RhodesTiltrotorProfile.NativeSequenceSeconds);
            Write(own,"m_UndockTotalSeconds",RhodesTiltrotorProfile.WholeBaySeconds);Write(own,"m_UndockLiftTime",RhodesTiltrotorProfile.LiftSeconds);
            Write(own,"m_UndockSlotStaggerSeconds",0f);Write(own,"m_DockSlotStaggerSeconds",0f);
            Write(own,"m_UndockSpeed",RhodesTiltrotorProfile.ReleaseSpeed);
            Write(own,"m_UndockXOffsetPos",RhodesTiltrotorProfile.RunwayX);Write(own,"m_UndockYOffsetPos",-RhodesTiltrotorProfile.ExitZ);
            Write(own,"m_UndockSlotXSeperationOffset",0f);Write(own,"m_UndockDegreesOffsetAngle",0f);
            Write(own,"m_DockReceivingXOffset",RhodesTiltrotorProfile.Capture.X);Write(own,"m_DockReceivingYOffset",-RhodesTiltrotorProfile.Capture.Z);
            Write(own,"m_DockBringInAnimationSeconds",RhodesTiltrotorProfile.BringInSeconds);
            Array old=(Array)Read(hangar,"m_StorableEntities");Array all=Array.CreateInstance(old.GetType().GetElementType(),old.Length+1);
            Array.Copy(old,all,old.Length);all.SetValue(own,old.Length);Write(hangar,"m_StorableEntities",all);
            // Keep the Medium interceptor's native docking triggers intact.
            // The Large U08 uses its rotor driver; copying jet triggers would
            // reintroduce fixed-wing fold/afterburner states on the rotorcraft.
            ConfigureV17AirBay(carrier,own,FactionUnits.TiltStrikeKey);
            VerifyTiltrotorBay(own);
            Runtime.Log("RHODES_AIRCRAFT_SPLIT nativeFighter="+AircraftKey+"; model=C_Interceptor_MP; fighterUpperDeckSlots=3; fighterCapacity=15; tilt="+FactionUnits.TiltStrikeKey+
                "; tiltSideRunwaySlots=1; tiltCapacityTrial="+RhodesTiltrotorProfile.Capacity+"; rectangularElevatorRecovery=True; independentC05Facility=True; sharedAcrossHullRoutes=True; classesDistinct=True; nativeOwnershipRetained=True; runtimeAcceptancePending=True");
        }
        static void VerifyTiltrotorBay(object bay)
        {
            if((string)Read(bay,"m_Name")!=RhodesTiltrotorProfile.BayName||(string)Read(bay,"m_EntityType")!=FactionUnits.TiltStrikeKey||
                (int)Read(bay,"m_MaxCount")!=RhodesTiltrotorProfile.Capacity||(int)Read(bay,"m_SlotCount")!=1||
                Convert.ToInt32(Read(bay,"m_Class"))!=RhodesTiltrotorProfile.AircraftClass||!(bool)Read(bay,"m_UsesStrictClassMatching")||
                (string)Read(bay,"m_UndockPresCtrlBone")!=RhodesTiltrotorProfile.LaunchBone||(string)Read(bay,"m_DockPresCtrlBone")!=RhodesTiltrotorProfile.DockBone)
                throw new InvalidOperationException("Separate tiltrotor native bay contract failed");
            if(!VerifyV17TiltrotorOffsets(bay)&&(Math.Abs((float)Read(bay,"m_UndockXOffsetPos")-RhodesTiltrotorProfile.RunwayX)>.001f||
                Math.Abs((float)Read(bay,"m_UndockYOffsetPos")+RhodesTiltrotorProfile.ExitZ)>.001f||
                Math.Abs((float)Read(bay,"m_DockReceivingXOffset")-RhodesTiltrotorProfile.Capture.X)>.001f||
                Math.Abs((float)Read(bay,"m_DockReceivingYOffset")+RhodesTiltrotorProfile.Capture.Z)>.001f))
                throw new InvalidOperationException("Tiltrotor native exit/recovery offsets differ from presentation endpoints");
        }
    }
}
