using System;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Core.Events;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Unity.Game;
using BBI.Unity.Game.World;

namespace EndfieldDoK.MapUnitLab
{
    public enum HumanoidWeaponVisualEventKind { Windup, Round, IdleOrWinddown }
    public struct HumanoidWeaponVisualEvent
    {
        public HumanoidWeaponVisualEventKind Kind;
        public long EventSequence, WindupSequence, RoundSequence;
        public int WeaponBindingIndex, TurretIndex;
        public bool HasAim;
        public bool Hit;
        public float AimX, AimZ;
        public Entity Target;
        public uint NativeLastFiredFrame;
    }

    // Read-only sim->presentation bridge. It never enables a weapon, requests
    // fire, adds a projectile, changes a target, or applies damage.
    public static class HumanoidWeaponEventBridge
    {
        const int MaximumQueuedEventsPerUnit = 128;
        sealed class Record
        {
            public int WeaponBindingIndex, DroppedEvents;
            public long EventSequence, WindupSequence, RoundSequence;
            public readonly Queue<HumanoidWeaponVisualEvent> Events = new Queue<HumanoidWeaponVisualEvent>();
        }
        static readonly object Gate = new object();
        static readonly Dictionary<Entity, Record> Records = new Dictionary<Entity, Record>();
        static readonly BBI.Core.Events.EventHandler<WeaponTriggerEvent> Handler = OnWeaponTrigger;
        static IEventReceiver Receiver;

        // Main/presentation thread only. Currently each human has one basic
        // weapon at binding index zero. Keep this explicit when loadouts change.
        public static void Bind(UnitView view, int weaponBindingIndex)
        {
            if (view == null) return;
            if (weaponBindingIndex < 0) throw new ArgumentOutOfRangeException("weaponBindingIndex");
            lock (Gate)
            {
                Record record;
                if (!Records.TryGetValue(view.EntityID, out record))
                    Records.Add(view.EntityID, new Record { WeaponBindingIndex = weaponBindingIndex });
                else if (record.WeaponBindingIndex != weaponBindingIndex)
                    throw new InvalidOperationException("Humanoid presentation weapon binding changed without Unbind");
            }
        }
        public static void Unbind(Entity entity)
        {
            lock (Gate) Records.Remove(entity);
        }
        public static void UnbindAll()
        {
            DetachReceiver();
            lock (Gate) Records.Clear();
        }
        // Call only while presentation dt > zero; leave events queued on pause.
        public static bool TryTake(Entity entity, out HumanoidWeaponVisualEvent value)
        {
            lock (Gate)
            {
                Record record;
                if (Records.TryGetValue(entity, out record) && record.Events.Count != 0)
                { value = record.Events.Dequeue(); return true; }
            }
            value = new HumanoidWeaponVisualEvent(); return false;
        }
        public static int DroppedEvents(Entity entity)
        {
            lock (Gate) { Record record; return Records.TryGetValue(entity, out record) ? record.DroppedEvents : 0; }
        }
        // Bind the native simulation receiver on its own thread. Aim and target
        // are copied before the mutable fire operation can change again.
        public static void SimTick(object sim)
        {
            bool bound; lock (Gate) bound = Records.Count != 0;
            if (!bound) { DetachReceiver(); return; }
            IEventReceiver current = (IEventReceiver)NativeRead.Get(sim,"SimEventSystem");
            if (object.ReferenceEquals(current, Receiver)) return;
            DetachReceiver();
            if (current == null) return;
            current.AddHandler<WeaponTriggerEvent>(Handler); Receiver = current;
        }
        static void DetachReceiver()
        {
            IEventReceiver old = Receiver; Receiver = null;
            if (old != null) old.RemoveHandler<WeaponTriggerEvent>(Handler);
        }
        static void OnWeaponTrigger(WeaponTriggerEvent message)
        {
            if (message == null) return;
            HumanoidWeaponVisualEventKind kind;
            if (message.Trigger == WeaponFireTrigger.WindUpStart) kind = HumanoidWeaponVisualEventKind.Windup;
            else if (message.Trigger == WeaponFireTrigger.FireRoundHit || message.Trigger == WeaponFireTrigger.FireRoundMiss)
                kind = HumanoidWeaponVisualEventKind.Round;
            else if (message.Trigger == WeaponFireTrigger.EnteringIdle || message.Trigger == WeaponFireTrigger.WindDownStart)
                kind = HumanoidWeaponVisualEventKind.IdleOrWinddown;
            else return; // FirstRoundFired accompanies a normal round: never count it twice.
            lock (Gate)
            {
                Record record;
                if (!Records.TryGetValue(message.Entity, out record) || message.WeaponBindingIndex != record.WeaponBindingIndex) return;
                record.EventSequence++;
                if (kind == HumanoidWeaponVisualEventKind.Windup) record.WindupSequence++;
                if (kind == HumanoidWeaponVisualEventKind.Round) record.RoundSequence++;
                HumanoidWeaponVisualEvent value = new HumanoidWeaponVisualEvent();
                value.Kind = kind; value.EventSequence = record.EventSequence;
                value.WindupSequence = record.WindupSequence; value.RoundSequence = record.RoundSequence;
                value.WeaponBindingIndex = message.WeaponBindingIndex; value.TurretIndex = message.TurretIndex;
                value.Target = Entity.None;
                value.Hit=message.Trigger==WeaponFireTrigger.FireRoundHit;
                // Copy optional aiming primitives immediately; do not retain the
                // mutable native WeaponFireOperation after this event callback.
                object fireOperation;
                if (NativeRead.TryGet(message, "WeaponFireOp", out fireOperation) && fireOperation != null)
                {
                    try
                    {
                        Vector2r aim = (Vector2r)NativeRead.Get(fireOperation, "AimTurretAtPosition");
                        value.AimX = Fixed64.UnsafeFloatValue(aim.X); value.AimZ = Fixed64.UnsafeFloatValue(aim.Y); value.HasAim = true;
                        value.Target = (Entity)NativeRead.Get(fireOperation, "CurrentTarget");
                        value.NativeLastFiredFrame = Convert.ToUInt32(NativeRead.Get(fireOperation, "LastFiredAtFrame"));
                    }
                    catch (Exception) { value.HasAim = false; }
                }
                if (record.Events.Count == MaximumQueuedEventsPerUnit) { record.Events.Dequeue(); record.DroppedEvents++; }
                record.Events.Enqueue(value);
            }
        }
    }
}
