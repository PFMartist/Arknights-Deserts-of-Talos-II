using System;
using System.Collections;
using System.Globalization;
using System.Text;
using BBI.Core.ComponentModel;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // OFFLINE CANDIDATE. A bounded reader, explicitly stepped by the existing
    // RhodesWeaponPresentation host. It is not a component or an update hook.
    // No validators, weapon/turret writes, target acquisition, or scene scans.
    public sealed class RhodesMainTurretReadOnlyDiagnostic
    {
        const int RegularTickInterval=8,MaximumRows=32,MaximumRoundSamples=4,MaximumParents=8;
        readonly object Gate=new object();
        readonly int[] MainIndices=new int[2],RoundMains=new int[MaximumRoundSamples];
        UnitView View;
        RhodesCarrierBinding ModelBinding;
        Entity Entity;
        bool HasEntity,InitialSampled,Failed,Bound;
        int Rows,LastRegularTick,AcceptedRounds,ConsumedRounds;
        string LastFingerprint;

        public void Initialize(UnitView view,int[] mainIndices)
        {
            if(!Runtime.DetailedDiagnosticsEnabled)return;
            if(view==null||mainIndices==null||mainIndices.Length!=2)return;
            Entity current=view.EntityID;
            lock(Gate)
            {
                if(!HasEntity||!Entity.Equals(current))
                {
                    Entity=current;HasEntity=true;Rows=0;InitialSampled=false;
                    AcceptedRounds=0;ConsumedRounds=0;LastFingerprint=null;
                    LastRegularTick=0;
                }
                View=view;MainIndices[0]=mainIndices[0];MainIndices[1]=mainIndices[1];
                Failed=false;Bound=true;
            }
            // Local carrier component only; never discover another view/unit.
            ModelBinding=view.GetComponentInChildren<RhodesCarrierBinding>();
        }

        // Called only inside the host's exact entity/main-index FireRound branch.
        // This callback records an ordinal; all Unity/native reads wait for Step.
        public void NoteMainRound(int main)
        {
            if(!Runtime.DetailedDiagnosticsEnabled)return;
            lock(Gate)
            {
                if(!Bound||Failed||Rows>=MaximumRows||main<0||main>=2||AcceptedRounds>=MaximumRoundSamples)return;
                RoundMains[AcceptedRounds++]=main;
            }
        }

        // Snapshot timing: the host's existing LateUpdate, before its pitch mirror
        // and recoil work. A FireRound row is the next Step after that exact event.
        public void Step(float presentationDeltaTime)
        {
            if(!Runtime.DetailedDiagnosticsEnabled)return;
            if(View==null||Failed||Rows>=MaximumRows||!HasEntity||!Entity.Equals(View.EntityID))return;
            UnitState state=View.NonInterpolatedTickState;
            if(state==null||state.UnitIsDead)return;
            int tick=ShipbreakersMain.CurrentSimTick;
            try
            {
                if(!InitialSampled)
                {
                    InitialSampled=true;LastRegularTick=tick;
                    Sample(state,tick,presentationDeltaTime,"initial",true);
                }
                while(Rows<MaximumRows)
                {
                    int ordinal,main;
                    lock(Gate)
                    {
                        if(ConsumedRounds>=AcceptedRounds)break;
                        ordinal=++ConsumedRounds;main=RoundMains[ordinal-1];
                    }
                    Sample(state,tick,presentationDeltaTime,"mainRound"+ordinal+"_station"+main,true);
                }
                // Preserve one slot for each as-yet-unconsumed first-four round
                // sample, so frequent state changes cannot crowd those out.
                if(Rows<MaximumRows-(MaximumRoundSamples-ConsumedRounds)&&
                    (tick<LastRegularTick||tick-LastRegularTick>=RegularTickInterval))
                {
                    LastRegularTick=tick;
                    Sample(state,tick,presentationDeltaTime,"discreteChange",false);
                }
            }
            catch(Exception error)
            {
                Failed=true;
                if(Rows<MaximumRows)
                {
                    Rows++;
                    Runtime.Log("RHODES_MAIN_TURRET_DIAG entity="+Entity+"; row="+Rows+
                        "; reason=readerStopped; exception="+error.GetType().Name+
                        "; message="+error.Message.Replace("\r"," ").Replace("\n"," ")+
                        "; readonly=True; runtimeAcceptancePending=True");
                }
            }
        }

        public void Release()
        {
            lock(Gate){Bound=false;View=null;ModelBinding=null;}
            // Same entity rebind retains its total row/event budget.
        }

        void Sample(UnitState state,int tick,float deltaTime,string reason,bool force)
        {
            if(!Runtime.DetailedDiagnosticsEnabled)return;
            if(Rows>=MaximumRows)return;
            Array turrets=NativeRead.Get(View,"mTurretData") as Array;
            IDictionary effects=NativeRead.Get(View,"mWeaponFXControllers") as IDictionary;
            Transform[] targets=NativeRead.Get(View,"mCurrentTargetUnitViewTransforms") as Transform[];
            object interpolator=NativeRead.Get(View,"mUnitInterpolator");
            float[] rotations=interpolator==null?null:NativeRead.Get(interpolator,"TurretRotations") as float[];
            int rotationCount=interpolator==null?-1:(int)NativeRead.Get(interpolator,"TurretRotationCount");
            StringBuilder fingerprint=new StringBuilder(256),details=new StringBuilder(1800);
            fingerprint.Append("turretsPresent=").Append(turrets!=null).Append(";fxPresent=").Append(effects!=null)
                .Append(";interpolatorCount=").Append(rotationCount);
            for(int main=0;main<2;main++)
            {
                int bindingIndex=MainIndices[main];
                WeaponBinding binding=bindingIndex>=0&&bindingIndex<View.UnitAttrs.WeaponLoadout.Length?
                    View.UnitAttrs.WeaponLoadout[bindingIndex]:null;
                WeaponState weapon=state.AllWeapons!=null&&bindingIndex>=0&&bindingIndex<state.AllWeapons.Length?
                    state.AllWeapons[bindingIndex]:null;
                int turretIndex=binding==null?-1:binding.TurretIndex;
                object turret=turrets!=null&&turretIndex>=0&&turretIndex<turrets.Length?turrets.GetValue(turretIndex):null;
                int active=turret==null?-1:(int)NativeRead.Get(turret,"ActiveWeaponBindingIndex");
                Transform yaw=Bone(turret,"Yaw"),pitch=Bone(turret,"Pitch");
                object attributes=turret==null?null:NativeRead.Get(turret,"Attributes");
                string yawName=attributes==null?null:(string)NativeRead.Get(attributes,"BoneName");
                string pitchName=attributes==null?null:(string)NativeRead.Get(attributes,"PitchBoneName");
                Transform sourceYaw=ModelBinding==null?null:ModelBinding.FindNode(yawName);
                Transform sourcePitch=ModelBinding==null?null:ModelBinding.FindNode(pitchName);
                Transform target=targets!=null&&active>=0&&active<targets.Length?targets[active]:null;
                bool fxPresent=effects!=null&&effects.Contains(bindingIndex);
                Vector3 original=turret==null?Vector3.zero:(Vector3)NativeRead.Get(turret,"OriginalYawLocalVector");
                float speed=turret==null?0f:(float)NativeRead.Get(turret,"RotationSpeed");
                bool angleAvailable=rotations!=null&&turretIndex>=0&&turretIndex<rotations.Length;
                float interpolatedAngle=angleAvailable?rotations[turretIndex]:0f;
                fingerprint.Append("|main=").Append(main).Append(";binding=").Append(bindingIndex)
                    .Append(";turret=").Append(turretIndex).Append(";weaponPresent=").Append(weapon!=null)
                    .Append(";enabled=").Append(weapon!=null&&weapon.Enabled)
                    .Append(";active=").Append(active).Append(";fxContains=").Append(fxPresent)
                    .Append(";yawID=").Append(ID(yaw)).Append(";pitchID=").Append(ID(pitch))
                    .Append(";sourceYawID=").Append(ID(sourceYaw)).Append(";sourcePitchID=").Append(ID(sourcePitch))
                    .Append(";targetTransformID=").Append(ID(target)).Append(";interpolatorAnglePresent=").Append(angleAvailable);
                if(weapon!=null)
                    fingerprint.Append(";target=").Append(weapon.CurrentTargetEntity)
                        .Append(";los=").Append(weapon.IsLineOfSightClear).Append(";engaged=").Append(weapon.HasEngagedTarget)
                        .Append(";running=").Append(weapon.IsRunning).Append(";outOfAmmo=").Append(weapon.OutOfAmmo);
                details.Append(" | main=").Append(main).Append("; id=").Append(binding==null?"missing":binding.WeaponID)
                    .Append("; binding=").Append(bindingIndex).Append("; turret=").Append(turretIndex)
                    .Append("; weaponStatePresent=").Append(weapon!=null).Append("; turretCachePresent=").Append(turret!=null)
                    .Append("; enabled=").Append(weapon!=null&&weapon.Enabled).Append("; activeBinding=").Append(active)
                    .Append("; fxContainsMainBinding=").Append(fxPresent)
                    .Append("; cachedYaw=").Append(Node(yaw)).Append("; sourceYaw=").Append(Node(sourceYaw))
                    .Append("; sameYaw=").Append(yaw!=null&&yaw==sourceYaw)
                    .Append("; cachedPitch=").Append(Node(pitch)).Append("; sourcePitch=").Append(Node(sourcePitch))
                    .Append("; samePitch=").Append(pitch!=null&&pitch==sourcePitch)
                    .Append("; yawParents=").Append(ParentChain(yaw)).Append("; pitchParents=").Append(ParentChain(pitch))
                    .Append("; originalYawLocal=").Append(Vec(original)).Append("; rotationSpeed=").Append(F(speed))
                    .Append("; yawLocalEuler=").Append(Euler(yaw,true)).Append("; yawWorldEuler=").Append(Euler(yaw,false))
                    .Append("; pitchLocalEuler=").Append(Euler(pitch,true)).Append("; pitchWorldEuler=").Append(Euler(pitch,false))
                    .Append("; yawParentWorldEuler=").Append(Euler(yaw==null?null:yaw.parent,false))
                    .Append("; interpolatorCount=").Append(rotationCount).Append("; interpolatorAngle=")
                    .Append(angleAvailable?F(interpolatedAngle):"missing");
                if(weapon!=null)
                    details.Append("; target=").Append(weapon.CurrentTargetEntity).Append("; los=").Append(weapon.IsLineOfSightClear)
                        .Append("; engaged=").Append(weapon.HasEngagedTarget).Append("; running=").Append(weapon.IsRunning)
                        .Append("; outOfAmmo=").Append(weapon.OutOfAmmo).Append("; aim=").Append(weapon.AimTurretAtPosition)
                        .Append("; simTurretPosition=").Append(weapon.TurretWorldPosition);
                // Match native's target-transform preference, else interpolated
                // simulation heading; this calculates values only, never rotates.
                if(yaw!=null&&yaw.parent!=null&&(target!=null||angleAvailable))
                {
                    Vector3 direction=target!=null?(target.position-yaw.position).normalized:
                        Quaternion.AngleAxis(interpolatedAngle,Vector3.up)*Vector3.forward;
                    Vector3 local=yaw.parent.InverseTransformDirection(direction);local.y=0f;local.Normalize();
                    float angle=Vector3.Angle(original,local),step=speed*deltaTime;
                    float halfFov=binding==null||binding.Weapon==null?0f:Fixed64.UnsafeFloatValue(binding.Weapon.Turret.FieldOfView)*.5f;
                    float comparison=angle+step*Mathf.Sign(local.x);
                    details.Append("; targetView=").Append(Node(target)).Append("; directionSource=").Append(target!=null?"cachedTargetTransform":"interpolator")
                        .Append("; targetParentLocalFlat=").Append(Vec(local)).Append("; nativeAngle=").Append(F(angle))
                        .Append("; nativeDelta=").Append(F(step)).Append("; nativeFovLHS=").Append(F(comparison))
                        .Append("; nativeHalfFov=").Append(F(halfFov)).Append("; nativeDirectionMagnitudePass=").Append(local.sqrMagnitude>.9f)
                        .Append("; nativeFovPass=").Append(comparison<halfFov)
                        .Append("; exactNativeActiveMain=").Append(active==bindingIndex)
                        .Append("; nativeYawActiveGate=").Append(active>=0);
                    // Native pitch still executes with active=-1. A horizontal
                    // backward direction projects antipodal to local forward;
                    // its RotateTowards plane may be ambiguous near zero Y.
                    if(pitch!=null&&pitch.parent!=null)
                    {
                        Vector3 projected=pitch.parent.InverseTransformDirection(direction);
                        projected.x=0f;projected.Normalize();
                        bool antipodalRisk=projected.z<0f&&Mathf.Abs(projected.y)<.01f;
                        details.Append("; pitchProjected=").Append(Vec(projected))
                            .Append("; antipodalRisk=").Append(antipodalRisk)
                            .Append("; antipodalEpsilonY=0.01; nativePitchIndependentOfActiveBinding=True");
                    }
                }
            }
            string currentFingerprint=fingerprint.ToString();
            if(!force&&string.Equals(currentFingerprint,LastFingerprint,StringComparison.Ordinal))return;
            LastFingerprint=currentFingerprint;Rows++;
            Runtime.Log("RHODES_MAIN_TURRET_DIAG entity="+Entity+"; row="+Rows+"; tick="+tick+"; reason="+reason+
                "; timing=existingLateUpdateBeforePitchMirrorAndRecoil; regularIntervalTicks=8; totalCap=32"+
                details+"; readonly=True; runtimeAcceptancePending=True");
        }

        static Transform Bone(object turret,string field)
        {
            if(turret==null)return null;
            object data=NativeRead.Get(turret,field);
            return data==null?null:NativeRead.Get(data,"Bone") as Transform;
        }
        static int ID(Transform value){return value==null?0:value.GetInstanceID();}
        static string Node(Transform value){return value==null?"missing":value.name+"#"+ID(value);}
        static string ParentChain(Transform value)
        {
            StringBuilder result=new StringBuilder();int count=0;
            while(value!=null&&count<MaximumParents)
            {
                if(count>0)result.Append('>');result.Append(Node(value));value=value.parent;count++;
            }
            if(value!=null)result.Append(">...");return result.ToString();
        }
        static string Euler(Transform value,bool local){return value==null?"missing":Vec(local?value.localEulerAngles:value.eulerAngles);}
        static string Vec(Vector3 value){return "("+F(value.x)+","+F(value.y)+","+F(value.z)+")";}
        static string F(float value){return value.ToString("0.###",CultureInfo.InvariantCulture);}
    }
}
