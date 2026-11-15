using System;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Presentation only: native simulation still owns movement, aiming, rounds,
    // projectiles and damage. No timers synthesize weapon-fire events.
    public sealed class BvmRigDriver : MonoBehaviour
    {
        static readonly string[] TrackTextures = { "_MainTex", "_BumpMap", "_OcclusionMap" };
        static float NextDiscovery;
        UnitView View;
        BvmEffects Effects;
        BvmRigBinding Rig;
        Entity BoundEntity;
        bool Bound, Initialized, HavePrevious, MovementLogged, TurnLogged, PitchLogged;
        Vector3 PreviousPosition, PreviousForward;
        float[] WheelDegrees, TrackCycles;
        Vector2[,] TrackBaseOffsets;
        Material[] InstanceTracks;
        int LastRound, ShotReports;
        float RecoilAge = 100f;
        float GroundY;

        public static void DiscoverActiveUnits()
        {
            if(!CustomUnits.Ready||Time.realtimeSinceStartup<NextDiscovery)return;
            NextDiscovery=Time.realtimeSinceStartup+.25f;
            foreach(UnitView view in UnitViewDiscovery.Get())
                if(view!=null&&view.gameObject.activeInHierarchy&&view.SimStateInterpolator!=null)Ensure(view);
        }

        public static void Ensure(UnitView view)
        {
            if (view == null || !view.gameObject.activeInHierarchy) return;
            BvmRigDriver driver = view.GetComponent<BvmRigDriver>();
            if(driver!=null&&driver.Initialized&&driver.BoundEntity.Equals(view.EntityID))return;
            BvmRigBinding rig = view.GetComponentInChildren<BvmRigBinding>();
            if (rig == null || rig.ModelRoot == null || !rig.ModelRoot.gameObject.activeInHierarchy) return;
            if(driver==null)driver=view.gameObject.AddComponent<BvmRigDriver>();
            else driver.ReleaseInstance();
            driver.Initialize(view, rig);
        }
        void Initialize(UnitView view, BvmRigBinding rig)
        {
            View = view; Rig = rig;
            HavePrevious=MovementLogged=TurnLogged=PitchLogged=false; ShotReports=0; RecoilAge=100f; GroundY=0f;
            if (Rig.Bones == null || Rig.RigKinds == null || Rig.Bones.Length != Rig.RigKinds.Length ||
                Rig.TrackMaterials == null || Rig.TrackMaterials.Length != 2 || Rig.TrackGauge <= 0f)
                throw new InvalidOperationException("BVM runtime rig is incomplete");
            WheelDegrees = new float[Rig.Bones.Length];
            TrackCycles = new float[Rig.TrackMaterials.Length];
            InstanceTracks = new Material[Rig.TrackMaterials.Length];
            TrackBaseOffsets = new Vector2[Rig.TrackMaterials.Length, TrackTextures.Length];
            Renderer[] renderers = Rig.ModelRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < InstanceTracks.Length; i++)
            {
                Material source = Rig.TrackMaterials[i];
                Material instance = new Material(source);
                instance.name = source.name + "_Instance_" + View.GetInstanceID();
                instance.hideFlags = HideFlags.HideAndDontSave;
                InstanceTracks[i] = instance;
                for (int p = 0; p < TrackTextures.Length; p++)
                    if (instance.HasProperty(TrackTextures[p])) TrackBaseOffsets[i,p] = instance.GetTextureOffset(TrackTextures[p]);
                foreach (Renderer renderer in renderers)
                {
                    Material[] materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int m = 0; m < materials.Length; m++)
                        if (materials[m] == source) { materials[m] = instance; changed = true; }
                    if (changed) renderer.sharedMaterials = materials;
                }
            }
            // Keep the prefab's material references unchanged. Another tank gets
            // independent track phases rather than sharing this one's UVs.
            BoundEntity = view.EntityID;
            BvmNativeState.Bind(BoundEntity); Bound = true;
            LastRound = BvmNativeState.GetRoundSequence(BoundEntity);
            BvmIdentity.Bind(view);
            Effects=view.GetComponent<BvmEffects>();if(Effects==null)Effects=view.gameObject.AddComponent<BvmEffects>();
            Effects.Initialize(view,rig);
            Initialized = true;
            Runtime.Log("BVM_RIG_DRIVER_READY entity=" + BoundEntity + "; ownWorldPose=True; nativeWrapperPitchRollIgnored=True; tracksPerInstance=2; wheelCount=26; nativeRoundEvents=True; groundAnchor=activeMapHeightOrNativeTerrain; ballistic125mmSimulated=False; actualPixelAcceptancePending=True");
        }

        void LateUpdate()
        {
            if (!Initialized || View == null || Rig == null || Rig.ModelRoot == null) return;
            if(!BoundEntity.Equals(View.EntityID)){ReleaseInstance();Initialize(View,Rig);}
            BvmNativeState.Snapshot state = BvmNativeState.Read(View);
            if (!state.Valid) return;
            // Both custom maps own their height surface: flat lab returns zero,
            // while the hub samples the same quantized height used by simulation.
            // Native-map diagnostics may raycast Terrain, never unit colliders.
            if (CustomFlatMap.Ready)
                GroundY = CustomFlatMap.SampleOriginalHeight(new Vector2r(
                    Fixed64.UnsafeFromFloat(state.Position.x), Fixed64.UnsafeFromFloat(state.Position.z)));
            else
            {
                int layer = LayerMask.NameToLayer("Terrain");
                RaycastHit ground;
                if (layer >= 0 && Physics.Raycast(new Vector3(state.Position.x, 2000f, state.Position.z), Vector3.down, out ground, 4000f, 1 << layer))
                    GroundY = ground.point.y;
            }
            Vector3 position = new Vector3(state.Position.x, GroundY, state.Position.z);
            Rig.ModelRoot.position = position;
            Rig.ModelRoot.rotation = Quaternion.LookRotation(state.Forward, Vector3.up);
            AnimateTravel(position, state.Forward);
            AnimateAim(state);
            if(state.RoundSequence<LastRound){LastRound=state.RoundSequence;RecoilAge=100f;}
            if (state.RoundSequence > LastRound)
            {
                int emitted = unchecked(state.RoundSequence - LastRound);
                LastRound = state.RoundSequence; RecoilAge = 0f;
                if (ShotReports < 12)
                {
                    Runtime.Log("BVM_NATIVE_ROUND_ANIMATION entity=" + BoundEntity + "; sequence=" + LastRound + "; newRounds=" + emitted + "; muzzle=" + (Rig.Muzzle == null ? "missing" : Rig.Muzzle.position.ToString()) + "; visualRecoilPrototype=True; damageAppliedByDriver=False");
                    ShotReports++;
                }
            }
            AnimateRecoil();
            if(Effects!=null)Effects.Tick(state,Runtime.PresentationDeltaTime);
        }

        void AnimateTravel(Vector3 position, Vector3 forward)
        {
            if (!HavePrevious)
            {
                PreviousPosition = position; PreviousForward = forward; HavePrevious = true; return;
            }
            Vector3 delta = position - PreviousPosition; delta.y = 0f;
            float turn = Mathf.Atan2(Vector3.Cross(PreviousForward, forward).y,
                Mathf.Clamp(Vector3.Dot(PreviousForward, forward), -1f, 1f));
            Vector3 average = PreviousForward + forward;
            if (average.sqrMagnitude < .000001f) average = forward;
            average.Normalize();
            float distance = Vector3.Dot(delta, average);
            PreviousPosition = position; PreviousForward = forward;
            // Spawn/reposition discontinuities must not become wheel impulses.
            if (delta.sqrMagnitude > 10000f || Mathf.Abs(turn) > 2f) return;
            for (int i = 0; i < Rig.Bones.Length; i++)
            {
                if (Rig.RigKinds[i] != BvmRigBinding.Wheel || Rig.Bones[i] == null) continue;
                float radius = Rig.WheelRadii[i];
                if (radius <= .00001f) continue;
                float travel = distance - Rig.RigSides[i] * turn * Rig.TrackGauge * .5f;
                WheelDegrees[i] = Mathf.Repeat(WheelDegrees[i] + travel / radius * Mathf.Rad2Deg * Rig.WheelSpinSigns[i], 360f);
                Rig.Bones[i].localRotation = Quaternion.AngleAxis(WheelDegrees[i], Rig.RigAxes[i]) * Rig.BindLocalRotations[i];
            }
            for (int i = 0; i < InstanceTracks.Length; i++)
            {
                float length = Rig.TrackLoopLengths[i];
                if (length <= .00001f) continue;
                float travel = distance - Rig.TrackMaterialSides[i] * turn * Rig.TrackGauge * .5f;
                float signedRepeats = Mathf.Abs(Rig.TrackUVRepeats[i]) * Rig.TrackVSigns[i];
                TrackCycles[i] = Mathf.Repeat(TrackCycles[i] + travel / length * signedRepeats, 1f);
                for (int p = 0; p < TrackTextures.Length; p++)
                    if (InstanceTracks[i].HasProperty(TrackTextures[p]))
                    {
                        Vector2 offset = TrackBaseOffsets[i,p]; offset.y += TrackCycles[i];
                        InstanceTracks[i].SetTextureOffset(TrackTextures[p], offset);
                    }
            }
            if (!MovementLogged && Mathf.Abs(distance) > .001f)
            {
                MovementLogged = true;
                Runtime.Log("BVM_TRAVEL_ANIMATION entity=" + BoundEntity + "; signedTravel=" + distance + "; rootY=" + Rig.ModelRoot.position.y + "; pitchRollIsolated=True; wheelAndTrackSource=actualInterpolatedDisplacement");
            }
            if (!TurnLogged && Mathf.Abs(turn) > .0001f)
            {
                TurnLogged = true;
                Runtime.Log("BVM_TRACK_DIFFERENTIAL entity=" + BoundEntity + "; yawRadians=" + turn + "; gauge=" + Rig.TrackGauge + "; sideFormula=distance-side*yaw*gauge/2");
            }
        }

        void AnimateAim(BvmNativeState.Snapshot state)
        {
            if (!state.HasTurretYaw) return;
            float elevation = state.HasTurretPitch ? Mathf.Clamp(state.TurretPitchDegrees, Rig.PitchMin, Rig.PitchMax) : 0f;
            float yaw = state.TurretYawDegrees * Mathf.Deg2Rad, pitch = elevation * Mathf.Deg2Rad;
            Vector3 desiredWorld = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
            Vector3 desired = Rig.ModelRoot.InverseTransformDirection(desiredWorld).normalized;
            // The turret axis is tilted 1.5 degrees. Solve in its orthonormal
            // rest frame, avoiding a heading-dependent elevation error.
            Vector3 axis = Rig.YawAxis.normalized;
            Vector3 restForward = Rig.RecoilAxis.normalized;
            Vector3 right = Vector3.Cross(axis, restForward).normalized;
            float relativeYaw = Mathf.Atan2(Vector3.Dot(desired, right), Vector3.Dot(desired, restForward)) * Mathf.Rad2Deg;
            float relativePitch = -Mathf.Asin(Mathf.Clamp(Vector3.Dot(desired, axis), -1f, 1f)) * Mathf.Rad2Deg;
            relativeYaw = Mathf.Clamp(relativeYaw, Rig.YawMin, Rig.YawMax);
            if (Rig.YawNode >= 0 && Rig.YawNode < Rig.Bones.Length)
                Rig.Bones[Rig.YawNode].localRotation = Quaternion.AngleAxis(relativeYaw, Rig.YawAxis) * Rig.BindLocalRotations[Rig.YawNode];
            if (Rig.PitchNode >= 0 && Rig.PitchNode < Rig.Bones.Length)
                Rig.Bones[Rig.PitchNode].localRotation = Quaternion.AngleAxis(relativePitch, Rig.PitchAxis) * Rig.BindLocalRotations[Rig.PitchNode];
            if (!PitchLogged && state.HasTurretPitch)
            {
                PitchLogged = true;
                Runtime.Log("BVM_AIM_DRIVER entity=" + BoundEntity + "; yawSource=nativeWorldYaw; pitchSource=nativePitchBone; inclinedAxisInverseSolve=True; restElevation=" + Rig.RestElevation + "; nativePitch=" + state.TurretPitchDegrees);
            }
        }
        void AnimateRecoil()
        {
            if (Rig.RecoilNode < 0 || Rig.RecoilNode >= Rig.Bones.Length) return;
            RecoilAge += Runtime.PresentationDeltaTime;
            // A tunable visual prototype, not a measured T-80 recoil profile.
            float kick;
            if (RecoilAge < .045f) kick = Mathf.Clamp01(RecoilAge / .045f);
            else kick = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((RecoilAge - .045f) / .30f));
            Rig.Bones[Rig.RecoilNode].localPosition = Rig.BindLocalPositions[Rig.RecoilNode] - Rig.RecoilAxis * (.24f * Rig.VisualScale * kick);
        }

        void ReleaseInstance()
        {
            BvmNativeState.ForgetView(View);
            if(Bound){BvmNativeState.Unbind(BoundEntity);Bound=false;}
            if(Effects!=null)Effects.Release();
            if(InstanceTracks!=null)
            {
                Renderer[] renderers=Rig==null||Rig.ModelRoot==null?new Renderer[0]:Rig.ModelRoot.GetComponentsInChildren<Renderer>(true);
                for(int i=0;i<InstanceTracks.Length;i++)
                {
                    Material owned=InstanceTracks[i];if(owned==null)continue;
                    foreach(Renderer renderer in renderers)
                    {
                        Material[] slots=renderer.sharedMaterials;bool changed=false;
                        for(int j=0;j<slots.Length;j++)if(slots[j]==owned){slots[j]=Rig.TrackMaterials[i];changed=true;}
                        if(changed)renderer.sharedMaterials=slots;
                    }
                    Destroy(owned);
                }
            }
            if(Rig!=null&&Rig.Bones!=null)
                for(int i=0;i<Rig.Bones.Length;i++)if(Rig.Bones[i]!=null)
                {
                    Rig.Bones[i].localPosition=Rig.BindLocalPositions[i];
                    Rig.Bones[i].localRotation=Rig.BindLocalRotations[i];
                }
            InstanceTracks=null;TrackBaseOffsets=null;WheelDegrees=null;TrackCycles=null;Initialized=false;HavePrevious=false;
        }
        void OnDisable(){ReleaseInstance();}
        void OnDestroy(){ReleaseInstance();}
    }
}