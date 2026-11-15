using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Visual-only tuning. No hit/armour/ammunition-cookoff state is inferred.
    public sealed class BvmTurretEjectionSettings
    {
        public bool Enabled=true,DemoOnCombatDeath=true;
        public float VerticalSpeed=12f,HorizontalSpeed=4.5f,Gravity=9.81f;
        public float SpinDegreesPerSecond=145f,Restitution=.16f;
        public int MaxBounces=1;
        public string Source="built-in visual prototype defaults";

        public static BvmTurretEjectionSettings Load(string path)
        {
            BvmTurretEjectionSettings result=new BvmTurretEjectionSettings();
            if(!File.Exists(path))return result;
            result.Source=Path.GetFullPath(path);
            foreach(string raw in File.ReadAllLines(path))
            {
                string line=raw.Trim();
                if(line.Length==0||line.StartsWith("#",StringComparison.Ordinal)||line.StartsWith(";",StringComparison.Ordinal)||line.StartsWith("[",StringComparison.Ordinal))continue;
                int split=line.IndexOf('=');if(split<=0)continue;
                string key=line.Substring(0,split).Trim().ToLowerInvariant();
                string value=line.Substring(split+1).Trim();int comment=value.IndexOf('#');if(comment>=0)value=value.Substring(0,comment).Trim();
                bool boolean;float number;
                if(key=="enabled"||key=="demo_eject_on_combat_death")
                {
                    if(!bool.TryParse(value,out boolean))throw new InvalidDataException("Invalid visual BVM wreck bool: "+key);
                    if(key=="enabled")result.Enabled=boolean;else result.DemoOnCombatDeath=boolean;
                }
                else
                {
                    if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out number)||float.IsNaN(number)||float.IsInfinity(number))
                        throw new InvalidDataException("Invalid visual BVM wreck number: "+key);
                    switch(key)
                    {
                        case "vertical_speed_mps":result.VerticalSpeed=Mathf.Clamp(number,2f,22f);break;
                        case "horizontal_speed_mps":result.HorizontalSpeed=Mathf.Clamp(number,0f,12f);break;
                        case "gravity_mps2":result.Gravity=Mathf.Clamp(number,3f,25f);break;
                        case "spin_degrees_per_second":result.SpinDegreesPerSecond=Mathf.Clamp(number,0f,360f);break;
                        case "restitution":result.Restitution=Mathf.Clamp(number,0f,.35f);break;
                        case "max_bounces":result.MaxBounces=Mathf.Clamp((int)number,0,2);break;
                        default:Runtime.Log("BVM_WRECK_CONFIG_UNKNOWN key="+key+"; ignored=True");break;
                    }
                }
            }
            return result;
        }
    }

    // One existing carcass child, containing only the actual YawNode subtree.
    // Meshes are already baked at their final pose, relative to the real yaw pivot.
    // No Rigidbody, Collider, UnitView, sim entity or damage is created.
    public sealed class BvmTurretDebris : MonoBehaviour
    {
        public Vector3 TemplatePivot;
        Vector3[] Support=new Vector3[0];
        BvmTurretEjectionSettings Options;
        Transform CarcassRoot;
        Vector3 RootAtSpawn,Position,Velocity,SpinAxis,ShapeScale;
        Quaternion Rotation;
        float Age,SpinSpeed,LastGround;
        int Bounces;
        bool Flying,Landed,GroundMissLogged;

        public bool HasFlight { get { return Flying; } }
        public bool HasLanded { get { return Landed; } }

        public void Begin(Transform carcassRoot,Vector3 worldPivot,Quaternion hullRotation,Vector3 shapeScale,
            Vector3[] support,BvmTurretEjectionSettings options,int deterministicSeed)
        {
            ResetFlight();
            if(carcassRoot==null||support==null||support.Length==0||options==null)return;
            Options=options;CarcassRoot=carcassRoot;RootAtSpawn=carcassRoot.position;
            Position=worldPivot;Rotation=hullRotation;ShapeScale=shapeScale;Support=support;
            // Entity-derived sign gives reproducible variation without a global RNG.
            float side=(deterministicSeed&1)==0?1f:-1f;
            Vector3 horizontal=(hullRotation*new Vector3(side*.90f,0f,-.43589f)).normalized;
            Velocity=horizontal*Options.HorizontalSpeed+Vector3.up*Options.VerticalSpeed;
            SpinAxis=(hullRotation*new Vector3(side*.78f,.32f,.54f)).normalized;
            SpinSpeed=Options.SpinDegreesPerSecond;LastGround=carcassRoot.position.y-.025f;
            Age=0f;Bounces=0;Flying=true;Landed=false;GroundMissLogged=false;
            ApplyPose();
            Runtime.Log("BVM_TURRET_EJECTION_STARTED pivot="+worldPivot+"; velocity="+Velocity+"; supportPoints="+Support.Length+
                "; visualDemo=True; realCookoffVerdict=False; damageWrites=0; simRigidbodies=0; nativeCarcassLifetimeShared=True");
        }

        // Called by the parent carcass with the same simulation delta as its
        // queue timer. No independent Update message or wall-clock animation.
        public void Tick(float simulationDeltaTime)
        {
            if(!Flying||CarcassRoot==null)return;
            if(simulationDeltaTime>0f&&!float.IsNaN(simulationDeltaTime)&&!float.IsInfinity(simulationDeltaTime)&&!Landed)
            {
                int steps=Math.Min(128,Math.Max(1,(int)Math.Ceiling(simulationDeltaTime*90f)));
                float dt=simulationDeltaTime/steps;
                for(int i=0;i<steps&&!Landed;i++)
                {
                    Position+=Velocity*dt+Vector3.down*(.5f*Options.Gravity*dt*dt);
                    Velocity+=Vector3.down*(Options.Gravity*dt);
                    Rotation=Quaternion.AngleAxis(SpinSpeed*dt,SpinAxis)*Rotation;
                    Age+=dt;
                    ResolveGround();
                    if(Age>9f&&!Landed)SettleOnGround("boundedFlightTimeout");
                }
            }
            ApplyPose();
        }

        void ResolveGround()
        {
            Vector3 lowest=LowestSupport();
            float ground=SampleTerrain(lowest);
            float penetration=ground+.025f-lowest.y;
            if(penetration<=0f)return;
            Position.y+=penetration;
            if(Velocity.y>=0f)return;
            if(Bounces<Options.MaxBounces&&-Velocity.y>2f)
            {
                Velocity=new Vector3(Velocity.x*.38f,-Velocity.y*Options.Restitution,Velocity.z*.38f);
                SpinSpeed*=.22f;Bounces++;
                Runtime.Log("BVM_TURRET_VISUAL_BOUNCE count="+Bounces+"; pivot="+Position+"; nativeTerrainOnly=True; unitCollidersIgnored=True");
            }
            else SettleOnGround("nativeTerrainContact");
        }

        Vector3 LowestSupport()
        {
            Vector3 result=Position;float height=float.PositiveInfinity;
            for(int i=0;i<Support.Length;i++)
            {
                Vector3 point=Position+Rotation*Vector3.Scale(Support[i],ShapeScale);
                if(point.y<height){height=point.y;result=point;}
            }
            return result;
        }
        float SampleTerrain(Vector3 support)
        {
            int layer=LayerMask.NameToLayer("Terrain");RaycastHit hit;
            float start=Math.Max(2000f,support.y+1000f);
            if(layer>=0&&Physics.Raycast(new Vector3(support.x,start,support.z),Vector3.down,out hit,start+4000f,1<<layer))
            {LastGround=hit.point.y;return LastGround;}
            if(!GroundMissLogged)
            {
                GroundMissLogged=true;
                Runtime.Log("BVM_TURRET_GROUND_SAMPLE_MISSING sourceGroundHeightFallback=True; position="+support+"; slopeAcceptancePending=True");
            }
            return LastGround;
        }
        void SettleOnGround(string reason)
        {
            Vector3 lowest=LowestSupport();
            Position.y+=SampleTerrain(lowest)+.025f-lowest.y;
            Velocity=Vector3.zero;SpinSpeed=0f;Landed=true;
            Runtime.Log("BVM_TURRET_EJECTION_LANDED reason="+reason+"; age="+Age+"; bounces="+Bounces+"; pivot="+Position+
                "; turretAndHullMeshesDisjoint=True; simCollisionAdded=False; nativePoolDespawnShared=True");
        }
        void ApplyPose()
        {
            // Remaining under the same FX root preserves native visibility and
            // pooling. The same native fading/sinking offset reaches both parts.
            Vector3 sink=CarcassRoot==null?Vector3.zero:CarcassRoot.position-RootAtSpawn;
            transform.position=Position+sink;transform.rotation=Rotation;
        }
        public void ResetFlight()
        {
            Flying=Landed=false;Options=null;CarcassRoot=null;Support=new Vector3[0];
            Age=SpinSpeed=0f;Velocity=Vector3.zero;Bounces=0;
            transform.localPosition=TemplatePivot;transform.localRotation=Quaternion.identity;transform.localScale=Vector3.one;
        }
        // The parent calls ResetFlight on native despawn/destroy. A temporary
        // visibility deactivation must not reset a still-owned flying turret.

        // Tight per-chunk boxes are a visual support proxy; they do not create
        // collision geometry in the simulation or change vehicle passage.
        public static Vector3[] BuildSupport(IEnumerable<Mesh> meshes)
        {
            List<Vector3> points=new List<Vector3>();
            foreach(Mesh mesh in meshes)
            {
                Bounds b=mesh.bounds;
                for(int i=0;i<8;i++)points.Add(new Vector3((i&1)==0?b.min.x:b.max.x,(i&2)==0?b.min.y:b.max.y,(i&4)==0?b.min.z:b.max.z));
            }
            return points.ToArray();
        }
    }
}
