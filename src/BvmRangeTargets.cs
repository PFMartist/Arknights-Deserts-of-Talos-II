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
    // Sim-thread-only native target range. This driver issues spawn commands only:
    // all targeting, firing, projectiles, health and death remain native.
    public static class BvmRangeTargets
    {
        public const int TargetCount = 12;
        const int TargetColumns = 3;
        sealed class Slot
        {
            public Vector2r Position;
            public string Key;
            public Entity Entity;
            public bool Requested, Confirmed, Gone;
            public uint RequestFrame;
            public int InitialHP, LastHP;
        }

        static readonly CommanderID Enemy = new CommanderID(2);
        static readonly CommanderID Player = new CommanderID(1);
        static readonly Dictionary<Entity, Slot> Registered = new Dictionary<Entity, Slot>();
        static Slot[] Slots = new Slot[0];
        static bool Configured, SpawnReady, InitialSafetyLogged, DelayedSafetyLogged;
        static int LastOpponentCount = -1, LastLiveCount = -1;
        static string SelectedKey;
        public static bool Ready { get { return SpawnReady; } }
        public static string UnitKey { get { return SelectedKey; } }
        public static int RegisteredCount { get { return Registered.Count; } }

        public static Entity[] LiveTargets
        {
            get
            {
                List<Entity> result = new List<Entity>();
                foreach (Slot slot in Slots) if (slot.Confirmed && !slot.Gone) result.Add(slot.Entity);
                return result.ToArray();
            }
        }

        public static void Reset()
        {
            Registered.Clear();
            Slots = new Slot[0];
            Configured = SpawnReady = InitialSafetyLogged = DelayedSafetyLogged = false;
            LastOpponentCount = LastLiveCount = -1;
            SelectedKey = null;
        }

        public static void Tick(object sim, Entity bvmEntity)
        {
            uint frame = Frame(sim);
            if (!Configured)
            {
                object bvm = FindUnit(sim, bvmEntity);
                if (bvm == null) return;
                if ((CommanderID)NativeRead.Call(typeof(Sim), "GetEntityCommanderID", bvmEntity) != Player)
                    throw new InvalidOperationException("Range origin is not the player's BVM");
                if ((string)NativeRead.Get(bvm, "TypeName") != CustomUnits.UnitKey)
                    throw new InvalidOperationException("Range origin identity does not match the custom BVM");
                Configure(sim, PositionOf(bvm));
            }
            for (int i = 0; i < Slots.Length; i++)
            {
                Slot slot = Slots[i];
                if (slot.Requested) continue;
                // Validate the registry again before any command; never strip a
                // native weapon merely to make a combat unit into a target.
                RequireNativeMiningUnit(sim, slot.Key);
                slot.Requested = true; slot.RequestFrame = frame;
                Type factory = typeof(Sim).Assembly.GetType("BBI.Game.Commands.SimCommandFactory", true);
                object command = NativeRead.Call(factory, "CreateSpawnUnitsCommand",
                    Enemy, new string[] { slot.Key }, new string[0], slot.Position, Orientation2.LocalForward);
                if (!(bool)NativeRead.Call(command, "RunCommand", sim,
                    NativeRead.Get(sim, "InteractionProvider"), NativeRead.Get(sim, "GlobalFrameCount")))
                    throw new InvalidOperationException("Native range target spawn rejected: " + slot.Key);
                Runtime.Log("BVM_RANGE_TARGET_REQUEST slot=" + (i + 1) + "; key=" + slot.Key +
                    "; owner=2; position=" + slot.Position + "; frame=" + frame +
                    "; nativeSpawn=True; automaticAttackCommand=False");
                AssertSafeOpponent(sim, frame);
            }
            AssertSafeOpponent(sim, frame);
            foreach (Slot slot in Slots)
                if (slot.Requested && !slot.Confirmed && frame - slot.RequestFrame > 80)
                    throw new InvalidOperationException("Native mining target did not appear at registered position " + slot.Position);
            if (!SpawnReady)
            {
                bool all = Slots.Length == TargetCount;
                foreach (Slot slot in Slots) all &= slot.Confirmed;
                if (all)
                {
                    SpawnReady = true;
                    Runtime.Log("BVM_RANGE_READY targets=" + Slots.Length + "; key=" + SelectedKey +
                        "; realEntityIDsRegistered=True; nativeMiningLoadoutPreserved=True; hostileCarrierCount=0" +
                        "; attackCommandsIssued=0; nativeDamageOnly=True; manualFireAcceptancePending=True");
                }
            }
        }

        static void Configure(object sim, Vector2r origin)
        {
            SelectedKey = ChooseNativeMiningType(sim);
            UnitAttributes attributes = RequireNativeMiningUnit(sim, SelectedKey);
            Vector2r direction = CustomFlatMap.Goal - CustomFlatMap.Start;
            if (direction.Length <= Fixed64.FromInt(1))
                throw new InvalidOperationException("Flat-map range direction is not defined");
            direction = direction * (Fixed64.FromInt(1) / direction.Length);
            Vector2r side = new Vector2r(-direction.Y, direction.X);
            int[] rowDistances = { 300, 800, 1300, 1800 };
            int[] columnOffsets = { -180, 0, 180 };
            int[] rowStagger = { 0, 45, -45, 0 };
            if (rowDistances.Length * TargetColumns != TargetCount || columnOffsets.Length != TargetColumns || rowStagger.Length != rowDistances.Length)
                throw new InvalidOperationException("Mining target range layout dimensions do not match TargetCount");
            Slots = new Slot[TargetCount];
            object map = NativeRead.Get(sim, "Map");
            for (int i = 0; i < Slots.Length; i++)
            {
                int row = i / TargetColumns, column = i % TargetColumns;
                int distance = rowDistances[row], offset = columnOffsets[column] + rowStagger[row];
                Vector2r point = origin + direction * Fixed64.FromInt(distance) + side * Fixed64.FromInt(offset);
                ValidateFootprint(map, point, attributes.Class);
                for (int previous = 0; previous < i; previous++)
                    if ((Slots[previous].Position - point).Length < Fixed64.FromInt(100))
                        throw new InvalidOperationException("Mining target layout overlaps a previous target: " + point);
                foreach (object unit in Units(sim))
                {
                    Entity entity = (Entity)NativeRead.Get(unit, "Entity");
                    if ((CommanderID)NativeRead.Call(typeof(Sim), "GetEntityCommanderID", entity) != Player) continue;
                    if ((PositionOf(unit) - point).Length < Fixed64.FromInt(200))
                        throw new InvalidOperationException("Range target is too close to an existing friendly unit: " + point);
                }
                Slots[i] = new Slot { Position = point, Key = SelectedKey };
                Runtime.Log("BVM_RANGE_LAYOUT slot=" + (i + 1) + "; actualBvmOrigin=" + origin +
                    "; row=" + (row + 1) + "; column=" + (column + 1) + "; forwardDistance=" + distance + "; lateralOffset=" + offset +
                    "; actualDistance=" + (point - origin).Length + "; point=" + point +
                    "; anchoredToActualPlayerSpawn=True");
            }
            Configured = true;
        }

        static string ChooseNativeMiningType(object sim)
        {
            EntityTypeCollection registry = Registry(sim);
            foreach (string key in new string[] { "G_Harvester_MP", "C_Harvester_MP" })
            {
                EntityTypeAttributes definition = registry.GetEntityType(key);
                UnitAttributes attributes = definition == null ? null : definition.Get<UnitAttributes>();
                int count = attributes == null ? -1 : attributes.WeaponLoadout == null ? 0 : attributes.WeaponLoadout.Length;
                Runtime.Log("BVM_RANGE_NATIVE_TYPE_CHECK key=" + key + "; registered=" + (attributes != null) +
                    "; weaponBindings=" + count + "; acceptedAsNativeMiningTarget=" + (attributes != null));
                if (attributes != null)
                {
                    if (attributes.WeaponLoadout != null) foreach (WeaponBinding binding in attributes.WeaponLoadout)
                    {
                        WeaponAttributes weapon=binding.Weapon;
                        Runtime.Log("BVM_RANGE_NATIVE_LOADOUT key="+key+"; binding="+binding.WeaponID+"; disabledOnSpawn="+binding.DisabledOnSpawn+
                            "; baseDamage="+(weapon==null?"none":weapon.BaseDamagePerRound.ToString())+
                            "; wreckDamage="+(weapon==null?"none":weapon.BaseWreckDamagePerRound.ToString())+
                            "; excludeAutoTarget="+(weapon!=null&&weapon.ExcludeFromAutoTargetAcquisition)+
                            "; excludeAutoFire="+(weapon!=null&&weapon.ExcludeFromAutoFire)+"; nativeDefinitionUnchanged=True");
                    }
                    return key;
                }
            }
            throw new InvalidOperationException("Neither native mining-unit candidate is registered");
        }

        static UnitAttributes RequireNativeMiningUnit(object sim, string key)
        {
            if (key != "G_Harvester_MP" && key != "C_Harvester_MP")
                throw new InvalidOperationException("Non-mining unit requested as a range target: " + key);
            EntityTypeAttributes definition = Registry(sim).GetEntityType(key);
            UnitAttributes attributes = definition == null ? null : definition.Get<UnitAttributes>();
            if (attributes == null)
                throw new InvalidOperationException("Native mining target is missing: " + key);
            return attributes;
        }

        // Run before and after spawn processing, and on every later sim tick.
        // A key whitelist alone is insufficient: only requested positions are
        // adopted once, then the exact native Entity identities are retained.
        public static void AssertSafeOpponent(object sim, uint frame)
        {
            HashSet<Entity> seen = new HashSet<Entity>();
            int opponentCount = 0, live = 0;
            foreach (object unit in Units(sim))
            {
                Entity entity = (Entity)NativeRead.Get(unit, "Entity");
                if ((CommanderID)NativeRead.Call(typeof(Sim), "GetEntityCommanderID", entity) != Enemy) continue;
                opponentCount++;
                string key = (string)NativeRead.Get(unit, "TypeName");
                Slot slot;
                if (!Registered.TryGetValue(entity, out slot))
                {
                    Vector2r position = PositionOf(unit);
                    foreach (Slot pending in Slots)
                        if (pending.Requested && !pending.Confirmed && frame - pending.RequestFrame <= 80 && pending.Key == key &&
                            (pending.Position - position).Length <= Fixed64.FromInt(20))
                        {
                            if (slot != null) throw new InvalidOperationException("Ambiguous target entity adoption");
                            slot = pending;
                        }
                    if (slot == null)
                        throw new InvalidOperationException("Unexpected opponent unit; no registered target slot: " + key + " entity=" + entity);
                    RequireNativeMiningUnit(sim, key);
                    UnitState created = StateOf(unit);
                    if (created.UnitIsDead || created.Hitpoints <= 0)
                        throw new InvalidOperationException("Mining target was already dead before ID registration: " + entity);
                    slot.Entity = entity; slot.Confirmed = true;
                    slot.InitialHP = slot.LastHP = created.Hitpoints;
                    Registered.Add(entity, slot);
                    Runtime.Log("BVM_RANGE_TARGET_REGISTERED entity=" + entity + "; key=" + key +
                        "; owner=2; position=" + position + "; initialHP=" + created.Hitpoints +
                        "; nativeMiningLoadoutPreserved=True; exactEntityWhitelist=True");
                }
                if (slot.Key != key)
                    throw new InvalidOperationException("Registered target Entity changed native type: " + entity + " -> " + key);
                RequireNativeMiningUnit(sim, key);
                if (!seen.Add(entity)) throw new InvalidOperationException("Duplicate native target Entity in UnitManager: " + entity);
                UnitState state = StateOf(unit);
                bool dead = state.UnitIsDead || state.Hitpoints <= 0;
                if (slot.Gone && !dead)
                    throw new InvalidOperationException("A destroyed mining target Entity reappeared alive: " + entity);
                if (state.Hitpoints != slot.LastHP)
                {
                    Runtime.Log("BVM_RANGE_NATIVE_HP entity=" + entity + "; previous=" + slot.LastHP +
                        "; current=" + state.Hitpoints + "; initial=" + slot.InitialHP +
                        "; lastAttackedBy=" + state.LastAttackedByEntity + "; driverAppliedDamage=False");
                    slot.LastHP = state.Hitpoints;
                }
                if (dead)
                {
                    if (!slot.Gone) Runtime.Log("BVM_RANGE_TARGET_DESTROYED entity=" + entity +
                        "; nativeDeath=True; respawn=False; identityTombstoneRetained=True");
                    slot.Gone = true;
                }
                else live++;
            }
            foreach (Slot slot in Slots)
                if (slot.Confirmed && !seen.Contains(slot.Entity) && !slot.Gone)
                {
                    slot.Gone = true;
                    Runtime.Log("BVM_RANGE_TARGET_DESPAWNED entity=" + slot.Entity +
                        "; respawn=False; identityTombstoneRetained=True; explicitKillNotAssumed=True");
                }
            if (opponentCount > TargetCount || Registered.Count > Slots.Length || live > Registered.Count)
                throw new InvalidOperationException("Range enemy population exceeded its explicit registered target slots");
            if (!InitialSafetyLogged || (!DelayedSafetyLogged && frame >= 85) ||
                opponentCount != LastOpponentCount || live != LastLiveCount)
            {
                Runtime.Log("SAFE LAB OPPONENT VERIFIED: frame=" + frame + "; actualOpponentUnits=" + opponentCount +
                    "; liveMiningTargets=" + live + "; registeredTargetEntityIDs=" + Registered.Count +
                    "; hostileCarrierCount=0; onlyExplicitNativeMiningTargets=True; originalOpponentFleetEmpty=True");
                InitialSafetyLogged = true;
                if (frame >= 85) DelayedSafetyLogged = true;
                LastOpponentCount = opponentCount; LastLiveCount = live;
            }
        }

        static void ValidateFootprint(object map, Vector2r point, UnitClass unitClass)
        {
            int[,] offsets = { { 0, 0 }, { 12, 0 }, { -12, 0 }, { 0, 12 }, { 0, -12 } };
            for (int i = 0; i < offsets.GetLength(0); i++)
                if (!(bool)NativeRead.Call(map, "MapPositionValid",
                    point + new Vector2r(Fixed64.FromInt(offsets[i, 0]), Fixed64.FromInt(offsets[i, 1])), unitClass))
                    throw new InvalidOperationException("Mining target footprint is not native-valid: " + point);
        }

        static EntityTypeCollection Registry(object sim)
        {
            return (EntityTypeCollection)NativeRead.Get(NativeRead.Get(sim, "Settings"), "EntityTypes");
        }
        static IEnumerable Units(object sim)
        {
            return (IEnumerable)NativeRead.Get(NativeRead.Get(sim, "UnitManager"), "Units");
        }
        static object FindUnit(object sim, Entity entity)
        {
            foreach (object unit in Units(sim))
                if (((Entity)NativeRead.Get(unit, "Entity")).Equals(entity)) return unit;
            return null;
        }
        static UnitState StateOf(object unit) { return (UnitState)NativeRead.Call(unit, "GetState"); }
        static Vector2r PositionOf(object unit) { return StateOf(unit).StartOfFrameDynamicState.Position; }
        static uint Frame(object sim)
        {
            return Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim, "GlobalFrameCount"), "FrameNumber"));
        }
    }
}