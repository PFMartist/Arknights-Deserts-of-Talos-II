using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BBI.Core.ComponentModel;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Rhodes-only UnitView cache correction. Native UpdateTurrets compares its
    // reference with a yaw-parent-local direction, but the TurretData constructor
    // can store an initial WORLD direction there. Do not replace native aiming.
    // This helper has no Update, hooks, scene scans or simulation writes.
    public sealed class RhodesTurretArcReference
    {
        const int Stations=6,MaximumAttempts=3,MaximumEntityPoseRows=14;
        const int MaximumProcessPoseRows=56,MaximumProcessReferenceRows=24;
        static readonly object ProcessGate=new object();
        static int ProcessPoseRows,ProcessReferenceRows;
        static readonly string[] IDs={"EF_RHODES_MAIN_FORE","EF_RHODES_MAIN_AFT",
            "EF_RHODES_CIWS_PF","EF_RHODES_CIWS_PA","EF_RHODES_CIWS_SF","EF_RHODES_CIWS_SA"};
        static readonly string[] YawNames={"V03_PIVOT_GN_Yaw_FORE","V03_PIVOT_GN_Yaw_AFT",
            "PIVOT_CIWS_PortFore_Yaw","PIVOT_CIWS_PortAft_Yaw","PIVOT_CIWS_StarboardFore_Yaw","PIVOT_CIWS_StarboardAft_Yaw"};
        static readonly string[] PitchNames={"V03_PIVOT_GN_Pitch_FORE_LEFT","V03_PIVOT_GN_Pitch_AFT_LEFT",
            "PIVOT_CIWS_PortFore_Pitch","PIVOT_CIWS_PortAft_Pitch","PIVOT_CIWS_StarboardFore_Pitch","PIVOT_CIWS_StarboardAft_Pitch"};
        static readonly string[] ParentNames={"V03_MOUNT_GN_Main_FORE","V03_MOUNT_GN_Main_AFT",
            "MOUNT_CIWS_PortFore","MOUNT_CIWS_PortAft","MOUNT_CIWS_StarboardFore","MOUNT_CIWS_StarboardAft"};
        static readonly string[] MuzzleNames={"V03_HP_GN_Muzzle_FORE_LEFT","V03_HP_GN_Muzzle_AFT_LEFT",
            "HP_CIWS_PortFore_Muzzle","HP_CIWS_PortAft_Muzzle","HP_CIWS_StarboardFore_Muzzle","HP_CIWS_StarboardAft_Muzzle"};
        readonly object Gate=new object();
        readonly int[] BindingIndices=new int[Stations],TurretIndices=new int[Stations];
        readonly Transform[] Yaws=new Transform[Stations],Pitches=new Transform[Stations],Muzzles=new Transform[Stations];
        UnitView View;
        RhodesCarrierBinding Model;
        Entity Entity;
        Array CorrectedCache;
        bool HasEntity,InitialCaptured;
        volatile bool Bound,ReferenceReady,PoseStopped;
        int Attempts,LastAttemptTick=int.MinValue,LastEnableTick=int.MinValue;
        int AcceptedRoundMask,PendingRoundMask,MainEnableMask,PoseRows,ReferenceRows;

        public void Initialize(UnitView view)
        {
            if(view==null)return;
            Entity current=view.EntityID;
            if(!HasEntity||!Entity.Equals(current))
            {
                Entity=current;HasEntity=true;CorrectedCache=null;
                InitialCaptured=false;PoseStopped=false;AcceptedRoundMask=0;PendingRoundMask=0;
                MainEnableMask=0;PoseRows=0;ReferenceRows=0;
            }
            View=view;Model=view.GetComponentInChildren<RhodesCarrierBinding>();
            Bound=true;ReferenceReady=false;Attempts=0;LastAttemptTick=int.MinValue;LastEnableTick=int.MinValue;
            // A real host binding is the only cache recreation check. After a
            // successful bind, Step never reflects mTurretData just to poll it.
        }

        public void Release()
        {
            lock(Gate){Bound=false;View=null;Model=null;PendingRoundMask=0;}
            // Preserve this entity's diagnostics budget across a view rebind.
        }
        // R19 transport parents change their frame during initial/aft deploy.
        // Called only at each station-ready edge by the existing host, never
        // as a continuous mTurretData identity/reflection poll.
        internal void RebindAfterTransport()
        {
            if(!Bound)return;
            CorrectedCache=null;ReferenceReady=false;Attempts=0;LastAttemptTick=int.MinValue;
        }

        // Called by the existing exact-entity native round handler. Unity reads
        // happen on the next presentation Step, never on the event thread.
        public void NoteRound(int weaponBindingIndex)
        {
            lock(Gate)
            {
                if(!Bound||!ReferenceReady||PoseStopped)return;
                for(int station=0;station<Stations;station++)
                {
                    int bit=1<<station;
                    if(BindingIndices[station]!=weaponBindingIndex||(AcceptedRoundMask&bit)!=0)continue;
                    AcceptedRoundMask|=bit;PendingRoundMask|=bit;return;
                }
            }
        }

        public void Step(float deltaTime)
        {
            if(!Bound||View==null||!Entity.Equals(View.EntityID))return;
            int tick=ShipbreakersMain.CurrentSimTick;
            if(!ReferenceReady&&Attempts<MaximumAttempts&&tick!=LastAttemptTick)
            {
                LastAttemptTick=tick;Attempts++;
                try{NormalizeCache();ReferenceReady=true;}
                catch(Exception error)
                {
                    if(Attempts==MaximumAttempts)
                        LogReference("stoppedAfterThreeTicks",error.GetType().Name+":"+Clean(error.Message));
                }
            }
            if(!ReferenceReady||PoseStopped)return;
            lock(ProcessGate)if(ProcessPoseRows>=MaximumProcessPoseRows){PoseStopped=true;return;}
            int rounds;lock(Gate){rounds=PendingRoundMask;}
            if(InitialCaptured&&rounds==0&&(MainEnableMask==3||tick==LastEnableTick))return;
            UnitState state=View.NonInterpolatedTickState;
            if(!ValidOwner(state))return;
            lock(Gate){rounds=PendingRoundMask;PendingRoundMask=0;}
            try
            {
                if(!InitialCaptured)
                {
                    InitialCaptured=true;
                    Capture(state,tick,deltaTime,63,"firstBoundAfterReferenceCorrection");
                }
                if(tick!=LastEnableTick)
                {
                    LastEnableTick=tick;
                    for(int main=0;main<2;main++)
                    {
                        int bit=1<<main,index=BindingIndices[main];
                        if((MainEnableMask&bit)!=0||index<0||index>=state.AllWeapons.Length)continue;
                        WeaponState weapon=state.AllWeapons[index];
                        if(weapon==null||!weapon.Enabled)continue;
                        MainEnableMask|=bit;Capture(state,tick,deltaTime,bit,"firstMainEnabled");
                    }
                }
                if(rounds!=0)Capture(state,tick,deltaTime,rounds,"firstNativeRound");
                if(PoseRows>=MaximumEntityPoseRows||(MainEnableMask==3&&AcceptedRoundMask==63))PoseStopped=true;
            }
            catch(Exception error)
            {
                PoseStopped=true;
                LogReference("boundedPoseReaderStopped",error.GetType().Name+":"+Clean(error.Message));
            }
        }

        bool ValidOwner(UnitState state)
        {
            return state!=null&&!state.UnitIsDead&&state.EntityID.Equals(Entity)&&
                state.TypeID==RhodesCarrierUnits.UnitKey&&state.OwnerCommander.Equals(View.CommanderID)&&
                state.AllWeapons!=null&&View.UnitAttrs!=null&&View.UnitAttrs.WeaponLoadout!=null;
        }

        void NormalizeCache()
        {
            if(!ValidOwner(View.NonInterpolatedTickState)||Model==null||Model.GeometryRoot==null)
                throw new InvalidOperationException("Rhodes owner/geometry not ready");
            Array cache=NativeRead.Get(View,"mTurretData") as Array;
            if(cache==null||cache.Rank!=1)throw new InvalidOperationException("Native turret cache not ready");
            // The same cache can survive OnDisable/Initialize. Verify identity
            // only at that bind, then avoid both rewrites and steady polling.
            if(object.ReferenceEquals(cache,CorrectedCache))return;
            WeaponBinding[] loadout=View.UnitAttrs.WeaponLoadout;
            UnitState state=View.NonInterpolatedTickState;
            HashSet<int> uniqueTurrets=new HashSet<int>();
            object[] originals=new object[Stations],amended=new object[Stations];
            Vector3[] before=new Vector3[Stations],after=new Vector3[Stations];
            for(int station=0;station<Stations;station++)
            {
                WeaponBinding binding=null;int count=0;
                for(int i=0;i<loadout.Length;i++)
                    if(loadout[i]!=null&&loadout[i].WeaponID==IDs[station]){binding=loadout[i];count++;}
                if(count!=1||binding==null||binding.WeaponBindingIndex<0||binding.WeaponBindingIndex>=loadout.Length||
                    !object.ReferenceEquals(loadout[binding.WeaponBindingIndex],binding)||
                    binding.WeaponBindingIndex>=state.AllWeapons.Length||state.AllWeapons[binding.WeaponBindingIndex]==null||
                    state.AllWeapons[binding.WeaponBindingIndex].WeaponBindingID!=binding.WeaponID||
                    binding.TurretIndex<0||binding.TurretIndex>=cache.Length||binding.Weapon==null||binding.Weapon.Turret==null||
                    !uniqueTurrets.Add(binding.TurretIndex))
                    throw new InvalidOperationException("Rhodes unique weapon/cache contract: "+IDs[station]);
                int shared=0;
                foreach(WeaponBinding candidate in loadout)if(candidate!=null&&candidate.TurretIndex==binding.TurretIndex)shared++;
                if(shared!=1)throw new InvalidOperationException("Rhodes turret has shared weapon binding: "+IDs[station]);
                object turret=cache.GetValue(binding.TurretIndex);
                UnitViewAttributes.TurretAttributes attrs=NativeRead.Get(turret,"Attributes") as UnitViewAttributes.TurretAttributes;
                Transform yaw=Bone(turret,"Yaw"),pitch=Bone(turret,"Pitch"),muzzle=Model.FindNode(MuzzleNames[station]);
                string parentName=station<2&&RhodesR19DeploymentData.IsR19(Model)?(station==0?"PIVOT_GN_FORE_TransportYaw":"PIVOT_GN_AFT_TransportYaw"):ParentNames[station];
                if(attrs==null||attrs.BoneName!=YawNames[station]||attrs.PitchBoneName!=PitchNames[station]||
                    yaw==null||pitch==null||yaw!=Model.FindNode(YawNames[station])||pitch!=Model.FindNode(PitchNames[station])||
                    yaw.parent==null||yaw.parent.name!=parentName||!yaw.IsChildOf(Model.GeometryRoot)||
                    pitch.parent!=yaw||muzzle==null||!muzzle.IsChildOf(pitch))
                    throw new InvalidOperationException("Rhodes exact turret bone/parent contract: "+IDs[station]);
                float simRadians=Fixed64.UnsafeFloatValue(binding.DefaultTurretAngleOffsetRadians);
                before[station]=(Vector3)NativeRead.Get(turret,"OriginalYawLocalVector");
                after[station]=CalculateParentLocalReference(Model.GeometryRoot.rotation,yaw.parent.rotation,simRadians);
                originals[station]=turret;amended[station]=cache.GetValue(binding.TurretIndex);
                NativeRead.Set(amended[station],"OriginalYawLocalVector",after[station]);
                BindingIndices[station]=binding.WeaponBindingIndex;TurretIndices[station]=binding.TurretIndex;
                Yaws[station]=yaw;Pitches[station]=pitch;Muzzles[station]=muzzle;
            }
            // All six entries and parent frames are valid before committing.
            // SetValue is required: TurretData is a boxed value type.
            try{for(int station=0;station<Stations;station++)cache.SetValue(amended[station],TurretIndices[station]);}
            catch
            {
                for(int station=0;station<Stations;station++)cache.SetValue(originals[station],TurretIndices[station]);
                throw;
            }
            CorrectedCache=cache;
            StringBuilder detail=new StringBuilder(700);
            for(int station=0;station<Stations;station++)
                detail.Append(" | id=").Append(IDs[station]).Append("; turret=").Append(TurretIndices[station])
                    .Append("; before=").Append(Vec(before[station])).Append("; after=").Append(Vec(after[station]));
            LogReference("normalizedSixParentLocalReferences",detail.ToString());
        }

        // Pure managed arithmetic, also exercised by the offline numeric
        // fixture against real RHS rest frames. Simulation +angle maps to
        // Unity -yaw. Both input quaternions are WORLD rotations.
        public static Vector3 CalculateParentLocalReference(Quaternion geometryWorld,Quaternion parentWorld,float simRadians)
        {
            if(float.IsNaN(simRadians)||float.IsInfinity(simRadians))throw new ArgumentException("Non-finite native default offset");
            Vector3 bodyLocal=new Vector3((float)-Math.Sin(simRadians),0f,(float)Math.Cos(simRadians));
            Vector3 world=Rotate(geometryWorld,bodyLocal);
            Quaternion inverseParent=new Quaternion(-parentWorld.x,-parentWorld.y,-parentWorld.z,parentWorld.w);
            Vector3 local=Rotate(inverseParent,world);
            double length=Math.Sqrt((double)local.x*local.x+(double)local.z*local.z);
            if(double.IsNaN(length)||double.IsInfinity(length)||length<.000001)
                throw new ArgumentException("Degenerate yaw-parent default direction");
            return new Vector3((float)(local.x/length),0f,(float)(local.z/length));
        }
        static Vector3 Rotate(Quaternion q,Vector3 v)
        {
            double n=(double)q.x*q.x+(double)q.y*q.y+(double)q.z*q.z+(double)q.w*q.w;
            if(double.IsNaN(n)||double.IsInfinity(n)||n<.000000000001)throw new ArgumentException("Invalid world rotation");
            double tx=2*((double)q.y*v.z-(double)q.z*v.y)/n;
            double ty=2*((double)q.z*v.x-(double)q.x*v.z)/n;
            double tz=2*((double)q.x*v.y-(double)q.y*v.x)/n;
            return new Vector3((float)(v.x+q.w*tx+q.y*tz-q.z*ty),
                (float)(v.y+q.w*ty+q.z*tx-q.x*tz),(float)(v.z+q.w*tz+q.x*ty-q.y*tx));
        }

        void Capture(UnitState state,int tick,float deltaTime,int stations,string reason)
        {
            Transform[] targets=NativeRead.Get(View,"mCurrentTargetUnitViewTransforms") as Transform[];
            object interpolator=NativeRead.Get(View,"mUnitInterpolator");
            float[] angles=interpolator==null?null:NativeRead.Get(interpolator,"TurretRotations") as float[];
            for(int station=0;station<Stations;station++)
            {
                if((stations&(1<<station))==0||PoseRows>=MaximumEntityPoseRows)continue;
                lock(ProcessGate)
                {
                    if(ProcessPoseRows>=MaximumProcessPoseRows){PoseStopped=true;return;}
                    ProcessPoseRows++;
                }
                PoseRows++;
                int index=BindingIndices[station],turretIndex=TurretIndices[station];
                WeaponBinding binding=View.UnitAttrs.WeaponLoadout[index];
                WeaponState weapon=index<state.AllWeapons.Length?state.AllWeapons[index]:null;
                object turret=CorrectedCache.GetValue(turretIndex);
                int active=(int)NativeRead.Get(turret,"ActiveWeaponBindingIndex");
                float speed=(float)NativeRead.Get(turret,"RotationSpeed");
                Vector3 reference=(Vector3)NativeRead.Get(turret,"OriginalYawLocalVector");
                Transform yaw=Yaws[station],pitch=Pitches[station];
                Transform target=targets!=null&&active>=0&&active<targets.Length?targets[active]:null;
                bool hasAngle=angles!=null&&turretIndex>=0&&turretIndex<angles.Length;
                Vector3 direction=target!=null?(target.position-yaw.position).normalized:
                    (hasAngle?Quaternion.AngleAxis(angles[turretIndex],Vector3.up)*Vector3.forward:Vector3.zero);
                Vector3 flat=yaw.parent.InverseTransformDirection(direction);flat.y=0f;flat.Normalize();
                Vector3 projected=pitch.parent.InverseTransformDirection(direction);projected.x=0f;projected.Normalize();
                float halfFov=Fixed64.UnsafeFloatValue(binding.Weapon.Turret.FieldOfView)*.5f;
                float comparison=Vector3.Angle(reference,flat)+speed*deltaTime*Mathf.Sign(flat.x);
                Runtime.Log("RHODES_TURRET_POSE_EVENT entity="+Entity+"; row="+PoseRows+"; tick="+tick+"; reason="+reason+
                    "; station="+IDs[station]+"; binding="+index+"; turret="+turretIndex+"; enabled="+(weapon!=null&&weapon.Enabled)+
                    "; active="+active+"; simTarget="+(weapon==null?"missing":weapon.CurrentTargetEntity.ToString())+
                    "; simEngaged="+(weapon!=null&&weapon.HasEngagedTarget)+"; simLOS="+(weapon!=null&&weapon.IsLineOfSightClear)+
                    "; yawParent="+yaw.parent.name+"; hullWorldEuler="+Vec(Model.GeometryRoot.eulerAngles)+
                    "; yawWorldEuler="+Vec(yaw.eulerAngles)+"; yawLocalEuler="+Vec(yaw.localEulerAngles)+
                    "; pitchLocalEuler="+Vec(pitch.localEulerAngles)+"; muzzleForward="+Vec(Muzzles[station].forward)+
                    "; nativeReferenceLocal="+Vec(reference)+"; targetDirectionWorld="+Vec(direction)+
                    "; directionSource="+(target!=null?"targetTransform":(hasAngle?"interpolator":"missing"))+
                    "; nativeTargetLocalFlat="+Vec(flat)+"; nativePitchProjection="+Vec(projected)+
                    "; nativeFovLHS="+F(comparison)+"; nativeHalfFov="+F(halfFov)+
                    "; nativeFovPass="+(flat.sqrMagnitude>.9f&&comparison<halfFov)+
                    "; behindPitchPlane="+(projected.z<0f)+"; nativePitchRunsWhenInactive=True"+
                    "; readonly=True; entityPoseCap=14; processPoseCap=56; continuousPoseSampling=False; runtimeAcceptancePending=True");
            }
        }
        void LogReference(string reason,string detail)
        {
            if(ReferenceRows>=4)return;
            lock(ProcessGate){if(ProcessReferenceRows>=MaximumProcessReferenceRows)return;ProcessReferenceRows++;}
            ReferenceRows++;
            Runtime.Log("RHODES_TURRET_REFERENCE entity="+Entity+"; reason="+reason+"; attempt="+Attempts+
                "; detail="+detail+"; nativeViewCacheOnly=True; simulationWrites=False; poseWrites=False; pollingAfterSuccess=False"+
                "; maxAttemptsPerBind=3; maxEntityReferenceRows=4; maxProcessReferenceRows=24; runtimeAcceptancePending=True");
        }
        static Transform Bone(object turret,string field)
        {
            object bone=NativeRead.Get(turret,field);return bone==null?null:NativeRead.Get(bone,"Bone") as Transform;
        }
        static string Clean(string value){return value.Replace("\r"," ").Replace("\n"," ");}
        static string Vec(Vector3 value){return "("+F(value.x)+","+F(value.y)+","+F(value.z)+")";}
        static string F(float value){return value.ToString("0.###",CultureInfo.InvariantCulture);}
    }
}
