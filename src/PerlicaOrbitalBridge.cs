// Read-only native simulation/presentation observer for the support ability.
// No mouse/keyboard replacement, targeting commands, projectiles or damage.
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
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static class PerlicaOrbitalBridge
    {
        const int ProjectileComponentID=27; // verified native build 12339551
        enum Kind { Windup, WeaponFired, CancelWindup, Projectile, Lost }
        struct Observation
        {
            public Kind Event;
            public Entity Caster,Projectile;
            public float X,Z,DetonationProgress;
            public bool HasAim,Arrived,Detonated;
            public uint Frame;
        }
        sealed class Presentation
        {
            public readonly PerlicaOrbitalTimeline Timeline=new PerlicaOrbitalTimeline();
            public UnitView View;
            public Vector3 Target;
            public bool HasAim;
            public GameObject Host;
            public PerlicaOrbitalEffect Effect;
            public PerlicaOrbitalAudio Audio;
            public bool AudioStarted;
            public long VoiceAbilitySequence;
            public long DeliveredVoiceSequence;
        }
        static readonly object Gate=new object();
        static readonly Queue<Observation> Queue=new Queue<Observation>();
        static readonly Dictionary<Entity,Presentation> Views=new Dictionary<Entity,Presentation>();
        // Live native objects are never retained or passed to the UI thread.
        static readonly Dictionary<Entity,Observation> SimProjectiles=new Dictionary<Entity,Observation>();
        static readonly BBI.Core.Events.EventHandler<WeaponTriggerEvent> WeaponHandler=OnWeapon;
        static IEventReceiver Receiver;
        static float NextScan;
        static uint LastFrame;
        static bool HaveFrame;

        public static void Reset()
        {
            if(Receiver!=null)Receiver.RemoveHandler<WeaponTriggerEvent>(WeaponHandler);
            Receiver=null;
            foreach(Presentation item in Views.Values)if(item.Host!=null)UnityEngine.Object.DestroyImmediate(item.Host);
            Views.Clear();SimProjectiles.Clear();
            lock(Gate)Queue.Clear();
            NextScan=0f;HaveFrame=false;LastFrame=0;
        }
        public static bool TryGetChannel(Entity caster,out Vector3 aim,out float age)
        {
            Presentation item;
            if(Views.TryGetValue(caster,out item)&&item.HasAim&&item.Timeline.Channeling)
            {aim=item.Target;age=item.Timeline.Age;return true;}
            aim=Vector3.zero;age=0f;return false;
        }
        // Consume after the driver's once-per-frame voice Tick. Native skill
        // observations are processed in Update, before that LateUpdate clock.
        public static bool TryTakeVoiceAcceptance(Entity caster,out long sequence)
        {
            Presentation item;
            if(Views.TryGetValue(caster,out item)&&item.VoiceAbilitySequence>item.DeliveredVoiceSequence)
            {sequence=item.VoiceAbilitySequence;item.DeliveredVoiceSequence=sequence;return true;}
            sequence=0;return false;
        }
        public static void MainTick()
        {
            if(Time.realtimeSinceStartup>=NextScan)
            {
                NextScan=Time.realtimeSinceStartup+.25f;
                foreach(UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))
                {
                    if(view==null||!view.gameObject.activeInHierarchy||view.SimStateInterpolator==null)continue;
                    HumanoidBinding binding=view.GetComponent<HumanoidBinding>();
                    if(binding==null||binding.MemberCount!=1||binding.AssetFolder!="humanoid_trial/perlica")continue;
                    Presentation item;
                    if(!Views.TryGetValue(view.EntityID,out item))
                    {item=new Presentation();Views.Add(view.EntityID,item);}
                    item.View=view;
                }
            }
            float delta=Runtime.PresentationDeltaTime;
            if(delta>0f)
            {
                Observation message;
                while(Take(out message))Apply(message);
                foreach(Presentation item in Views.Values)item.Timeline.Step(delta);
            }
            foreach(Presentation item in Views.Values)
            {
                bool active=item.HasAim&&item.Timeline.Active;
                if(active)EnsurePresentation(item);
                if(item.Effect!=null)item.Effect.Render(item.Target,item.Timeline.Age,PerlicaOrbitalTimeline.ChargeSeconds,
                    PerlicaOrbitalTimeline.FireSeconds,PerlicaOrbitalAssets.RadiusMeters,active);
                if(item.Audio!=null)item.Audio.Tick(delta);
                // Native cancellation is authoritative. If a charging caster
                // disappears without an idle event, discard only that windup;
                // a native projectile already in flight remains committed.
                if(item.Timeline.Current==PerlicaOrbitalTimeline.Phase.Charging&&
                    (item.View==null||!item.View.gameObject.activeInHierarchy||
                     item.View.NonInterpolatedTickState!=null&&item.View.NonInterpolatedTickState.UnitIsDead))
                {item.Timeline.CancelBeforeLaunch();if(item.Audio!=null)item.Audio.Cancel();item.AudioStarted=false;}
            }
        }
        static void EnsurePresentation(Presentation item)
        {
            if(item.Host==null){item.Host=new GameObject("EF_Perlica_OrbitalSupportObserver");item.Host.hideFlags=HideFlags.DontSave;}
            if(item.Effect==null)item.Effect=item.Host.AddComponent<PerlicaOrbitalEffect>();
            if(item.Audio==null)item.Audio=item.Host.AddComponent<PerlicaOrbitalAudio>();
        }
        static void StartAudio(Presentation item)
        {
            if(item.AudioStarted||!item.HasAim)return;EnsurePresentation(item);
            Vector3 caster=item.View!=null&&item.View.SimStateInterpolator!=null?item.View.SimStateInterpolator.CurrentSimPosition:item.Target;
            item.Audio.Designate(item.Target,caster);item.AudioStarted=true;
            item.VoiceAbilitySequence++;
        }
        static bool Take(out Observation observation)
        {
            lock(Gate)
            {
                if(Queue.Count>0){observation=Queue.Dequeue();return true;}
            }
            observation=new Observation();return false;
        }
        static void Push(Observation observation)
        {
            lock(Gate)
            {
                if(Queue.Count>=256)throw new InvalidOperationException("Orbital observation queue overflow; no simulated damage was altered");
                Queue.Enqueue(observation);
            }
        }
        static void OnWeapon(WeaponTriggerEvent message)
        {
            // SimEventSystem callback: copy mutable fire-op primitives here on
            // the simulation thread, before presentation receives any event.
            if(message==null||message.WeaponBindingIndex!=PerlicaOrbitalAssets.WeaponIndex)return;
            Kind kind;
            if(message.Trigger==WeaponFireTrigger.WindUpStart)kind=Kind.Windup;
            else if(message.Trigger==WeaponFireTrigger.FirstRoundFired)kind=Kind.WeaponFired;
            else if(message.Trigger==WeaponFireTrigger.EnteringIdle||message.Trigger==WeaponFireTrigger.WindDownStart)kind=Kind.CancelWindup;
            else return;
            // Entity identity is checked when the immutable event is consumed.
            Observation value=new Observation();value.Event=kind;value.Caster=message.Entity;
            object operation;
            if(NativeRead.TryGet(message,"WeaponFireOp",out operation)&&operation!=null)
            {
                Vector2r aim=(Vector2r)NativeRead.Get(operation,"AimTurretAtPosition");
                value.X=Fixed64.UnsafeFloatValue(aim.X);value.Z=Fixed64.UnsafeFloatValue(aim.Y);value.HasAim=true;
            }
            Push(value);
        }
        static void Apply(Observation value)
        {
            Presentation item;if(!Views.TryGetValue(value.Caster,out item))return;
            if(value.HasAim){item.Target=new Vector3(value.X,0f,value.Z);item.HasAim=true;}
            if(value.Event==Kind.Windup)
            {
                if(!item.Timeline.Active){item.Timeline.Begin();item.AudioStarted=false;}
                StartAudio(item);
                Runtime.Log("PERLICA_ORBITAL_DESIGNATING caster="+value.Caster+"; x="+value.X+"; z="+value.Z+"; radius=12; nativeWindup=True; presentationOnly=True");
            }
            else if(value.Event==Kind.WeaponFired){StartAudio(item);item.Timeline.Launched();if(item.Audio!=null)item.Audio.Launched();}
            else if(value.Event==Kind.CancelWindup)
            {bool charging=item.Timeline.Current==PerlicaOrbitalTimeline.Phase.Charging;item.Timeline.CancelBeforeLaunch();if(charging&&item.Audio!=null){item.Audio.Cancel();item.AudioStarted=false;}}
            else if(value.Event==Kind.Lost)
            {
                item.Timeline.Reset();
                if(item.Audio!=null)item.Audio.Cancel();item.AudioStarted=false;
                Runtime.Log("PERLICA_ORBITAL_PROJECTILE_GONE_BEFORE_DETONATION projectile="+value.Projectile+"; presentationDiscarded=True");
            }
            else
            {
                StartAudio(item);item.Timeline.Launched();if(item.Audio!=null)item.Audio.Launched();
                if(value.Arrived){item.Timeline.SetNativeDetonationProgress(value.DetonationProgress);if(item.Audio!=null)item.Audio.Arrived();}
                if(value.Detonated){item.Timeline.Detonated();if(item.Audio!=null)item.Audio.Detonated();}
            }
        }

        // Existing end-of-Sim.Tick hook, simulation thread only. Native
        // Detonate applies AOE before setting Detonated; removal is next tick.
        public static void SimTick(object sim)
        {
            IEventReceiver current=(IEventReceiver)NativeRead.Get(sim,"SimEventSystem");
            if(!object.ReferenceEquals(current,Receiver))
            {
                if(Receiver!=null)Receiver.RemoveHandler<WeaponTriggerEvent>(WeaponHandler);
                Receiver=current;if(Receiver!=null)Receiver.AddHandler<WeaponTriggerEvent>(WeaponHandler);
            }
            uint frame=Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));
            if(HaveFrame&&frame==LastFrame)return;HaveFrame=true;LastFrame=frame;
            IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
            HashSet<Entity> seen=new HashSet<Entity>();
            foreach(Entity entity in system.Query().Has(ProjectileComponentID))
            {
                object projectile=system.GetComponent<object>(entity,ProjectileComponentID);
                if(projectile==null||(string)NativeRead.Get(projectile,"ProjectileTypeName")!=PerlicaOrbitalAssets.ProjectileKey)continue;
                Observation value=new Observation();value.Event=Kind.Projectile;value.Projectile=entity;
                value.Caster=(Entity)NativeRead.Get(NativeRead.Get(projectile,"ShotSender"),"WeaponOwner");
                if((int)NativeRead.Get(projectile,"WeaponBindingLaunchedFrom")!=PerlicaOrbitalAssets.WeaponIndex)
                    throw new InvalidOperationException("Custom orbital projectile has unexpected weapon binding");
                Vector2r aim=(Vector2r)NativeRead.Get(projectile,"OriginalAimAtPosition");
                value.X=Fixed64.UnsafeFloatValue(aim.X);value.Z=Fixed64.UnsafeFloatValue(aim.Y);value.HasAim=true;
                value.Frame=frame;value.Arrived=(bool)NativeRead.Get(projectile,"Arrived");value.Detonated=(bool)NativeRead.Get(projectile,"Detonated");
                value.DetonationProgress=Fixed64.UnsafeFloatValue((Fixed64)NativeRead.Get(projectile,"DetonationDelayTimeElapsed"));
                seen.Add(entity);Observation previous;bool known=SimProjectiles.TryGetValue(entity,out previous);
                if(!known)Runtime.Log("PERLICA_ORBITAL_NATIVE_PROJECTILE caster="+value.Caster+"; projectile="+entity+"; frame="+frame+"; x="+value.X+"; z="+value.Z+"; damageOwnedByNative=True");
                if(value.Detonated&&(!known||!previous.Detonated))
                    Runtime.Log("PERLICA_ORBITAL_NATIVE_DETONATED projectile="+entity+"; frame="+frame+"; radius=12; configuredCenterDamage=1200; actualNativeAOEAlreadyProcessed=True; finalDamageDependsOnNativeArmourAndFalloff=True");
                if(!known||!previous.Detonated)Push(value);
                SimProjectiles[entity]=value;
            }
            List<Entity> remove=new List<Entity>();
            foreach(KeyValuePair<Entity,Observation> pair in SimProjectiles)
                if(!seen.Contains(pair.Key))
                {
                    if(!pair.Value.Detonated){Observation gone=pair.Value;gone.Event=Kind.Lost;Push(gone);}
                    remove.Add(pair.Key);
                }
            foreach(Entity entity in remove)SimProjectiles.Remove(entity);
        }
    }
}
