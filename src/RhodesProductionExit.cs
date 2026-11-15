using System;
using System.Reflection;
using BBI.Core.Data;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // External ground spawn only. Native code owns production, release,
    // population, initial formation and rally goals. No added Update or
    // simulation-position writes, no ramp or interior movement animation.
    public static class RhodesProductionExit
    {
        public const string BayName="EF_Rhodes_GroundBay",ControlBone="EF_Rhodes_GroundExit_1";
        public const int SlotCount=1;
        public const float SequenceSeconds=1f,UndockSpeed=3f;
        public sealed class SpawnPoint {public float Right,Back;}
        public static SpawnPoint CreatePoint(float length,float[] hatch)
        {
            if(float.IsNaN(length)||float.IsInfinity(length)||length<100f||length>1000f||hatch==null||hatch.Length!=3)
                throw new ArgumentException("Invalid private carrier ground spawn input");
            foreach(float value in hatch)if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentException("Invalid ground hatch coordinate");
            if(hatch[2]>=0f||Math.Abs(hatch[0])>length*.5f)throw new ArgumentException("Ground hatch must be on the rear of the carrier");
            // Clear the native hull footprint while remaining centred on the
            // authored hatch. The original 400m/parallel class exits are gone.
            return new SpawnPoint{Right=hatch[0],Back=Math.Max(length*.5f+38f,-hatch[2]+45f)};
        }
        static RhodesCarrierBinding CarrierBinding(UnitAssetContainer custom)
        {
            UnitViewAttributes view=NativeRead.Get(NativeRead.Get(custom,"m_View"),"Data") as UnitViewAttributes;
            RhodesCarrierBinding binding=view==null||view.ModelPrefab==null?null:view.ModelPrefab.GetComponent<RhodesCarrierBinding>();
            if(binding==null||binding.GeometryRoot==null||!binding.GeometryRoot.IsChildOf(view.ModelPrefab.transform))
                throw new InvalidOperationException("Ground spawning requires the private carrier wrapper");
            return binding;
        }
        static SpawnPoint Point(RhodesCarrierBinding binding,float length)
        {
            Transform hatch=binding.FindNode("EXIT_GroundVehicle");
            if(hatch==null||!hatch.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("Ground hatch marker missing");
            Vector3 p=binding.GeometryRoot.InverseTransformPoint(hatch.position);return CreatePoint(length,new float[]{p.x,p.y,p.z});
        }
        static FieldInfo Field(object owner,string name)
        {
            FieldInfo field=owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(field==null)throw new MissingFieldException(owner.GetType().FullName,name);return field;
        }
        public static void Prepare(UnitAssetContainer custom,float lengthMetres)
        {
            if(custom==null)throw new ArgumentNullException("custom");
            object asset=NativeRead.Get(custom,"m_UnitHangar");
            if(asset==null)throw new InvalidOperationException("Private carrier needs its native hangar asset");
            object data=NativeRead.Get(asset,"m_Attributes");RhodesCarrierBinding binding=CarrierBinding(custom);SpawnPoint point=Point(binding,lengthMetres);
            if(binding.GeometryRoot.Find(ControlBone)!=null)throw new InvalidOperationException("Duplicate private ground spawn anchor");
            GameObject marker=new GameObject(ControlBone);marker.layer=binding.GeometryRoot.gameObject.layer;marker.transform.parent=binding.GeometryRoot;
            marker.transform.localPosition=new Vector3(point.Right,0f,-point.Back);marker.transform.localRotation=Quaternion.Euler(0f,180f,0f);marker.transform.localScale=Vector3.one;
            Type bayType=Field(data,"m_StorableEntities").FieldType.GetElementType();Array bays=Array.CreateInstance(bayType,1);bays.SetValue(Bay(bayType,point),0);
            NativeRead.Set(data,"m_StorableEntities",bays);
            foreach(string name in new string[]{"m_UnitDockingTriggers","m_HangarDockingTriggers","m_BoneResetTriggers"})
                NativeRead.Set(data,name,Array.CreateInstance(Field(data,name).FieldType.GetElementType(),0));
            NativeRead.Set(data,"m_AlignmentTime",.1f);NativeRead.Set(data,"m_ApproachTime",1f);
            Verify(custom,lengthMetres);
            Runtime.Log("RHODES_PRODUCTION_EXITS nativeGroundBay=1; slots=1; externalGroundSpawn=True; centredOnRearHatch=True; nativeRight="+point.Right+"; nativeBack="+point.Back+
                "; movingGuides=False; rampAnimation=False; initialFleetNativeDeploy=True; nativeRally=True; simulationPositionWrites=0; runtimeVisualAcceptancePending=True");
        }
        static object Bay(Type bayType,SpawnPoint point)
        {
            object bay=Activator.CreateInstance(bayType,true);
            NativeRead.Set(bay,"m_Name",BayName);NativeRead.Set(bay,"m_EntityType",string.Empty);
            NativeRead.Set(bay,"m_UsesStrictClassMatching",false);NativeRead.Set(bay,"m_Class",UnitClass.Ground);
            NativeRead.Set(bay,"m_MaxCount",128);NativeRead.Set(bay,"m_SlotCount",SlotCount);
            // Nonempty control keeps pending native rally goals suspended until
            // the queue's final ResetPosition, which otherwise loses the path.
            NativeRead.Set(bay,"m_UndockPresCtrlBone",ControlBone);NativeRead.Set(bay,"m_DockPresCtrlBone",string.Empty);
            NativeRead.Set(bay,"m_UndockXOffsetPos",point.Right);NativeRead.Set(bay,"m_UndockYOffsetPos",point.Back);
            NativeRead.Set(bay,"m_UndockSlotXSeperationOffset",0f);NativeRead.Set(bay,"m_UndockDegreesOffsetAngle",180f);
            NativeRead.Set(bay,"m_UndockTotalSeconds",SequenceSeconds);NativeRead.Set(bay,"m_UndockTotalSecondsPerSlot",SequenceSeconds);
            NativeRead.Set(bay,"m_UndockSlotStaggerSeconds",0f);NativeRead.Set(bay,"m_UndockSpeed",UndockSpeed);NativeRead.Set(bay,"m_UndockLiftTime",0f);
            NativeRead.Set(bay,"m_DockBringInAnimationSeconds",0f);NativeRead.Set(bay,"m_DockSlotStaggerSeconds",0f);
            NativeRead.Set(bay,"m_MinDockCoolingSeconds",0f);NativeRead.Set(bay,"m_MaxDamageCoolingSeconds",0f);NativeRead.Set(bay,"m_MaxPayloadCoolingSeconds",0f);
            NativeRead.Set(bay,"m_DockReceivingXOffset",point.Right);NativeRead.Set(bay,"m_DockReceivingYOffset",point.Back);
            NativeRead.Set(bay,"m_DoorAnimationSeconds",0f);return bay;
        }
        // Existing native event prefix, once per spawn. Rebase the private
        // anchor to native yaw/position and Terrain; hull pitch cannot place
        // the newborn vehicle in midair. No unit Transform is written here.
        public static void OnDockingUnitOperation(UnitView view,DockingUnitOperationEvent evt)
        {
            if(view==null||evt==null||(int)evt.Action!=0||evt.BoneName!=ControlBone||evt.DockSlotIndex!=0||!view.EntityID.Equals(evt.DockingEntity))return;
            UnitView carrier;if(!UnitViewDiscovery.TryGet(evt.HangarOwner,out carrier)||carrier==null||carrier.NonInterpolatedTickState==null)return;
            UnitState state=carrier.NonInterpolatedTickState;if(state.TypeID!=RhodesCarrierUnits.UnitKey)return;
            RhodesCarrierBinding binding=carrier.GetComponent<RhodesCarrierBinding>();if(binding==null||binding.GeometryRoot==null)return;
            Transform anchor=binding.GeometryRoot.Find(ControlBone);if(anchor==null)return;
            SpawnPoint point=Point(binding,RhodesCarrierUnits.LengthMetres);
            Vector2r spawn=state.StartOfFrameDynamicState.Position+state.StartOfFrameDynamicState.Orientation.Right*Fixed64.UnsafeFromFloat(point.Right)+
                state.StartOfFrameDynamicState.Orientation.Back*Fixed64.UnsafeFromFloat(point.Back);
            Vector3 position=VectorHelper.SimVector2ToUnityVector3(spawn,carrier.transform.position.y),normal=Vector3.up;RaycastHit hit;
            if(UnitView.RaycastToGroundFromPoint(position,out hit)){position.y=hit.point.y;normal=hit.normal;}
            Vector3 forward=VectorHelper.SimVector2ToUnityVector3(state.StartOfFrameDynamicState.Orientation.Back,0f);
            anchor.position=position;anchor.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(forward,normal),normal);
        }
        public static void Verify(UnitAssetContainer custom,float lengthMetres)
        {
            object data=NativeRead.Get(NativeRead.Get(custom,"m_UnitHangar"),"m_Attributes");Array bays=NativeRead.Get(data,"m_StorableEntities") as Array;
            RhodesCarrierBinding binding=CarrierBinding(custom);SpawnPoint point=Point(binding,lengthMetres);object ground=null;
            if(bays==null)throw new InvalidOperationException("Native ground spawn bay array absent");
            foreach(object bay in bays)if((string)NativeRead.Get(bay,"m_EntityType")==string.Empty)
            {if(ground!=null)throw new InvalidOperationException("More than one native ground spawn queue");ground=bay;}
            if(ground==null||(string)NativeRead.Get(ground,"m_Name")!=BayName||(UnitClass)NativeRead.Get(ground,"m_Class")!=UnitClass.Ground||
                (bool)NativeRead.Get(ground,"m_UsesStrictClassMatching")||(int)NativeRead.Get(ground,"m_SlotCount")!=SlotCount||
                (string)NativeRead.Get(ground,"m_UndockPresCtrlBone")!=ControlBone||
                Math.Abs(Convert.ToSingle(NativeRead.Get(ground,"m_UndockYOffsetPos"))-point.Back)>.001f||
                Math.Abs(Convert.ToSingle(NativeRead.Get(ground,"m_UndockXOffsetPos"))-point.Right)>.001f||
                Math.Abs(Convert.ToSingle(NativeRead.Get(ground,"m_UndockTotalSecondsPerSlot"))-SequenceSeconds)>.001f)
                throw new InvalidOperationException("Native ground spawn configuration differs from rear hatch");
            EntityTypeCollection registry=(EntityTypeCollection)NativeRead.Get(typeof(ShipbreakersMain),"sEntityTypes");
            foreach(FactionUnits.UnitSpec spec in FactionUnits.Specs)
            {
                EntityTypeAttributes definition=registry.GetEntityType(spec.Key);UnitAttributes unit=definition==null?null:definition.Get<UnitAttributes>();
                if(unit==null)throw new InvalidOperationException("Private ground spawn product absent: "+spec.Key);
                bool groundClass=(unit.Class&UnitClass.Ground)==UnitClass.Ground;
                if(groundClass==(spec.Key==FactionUnits.TiltStrikeKey)||groundClass&&!unit.StartsInHangar)
                    throw new InvalidOperationException("Private ground hangar policy differs: "+spec.Key);
            }
            Transform control=binding.GeometryRoot.Find(ControlBone);
            if(control==null||(control.localPosition-new Vector3(point.Right,0f,-point.Back)).sqrMagnitude>.0001f)
                throw new InvalidOperationException("Private ground spawn anchor differs from native offsets");
        }
    }
}
