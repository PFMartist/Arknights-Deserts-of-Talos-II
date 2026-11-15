using System;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Core.Events;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Unity.Game;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Read-only presentation bridge. It does not schedule fire, spawn projectiles,
    // change targets, or apply damage. Call Read after native UnitView updates.
    public static class BvmNativeState
    {
        public struct Snapshot
        {
            public bool Valid;
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Velocity;
            // World yaw degrees, not hull-relative. Pitch is positive elevation.
            public float TurretYawDegrees;
            public float TurretPitchDegrees;
            public bool HasTurretYaw;
            public bool HasTurretPitch;
            public int RoundSequence;
        }

        static readonly object Gate = new object();
        static readonly Dictionary<Entity, int> Rounds = new Dictionary<Entity, int>();
        static readonly Dictionary<Entity, int> LastRoundBindings = new Dictionary<Entity, int>();
        struct RoundStamp{public int Tick;public float SubFrame;}
        static readonly Dictionary<Entity,RoundStamp> RoundTimes=new Dictionary<Entity,RoundStamp>();
        static readonly Dictionary<int, Transform> PitchBones = new Dictionary<int, Transform>();
        static readonly BBI.Core.Events.EventHandler<WeaponTriggerEvent> RoundHandler = OnWeaponTrigger;
        static IEventReceiver Receiver;

        public static void Bind(UnitView view)
        {
            if (view != null) Bind(view.EntityID);
        }

        public static void Bind(Entity entity)
        {
            lock (Gate)
                if (!Rounds.ContainsKey(entity)) { Rounds.Add(entity, 0); LastRoundBindings.Add(entity, -1);RoundTimes.Add(entity,new RoundStamp{Tick=-1}); }
            EnsureReceiver();
        }

        public static void Unbind(UnitView view)
        {
            if (view == null) return;
            PitchBones.Remove(view.GetInstanceID());
            Unbind(view.EntityID);
        }

        public static void ForgetView(UnitView view)
        {
            if(view!=null)PitchBones.Remove(view.GetInstanceID());
        }
        public static void Unbind(Entity entity)
        {
            bool empty;
            lock (Gate) { Rounds.Remove(entity); LastRoundBindings.Remove(entity);RoundTimes.Remove(entity); empty = Rounds.Count == 0; }
            if (empty) DetachReceiver();
        }

        // Session/unload cleanup, including the exact same native delegate.
        public static void Unbind()
        {
            DetachReceiver();
            lock (Gate) { Rounds.Clear(); LastRoundBindings.Clear();RoundTimes.Clear(); }
            PitchBones.Clear();
        }

        public static int GetRoundSequence(Entity entity)
        {
            // Read-only from either thread; Bind/Read attach on the presentation thread.
            lock (Gate)
            {
                int value;
                return Rounds.TryGetValue(entity, out value) ? value : 0;
            }
        }

        public static int GetLastRoundWeaponBindingIndex(Entity entity)
        {
            lock (Gate)
            {
                int value;
                return LastRoundBindings.TryGetValue(entity,out value)?value:-1;
            }
        }

        public static float GetLastRoundPresentationAge(Entity entity)
        {
            RoundStamp stamp;lock(Gate)if(!RoundTimes.TryGetValue(entity,out stamp)||stamp.Tick<0)return float.PositiveInfinity;
            return RhodesAviationLaunchProfile.NativePresentationAge(stamp.Tick,stamp.SubFrame,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame);
        }

        public static Snapshot Read(UnitView view)
        {
            Snapshot result = new Snapshot();
            if (view == null) return result;
            EnsureReceiver();
            UnitSimStateInterpolator sim = view.SimStateInterpolator;
            if (sim == null) return result;

            result.Valid = true;
            result.Position = sim.CurrentSimPosition;
            result.Forward = sim.CurrentSimForward;
            result.Forward.y = 0f;
            if (result.Forward.sqrMagnitude < 0.000001f)
            {
                result.Forward = view.ForwardHorizontal;
                result.Forward.y = 0f;
            }
            if (result.Forward.sqrMagnitude < 0.000001f) result.Forward = Vector3.forward;
            result.Forward.Normalize();
            result.Velocity = sim.CurrentSimVelocity;
            result.TurretYawDegrees = Mathf.Atan2(result.Forward.x, result.Forward.z) * Mathf.Rad2Deg;

            float[] angles = sim.TurretRotations;
            if (angles != null && sim.TurretRotationCount > 0 && angles.Length > 0)
            {
                result.TurretYawDegrees = angles[0];
                result.HasTurretYaw = true;
            }

            Transform pitch = FindNativePitchBone(view);
            if (pitch != null)
            {
                Vector3 localDirection = pitch.localRotation * Vector3.forward;
                result.TurretPitchDegrees = Mathf.Atan2(localDirection.y,
                    Mathf.Sqrt(localDirection.x * localDirection.x + localDirection.z * localDirection.z)) * Mathf.Rad2Deg;
                result.HasTurretPitch = true;
            }
            result.RoundSequence = GetRoundSequence(view.EntityID);
            return result;
        }

        static void EnsureReceiver()
        {
            bool hasBindings;
            lock (Gate) hasBindings = Rounds.Count > 0;
            if (!hasBindings) return;
            IEventReceiver current = ShipbreakersMain.SimToPresentationEventSystem;
            if (object.ReferenceEquals(current, Receiver)) return;
            DetachReceiver();
            if (current == null) return;
            current.AddHandler<WeaponTriggerEvent>(RoundHandler);
            Receiver = current;
        }

        static void DetachReceiver()
        {
            IEventReceiver old = Receiver;
            Receiver = null;
            if (old != null) old.RemoveHandler<WeaponTriggerEvent>(RoundHandler);
        }

        static void OnWeaponTrigger(WeaponTriggerEvent message)
        {
            if (message == null ||
                (message.Trigger != WeaponFireTrigger.FireRoundHit &&
                 message.Trigger != WeaponFireTrigger.FireRoundMiss)) return;
            lock (Gate)
            {
                int value;
                if (!Rounds.TryGetValue(message.Entity, out value)) return;
                Rounds[message.Entity] = unchecked(value + 1);
                LastRoundBindings[message.Entity] = message.WeaponBindingIndex;
                RoundTimes[message.Entity]=new RoundStamp{Tick=ShipbreakersMain.CurrentSimTick,SubFrame=ShipbreakersMain.CurrentSimSubFrame};
            }
        }

        // UnitView has no public pitch angle. These are the exact native cached
        // TurretData.Pitch.Bone fields verified in build 12339551 with Cecil.
        // Retry until native spawn initialization supplies its turret cache.
        static Transform FindNativePitchBone(UnitView view)
        {
            int key = view.GetInstanceID();
            Transform cached;
            if (PitchBones.TryGetValue(key, out cached) && cached != null) return cached;
            Array turrets = NativeRead.Get(view, "mTurretData") as Array;
            if (turrets == null || turrets.Length == 0) return null;
            object turret = turrets.GetValue(0);
            object pitch = NativeRead.Get(turret, "Pitch");
            Transform bone = NativeRead.Get(pitch, "Bone") as Transform;
            if (bone != null) PitchBones[key] = bone;
            return bone;
        }
    }
}
