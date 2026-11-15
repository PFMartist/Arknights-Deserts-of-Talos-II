using System;

namespace EndfieldDoK.MapUnitLab
{
    // Pure presentation math. No Unity, native entities, commands or damage.
    public enum HumanoidLocomotionRole { Idle, Walk, Run, Sprint }

    public sealed class HumanoidAnimationProfile
    {
        public float IdleDuration = 3f;
        public float WalkDuration = 2f;
        public float RunDuration = .8f;
        public float SprintDuration=.5333334f,SprintReferenceSpeed=7f,SprintEnterSpeed=5.8f,SprintExitSpeed=5.2f;
        public bool HasSprint;
        public float AttackDuration = 1f;
        public float WalkReferenceSpeed = 1.4f;
        public float RunReferenceSpeed = 3.6f;
        public float MoveEnterSpeed = .12f;
        public float MoveExitSpeed = .06f;
        public float RunEnterSpeed = 2.65f;
        public float RunExitSpeed = 2.1f;
        public float MinimumPlaybackRate = .55f;
        public float MaximumPlaybackRate = 1.6f;
        public float RateResponseSeconds = .09f;
        // An observed FireRound event is authoritative. Start a non-looping
        // attack at its audited release marker, rather than delaying native fire.
        public float AttackReleaseTime;
        public bool AttackReleaseTimeKnown;
        public bool PreserveWindupAttackOnRounds = true;
        public bool HasRun = true;
        public bool HasAttack = true;

        public void Validate()
        {
            Positive(IdleDuration, "IdleDuration"); Positive(WalkDuration, "WalkDuration");
            Positive(WalkReferenceSpeed, "WalkReferenceSpeed"); Positive(RateResponseSeconds, "RateResponseSeconds");
            if (HasRun) { Positive(RunDuration, "RunDuration"); Positive(RunReferenceSpeed, "RunReferenceSpeed"); }
            if(HasSprint){Positive(SprintDuration,"SprintDuration");Positive(SprintReferenceSpeed,"SprintReferenceSpeed");if(!HasRun||SprintExitSpeed<=RunEnterSpeed||SprintEnterSpeed<=SprintExitSpeed)throw new ArgumentException("Invalid sprint thresholds");}
            if (HasAttack)
            {
                Positive(AttackDuration, "AttackDuration");
                if (!Finite(AttackReleaseTime) || AttackReleaseTime < 0f || AttackReleaseTime >= AttackDuration)
                    throw new ArgumentOutOfRangeException("AttackReleaseTime");
            }
            if (!Finite(MoveExitSpeed) || MoveExitSpeed < 0f || !Finite(MoveEnterSpeed) || MoveEnterSpeed <= MoveExitSpeed ||
                !Finite(RunExitSpeed) || RunExitSpeed <= MoveEnterSpeed || !Finite(RunEnterSpeed) || RunEnterSpeed <= RunExitSpeed ||
                !Finite(MinimumPlaybackRate) || MinimumPlaybackRate <= 0f || !Finite(MaximumPlaybackRate) || MaximumPlaybackRate < MinimumPlaybackRate)
                throw new ArgumentException("Humanoid animation thresholds or rates are invalid");
        }
        static void Positive(float value, string name) { if (!Finite(value) || value <= 0f) throw new ArgumentOutOfRangeException(name); }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }

    public sealed class HumanoidAnimationCore
    {
        readonly HumanoidAnimationProfile Profile;
        // Per-instance current attack timing. Never modify cached Data.Actions.
        float InstanceAttackDuration,InstanceAttackReleaseTime;
        bool InstanceAttackReleaseTimeKnown;
        float InstanceWindupRate=1f;
        public void SetAttackTiming(float duration,float releaseTime,bool releaseKnown)
        {SetAttackTiming(duration,releaseTime,releaseKnown,0f);}
        // An optional, known native windup maps the source pre-release portion
        // into that actual interval. Only a real unfired windup uses the rate;
        // native FireRound remains authoritative and recovery returns to1x.
        public void SetAttackTiming(float duration,float releaseTime,bool releaseKnown,float expectedNativeWindupSeconds)
        {
            if(float.IsNaN(duration)||float.IsInfinity(duration)||duration<=0f||
                float.IsNaN(releaseTime)||float.IsInfinity(releaseTime)||releaseTime<0f||releaseTime>=duration||
                float.IsNaN(expectedNativeWindupSeconds)||float.IsInfinity(expectedNativeWindupSeconds)||expectedNativeWindupSeconds<0f)
                throw new ArgumentOutOfRangeException("duration/releaseTime");
            float rate=releaseKnown&&expectedNativeWindupSeconds>0f?releaseTime/expectedNativeWindupSeconds:1f;
            if(float.IsNaN(rate)||float.IsInfinity(rate))throw new ArgumentOutOfRangeException("expectedNativeWindupSeconds");
            InstanceAttackDuration=duration;InstanceAttackReleaseTime=releaseTime;InstanceAttackReleaseTimeKnown=releaseKnown;
            InstanceWindupRate=rate;
        }

        readonly float InitialPhase;
        float Phase, PendingDelay, PendingWindupDelay;
        long PendingSequence, PendingWindupSequence;
        bool PendingCancel,PendingPreview;
        public HumanoidLocomotionRole LocomotionRole { get; private set; }
        public float LocomotionPhase { get { return Phase; } }
        public float LocomotionClock { get { return Phase * Duration(LocomotionRole); } }
        public float LocomotionPlaybackRate { get; private set; }
        public int LocomotionTransitions { get; private set; }
        public bool LocomotionChanged { get; private set; }
        public bool AttackActive { get; private set; }
        public float AttackClock { get; private set; }
        public bool AttackStarted { get; private set; }
        public bool RoundObserved { get; private set; }
        public bool AttackBeganAtWindup { get; private set; }
        public bool AttackHasFired { get; private set; }
        public long AttackSequence { get; private set; }
        public long LastReceivedSequence { get; private set; }
        public long LastReceivedWindupSequence { get; private set; }
        public int SupersededPendingShots { get; private set; }

        public HumanoidAnimationCore(HumanoidAnimationProfile profile, int memberIndex)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (memberIndex < 0 || memberIndex >= 12) throw new ArgumentOutOfRangeException("memberIndex");
            profile.Validate(); Profile = profile;
            InitialPhase = (memberIndex * .173f / profile.WalkDuration) % 1f;
            Reset();
        }
        public void Reset()
        {
            SetAttackTiming(Profile.HasAttack?Profile.AttackDuration:1f,Profile.HasAttack?Profile.AttackReleaseTime:0f,Profile.HasAttack&&Profile.AttackReleaseTimeKnown);
            Phase = InitialPhase; PendingDelay = 0f; PendingSequence = 0; PendingWindupDelay = 0f; PendingWindupSequence = 0; PendingCancel = false; PendingPreview = false;
            LocomotionRole = HumanoidLocomotionRole.Idle; LocomotionPlaybackRate = 1f;
            LocomotionTransitions = 0; LocomotionChanged = false;
            AttackActive = false; AttackClock = 0f; AttackStarted = false; RoundObserved = false; AttackBeganAtWindup = false; AttackHasFired = false;
            AttackSequence = 0; LastReceivedSequence = 0; LastReceivedWindupSequence = 0; SupersededPendingShots = 0;
        }

        // Explicit local animation preview only: no native round or damage.
        public void RequestAttackPreview() { if (Profile.HasAttack) PendingPreview = true; }

        public bool ObserveWindup(long sequence, float visualDelaySeconds)
        {
            if (!Profile.HasAttack || sequence <= LastReceivedWindupSequence) return false;
            ValidateDelay(visualDelaySeconds); LastReceivedWindupSequence = sequence;
            // A later accepted windup supersedes an earlier idle in this batch.
            PendingCancel=false;
            PendingWindupSequence = sequence; PendingWindupDelay = visualDelaySeconds;
            return true;
        }
        // Cancellation only interrupts a real native windup that has not fired.
        public void ObserveNativeIdleOrWinddown() { PendingCancel = true; }

        // Call only for observed native FireRoundHit / FireRoundMiss events.
        // One selected member receives each native round. Repeated sequence IDs
        // cannot retrigger. No aim/target detection invents an attack here.
        public bool ObserveFire(long sequence, float visualDelaySeconds)
        {
            if (!Profile.HasAttack || sequence <= LastReceivedSequence) return false;
            ValidateDelay(visualDelaySeconds);
            LastReceivedSequence = sequence;
            if (PendingSequence != 0) SupersededPendingShots++;
            PendingSequence = sequence; PendingDelay = visualDelaySeconds;
            return true;
        }

        public void Step(float actualMetresPerSecond, float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException("deltaTime");
            if (float.IsNaN(actualMetresPerSecond) || float.IsInfinity(actualMetresPerSecond) || actualMetresPerSecond < 0f)
                throw new ArgumentOutOfRangeException("actualMetresPerSecond");
            // Freeze all visible state and event flags while paused. The caller
            // also leaves native event messages queued until presentation resumes.
            if (deltaTime == 0f) return;
            LocomotionChanged = false; AttackStarted = false; RoundObserved = false;
            HumanoidLocomotionRole next = SelectRole(actualMetresPerSecond);
            if (next != LocomotionRole)
            {
                bool oldMoving = LocomotionRole != HumanoidLocomotionRole.Idle;
                bool nextMoving = next != HumanoidLocomotionRole.Idle;
                // Preserve normalized gait phase between walk and run. An idle
                // boundary starts the authored member phase for a fresh blend.
                if (!oldMoving || !nextMoving) Phase = InitialPhase;
                LocomotionRole = next; LocomotionTransitions++; LocomotionChanged = true;
            }
            float reference = LocomotionRole==HumanoidLocomotionRole.Sprint?Profile.SprintReferenceSpeed:LocomotionRole == HumanoidLocomotionRole.Run ? Profile.RunReferenceSpeed : Profile.WalkReferenceSpeed;
            float requestedRate = LocomotionRole == HumanoidLocomotionRole.Idle ? 1f : Clamp(actualMetresPerSecond / reference, Profile.MinimumPlaybackRate, Profile.MaximumPlaybackRate);
            float response = 1f - (float)Math.Exp(-deltaTime / Profile.RateResponseSeconds);
            LocomotionPlaybackRate += (requestedRate - LocomotionPlaybackRate) * response;
            Phase = (Phase + deltaTime * LocomotionPlaybackRate / Duration(LocomotionRole)) % 1f;

            // Ordinary infantry fire is independently gated in the native
            // weapon processor. Do not slide a kneeling/standing attack pose
            // through the walking formation, or replay stale shots on arrival.
            if (actualMetresPerSecond > Profile.MoveExitSpeed)
            {
                AttackActive = false; AttackClock = 0f;
                AttackBeganAtWindup = false; AttackHasFired = false;
                PendingPreview = false; PendingCancel = false;
                PendingWindupSequence = 0; PendingWindupDelay = 0f;
                PendingSequence = 0; PendingDelay = 0f;
                return;
            }

            if (AttackActive)
            {
                AttackClock += deltaTime*(AttackBeganAtWindup&&!AttackHasFired?InstanceWindupRate:1f);
                if (AttackBeganAtWindup && !AttackHasFired && InstanceAttackReleaseTimeKnown)
                    AttackClock = Math.Min(AttackClock, InstanceAttackReleaseTime);
                if (AttackClock >= InstanceAttackDuration) { AttackClock = InstanceAttackDuration; AttackActive = false; }
            }
            if (PendingPreview)
            {
                PendingPreview = false; AttackActive = true; AttackStarted = true; AttackClock = 0f;
                AttackBeganAtWindup = false; AttackHasFired = false; AttackSequence = 0;
            }
            if (PendingCancel)
            {
                PendingWindupSequence = 0; PendingWindupDelay = 0f;
                if (AttackBeganAtWindup && !AttackHasFired) AttackActive = false;
                PendingCancel = false;
            }
            if (PendingWindupSequence != 0)
            {
                PendingWindupDelay -= deltaTime;
                if (PendingWindupDelay <= 0f)
                {
                    PendingWindupSequence = 0; PendingWindupDelay = 0f;
                    AttackActive = true; AttackStarted = true; AttackClock = 0f;
                    AttackBeganAtWindup = true; AttackHasFired = false;
                }
            }
            if (PendingSequence != 0)
            {
                PendingDelay -= deltaTime;
                if (PendingDelay <= 0f)
                {
                    AttackSequence = PendingSequence; PendingSequence = 0; PendingDelay = 0f; RoundObserved = true;
                    if (!(AttackActive && AttackBeganAtWindup && Profile.PreserveWindupAttackOnRounds))
                    {
                        AttackActive = true; AttackStarted = true; AttackBeganAtWindup = false;
                        AttackClock = InstanceAttackReleaseTimeKnown ? InstanceAttackReleaseTime : 0f;
                    }
                    else if (InstanceAttackReleaseTimeKnown) AttackClock = InstanceAttackReleaseTime;
                    AttackHasFired = true;
                }
            }
        }

        HumanoidLocomotionRole SelectRole(float speed)
        {
            if(Profile.HasSprint&&speed>=Profile.MoveExitSpeed&&(LocomotionRole==HumanoidLocomotionRole.Sprint?speed>=Profile.SprintExitSpeed:speed>=Profile.SprintEnterSpeed))return HumanoidLocomotionRole.Sprint;
            if (LocomotionRole == HumanoidLocomotionRole.Idle)
            {
                if (speed <= Profile.MoveEnterSpeed) return HumanoidLocomotionRole.Idle;
                return Profile.HasRun && speed >= Profile.RunEnterSpeed ? HumanoidLocomotionRole.Run : HumanoidLocomotionRole.Walk;
            }
            if (speed < Profile.MoveExitSpeed) return HumanoidLocomotionRole.Idle;
            if (!Profile.HasRun) return HumanoidLocomotionRole.Walk;
            if (LocomotionRole == HumanoidLocomotionRole.Run||LocomotionRole==HumanoidLocomotionRole.Sprint)
                return speed < Profile.RunExitSpeed ? HumanoidLocomotionRole.Walk : HumanoidLocomotionRole.Run;
            return speed > Profile.RunEnterSpeed ? HumanoidLocomotionRole.Run : HumanoidLocomotionRole.Walk;
        }
        float Duration(HumanoidLocomotionRole role)
        { return role==HumanoidLocomotionRole.Sprint?Profile.SprintDuration:role == HumanoidLocomotionRole.Run ? Profile.RunDuration : role == HumanoidLocomotionRole.Walk ? Profile.WalkDuration : Profile.IdleDuration; }
        static float Clamp(float value, float minimum, float maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }
        static void ValidateDelay(float value)
        { if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > .25f) throw new ArgumentOutOfRangeException("visualDelaySeconds"); }
    }

    public static class HumanoidShotDispatch
    {
        // Deterministic assignment creates no extra simulation entities or shots.
        public static int MemberForNativeRound(long sequence, int memberCount)
        {
            if (sequence <= 0) throw new ArgumentOutOfRangeException("sequence");
            if (memberCount < 1 || memberCount > 12) throw new ArgumentOutOfRangeException("memberCount");
            return (int)((sequence - 1) % memberCount);
        }
        public static float MemberVisualDelay(int memberIndex) { return memberIndex * .011f; }
    }
}
