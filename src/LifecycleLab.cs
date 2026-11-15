using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Core.Data;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Isolated, opt-in lifecycle scenario. Runtime owns the launch flag and
    // Annihilation settings. No native definitions, hitpoints or deaths are edited.
    public static class LifecycleLab
    {
        public const int FriendlyCount = 2;
        public const int AttackerCount = 2;
        public const string ReserveEnemyKey = "G_Harvester_MP";
        public const string AttackerKey = CustomUnits.UnitKey;
        static readonly CommanderID Player = new CommanderID(1), Enemy = new CommanderID(2);
        static readonly object CaptureGate = new object();
        static readonly Entity[] InitialBvms = new Entity[FriendlyCount];
        static readonly bool[] BvmCaptured = new bool[FriendlyCount];
        static readonly bool[] BvmSeen = new bool[FriendlyCount], BvmDeathReported = new bool[FriendlyCount], BvmGoneReported = new bool[FriendlyCount];
        static readonly int[] PreviousHP = new int[FriendlyCount];
        static readonly HashSet<Entity> RegisteredHostiles = new HashSet<Entity>();
        static readonly Dictionary<Entity,int> EnemyHealth = new Dictionary<Entity,int>();
        static Entity ReserveEnemy;
        static bool ReserveCaptured, ReserveRegistered, LayoutReady;
        static volatile bool CombatArmed;
        static Slot[] Attackers = new Slot[0];
        static uint LastFrame;
        static int PreviousAlive = -1, PreviousNativeCount = -1, PreviousAuxiliary = -1, OrderedForAlive = -1;
        static string PreviousAuxiliaryRoster;
        static bool EmptyConditionReported;

        sealed class Slot
        {
            public Vector2r Position;
            public Entity Entity;
            public bool Spawned, DeathReported;
        }

        // Call before session creation, or after that session has stopped.
        public static void Reset()
        {
            CombatArmed = false;
            lock(CaptureGate)
            {
                for(int i=0;i<FriendlyCount;i++)
                {
                    InitialBvms[i]=Entity.None; BvmCaptured[i]=false; BvmSeen[i]=false;
                    BvmDeathReported[i]=BvmGoneReported[i]=false; PreviousHP[i]=-1;
                }
                ReserveEnemy=Entity.None; ReserveCaptured=false;
            }
            RegisteredHostiles.Clear(); EnemyHealth.Clear();
            ReserveRegistered=LayoutReady=EmptyConditionReported=false;
            Attackers=new Slot[0]; LastFrame=0;
            PreviousAlive=PreviousNativeCount=PreviousAuxiliary=OrderedForAlive=-1;
            PreviousAuxiliaryRoster=null;
        }

        public static StartingUnit[] CreateFriendlyFleet()
        {
            StartingUnit[] fleet=new StartingUnit[FriendlyCount];
            for(int i=0;i<fleet.Length;i++)
            {
                int slot=i;
                fleet[i]=new StartingUnit(); fleet[i].Unit=CustomUnits.UnitKey;
                fleet[i].Tag="EF_LIFECYCLE_BVM_"+i;
                // Native Sim.SpawnGroundUnits invokes this on the simulation thread.
                fleet[i].OnSpawnedInSim=delegate(Entity entity)
                {
                    lock(CaptureGate)
                    {
                        if(BvmCaptured[slot]&&!InitialBvms[slot].Equals(entity))
                            throw new InvalidOperationException("Lifecycle initial BVM callback repeated with another entity");
                        InitialBvms[slot]=entity; BvmCaptured[slot]=true;
                    }
                    Runtime.Log("LIFECYCLE_INITIAL_BVM slot="+slot+"; entity="+entity+"; nativeStartingUnit=True; autoRespawn=False");
                };
            }
            return fleet;
        }

        public static StartingUnit[] CreateEnemyFleet()
        {
            StartingUnit reserve=new StartingUnit(); reserve.Unit=ReserveEnemyKey;
            reserve.Tag="EF_LIFECYCLE_DISTANT_RESERVE";
            reserve.OnSpawnedInSim=delegate(Entity entity)
            {
                lock(CaptureGate)
                {
                    if(ReserveCaptured&&!ReserveEnemy.Equals(entity))
                        throw new InvalidOperationException("Lifecycle reserve callback repeated with another entity");
                    ReserveEnemy=entity; ReserveCaptured=true;
                }
                Runtime.Log("LIFECYCLE_INITIAL_ENEMY entity="+entity+"; key="+ReserveEnemyKey+"; nativeStartingUnit=True; preventsEmptyEnemyAtMatchStart=True");
            };
            return new StartingUnit[]{reserve};
        }

        // Main-thread callers receive a copy, never the simulation's mutable array.
        public static Entity[] FriendlyEntities
        {
            get
            {
                lock(CaptureGate)
                {
                    List<Entity> result=new List<Entity>();
                    for(int i=0;i<FriendlyCount;i++)if(BvmCaptured[i])result.Add(InitialBvms[i]);
                    return result.ToArray();
                }
            }
        }

        // A main-thread handoff only: call after the BVM views/camera are ready.
        public static void ArmCombat()
        {
            if(CombatArmed)return;
            CombatArmed=true;
            Runtime.Log("LIFECYCLE_COMBAT_ARMED nativeEnemyAttackOrdersOnly=True; playerOrdersIssued=0");
        }

        // Simulation thread only. Missing/dead initial units are never replaced.
        public static bool TryGetLeadBvm(object sim,out object nativeUnit,out Entity entity)
        {
            nativeUnit=null; entity=Entity.None;
            foreach(Entity candidate in FriendlyEntities)
            {
                object unit=FindUnit(sim,candidate);
                if(unit==null)continue;
                RequireIdentity(unit,candidate,Player,CustomUnits.UnitKey);
                UnitState state=StateOf(unit);
                if(state.UnitIsDead||state.Hitpoints<=0)continue;
                nativeUnit=unit;entity=candidate;return true;
            }
            return false;
        }

        public static void Tick(object sim)
        {
            uint frame=Frame(sim); if(frame==LastFrame)return;LastFrame=frame;
            Entity[] initial=FriendlyEntities;
            if(initial.Length!=FriendlyCount)return;
            RegisterReserve(sim);
            int alive=ObserveFriendlyRoster(sim,initial,frame);
            if(CombatArmed&&!LayoutReady&&alive>0)
            {
                object lead;Entity leadEntity;
                if(TryGetLeadBvm(sim,out lead,out leadEntity))ConfigureAttackers(sim,lead,frame);
            }
            if(LayoutReady)
            {
                SpawnAttackers(sim,frame);
                ValidateHostileRoster(sim,frame);
                if(alive>0&&alive!=OrderedForAlive)OrderNativeEnemyAdvance(sim,alive,frame);
            }
        }

        static void RegisterReserve(object sim)
        {
            Entity reserve;bool captured;
            lock(CaptureGate){reserve=ReserveEnemy;captured=ReserveCaptured;}
            if(!captured)return;
            if(ReserveRegistered)return;
            RequireType(sim,ReserveEnemyKey,false);
            object unit=FindUnit(sim,reserve);
            if(unit==null)return;
            RequireIdentity(unit,reserve,Enemy,ReserveEnemyKey);
            RegisteredHostiles.Add(reserve);ReserveRegistered=true;
            Runtime.Log("LIFECYCLE_RESERVE_REGISTERED entity="+reserve+"; position="+PositionOf(unit)+"; exactCallbackEntity=True; nativeDefinitionUnchanged=True");
        }

        static void ConfigureAttackers(object sim,object lead,uint frame)
        {
            if(!ReserveRegistered)return;
            UnitAttributes attributes=RequireType(sim,AttackerKey,true);
            UnitDynamicState dynamic=StateOf(lead).StartOfFrameDynamicState;
            Vector2r origin=dynamic.Position,forward=dynamic.Orientation.Forward;
            Vector2r centerDirection=new Vector2r(-origin.X,-origin.Y);
            string directionSource="nativeInitialOrientation";
            if(forward.Length<=Fixed64.FromInt(0)||Dot(forward,centerDirection)<=0f)
            {
                if(centerDirection.Length<=Fixed64.FromInt(1))centerDirection=new Vector2r(Fixed64.FromInt(1),Fixed64.Zero);
                forward=Normalized(centerDirection);directionSource="mapCenterFallback";
            }
            else forward=Normalized(forward);
            Vector2r side=new Vector2r(-forward.Y,forward.X);
            int[] ahead={620,620},lateral={-90,90};
            Slot[] slots=new Slot[AttackerCount];
            object map=NativeRead.Get(sim,"Map");
            for(int i=0;i<slots.Length;i++)
            {
                Vector2r point=origin+forward*Fixed64.FromInt(ahead[i])+side*Fixed64.FromInt(lateral[i]);
                ValidateFootprint(map,point,attributes.Class);
                foreach(object existing in Units(sim))
                    if((PositionOf(existing)-point).Length<Fixed64.FromInt(45))
                        throw new InvalidOperationException("Lifecycle attacker position overlaps an existing unit: "+point);
                slots[i]=new Slot();slots[i].Position=point;slots[i].Entity=Entity.None;
                Runtime.Log("LIFECYCLE_ENEMY_LAYOUT slot="+i+"; origin="+origin+"; position="+point+"; distance="+(point-origin).Length+"; directionSource="+directionSource+"; mapFootprintValidated=True; frame="+frame);
            }
            Attackers=slots;LayoutReady=true;
            Runtime.Log("LIFECYCLE_NATIVE_ENEMY_CONFIG key="+AttackerKey+"; count="+AttackerCount+"; nativeHP="+attributes.MaxHealth+"; nativeWeapons="+attributes.WeaponLoadout.Length+"; mirroredBVMStats=True; HPAndWeaponsUnmodified=True; playerOrdersIssued=0; outcomeNotForced=True");
        }

        static void SpawnAttackers(object sim,uint frame)
        {
            for(int i=0;i<Attackers.Length;i++)
            {
                Slot slot=Attackers[i];if(slot.Spawned)continue;
                RequireType(sim,AttackerKey,true);
                HashSet<Entity> before=new HashSet<Entity>();
                foreach(object existing in Units(sim))before.Add((Entity)NativeRead.Get(existing,"Entity"));
                object lead;Entity leadEntity;
                if(!TryGetLeadBvm(sim,out lead,out leadEntity))return;
                Orientation2 facing=Orientation2.FromDirection(PositionOf(lead)-slot.Position);
                Type factory=typeof(Sim).Assembly.GetType("BBI.Game.Commands.SimCommandFactory",true);
                object command=NativeRead.Call(factory,"CreateSpawnUnitsCommand",Enemy,new string[]{AttackerKey},new string[]{"EF_LIFECYCLE_ENEMY_BVM_"+i},slot.Position,facing);
                if(!(bool)NativeRead.Call(command,"RunCommand",sim,NativeRead.Get(sim,"InteractionProvider"),NativeRead.Get(sim,"GlobalFrameCount")))
                    throw new InvalidOperationException("Lifecycle native attacker spawn rejected at "+slot.Position);
                // This inspected build creates the native entity synchronously.
                // Snapshot difference excludes every pre-existing native unit.
                Entity found=Entity.None;int created=0;
                foreach(object unit in Units(sim))
                {
                    Entity candidate=(Entity)NativeRead.Get(unit,"Entity");
                    if(before.Contains(candidate))continue;
                    RequireIdentity(unit,candidate,Enemy,AttackerKey);
                    if((PositionOf(unit)-slot.Position).Length>Fixed64.FromInt(20))
                        throw new InvalidOperationException("Lifecycle attacker appeared outside its requested footprint");
                    found=candidate;created++;
                }
                if(created!=1)throw new InvalidOperationException("Lifecycle command did not produce exactly one synchronously identifiable attacker; created="+created);
                slot.Entity=found;slot.Spawned=true;RegisteredHostiles.Add(found);
                Runtime.Log("LIFECYCLE_ENEMY_REGISTERED slot="+i+"; entity="+found+"; frame="+frame+"; exactCommandSnapshotEntity=True; nativeSpawn=True; respawn=False");
            }
        }

        static void OrderNativeEnemyAdvance(object sim,int alive,uint frame)
        {
            List<Entity> enemies=new List<Entity>();
            foreach(Slot slot in Attackers)
            {
                if(!slot.Spawned)continue;
                object unit=FindUnit(sim,slot.Entity);if(unit==null)continue;
                UnitState state=StateOf(unit);if(!state.UnitIsDead&&state.Hitpoints>0)enemies.Add(slot.Entity);
            }
            object target;Entity targetEntity;
            if(enemies.Count==0||!TryGetLeadBvm(sim,out target,out targetEntity))return;
            Type factory=typeof(Sim).Assembly.GetType("BBI.Game.Commands.SimCommandFactory",true);
            object command=NativeRead.Call(factory,"CreateUnitAtackMoveCommand",Enemy,enemies.ToArray(),PositionOf(target),false);
            if(!(bool)NativeRead.Call(command,"RunCommand",sim,NativeRead.Get(sim,"InteractionProvider"),NativeRead.Get(sim,"GlobalFrameCount")))
                throw new InvalidOperationException("Lifecycle native enemy attack-move command rejected");
            OrderedForAlive=alive;
            Runtime.Log("LIFECYCLE_ENEMY_NATIVE_ATTACK_MOVE frame="+frame+"; attackers="+enemies.Count+"; targetArea="+PositionOf(target)+"; friendlyAlive="+alive+"; playerOrdersIssued=0; damageOrKillWrites=0");
        }

        static int ObserveFriendlyRoster(object sim,Entity[] initial,uint frame)
        {
            int alive=0,auxiliary=0,liveTotal=0,deathConfirmed=0;
            List<string> auxiliaryTypes=new List<string>();
            foreach(object unit in Units(sim))
            {
                Entity entity=(Entity)NativeRead.Get(unit,"Entity");
                if(Owner(entity)!=Player)continue;
                UnitState state=StateOf(unit);bool living=!state.UnitIsDead&&state.Hitpoints>0;
                int index=Array.IndexOf(initial,entity);
                if(living){liveTotal++;if(index<0){auxiliary++;auxiliaryTypes.Add((string)NativeRead.Get(unit,"TypeName"));}}
            }
            for(int i=0;i<initial.Length;i++)
            {
                object unit=FindUnit(sim,initial[i]);
                if(unit==null)
                {
                    if(BvmSeen[i]&&!BvmGoneReported[i])
                    {
                        Runtime.Log("LIFECYCLE_BVM_REMOVED slot="+i+"; entity="+initial[i]+"; frame="+frame+"; nativeDeathPreviouslyObserved="+BvmDeathReported[i]+"; autoRespawn=False");
                        BvmGoneReported[i]=true;
                    }
                    if(BvmDeathReported[i])deathConfirmed++;
                    continue;
                }
                RequireIdentity(unit,initial[i],Player,CustomUnits.UnitKey);
                UnitState state=StateOf(unit);BvmSeen[i]=true;
                if(PreviousHP[i]!=state.Hitpoints)
                {
                    Runtime.Log("LIFECYCLE_BVM_HP slot="+i+"; entity="+initial[i]+"; frame="+frame+"; previous="+PreviousHP[i]+"; current="+state.Hitpoints+"; lastAttackedBy="+state.LastAttackedByEntity+"; nativeDeath="+state.UnitIsDead+"; driverDamageWrites=0");
                    PreviousHP[i]=state.Hitpoints;
                }
                if(state.UnitIsDead||state.Hitpoints<=0)
                {
                    if(!BvmDeathReported[i])Runtime.Log("LIFECYCLE_BVM_NATIVE_DEATH slot="+i+"; entity="+initial[i]+"; frame="+frame+"; hp="+state.Hitpoints+"; wreckAppearanceRequiresVisualAcceptance=True");
                    BvmDeathReported[i]=true;deathConfirmed++;
                }
                else alive++;
            }
            auxiliaryTypes.Sort(StringComparer.Ordinal);
            string roster=string.Join(",",auxiliaryTypes.ToArray());
            object manager=NativeRead.Get(sim,"CommanderManager");
            object commander=NativeRead.Call(manager,"GetCommanderFromID",Player);
            int nativeCount=commander==null?-1:Convert.ToInt32(NativeRead.Get(commander,"CurrentUnitCount"));
            if(alive!=PreviousAlive||nativeCount!=PreviousNativeCount||auxiliary!=PreviousAuxiliary||roster!=PreviousAuxiliaryRoster)
            {
                string phase=alive==2?"TWO_BVMS":alive==1?"ONE_SURVIVOR":"NO_BVM_SURVIVORS";
                Runtime.Log("LIFECYCLE_FRIENDLY_COUNTS frame="+frame+"; phase="+phase+"; aliveInitialBVM="+alive+"; confirmedNativeDeaths="+deathConfirmed+"; actualLiveFriendlyUnits="+liveTotal+"; nativeCommanderCurrentUnitCount="+nativeCount+"; hiddenAuxiliaryLiveUnits="+auxiliary+"; auxiliaryTypes="+roster+"; nativeCountNotModified=True");
                if(auxiliary>0)Runtime.Log("LIFECYCLE_UNEXPECTED_FRIENDLY_AUXILIARIES count="+auxiliary+"; types="+roster+"; theseCanPreventNativeAnnihilation=True; unitsNotRemovedByTest=True");
                PreviousAlive=alive;PreviousNativeCount=nativeCount;PreviousAuxiliary=auxiliary;PreviousAuxiliaryRoster=roster;
            }
            if(alive==0&&nativeCount==0&&!EmptyConditionReported)
            {
                EmptyConditionReported=true;
                Runtime.Log("LIFECYCLE_NATIVE_ALL_UNITS_ZERO frame="+frame+"; confirmedNativeBVMDeaths="+deathConfirmed+"; nativeCommanderCurrentUnitCount=0; nativeAnnihilationConditionObserved=True; forcedGameOverCommand=False; defeatPanelStillRequiresVisualAcceptance=True");
            }
            return alive;
        }

        static void ValidateHostileRoster(object sim,uint frame)
        {
            int count=0;
            foreach(object unit in Units(sim))
            {
                Entity entity=(Entity)NativeRead.Get(unit,"Entity");if(Owner(entity)!=Enemy)continue;
                count++;
                if(!RegisteredHostiles.Contains(entity))throw new InvalidOperationException("Unregistered hostile in isolated lifecycle scenario: "+entity+" type="+NativeRead.Get(unit,"TypeName"));
                string key=entity.Equals(ReserveEnemy)?ReserveEnemyKey:AttackerKey;
                RequireIdentity(unit,entity,Enemy,key);
                UnitState state=StateOf(unit);int previous;
                if(!EnemyHealth.TryGetValue(entity,out previous)||previous!=state.Hitpoints)
                {
                    Runtime.Log("LIFECYCLE_ENEMY_HP entity="+entity+"; key="+key+"; frame="+frame+"; hp="+state.Hitpoints+"; nativeDeath="+state.UnitIsDead+"; driverDamageWrites=0");
                    EnemyHealth[entity]=state.Hitpoints;
                }
                foreach(Slot slot in Attackers)if(slot.Entity.Equals(entity)&&(state.UnitIsDead||state.Hitpoints<=0)&&!slot.DeathReported)
                {
                    slot.DeathReported=true;
                    Runtime.Log("LIFECYCLE_ATTACKER_NATIVE_DEATH entity="+entity+"; frame="+frame+"; respawn=False");
                }
            }
            if(count>AttackerCount+1||RegisteredHostiles.Count>AttackerCount+1)
                throw new InvalidOperationException("Lifecycle hostile population exceeded exact scenario entries");
        }

        static UnitAttributes RequireType(object sim,string key,bool armed)
        {
            EntityTypeCollection registry=(EntityTypeCollection)NativeRead.Get(NativeRead.Get(sim,"Settings"),"EntityTypes");
            EntityTypeAttributes definition=registry.GetEntityType(key);
            UnitAttributes core=definition==null?null:definition.Get<UnitAttributes>();
            if(core==null||core.MaxHealth<=0||(armed&&(core.WeaponLoadout==null||core.WeaponLoadout.Length==0)))
                throw new InvalidOperationException("Lifecycle native unit definition unavailable: "+key);
            return core;
        }
        static void RequireIdentity(object unit,Entity entity,CommanderID owner,string key)
        {
            if(Owner(entity)!=owner||(string)NativeRead.Get(unit,"TypeName")!=key)
                throw new InvalidOperationException("Lifecycle entity identity mismatch: "+entity+" expected "+owner+"/"+key);
        }
        static void ValidateFootprint(object map,Vector2r point,UnitClass unitClass)
        {
            int[,] offsets={{0,0},{16,0},{-16,0},{0,16},{0,-16}};
            for(int i=0;i<offsets.GetLength(0);i++)
                if(!(bool)NativeRead.Call(map,"MapPositionValid",point+new Vector2r(Fixed64.FromInt(offsets[i,0]),Fixed64.FromInt(offsets[i,1])),unitClass))
                    throw new InvalidOperationException("Lifecycle railgun footprint is not native-valid: "+point);
        }
        static Vector2r Normalized(Vector2r direction){return direction*(Fixed64.FromInt(1)/direction.Length);}
        static float Dot(Vector2r a,Vector2r b){return Fixed64.UnsafeFloatValue(a.X*b.X+a.Y*b.Y);}
        static CommanderID Owner(Entity entity){return (CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity);}
        static IEnumerable Units(object sim){return (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units");}
        static object FindUnit(object sim,Entity entity)
        {
            foreach(object unit in Units(sim))if(((Entity)NativeRead.Get(unit,"Entity")).Equals(entity))return unit;
            return null;
        }
        static UnitState StateOf(object unit){return (UnitState)NativeRead.Call(unit,"GetState");}
        static Vector2r PositionOf(object unit){return StateOf(unit).StartOfFrameDynamicState.Position;}
        static uint Frame(object sim){return Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));}
    }
}
