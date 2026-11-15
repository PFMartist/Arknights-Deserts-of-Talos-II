using System;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Native turret pitch is line-of-sight only. The native ballistic renderer
    // adds 4*A*range*t*(1-t); its launch tangent is LOS slope + 4*A.
    // Use the current native weapon/target and its original projectile stage.
    // This changes only the authored GunPitch parent of recoil and muzzle.
    public sealed class FactionArtilleryElevation
    {
        sealed class Mode
        {internal string ID,Projectile;internal int BindingIndex;internal float ApexRatio,Clearance;}
        UnitView View;FactionModelBinding Binding;Transform Pitch;Quaternion Rest;
        Mode[] Modes;Mode LastMode;Transform[] TargetViews;
        float Minimum,Maximum,Rate,Angle,Hold,NextTargetRetry;
        int Sequence,Reports;

        public static float LaunchElevation(float apexRatio,float losDegrees,float range,float clearance,float minimum,float maximum)
        {
            if(float.IsNaN(apexRatio)||float.IsInfinity(apexRatio)||apexRatio<0f||
                float.IsNaN(losDegrees)||float.IsInfinity(losDegrees)||range<1f)return Mathf.Clamp(losDegrees,minimum,maximum);
            float slope=Mathf.Tan(Mathf.Clamp(losDegrees,-89f,89f)*Mathf.Deg2Rad)+4f*apexRatio+clearance/range;
            return Mathf.Clamp(Mathf.Atan(slope)*Mathf.Rad2Deg,minimum,maximum);
        }

        public static float Advance(float current,float desired,float deltaSeconds,float rate,bool actualRound)
        {
            if(deltaSeconds<=0f)return current;
            // Native artillery has no wind-up. Its first actual fire event can
            // therefore precede a smooth raise; match that shot immediately.
            return actualRound?desired:Mathf.MoveTowards(current,desired,Mathf.Max(0f,rate)*deltaSeconds);
        }

        public static FactionArtilleryElevation Bind(UnitView view,FactionModelBinding binding,int sequence)
        {
            if(binding.UnitKey!=FactionUnits.ArtilleryKey)return null;
            FactionArtilleryElevation rig=new FactionArtilleryElevation();rig.View=view;rig.Binding=binding;rig.Sequence=sequence;
            for(int i=0;i<binding.Nodes.Length;i++)if(binding.Names[i]=="PIVOT_U09_GunPitch")
            {if(rig.Pitch!=null)throw new InvalidOperationException("Duplicate U09 gun-elevation pivot");rig.Pitch=binding.Nodes[i];rig.Rest=binding.RestRotations[i];}
            if(rig.Pitch==null||!rig.Pitch.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("U09 elevation pivot outside owned rig");
            object spawn=NativeRead.Get(view,"mSpawnSettings");
            UnitViewAttributes attributes=NativeRead.Get(spawn,"UnitViewSettings") as UnitViewAttributes;
            if(attributes==null||attributes.TurretDefinitions==null||attributes.TurretDefinitions.Length!=1)
                throw new InvalidOperationException("U09 native single-turret view is not initialized");
            UnitViewAttributes.TurretAttributes turret=attributes.TurretDefinitions[0];
            rig.Minimum=turret.PitchMin;rig.Maximum=turret.PitchMax;rig.Rate=turret.MaxPitchDegreesPerSecond;
            WeaponBinding[] weapons=view.UnitAttrs.WeaponLoadout;rig.Modes=new Mode[weapons.Length];
            for(int i=0;i<weapons.Length;i++)
            {
                WeaponBinding weapon=weapons[i];string projectile=weapon.Weapon.ProjectileEntityTypeToSpawn;
                ProjectileAttributesData data=ShipbreakersMain.GetEntityTypeAttributes<ProjectileAttributes>(projectile) as ProjectileAttributesData;
                if(data==null||data.ProjectileType!=ProjectileType.Ballistic||data.MotionStages==null||data.MotionStages.Length==0)
                    throw new InvalidOperationException("U09 original ballistic presentation stage absent: "+projectile);
                rig.Modes[i]=new Mode{ID=weapon.WeaponID,Projectile=projectile,BindingIndex=i,
                    ApexRatio=data.MotionStages[0].DesiredAltitude,Clearance=data.MotionStages[0].MinimumGroundClearance};
            }
            Runtime.Log("RHODES_U09_ELEVATION_BOUND entity="+view.EntityID+"; modes="+rig.Modes.Length+
                "; pivot=PIVOT_U09_GunPitch; localAxis=-X; nativeBallisticStageReadOnly=True; launchTangent=LOSslope+4*apexRatio; nativePitchLimits="+
                rig.Minimum+".."+rig.Maximum+"; degreesPerSecond="+rig.Rate+"; muzzleAndRecoilArePivotDescendants=True; actualVisualAcceptancePending=True");
            return rig;
        }

        public void Step(Vector3 nativeAim,int sequence,float deltaSeconds,bool shownAlive)
        {
            if(!shownAlive){Reset();Sequence=sequence;return;}
            if(deltaSeconds<=0f)return;
            int change=unchecked(sequence-Sequence);Sequence=sequence;
            if(change<0){Reset();return;}
            bool actualRound=change>0;Mode active=null;WeaponState chosen=null;
            UnitState state=View.NonInterpolatedTickState;WeaponState[] states=state==null?null:state.AllWeapons;
            if(states!=null)foreach(WeaponState weapon in states)
            {
                if(weapon==null||!weapon.Enabled||(!weapon.IsRunning&&!weapon.HasEngagedTarget))continue;
                foreach(Mode mode in Modes)if(mode.ID==weapon.WeaponBindingID)
                {if(active==null||weapon.IsRunning){active=mode;chosen=weapon;}break;}
                if(chosen!=null&&chosen.IsRunning)break;
            }
            if(actualRound)
            {
                int firedBinding=BvmNativeState.GetLastRoundWeaponBindingIndex(View.EntityID);
                foreach(Mode mode in Modes)if(mode.BindingIndex==firedBinding)
                {
                    active=mode;chosen=null;
                    if(states!=null)foreach(WeaponState weapon in states)if(weapon!=null&&weapon.WeaponBindingID==mode.ID){chosen=weapon;break;}
                    break;
                }
            }
            if(active!=null){LastMode=active;Hold=.75f;}
            else if(actualRound&&LastMode!=null){active=LastMode;Hold=.75f;}
            else if(Hold>0f){Hold=Mathf.Max(0f,Hold-deltaSeconds);active=LastMode;}
            if(TargetViews==null&&Time.realtimeSinceStartup>=NextTargetRetry)
            {NextTargetRetry=Time.realtimeSinceStartup+.25f;TargetViews=NativeRead.Get(View,"mCurrentTargetUnitViewTransforms") as Transform[];}
            Vector3 aim=nativeAim;float range=1000f;
            if(active!=null&&TargetViews!=null&&active.BindingIndex<TargetViews.Length&&TargetViews[active.BindingIndex]!=null)
            {
                Vector3 offset=TargetViews[active.BindingIndex].position-Pitch.position;
                range=new Vector2(offset.x,offset.z).magnitude;if(offset.sqrMagnitude>.000001f)aim=offset.normalized;
            }
            else if(chosen!=null)
            {
                Vector3 target=VectorHelper.SimVector2ToUnityVector3(chosen.AimTurretAtPosition,Pitch.position.y);
                Vector3 offset=target-Pitch.position;range=new Vector2(offset.x,offset.z).magnitude;
            }
            float horizontal=Mathf.Sqrt(aim.x*aim.x+aim.z*aim.z);
            float los=Mathf.Atan2(aim.y,horizontal)*Mathf.Rad2Deg;
            float elevation=active==null?Mathf.Clamp(los,Minimum,Maximum):LaunchElevation(active.ApexRatio,los,range,active.Clearance,Minimum,Maximum);
            Vector3 forward=new Vector3(aim.x,0f,aim.z);
            if(forward.sqrMagnitude<.000001f)forward=View.ForwardHorizontal;forward.Normalize();
            Vector3 worldAim=forward*Mathf.Cos(elevation*Mathf.Deg2Rad)+Vector3.up*Mathf.Sin(elevation*Mathf.Deg2Rad);
            Vector3 localAim=Binding.GeometryRoot.InverseTransformDirection(worldAim);
            float localElevation=Mathf.Atan2(localAim.y,Mathf.Sqrt(localAim.x*localAim.x+localAim.z*localAim.z))*Mathf.Rad2Deg;
            float desired=Mathf.Clamp(localElevation,Minimum,Maximum);
            Angle=Advance(Angle,desired,deltaSeconds,Rate,actualRound);
            Pitch.localRotation=Quaternion.AngleAxis(-Angle,Vector3.right)*Rest;
            if(actualRound&&Reports<3)
            {
                Reports++;Runtime.Log("RHODES_U09_ELEVATION_ROUND entity="+View.EntityID+"; sequence="+sequence+
                    "; weapon="+(active==null?"nativeLOS":active.ID)+"; projectile="+(active==null?"none":active.Projectile)+
                    "; apexRatio="+(active==null?0f:active.ApexRatio)+"; actualElevation="+Angle+"; nativeEvent=True; muzzleFollowsAuthoredPitchAndRecoil=True");
            }
        }

        public void Reset(){Angle=0f;Hold=0f;LastMode=null;if(Pitch!=null)Pitch.localRotation=Rest;}
    }
}
