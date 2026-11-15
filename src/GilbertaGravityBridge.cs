using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;
using System.Text;
using BBI.Core.ComponentModel;
using BBI.Core.Events;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Game.Simulation;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Native weapon acceptance, native path/MoveToGoal and finite native status
    // effects. Never changes an entity Transform, Position, HP or weapon flag.
    public static class GilbertaGravityBridge
    {
        enum Kind {Windup,Fire,Cancelled,Detonated,Ended,WorldChanged}
        struct Observation
        {
            internal Kind Kind;
            internal Entity Caster;
            internal float X,Z;
            internal bool Aim;
            internal bool CancelPose;
            internal int CastID;
        }
        sealed class Presentation
        {
            internal UnitView View;
            internal Vector3 Target;
            internal float Age=100f;
            internal bool Pose,Visible,Committed;
            internal int ActiveCast;
            internal GilbertaGravityEffect Effect;
            internal long Voice,Delivered;
        }
        sealed class Victim
        {
            internal Entity Entity;
            internal string TypeName;
            internal object OwnGoal,PullEffect,HoldEffect;
            internal readonly List<object> Saved=new List<object>();
            internal bool NewIntent,Settled,Pulling,Air,OwnsIntent;
            internal int IntentRevision;
            internal string ExitReason;
            internal Vector2r Start,Destination;
            internal float Radius,ArrivalDistance;
        }
        sealed class Field
        {
            internal Entity Caster;
            internal CommanderID Owner;
            internal Vector2r Center;
            internal uint StartFrame,EndFrame,NextRefresh,LastMaintenanceFrame;
            internal bool HasMaintenanceFrame;
            internal int CastID,Pulses,StartsCount,ResultsCount;
            internal int CandidatesLeft=GilbertaGravityPolicy.MaximumCandidates;
            internal int SnapshotsLeft=GilbertaGravityPolicy.MaximumSpatialCandidates;
            internal int AttemptsLeft=GilbertaGravityPolicy.MaximumAttempts;
            internal int ReachChecksLeft=GilbertaGravityPolicy.MaximumReachChecks;
            internal int RouteChecksLeft=GilbertaGravityPolicy.MaximumPathQueries;
            internal int ObstacleTestsLeft=GilbertaGravityPolicy.MaximumObstacleTests;
            internal int ReissuesLeft=GilbertaGravityPolicy.GoalReissueBudget(GilbertaGravityAssets.ActiveFrames);
            internal readonly List<Victim> Victims=new List<Victim>();
            internal readonly List<GilbertaGravityPolicy.Point> Packed=new List<GilbertaGravityPolicy.Point>();
            internal readonly HashSet<Entity> Unsafe=new HashSet<Entity>(),Terminal=new HashSet<Entity>(),OutsideSeen=new HashSet<Entity>();
            internal readonly CastAudit Audit=new CastAudit();
            internal readonly StringBuilder Results=new StringBuilder(),Starts=new StringBuilder();
        }
        sealed class Candidate
        {
            internal Entity Entity;
            internal object Maneuver,Goals;
            internal UnitState State;
            internal float Radius,Distance;
            internal bool CanPull,Air;
            internal Vector2r Position;
        }
        sealed class Nearby
        {
            internal Entity Entity;
            internal string Type;
            internal float Distance;
        }
        sealed class CastAudit
        {
            internal int InRadiusUnits,InRadiusEnemies,Eligible,ReachChecks,PathQueries;
            internal int ObstacleTests,DirectSweeps,NativePaths,Reissues,Arrivals,LateEntries,Accepted,SpatialQueries;
            internal int ControlFrames,ExternalOrders,RangeExits,Reentries,Transfers;
            internal readonly Dictionary<string,int> Rejections=new Dictionary<string,int>();
            internal readonly List<Nearby> Nearest=new List<Nearby>();
            internal readonly StringBuilder Details=new StringBuilder();
            int DetailCount;
            internal void Reject(string reason,string detail)
            {
                int count;Rejections.TryGetValue(reason,out count);Rejections[reason]=count+1;
                if(string.IsNullOrEmpty(detail)||DetailCount>=6)return;
                Details.Append(" [").Append(reason).Append(' ').Append(detail).Append(']');
                DetailCount++;
            }
            internal bool NeedsNearby(float distance){return Nearest.Count<3||distance<Nearest[Nearest.Count-1].Distance;}
            internal void Observe(Entity entity,string type,float distance)
            {
                int index=0;
                while(index<Nearest.Count&&Nearest[index].Distance<=distance)index++;
                if(index>=3)return;
                Nearest.Insert(index,new Nearby{Entity=entity,Type=type,Distance=distance});
                if(Nearest.Count>3)Nearest.RemoveAt(3);
            }
            internal string Summary()
            {
                StringBuilder text=new StringBuilder();
                text.Append("; inRadiusSamples=").Append(InRadiusUnits).Append("; inRadiusEnemySamples=").Append(InRadiusEnemies).Append("; eligibleSamples=").Append(Eligible);
                text.Append("; accepted=").Append(Accepted).Append("; lateEntries=").Append(LateEntries).Append("; arrivals=").Append(Arrivals).Append("; reissues=").Append(Reissues);
                text.Append("; controlFrames=").Append(ControlFrames).Append("; externalOrders=").Append(ExternalOrders).Append("; rangeExits=").Append(RangeExits).Append("; reentries=").Append(Reentries).Append("; overlappingTransfers=").Append(Transfers);
                text.Append("; spatialQueries=").Append(SpatialQueries).Append("; reachChecks=").Append(ReachChecks).Append("; routeChecks=").Append(PathQueries).Append("; safeDirectRoutes=").Append(DirectSweeps).Append("; nativePaths=").Append(NativePaths).Append("; obstacleTests=").Append(ObstacleTests);
                foreach(KeyValuePair<string,int> row in Rejections)text.Append("; reject_").Append(row.Key).Append('=').Append(row.Value);
                foreach(Nearby row in Nearest)text.Append("; localNearest={entity=").Append(row.Entity).Append(",type=").Append(row.Type).Append(",distance=").Append(Number(row.Distance)).Append('}');
                return text.Append("; details=").Append(Details).ToString();
            }
        }
        static readonly object Gate=new object();
        static readonly Queue<Observation> Pending=new Queue<Observation>();
        static readonly Dictionary<Entity,Presentation> Views=new Dictionary<Entity,Presentation>();
        static readonly List<Entity> Removed=new List<Entity>(),SpatialScratch=new List<Entity>(),ProjectileScratch=new List<Entity>();
        static readonly HashSet<Entity> Detonated=new HashSet<Entity>(),Controlled=new HashSet<Entity>(),MovementControlled=new HashSet<Entity>();
        static readonly Dictionary<Entity,int> ReleasedThisFrame=new Dictionary<Entity,int>();
        static readonly List<Field> Fields=new List<Field>();
        static readonly Comparison<Field> FieldOrder=CompareFields;
        static readonly Comparison<Victim> VictimOrder=CompareVictims;
        static readonly List<Candidate> Candidates=new List<Candidate>();
        static readonly List<Vector2r> PathScratch=new List<Vector2r>();
        static readonly List<object> IntentScratch=new List<object>();
        static readonly BBI.Core.Events.EventHandler<WeaponTriggerEvent> Handler=OnWeapon;
        static readonly BBI.Core.Events.EventHandler<AbilityDeploymentEvent> StopHandler=OnStopAbility;
        static MethodInfo AbilityAttributesGetter;
        static IEventReceiver Receiver;
        static object Simulation;
        static uint Frame,WatchUntil;
        static int Reports,CastSequence;
        static volatile bool ResetRequested;
        static int CompareFields(Field first,Field second){return first.CastID.CompareTo(second.CastID);}
        static int CompareVictims(Victim first,Victim second){return RhodesSimulationEvents.CompareEntities(first.Entity,second.Entity);}
        static Type Native(string name){return typeof(Sim).Assembly.GetType("BBI.Game.Simulation."+name,true);}
        static object New(Type type,params object[] args){return Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,args,null);}
        static string Number(float value){return value.ToString("0.###",CultureInfo.InvariantCulture);}
        static string Point(Vector2r position){return "("+Number(F(position.X))+","+Number(F(position.Y))+")";}
        static void OnStopAbility(AbilityDeploymentEvent observation)
        {
            if(ResetRequested||Fields.Count==0||observation==null||observation.ActionResult!=AbilityDeploymentActionResult.DeploymentSucceeded||observation.SourceEntities==null)return;
            if(AbilityAttributesGetter==null)
            {
                foreach(MethodInfo method in typeof(Sim).GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic))
                    if(method.Name=="GetEntityTypeAttributes"&&method.IsGenericMethodDefinition&&method.GetParameters().Length==1)
                    {AbilityAttributesGetter=method.MakeGenericMethod(typeof(AbilityAttributes));break;}
            }
            if(AbilityAttributesGetter==null)return;
            AbilityAttributes attributes=AbilityAttributesGetter.Invoke(null,new object[]{observation.AbilityType}) as AbilityAttributes;
            if(attributes==null||attributes.AbilityType!=AbilityClass.Stop)return;
            foreach(Entity entity in observation.SourceEntities)
            {
                if(!Controlled.Contains(entity))continue;
                foreach(Field field in Fields)foreach(Victim victim in field.Victims)
                    if(victim.Entity==entity){victim.Saved.Clear();victim.NewIntent=true;victim.IntentRevision++;}
            }
        }
        public static void Bind(UnitView view)
        {
            if(view==null||view.NonInterpolatedTickState==null||view.NonInterpolatedTickState.TypeID!=GilbertaGravityAssets.UnitKey)return;
            Presentation p;if(!Views.TryGetValue(view.EntityID,out p)){p=new Presentation();Views.Add(view.EntityID,p);}p.View=view;p.Visible=Convert.ToInt32(view.Visibility)==2;
        }
        public static void Unbind(Entity entity){Presentation p;if(Views.TryGetValue(entity,out p))p.View=null;}
        public static void BindCarrier(UnitView view)
        {
            if(view==null||view.NonInterpolatedTickState==null||view.NonInterpolatedTickState.TypeID!=FactionUnits.TiltStrikeKey)return;
            Presentation p;if(!Views.TryGetValue(view.EntityID,out p)){p=new Presentation();Views.Add(view.EntityID,p);}
            p.View=view;p.Visible=Convert.ToInt32(view.Visibility)==2;
        }
        public static bool TryGetChannel(Entity entity,out Vector3 aim,out float age)
        {Presentation p;if(Views.TryGetValue(entity,out p)&&p.Pose&&p.Age<6.75f){aim=p.Target;age=p.Age;return true;}aim=Vector3.zero;age=0;return false;}
        public static bool TryTakeVoiceAcceptance(Entity entity,out long sequence)
        {Presentation p;if(Views.TryGetValue(entity,out p)&&p.Voice>p.Delivered){p.Delivered=p.Voice;sequence=p.Voice;return true;}sequence=0;return false;}
        static void Push(Observation o){lock(Gate)if(Pending.Count<128)Pending.Enqueue(o);}
        static bool Unit(IEntitySystem system,Entity entity,out object unit,out UnitState state)
        {unit=system.GetComponent<object>(entity,2);state=unit==null?null:(UnitState)NativeRead.Call(unit,"GetState");return state!=null&&!state.UnitIsDead;}
        static void OnWeapon(WeaponTriggerEvent e)
        {
            if(e==null||Simulation==null||ResetRequested)return;
            IEntitySystem system=(IEntitySystem)NativeRead.Get(Simulation,"EntitySystem");object unit;UnitState state;
            if(!Unit(system,e.Entity,out unit,out state))return;
            bool ground=state.TypeID==GilbertaGravityAssets.UnitKey&&e.WeaponBindingIndex==GilbertaGravityAssets.WeaponIndex;
            bool embarked=state.TypeID==FactionUnits.TiltStrikeKey&&RhodesEmbarkedOperatorSkills.IsProxyEvent(e,1);
            if(!ground&&!embarked)return;
            Observation o=new Observation{Caster=e.Entity};
            if(e.Trigger==WeaponFireTrigger.WindUpStart)o.Kind=Kind.Windup;
            else if(e.Trigger==WeaponFireTrigger.FirstRoundFired){o.Kind=Kind.Fire;WatchUntil=Frame+24;}
            else if(e.Trigger==WeaponFireTrigger.EnteringIdle||e.Trigger==WeaponFireTrigger.WindDownStart)o.Kind=Kind.Cancelled;else return;
            object operation;if(NativeRead.TryGet(e,"WeaponFireOp",out operation)&&operation!=null)
            {Vector2r p=(Vector2r)NativeRead.Get(operation,"AimTurretAtPosition");o.X=F(p.X);o.Z=F(p.Y);o.Aim=true;}Push(o);
        }
        static float F(Fixed64 x){return Fixed64.UnsafeFloatValue(x);}
        static Vector2r P(float x,float z){return new Vector2r(Fixed64.FromRational((int)Math.Round(x*1000f),1000),Fixed64.FromRational((int)Math.Round(z*1000f),1000));}
        static bool HasExactStatus(IEntitySystem system,Entity entity,object wanted)
        {
            object statuses=system.GetComponent<object>(entity,28);if(statuses==null)return false;
            foreach(object status in (IEnumerable)NativeRead.Get(statuses,"ActiveStatusEffects"))if(object.ReferenceEquals(status,wanted))return true;return false;
        }
        static object Apply(IEntitySystem system,Entity caster,Entity target,StatusEffectAttributes attributes)
        {
            if(attributes==null)return null;object effect=New(Native("StatusEffect"),caster,attributes);
            // The original initial-insert path returns false even after OnApply;
            // inspect the actual effect collection rather than that return flag.
            NativeRead.Call(Native("StatusEffectProcessor"),"ApplyStatusEffect",caster,target,effect);
            return HasExactStatus(system,target,effect)?effect:null;
        }
        // Explicit test-scene setup only, called once by the sim-side spawner.
        // The isolated non-expiring lease disables no movement or ability and
        // cannot affect any other commander/type. It is not the skill's EMP.
        public static bool PrepareRangeTarget(object sim,Entity target)
        {
            if(sim==null||!Runtime.RhodesCarrier||!GilbertaGravityAssets.Ready||target==Entity.None)return false;
            IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");object unit;UnitState state;
            if(!Unit(system,target,out unit,out state)||state.TypeID!="RI_U04_LIGHT_ESCORT"||state.OwnerCommander!=new CommanderID(2))return false;
            object effects=system.GetComponent<object>(target,28);if(effects==null)return false;
            foreach(object effect in (IEnumerable)NativeRead.Get(effects,"ActiveStatusEffects"))
                if(object.ReferenceEquals(NativeRead.Get(effect,"Attributes"),GilbertaGravityAssets.RangeNoFireStatus))return true;
            bool ready=Apply(system,target,target,GilbertaGravityAssets.RangeNoFireStatus)!=null;
            if(ready&&Reports++<24)Runtime.Log("GILBERTA_RANGE_TARGET_NO_FIRE entity="+target+"; type=RI_U04_LIGHT_ESCORT; commander=2; nativeIndefiniteSceneLease=True; moveHPArmourUnchanged=True; independentOfSkillEMP=True");
            return ready;
        }
        static bool NativeQueueOwnsMovement(object goals,object owned)
        {
            if(goals==null)return false;
            int inspected=0;
            foreach(object goal in (IEnumerable)goals)
            {
                if(object.ReferenceEquals(goal,owned))continue;
                // Even a stage-zero/queued DockGoal may already reserve a
                // native hangar slot. Do not copy, clear or take it over.
                if(goal.GetType().Name=="DockGoal")return true;
                if(++inspected>GilbertaGravityPolicy.MaximumGoals)return true;
            }
            return false;
        }

        static bool CopyIntent(object goals,object excluded,List<object> destination)
        {
            destination.Clear();
            if(NativeQueueOwnsMovement(goals,excluded))return false;
            foreach(object goal in (IEnumerable)goals)
            {
                if(object.ReferenceEquals(goal,excluded))continue;
                if(destination.Count>=GilbertaGravityPolicy.MaximumGoals||!GilbertaGravityPolicy.SafeGoal(Convert.ToInt32(NativeRead.Get(goal,"GoalType"))))return false;
                object copy;
                try{copy=NativeRead.Call(goal,"StatelessCopy");}
                catch(Exception){destination.Clear();return false;}
                if(copy==null||object.ReferenceEquals(copy,goal)){destination.Clear();return false;}
                destination.Add(copy);
            }
            return true;
        }

        static bool CaptureExternalIntent(Victim victim,object goals)
        {
            object current=NativeRead.Get(goals,"CurrentGoal");
            int count=Convert.ToInt32(NativeRead.Get(goals,"Count"));
            bool owns=object.ReferenceEquals(current,victim.OwnGoal)&&victim.OwnGoal!=null;
            if(count==0)
            {
                bool natural=victim.OwnGoal!=null&&Convert.ToInt32(NativeRead.Get(victim.OwnGoal,"GoalState"))==2;
                if(victim.OwnGoal!=null&&!natural){victim.Saved.Clear();victim.NewIntent=true;victim.IntentRevision++;}
                return true;
            }
            if(owns&&count==1)return true;
            IntentScratch.Clear();
            if(!CopyIntent(goals,victim.OwnGoal,IntentScratch))return false;
            if(IntentScratch.Count==0)return true;
            victim.Saved.Clear();
            victim.Saved.AddRange(IntentScratch);
            victim.NewIntent=true;
            victim.IntentRevision++;
            return true;
        }

        static void RemoveLease(IEntitySystem system,Entity entity,object lease,string key)
        {
            if(lease!=null&&HasExactStatus(system,entity,lease))NativeRead.Call(Native("StatusEffectProcessor"),"RemoveStatusEffect",entity,key);
        }

        static void ClearOwnHead(IEntitySystem system,Victim victim,object goals)
        {
            if(goals==null||victim.OwnGoal==null||!object.ReferenceEquals(NativeRead.Get(goals,"CurrentGoal"),victim.OwnGoal))return;
            NativeRead.Call(victim.OwnGoal,"SetStateToRemove",victim.Entity);
            if(!object.ReferenceEquals(NativeRead.Get(goals,"CurrentGoal"),victim.OwnGoal))return;
            NativeRead.Call(goals,"DequeueCurrentGoal");
            object maneuver=system.GetComponent<object>(victim.Entity,12);
            if(maneuver==null)return;
            NativeRead.Call(maneuver,"ClearManeuver");
            NativeRead.Call(Native("ManeuverUpdater"),"ForceUpdateIdleManeuver",victim.Entity);
        }

        static void FinishVictim(IEntitySystem system,Field field,Victim victim,bool restore,string reason)
        {
            object unit;UnitState state;
            bool alive=Unit(system,victim.Entity,out unit,out state);
            object goals=system.GetComponent<object>(victim.Entity,19);
            if(NativeQueueOwnsMovement(goals,victim.OwnGoal)){victim.OwnsIntent=false;victim.Saved.Clear();}
            bool safe=alive&&victim.OwnsIntent&&goals!=null&&restore&&CaptureExternalIntent(victim,goals);
            RemoveLease(system,victim.Entity,victim.HoldEffect,GilbertaGravityAssets.EMPStatusKey);
            RemoveLease(system,victim.Entity,victim.PullEffect,GilbertaGravityAssets.PullStatusKey);
            victim.HoldEffect=victim.PullEffect=null;
            if(goals!=null)
            {
                if(safe)
                {
                    // Every queued safe intent has an independent pending copy.
                    // Reset its native execution state after the movement stun.
                    NativeRead.Call(Native("EntityGoalsCollection"),"ClearGoals",victim.Entity);
                    object maneuver=system.GetComponent<object>(victim.Entity,12);
                    if(maneuver!=null){NativeRead.Call(maneuver,"ClearManeuver");NativeRead.Call(Native("ManeuverUpdater"),"ForceUpdateIdleManeuver",victim.Entity);}
                    if(Convert.ToInt32(NativeRead.Get(goals,"Count"))==0)
                        for(int index=0;index<victim.Saved.Count;index++)NativeRead.Call(Native("EntityGoalsCollection"),"AddGoal",victim.Entity,victim.Saved[index],index==0?SelectedUnitManager.AddMode.NewSet:SelectedUnitManager.AddMode.Add);
                }
                else ClearOwnHead(system,victim,goals);
            }
            if(Controlled.Remove(victim.Entity))ReleasedThisFrame[victim.Entity]=field.CastID;
            MovementControlled.Remove(victim.Entity);
            if(field.CastID<=8&&field.ResultsCount<12)
            {
                field.ResultsCount++;
                object position=system.GetComponent<object>(victim.Entity,10);
                Vector2r end=position==null?victim.Start:(Vector2r)NativeRead.Get(position,"Position2D");
                float inward=F((victim.Start-field.Center).Length)-F((end-field.Center).Length);
                field.Results.Append(" [entity=").Append(victim.Entity).Append(",reason=").Append(reason).Append(",start=").Append(Point(victim.Start)).Append(",goal=").Append(Point(victim.Destination)).Append(",end=").Append(Point(end));
                field.Results.Append(",netMeters=").Append(Number(F((end-victim.Start).Length))).Append(",inwardMeters=").Append(Number(inward)).Append(",arrived=").Append(victim.Settled).Append(",latestIntentSaved=").Append(victim.NewIntent).Append(",intentRevisions=").Append(victim.IntentRevision).Append(",restored=").Append(safe).Append(']');
            }
        }

        static void FinishField(IEntitySystem system,Field field,string reason)
        {
            field.Victims.Sort(VictimOrder);
            foreach(Victim victim in field.Victims)FinishVictim(system,field,victim,true,reason);
            field.Victims.Clear();
            if(field.CastID<=8)Runtime.Log("GILBERTA_GRAVITY_COMPLETE cast="+field.CastID+"; frame="+Frame+"; reason="+reason+field.Audit.Summary()+"; results="+field.Results);
            Push(new Observation{Kind=Kind.Ended,Caster=field.Caster,CastID=field.CastID,CancelPose=reason!="field_end"});
        }

        static bool ActiveWorld(IEntitySystem system,Entity entity,object unit,UnitState state)
        {return system.HasComponent(entity,10)&&!state.IsDocked&&!state.IsSuspended&&!(bool)NativeRead.Get(unit,"PresentationOnly");}
        static bool Enemy(object sim,CommanderID owner,Entity entity)
        {
            CommanderID target=(CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity);
            return target!=owner&&Convert.ToInt32(NativeRead.Call(NativeRead.Get(sim,"InteractionProvider"),"GetRelationship",owner,target))==4;
        }
        // Used only by the already installed native-sortie prefix. Unaffected
        // aircraft return immediately; no scene search or height write occurs.
        internal static bool HasPullMovement(Entity entity)
        {
            if(ResetRequested||Simulation==null||!MovementControlled.Contains(entity))return false;
            IEntitySystem system=(IEntitySystem)NativeRead.Get(Simulation,"EntitySystem");object unit;UnitState state;
            if(!Unit(system,entity,out unit,out state)||!ActiveWorld(system,entity,unit,state))return false;
            if(NativeQueueOwnsMovement(system.GetComponent<object>(entity,19),null))return false;
            uint now=Convert.ToUInt32(NativeRead.Get(NativeRead.Get(Simulation,"GlobalFrameCount"),"FrameNumber"));
            foreach(Field field in Fields)if(now<field.EndFrame)
            {
                object caster;UnitState casterState;if(!Unit(system,field.Caster,out caster,out casterState))continue;
                foreach(Victim victim in field.Victims)if(victim.Entity==entity&&victim.Pulling&&Enemy(Simulation,field.Owner,entity))return true;
            }
            return false;
        }
        static bool CandidateFor(object sim,IEntitySystem system,Field field,Entity entity,out Candidate candidate)
        {
            candidate=null;
            if(entity==Entity.None||entity==field.Caster||Controlled.Contains(entity)||field.Terminal.Contains(entity))return false;
            object position=system.GetComponent<object>(entity,10);
            if(position==null)return false;
            Vector2r point=(Vector2r)NativeRead.Get(position,"Position2D");
            float distance=F((point-field.Center).Length);
            if(distance>GilbertaGravityAssets.RadiusMeters)return false;
            field.Audit.InRadiusUnits++;
            bool enemy=Enemy(sim,field.Owner,entity);
            if(!enemy){field.Audit.Reject("friendly_or_neutral",null);return false;}
            field.Audit.InRadiusEnemies++;
            if(field.SnapshotsLeft<=0)return false;
            field.SnapshotsLeft--;
            object unit;UnitState state;
            if(!Unit(system,entity,out unit,out state)){field.Terminal.Add(entity);field.Audit.Reject("dead_or_missing",null);return false;}
            if(!GilbertaGravityPolicy.Affects(true,enemy,ActiveWorld(system,entity,unit,state),true))
            {field.Audit.Reject("not_active_world",state.TypeID);return false;}
            if(field.Audit.NeedsNearby(distance))field.Audit.Observe(entity,state.TypeID,distance);
            object maneuver=system.GetComponent<object>(entity,12),goals=system.GetComponent<object>(entity,19);
            UnitAttributes attrs=NativeRead.Get(unit,"Attributes") as UnitAttributes;
            UnitMovementAttributes movement=maneuver==null?null:NativeRead.Get(maneuver,"MovementAttributes") as UnitMovementAttributes;
            float radius=.5f;
            if(movement!=null&&movement.Dynamics!=null)
            {
                float width=F(movement.Dynamics.Width),length=F(movement.Dynamics.Length);
                radius=Math.Max(.5f,(float)Math.Sqrt(width*width+length*length)*.5f);
            }
            else if(attrs!=null&&attrs.NavMeshAttributes!=null)radius=Math.Max(.5f,F(attrs.NavMeshAttributes.DistanceFromObstacles));
            bool blocked=movement==null||movement.Dynamics==null||state.Immobile||movement.Dynamics.PermanentlyImmobile||F(movement.Dynamics.MaxSpeed)<=0f;
            bool canPull=GilbertaGravityPolicy.Eligible(attrs==null?0:(int)attrs.Class,false,maneuver!=null&&goals!=null&&!blocked,blocked,false,radius);
            // Native transfer ownership includes a pending stage-zero goal
            // anywhere in the queue, never an aircraft class immunity.
            if(NativeQueueOwnsMovement(goals,null))canPull=false;
            field.Audit.Eligible++;
            candidate=new Candidate{Entity=entity,Maneuver=maneuver,Goals=goals,State=state,Radius=radius,Distance=distance,Position=point,CanPull=canPull,Air=attrs!=null&&(attrs.Class&UnitClass.Air)!=0};
            return true;
        }

        static bool SweepClear(object map,Field field,Candidate candidate,Vector2r from,Vector2r to)
        {
            UnitClass layer=(UnitClass)NativeRead.Get(candidate.Maneuver,"PathFindLayer");
            float footprint=candidate.Radius+.1f;
            Fixed64 clearance=Fixed64.FromRational((int)Math.Ceiling(footprint*1000f),1000);
            Vector2r midpoint=(from+to)*Fixed64.FromRational(1,2);
            Fixed64 broadRadius=(to-from).Length*Fixed64.FromRational(1,2)+clearance;
            Segment2 segment=new Segment2(from,to);
            IEnumerator iterator=(IEnumerator)NativeRead.Call(map,"GetObstacleCircleBroadSearch",midpoint,broadRadius,layer);
            try
            {
                while(iterator.MoveNext())
                {
                    if(field.ObstacleTestsLeft<=0){field.Audit.Reject("obstacle_budget",null);return false;}
                    field.ObstacleTestsLeft--;
                    field.Audit.ObstacleTests++;
                    Fixed64 distance=(Fixed64)NativeRead.Call(iterator.Current,"DistanceToSegment",segment);
                    if(distance<=clearance)return false;
                }
            }
            finally
            {
                IDisposable disposable=iterator as IDisposable;
                if(disposable!=null)disposable.Dispose();
            }
            return true;
        }

        static bool Reachable(object map,Field field,Candidate candidate,Vector2r destination)
        {
            if(field.ReachChecksLeft<=0){field.Audit.Reject("reach_budget",null);return false;}
            field.ReachChecksLeft--;
            field.Audit.ReachChecks++;
            UnitClass layer=(UnitClass)NativeRead.Get(candidate.Maneuver,"PathFindLayer");
            for(int index=0;index<9;index++)
            {
                float x=0f,z=0f;
                if(index>0){x=(float)Math.Sin(index*Math.PI/4)*candidate.Radius;z=(float)Math.Cos(index*Math.PI/4)*candidate.Radius;}
                if(!(bool)NativeRead.Call(map,"MapPositionValid",destination+P(x,z),layer)){field.Audit.Reject("map_footprint",null);return false;}
            }
            if(F((candidate.Position-destination).Length)<=.25f)return true;
            if(field.RouteChecksLeft<=0){field.Audit.Reject("route_budget",null);return false;}
            field.RouteChecksLeft--;
            field.Audit.PathQueries++;
            // Native SetDestination permits a missing baked profile. Validate
            // the continuous swept footprint first, then issue native movement.
            if(SweepClear(map,field,candidate,candidate.Position,destination))
            {
                field.Audit.DirectSweeps++;
                return true;
            }
            if(field.ObstacleTestsLeft<=0)return false;
            object nav=NativeRead.Call(map,"GetNavMeshFor",NativeRead.Get(candidate.Maneuver,"NavMeshAttributes"));
            if(nav==null){field.Audit.Reject("blocked_without_navmesh",candidate.State.TypeID);return false;}
            PathScratch.Clear();
            NativeRead.Call(nav,"FindPath",candidate.Position,destination,PathScratch);
            field.Audit.NativePaths++;
            if(PathScratch.Count<2||PathScratch.Count-1>GilbertaGravityPolicy.MaximumPathSegments||F((PathScratch[PathScratch.Count-1]-destination).Length)>1f)
            {field.Audit.Reject("no_native_path",null);return false;}
            for(int index=1;index<PathScratch.Count;index++)if(!SweepClear(map,field,candidate,PathScratch[index-1],PathScratch[index]))return false;
            return true;
        }

        static bool IssueMove(Field field,Victim victim)
        {
            if(field.ReissuesLeft<=0){field.Audit.Reject("reissue_budget",null);return false;}
            field.ReissuesLeft--;
            object move;
            if(victim.Settled)move=New(Native("HoldGoal"),string.Empty);
            else move=New(Native("MoveToGoal"),victim.Destination);
            if(!(bool)NativeRead.Call(Native("EntityGoalsCollection"),"AddGoal",victim.Entity,move,SelectedUnitManager.AddMode.NewSet))return false;
            victim.OwnGoal=move;
            field.Audit.Reissues++;
            return true;
        }

        static bool EnsureSuppression(IEntitySystem system,Field field,Victim victim,bool immobilize)
        {
            if(!HasExactStatus(system,victim.Entity,victim.PullEffect))victim.PullEffect=Apply(system,field.Caster,victim.Entity,GilbertaGravityAssets.PullStatus);
            if(victim.PullEffect==null)return false;
            if(immobilize&&!HasExactStatus(system,victim.Entity,victim.HoldEffect))victim.HoldEffect=Apply(system,field.Caster,victim.Entity,GilbertaGravityAssets.EMPStatus);
            // Weapon/ability suppression is independent of placement, paths,
            // movement components and even an unavailable immobilize modifier.
            return true;
        }
        static bool KeepEMPOnly(IEntitySystem system,Field field,Victim victim,object goals,string reason)
        {
            if(victim.Pulling)
            {
                ClearOwnHead(system,victim,goals);MovementControlled.Remove(victim.Entity);
                victim.Pulling=false;victim.OwnGoal=null;
                if(reason=="uncopyable_intent_emp_only"){victim.OwnsIntent=false;victim.Saved.Clear();}
                field.Audit.Reject(reason,victim.TypeName);
            }
            victim.Settled=true;return EnsureSuppression(system,field,victim,true);
        }
        static bool MaintainVictim(object map,IEntitySystem system,Field field,Victim victim)
        {
            victim.ExitReason=null;
            object unit;UnitState state;
            if(!Unit(system,victim.Entity,out unit,out state)){victim.ExitReason="dead_or_missing";return false;}
            if(!ActiveWorld(system,victim.Entity,unit,state)){victim.ExitReason="not_active_world";return false;}
            if(!Enemy(Simulation,field.Owner,victim.Entity)){victim.ExitReason="no_longer_enemy";return false;}
            object position=system.GetComponent<object>(victim.Entity,10);
            Vector2r point=(Vector2r)NativeRead.Get(position,"Position2D");
            if(F((point-field.Center).Length)>GilbertaGravityAssets.RadiusMeters){victim.ExitReason="outside_radius";return false;}
            if(!EnsureSuppression(system,field,victim,false)){victim.ExitReason="suppression_unavailable";return false;}
            object goals=system.GetComponent<object>(victim.Entity,19);
            if(!victim.Pulling)return EnsureSuppression(system,field,victim,true);
            if(NativeQueueOwnsMovement(goals,victim.OwnGoal))
            {
                victim.OwnsIntent=false;victim.Saved.Clear();
                return KeepEMPOnly(system,field,victim,goals,"native_transfer_emp_only");
            }
            if(goals==null||system.GetComponent<object>(victim.Entity,12)==null)return KeepEMPOnly(system,field,victim,goals,"movement_unavailable_emp_only");
            int intentRevision=victim.IntentRevision;
            if(!CaptureExternalIntent(victim,goals))return KeepEMPOnly(system,field,victim,goals,"uncopyable_intent_emp_only");
            if(victim.IntentRevision!=intentRevision)field.Audit.ExternalOrders++;
            if(victim.Settled&&F((point-victim.Destination).Length)>victim.ArrivalDistance)
            {
                RemoveLease(system,victim.Entity,victim.HoldEffect,GilbertaGravityAssets.EMPStatusKey);
                victim.HoldEffect=null;ClearOwnHead(system,victim,goals);victim.OwnGoal=null;victim.Settled=false;
                object maneuver=system.GetComponent<object>(victim.Entity,12);
                Candidate displaced=new Candidate{Entity=victim.Entity,Maneuver=maneuver,State=state,Radius=victim.Radius,Position=point,Air=victim.Air};
                if(!Reachable(map,field,displaced,victim.Destination))return KeepEMPOnly(system,field,victim,goals,"no_path_emp_only");
                if(!IssueMove(field,victim))return KeepEMPOnly(system,field,victim,goals,"move_rejected_emp_only");
                return true;
            }
            object current=NativeRead.Get(goals,"CurrentGoal");
            bool own=victim.OwnGoal!=null&&object.ReferenceEquals(current,victim.OwnGoal);
            bool succeeded=victim.OwnGoal!=null&&Convert.ToInt32(NativeRead.Get(victim.OwnGoal,"GoalState"))==2;
            bool arrived=succeeded||F((point-victim.Destination).Length)<=victim.ArrivalDistance;
            if(arrived&&!victim.Settled){victim.Settled=true;field.Audit.Arrivals++;}
            if(victim.Settled)
            {
                if(!EnsureSuppression(system,field,victim,true))return false;
                bool ownsHold=own&&victim.OwnGoal.GetType()==Native("HoldGoal");
                if((!ownsHold||Convert.ToInt32(NativeRead.Get(goals,"Count"))>1)&&!IssueMove(field,victim))return KeepEMPOnly(system,field,victim,goals,"hold_rejected_emp_only");
                return true;
            }
            if((!own||Convert.ToInt32(NativeRead.Get(goals,"Count"))>1)&&!IssueMove(field,victim))return KeepEMPOnly(system,field,victim,goals,"move_rejected_emp_only");
            return true;
        }

        static void MaintainField(object sim,IEntitySystem system,Field field)
        {
            if(!GilbertaGravityPolicy.ShouldMaintain(Frame,field.EndFrame,field.HasMaintenanceFrame,field.LastMaintenanceFrame))return;
            field.HasMaintenanceFrame=true;field.LastMaintenanceFrame=Frame;field.Audit.ControlFrames++;
            object map=NativeRead.Get(sim,"Map");
            field.Victims.Sort(VictimOrder);
            for(int index=0;index<field.Victims.Count;)
            {
                Victim victim=field.Victims[index];
                if(MaintainVictim(map,system,field,victim)){index++;continue;}
                bool unsafeIntent=field.Unsafe.Contains(victim.Entity);
                string reason=victim.ExitReason??"suppression_unavailable";
                if(reason=="outside_radius"){field.Audit.RangeExits++;field.OutsideSeen.Add(victim.Entity);}
                if(GilbertaGravityPolicy.TerminalExit(reason))field.Terminal.Add(victim.Entity);
                field.Audit.Reject("control_"+reason,victim.TypeName);
                FinishVictim(system,field,victim,!unsafeIntent,reason);
                field.Victims.RemoveAt(index);
            }
        }

        static void RefreshField(object sim,IEntitySystem system,Field field)
        {
            field.Pulses++;
            if(field.Victims.Count>=GilbertaGravityPolicy.MaximumTargets||field.CandidatesLeft<=0||field.SnapshotsLeft<=0||field.AttemptsLeft<=0)return;
            object map=NativeRead.Get(sim,"Map");
            SpatialScratch.Clear();
            NativeRead.Call(NativeRead.Get(map,"SpatialHash"),"FindEntitiesInCircle",field.Center,Fixed64.FromInt(GilbertaGravityAssets.RadiusMeters),SpatialScratch,2);
            field.Audit.SpatialQueries++;
            Candidates.Clear();
            SpatialScratch.Sort(RhodesSimulationEvents.EntityComparer);
            foreach(Entity entity in SpatialScratch)
            {
                Candidate candidate;
                if(CandidateFor(sim,system,field,entity,out candidate)){Candidates.Add(candidate);field.CandidatesLeft--;}
                if(field.CandidatesLeft<=0||field.SnapshotsLeft<=0)break;
            }
            Candidates.Sort(delegate(Candidate first,Candidate second){int order=first.Distance.CompareTo(second.Distance);return order!=0?order:RhodesSimulationEvents.CompareEntities(first.Entity,second.Entity);});
            field.Packed.Clear();
            foreach(Victim victim in field.Victims)
            {
                Vector2r offset=victim.Destination-field.Center;
                field.Packed.Add(new GilbertaGravityPolicy.Point(F(offset.X),F(offset.Y),victim.Radius,victim.Air?1:0));
            }
            foreach(Candidate candidate in Candidates)if(candidate.Distance<=candidate.Radius+1f)
            {
                Vector2r offset=candidate.Position-field.Center;
                field.Packed.Add(new GilbertaGravityPolicy.Point(F(offset.X),F(offset.Y),candidate.Radius,candidate.Air?1:0));
            }
            foreach(Candidate candidate in Candidates)
            {
                if(field.Victims.Count>=GilbertaGravityPolicy.MaximumTargets||field.AttemptsLeft<=0)break;
                field.AttemptsLeft--;
                Victim victim=new Victim{Entity=candidate.Entity,TypeName=candidate.State.TypeID,Start=candidate.Position,Destination=candidate.Position,Radius=candidate.Radius,Air=candidate.Air};
                // Register suppression before any movement-specific rejection.
                if(!EnsureSuppression(system,field,victim,false)){field.Audit.Reject("suppression_apply",victim.TypeName);continue;}
                Vector2r offset=candidate.Position-field.Center;
                bool central=candidate.Distance<=candidate.Radius+1f;
                GilbertaGravityPolicy.Point original=new GilbertaGravityPolicy.Point(F(offset.X),F(offset.Y),candidate.Radius,candidate.Air?1:0),slot=original;
                if(central)field.Packed.Remove(original);
                bool placed=false;
                if(candidate.CanPull&&field.ReachChecksLeft>0&&CopyIntent(candidate.Goals,null,victim.Saved))
                {
                    UnitMovementAttributes movement=(UnitMovementAttributes)NativeRead.Get(candidate.Maneuver,"MovementAttributes");
                    victim.ArrivalDistance=Math.Max(.25f,F(movement.Maneuvers.ArrivalDistance)+.15f);
                    // Eligibility tests the target's center. A carrier need not
                    // fit its entire hull inside the90m visual effect circle.
                    float packingRadius=Math.Min(GilbertaGravityAssets.RadiusMeters,candidate.Distance)+candidate.Radius;
                    placed=GilbertaGravityPolicy.Place(candidate.Radius,packingRadius,
                        (float)Math.Atan2(F(offset.X),F(offset.Y)),candidate.Air?1:0,field.Packed,
                        delegate(GilbertaGravityPolicy.Point point){return Reachable(map,field,candidate,field.Center+P(point.X,point.Z));},out slot);
                    if(placed)
                    {
                        victim.Destination=field.Center+P(slot.X,slot.Z);
                        victim.Settled=F((candidate.Position-victim.Destination).Length)<=victim.ArrivalDistance;
                        victim.Pulling=true;
                        if(IssueMove(field,victim)){victim.OwnsIntent=true;MovementControlled.Add(victim.Entity);}
                        else{victim.Pulling=false;placed=false;victim.OwnGoal=null;field.Audit.Reject("native_move_rejected_emp_only",victim.TypeName);}
                    }
                    else field.Audit.Reject("no_destination_emp_only",victim.TypeName);
                }
                if(!victim.Pulling)
                {
                    victim.Saved.Clear();victim.Settled=true;
                    field.Audit.Reject("emp_only",victim.TypeName);
                }
                if(victim.Settled)EnsureSuppression(system,field,victim,true);
                field.Victims.Add(victim);
                Controlled.Add(victim.Entity);
                if(field.OutsideSeen.Remove(victim.Entity))field.Audit.Reentries++;
                int priorCast;
                if(ReleasedThisFrame.TryGetValue(victim.Entity,out priorCast)&&priorCast!=field.CastID)field.Audit.Transfers++;
                field.Packed.Add(placed?slot:original);
                field.Audit.Accepted++;
                if(Frame>field.StartFrame)field.Audit.LateEntries++;
                if(field.CastID<=8&&field.StartsCount<12)
                {
                    field.Starts.Append(" [entity=").Append(victim.Entity).Append(",atFrame=").Append(Frame).Append(",start=").Append(Point(victim.Start)).Append(",goal=").Append(Point(victim.Destination)).Append(']');
                    field.StartsCount++;
                }
            }
            Candidates.Clear();
            SpatialScratch.Clear();
        }

        static int BeginField(object sim,IEntitySystem system,Entity caster,Vector2r center)
        {
            if(Fields.Count>=GilbertaGravityPolicy.MaximumCasts)return 0;
            object unit;UnitState state;
            if(!Unit(system,caster,out unit,out state)||(state.TypeID!=GilbertaGravityAssets.UnitKey&&state.TypeID!=FactionUnits.TiltStrikeKey))return 0;
            Field field=new Field{Caster=caster,Owner=state.OwnerCommander,Center=center,CastID=++CastSequence,StartFrame=Frame,EndFrame=Frame+GilbertaGravityAssets.ActiveFrames,NextRefresh=Frame+GilbertaGravityPolicy.RefreshFrames,HasMaintenanceFrame=true,LastMaintenanceFrame=Frame};
            Fields.Add(field);
            RefreshField(sim,system,field);
            if(field.CastID<=8)Runtime.Log("GILBERTA_GRAVITY_FIELD cast="+field.CastID+"; caster="+caster+"; center="+Point(center)+"; radius="+GilbertaGravityAssets.RadiusMeters+"; targets="+field.Victims.Count+"; activeFrames="+GilbertaGravityAssets.ActiveFrames+"; activeSeconds="+GilbertaGravityAssets.ActiveSeconds+"; endFrame="+field.EndFrame+"; refreshFrames="+GilbertaGravityPolicy.RefreshFrames+"; existingControlEverySimTick=True; goalReissueBudget="+GilbertaGravityPolicy.GoalReissueBudget(GilbertaGravityAssets.ActiveFrames)+field.Audit.Summary()+"; starts="+field.Starts+"; nativeSafeDirectRoute=True; fullFieldSuppression=True; emptyFieldRetained=True; runtimeAcceptancePending=True");
            return field.CastID;
        }

        internal static void PrepareEvents(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;
            if(!object.ReferenceEquals(Simulation,sim))
            {
                bool replaced=Simulation!=null;
                Fields.Clear();Controlled.Clear();MovementControlled.Clear();ReleasedThisFrame.Clear();Detonated.Clear();Candidates.Clear();SpatialScratch.Clear();PathScratch.Clear();
                Frame=WatchUntil=0;CastSequence=0;ResetRequested=false;
                lock(Gate){Pending.Clear();if(replaced)Pending.Enqueue(new Observation{Kind=Kind.WorldChanged});}
            }
            Simulation=sim;
            IEventReceiver receiver=(IEventReceiver)NativeRead.Get(sim,"SimEventSystem");
            if(!object.ReferenceEquals(receiver,Receiver))
            {
                if(Receiver!=null){Receiver.RemoveHandler<WeaponTriggerEvent>(Handler);Receiver.RemoveHandler<AbilityDeploymentEvent>(StopHandler);}
                Receiver=receiver;
                if(Receiver!=null){Receiver.AddHandler<WeaponTriggerEvent>(Handler);Receiver.AddHandler<AbilityDeploymentEvent>(StopHandler);}
            }
        }
        public static void SimTick(object sim)
        {
            if(sim==null)return;
            ReleasedThisFrame.Clear();
            bool sameWorld=Simulation!=null&&object.ReferenceEquals(Simulation,sim);
            IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
            if(ResetRequested)
            {
                // Reset can be requested by the main-thread unload path. Any
                // still-live world is cleaned here before the mode early-out.
                if(sameWorld)foreach(Field field in Fields)FinishField(system,field,"reset");
                Fields.Clear();Controlled.Clear();MovementControlled.Clear();ReleasedThisFrame.Clear();Candidates.Clear();SpatialScratch.Clear();PathScratch.Clear();
                ResetRequested=false;
            }
            if(!Runtime.RhodesCarrier)return;
            PrepareEvents(sim);
            Frame=Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));
            // Retire every expired owner first. Then maintain existing leases
            // every sim frame, after native AI has issued its latest orders.
            // Only new-target spatial discovery remains on the four-frame
            // cadence. A release also gives overlapping fields an admission
            // pass this frame, in stable cast order, with unchanged query caps.
            Fields.Sort(FieldOrder);
            for(int index=0;index<Fields.Count;)
            {
                Field field=Fields[index];
                object caster;UnitState casterState;
                bool alive=Unit(system,field.Caster,out caster,out casterState);
                if(!alive||Frame>=field.EndFrame)
                {
                    FinishField(system,field,alive?"field_end":"caster_removed");
                    Fields.RemoveAt(index);
                    continue;
                }
                index++;
            }
            foreach(Field field in Fields)MaintainField(sim,system,field);
            bool ownershipReleased=ReleasedThisFrame.Count>0;
            foreach(Field field in Fields)
            {
                if(GilbertaGravityPolicy.ShouldRefresh(Frame,field.NextRefresh,ownershipReleased))
                {
                    if(Frame>=field.NextRefresh)field.NextRefresh=Frame+GilbertaGravityPolicy.RefreshFrames;
                    RefreshField(sim,system,field);
                }
            }
            if(Frame>WatchUntil){Detonated.Clear();return;}
            ProjectileScratch.Clear();
            foreach(Entity entity in system.Query().Has(27))
            {
                object projectile=system.GetComponent<object>(entity,27);
                if(projectile!=null&&(string)NativeRead.Get(projectile,"ProjectileTypeName")==GilbertaGravityAssets.ProjectileKey&&
                    (bool)NativeRead.Get(projectile,"Detonated")&&!Detonated.Contains(entity))ProjectileScratch.Add(entity);
            }
            ProjectileScratch.Sort(RhodesSimulationEvents.EntityComparer);
            foreach(Entity entity in ProjectileScratch)
            {
                object projectile=system.GetComponent<object>(entity,27);
                if(!Detonated.Add(entity))continue;
                Entity caster=(Entity)NativeRead.Get(NativeRead.Get(projectile,"ShotSender"),"WeaponOwner");
                Vector2r target=(Vector2r)NativeRead.Get(projectile,"OriginalAimAtPosition");
                int castID=BeginField(sim,system,caster,target);
                if(castID>0)Push(new Observation{Kind=Kind.Detonated,Caster=caster,CastID=castID,X=F(target.X),Z=F(target.Y),Aim=true});
            }
            ProjectileScratch.Clear();
        }

        public static void MainTick()
        {
            float dt=Runtime.PresentationDeltaTime;if(dt<=0f)return;
            for(;;)
            {
                Observation o;lock(Gate){if(Pending.Count==0)break;o=Pending.Dequeue();}
                if(o.Kind==Kind.WorldChanged){foreach(Presentation old in Views.Values){if(old.Effect!=null)UnityEngine.Object.Destroy(old.Effect.gameObject);old.Effect=null;old.Pose=false;old.Age=100;old.Voice=old.Delivered=0;}continue;}
                Presentation p;if(!Views.TryGetValue(o.Caster,out p))continue;
                if(o.Aim)p.Target=new Vector3(o.X,0,o.Z);
                if(o.Kind==Kind.Ended){if(p.ActiveCast==o.CastID){if(o.CancelPose)p.Pose=false;if(p.Effect!=null)p.Effect.Stop();}continue;}
                if(o.Kind==Kind.Windup){p.Pose=true;p.Committed=false;p.Age=0;p.Voice++;if(p.View!=null)p.Visible=Convert.ToInt32(p.View.Visibility)==2;}
                else if(o.Kind==Kind.Fire)p.Committed=true;
                else if(o.Kind==Kind.Cancelled&&!p.Committed)p.Pose=false;
                else if(o.Kind==Kind.Detonated&&p.Visible)
                {
                    p.ActiveCast=o.CastID;
                    try{if(p.Effect==null){GameObject host=new GameObject("EF_Gilberta_SourceGravityField");host.hideFlags=HideFlags.DontSave;p.Effect=host.AddComponent<GilbertaGravityEffect>();}p.Effect.Emit(p.Target,GilbertaGravityAssets.RadiusMeters);}
                    catch(Exception e){Runtime.Log("GILBERTA_GRAVITY_VISUAL_UNAVAILABLE "+e.Message+"; nativeFiniteEffectsUnchanged=True");}
                }
            }
            Removed.Clear();foreach(KeyValuePair<Entity,Presentation> row in Views)
            {
                Presentation p=row.Value;p.Age+=dt;if(p.Age>=6.75f||(p.View!=null&&p.View.NonInterpolatedTickState!=null&&p.View.NonInterpolatedTickState.UnitIsDead))p.Pose=false;
                if(p.Effect!=null)p.Effect.Step(dt);if(p.View==null&&!p.Pose&&(p.Effect==null||!p.Effect.Active))Removed.Add(row.Key);
            }
            foreach(Entity entity in Removed){Presentation p=Views[entity];if(p.Effect!=null)UnityEngine.Object.Destroy(p.Effect.gameObject);Views.Remove(entity);}
        }
        public static void Reset()
        {
            ResetRequested=true;
            if(Receiver!=null){Receiver.RemoveHandler<WeaponTriggerEvent>(Handler);Receiver.RemoveHandler<AbilityDeploymentEvent>(StopHandler);}
            Receiver=null;
            foreach(Presentation presentation in Views.Values)
                if(presentation.Effect!=null)UnityEngine.Object.Destroy(presentation.Effect.gameObject);
            Views.Clear();Removed.Clear();
            lock(Gate)Pending.Clear();
            GilbertaGravityEffect.ReleaseShared();
        }
    }
}
