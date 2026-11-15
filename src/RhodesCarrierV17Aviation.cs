using System;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesCarrierUnits
    {
        // Asset-generation capability cached before simulation starts. Native
        // simulation gates must not query Unity model components themselves.
        internal static bool V17AviationConfigured;
        static bool HasV17Hull()
        {
            if(Current==null||Current.View==null)return false;
            UnitViewAttributes view=(UnitViewAttributes)Read(Current.View,"Data");
            return view!=null&&view.ModelPrefab!=null&&RhodesV17AviationGuideDriver.IsV17(view.ModelPrefab.GetComponent<RhodesCarrierBinding>());
        }
        static AviationRunway GetV17Runway()
        {
            RhodesV17AviationData data=RhodesV17AviationData.Load();
            V17AirPose a=data.Paths["F01_Launch"].Sample(14f),b=data.Paths["F02_Launch"].Sample(14f),c=data.Paths["F03_Launch"].Sample(14f);
            V17AirPose dock=data.Paths["F01_Recovery"].Sample(5f);float separation=b.Position.x-a.Position.x;
            if(Math.Abs(c.Position.x-b.Position.x-separation)>.05f||Math.Abs(b.Position.z-a.Position.z)>.05f||Math.Abs(c.Position.z-a.Position.z)>.05f)
                throw new InvalidOperationException("V17 fighter lanes do not fit the native equal-offset bay");
            return new AviationRunway{Right=a.Position.x,Back=-a.Position.z,Separation=separation,Degrees=-a.Rotation.eulerAngles.y,
                ReceivingRight=dock.Position.x,ReceivingBack=-dock.Position.z,UndockBone="EF_AV_UndockControl_0$Slot$",DockBone="EF_AV_DockControl_0$Slot$"};
        }
        static void ConfigureV17AirBay(Context carrier,object bay,string key)
        {
            if(!HasV17Hull())return;
            if(key!=FactionUnits.TiltStrikeKey&&key!=HeavyBomberKey)return;
            string name=key==HeavyBomberKey?"B01":"U08";RhodesV17AviationData data=RhodesV17AviationData.Load();
            V17AirPose exit=data.Paths[name+"_Launch"].Sample(14f),capture=data.Paths[name+"_Recovery"].Sample(5f);
            Write(bay,"m_UndockXOffsetPos",exit.Position.x);Write(bay,"m_UndockYOffsetPos",-exit.Position.z);
            Write(bay,"m_DockReceivingXOffset",capture.Position.x);Write(bay,"m_DockReceivingYOffset",-capture.Position.z);
            Write(bay,"m_UndockDegreesOffsetAngle",-exit.Rotation.eulerAngles.y);Write(bay,"m_UndockSlotXSeperationOffset",0f);
            Write(bay,"m_UndockTotalSecondsPerSlot",RhodesTiltrotorProfile.NativeSequenceSeconds);
            Write(bay,"m_UndockTotalSeconds",RhodesTiltrotorProfile.WholeBaySeconds);Write(bay,"m_UndockLiftTime",.125f);
            Write(bay,"m_DockBringInAnimationSeconds",RhodesBomberRecoveryProfile.BringInSeconds(key==HeavyBomberKey));
            // The fixed side runway has no vertical departure elevator.
            // B01 keeps its native 300m/s release speed; U08 keeps its90m/s.
            Write(bay,"m_UndockSpeed",key==HeavyBomberKey?300f:RhodesTiltrotorProfile.ReleaseSpeed);
        }
        static void ConfigureV17AirBays(Context carrier)
        {
            V17AviationConfigured=false;
            if(!HasV17Hull())return;
            Array bays=(Array)Read(Read(Read(carrier.Custom,"m_UnitHangar"),"m_Attributes"),"m_StorableEntities");
            foreach(object bay in bays)ConfigureV17AirBay(carrier,bay,(string)Read(bay,"m_EntityType"));
            V17AviationConfigured=true;
            Runtime.Log("RHODES_V17_NATIVE_BAYS_BOUND fighterSlots=3; bomberSlots=1; rotorSlots=1; sideLaunchLift=False; exactAuthorPathEndpoints=True; bomberBringInSeconds="+RhodesBomberRecoveryProfile.BomberBringInSeconds+
                "; bomberQueueTailGuardSeconds="+RhodesBomberRecoveryProfile.BomberResetSeconds+"; otherQueueTailGuardSeconds="+RhodesBomberRecoveryProfile.StandardResetSeconds+"; nativeApproachSeconds="+RhodesBomberRecoveryProfile.TotalApproachSeconds+"; gameAcceptancePending=True");
        }
        static bool VerifyV17TiltrotorOffsets(object bay)
        {
            if(!HasV17Hull())return false;RhodesV17AviationData data=RhodesV17AviationData.Load();
            V17AirPose exit=data.Paths["U08_Launch"].Sample(14f),capture=data.Paths["U08_Recovery"].Sample(5f);
            if(Math.Abs((float)Read(bay,"m_UndockXOffsetPos")-exit.Position.x)>.001f||Math.Abs((float)Read(bay,"m_UndockYOffsetPos")+exit.Position.z)>.001f||
                Math.Abs((float)Read(bay,"m_DockReceivingXOffset")-capture.Position.x)>.001f||Math.Abs((float)Read(bay,"m_DockReceivingYOffset")+capture.Position.z)>.001f)
                throw new InvalidOperationException("V17 tiltrotor native endpoints differ from source-bound guides");
            return true;
        }
    }
}
