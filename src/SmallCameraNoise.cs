using System;
using System.Reflection;
using BBI.Unity.Core.Data;
using BBI.Unity.Game.FX;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Host-owned, main-thread helper. No Update/LateUpdate and no scene search.
    // The host supplies the already known UserCamera and native focus distance.
    public sealed class SmallCameraNoise
    {
        readonly UserCamera camera;
        readonly float nearNoise,nearShake,restoreStart,restoreEnd;
        Camera renderCamera;
        CrossFadingNoiseFilter noise;
        CameraShake shake;
        PrivateFilter first,second;
        Vector2 originalPositionScale,originalRotationScale,lastPositionScale,lastRotationScale;
        bool shakeApplied,stopped;
        string lastError=String.Empty;
        float noiseStrength=1f,shakeStrength=1f;

        public SmallCameraNoise(UserCamera camera) : this(camera,.15f,.4f,40f,250f) { }
        public SmallCameraNoise(UserCamera camera,float nearNoiseStrength,float nearShakeStrength,float fullNearDistance,float nativeDistance)
        {
            this.camera=camera; nearNoise=nearNoiseStrength; nearShake=nearShakeStrength;
            restoreStart=fullNearDistance; restoreEnd=nativeDistance;
            if(!SmallCameraNoiseMath.ValidSettings(nearNoise,restoreStart,restoreEnd) ||
               !SmallCameraNoiseMath.ValidSettings(nearShake,restoreStart,restoreEnd))
                throw new ArgumentException("Finite strengths in [0,1] and increasing nonnegative distances required");
        }
        public float AppliedNoiseStrength { get { return noiseStrength; } }
        public float AppliedShakeStrength { get { return shakeStrength; } }
        public int PrivateFilterCount { get { return (first!=null&&first.Owned!=null?1:0)+(second!=null&&second.Owned!=null?1:0); } }
        public string LastError { get { return lastError; } }

        public void Tick(float distance,bool sensors)
        {
            if(stopped) return;
            try
            {
                if(camera==null) { Restore(); return; }
                Camera active=camera.ActiveCamera;
                if(active==null) return;
                if(renderCamera!=active) Bind(active);
                bool preserveNative=sensors || camera.IsCinematicMode;
                noiseStrength=SmallCameraNoiseMath.Strength(distance,preserveNative,nearNoise,restoreStart,restoreEnd);
                shakeStrength=SmallCameraNoiseMath.Strength(distance,preserveNative,nearShake,restoreStart,restoreEnd);
                if(first!=null) first.Apply(noiseStrength);
                if(second!=null) second.Apply(noiseStrength);
                ApplyShake(shakeStrength);
            }
            catch(Exception error)
            {
                lastError=error.GetType().Name+": "+error.Message;
                // A presentation-only reduction cannot poison global Runtime state.
                Restore();
            }
        }
        void Bind(Camera active)
        {
            RestoreTargets(); renderCamera=active;
            noise=active.GetComponent<CrossFadingNoiseFilter>();
            shake=active.GetComponent<CameraShake>();
            if(noise!=null) { first=new PrivateFilter(noise,false); second=new PrivateFilter(noise,true); }
            if(shake!=null)
            {
                originalPositionScale=shake.PositionScale; originalRotationScale=shake.RotationScale;
                lastPositionScale=originalPositionScale; lastRotationScale=originalRotationScale;
            }
        }
        static bool Same(Vector2 a,Vector2 b) { return a.x==b.x && a.y==b.y; }
        void ApplyShake(float strength)
        {
            if(shake==null) return;
            // Honour an external change as the new baseline, without multiplying our
            // own prior result again. Native CameraShake.Update only reads these.
            if(shakeApplied && !Same(shake.PositionScale,lastPositionScale)) originalPositionScale=shake.PositionScale;
            if(shakeApplied && !Same(shake.RotationScale,lastRotationScale)) originalRotationScale=shake.RotationScale;
            Vector2 position=originalPositionScale*strength,rotation=originalRotationScale*strength;
            if(!Same(shake.PositionScale,position)) shake.PositionScale=position;
            if(!Same(shake.RotationScale,rotation)) shake.RotationScale=rotation;
            lastPositionScale=position; lastRotationScale=rotation; shakeApplied=true;
        }
        void RestoreTargets()
        {
            if(first!=null) { first.Restore(); first=null; }
            if(second!=null) { second.Restore(); second=null; }
            if(shake!=null && shakeApplied)
            {
                if(Same(shake.PositionScale,lastPositionScale)) shake.PositionScale=originalPositionScale;
                if(Same(shake.RotationScale,lastRotationScale)) shake.RotationScale=originalRotationScale;
            }
            shakeApplied=false; noise=null; shake=null; renderCamera=null;
        }
        public void Restore()
        {
            if(stopped) return;
            stopped=true;
            try { RestoreTargets(); }
            catch(Exception error) { if(lastError.Length==0) lastError=error.GetType().Name+": "+error.Message; }
            noiseStrength=1f; shakeStrength=1f;
        }

        sealed class PrivateFilter
        {
            const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            static readonly FieldInfo Data=typeof(TransformNoiseFilterAsset).GetField("m_Data",Fields);
            static readonly FieldInfo Position=Data==null?null:Data.FieldType.GetField("m_Position",Fields);
            static readonly FieldInfo Orientation=Data==null?null:Data.FieldType.GetField("m_Orientation",Fields);
            readonly CrossFadingNoiseFilter owner;
            readonly bool second;
            readonly FieldInfo sourceField;
            TransformNoiseFilterAsset original;
            internal TransformNoiseFilterAsset Owned;
            TransformNoiseParams[] basePosition,baseOrientation,privatePosition,privateOrientation;
            float applied=Single.NaN;
            internal PrivateFilter(CrossFadingNoiseFilter owner,bool second) { this.owner=owner; this.second=second; sourceField=typeof(CrossFadingNoiseFilter).GetField(second?"Filter2":"Filter1",Fields); if(sourceField==null) throw new MissingFieldException("Native noise source field"); }
            TransformNoiseFilterAsset Current { get { return sourceField.GetValue(owner) as TransformNoiseFilterAsset; } }
            void Set(TransformNoiseFilterAsset asset) { if(second) owner.SetFilter2(asset); else owner.SetFilter1(asset); }
            static TransformNoiseParams[] Copy(TransformNoiseParams[] source)
            {
                if(source==null) return null;
                TransformNoiseParams[] result=new TransformNoiseParams[source.Length]; Array.Copy(source,result,source.Length); return result;
            }
            internal void Apply(float strength)
            {
                if(owner==null) return;
                TransformNoiseFilterAsset current=Current;
                if(Owned==null || current!=Owned)
                {
                    if(Owned!=null && current==original)
                    {
                        // The native target setter may reattach this same source.
                        // Reuse our clone, rather than allocate another one.
                        Scale(strength); Set(Owned); return;
                    }
                    Drop(false); original=current;
                    if(original==null) return;
                    TransformNoiseFilter source=original.Data as TransformNoiseFilter;
                    if(source==null) return;
                    if(Data==null || Position==null || Orientation==null) throw new MissingFieldException("Native noise data fields");
                    basePosition=Copy(source.Position); baseOrientation=Copy(source.Orientation);
                    privatePosition=Copy(basePosition); privateOrientation=Copy(baseOrientation);
                    object data=Activator.CreateInstance(Data.FieldType,true);
                    Position.SetValue(data,privatePosition); Orientation.SetValue(data,privateOrientation);
                    Owned=RhodesAssetClone59.CreateEmpty<TransformNoiseFilterAsset>();
                    Owned.name=original.name+"_SmallCameraPrivate"; Owned.hideFlags=HideFlags.DontSave;
                    Data.SetValue(Owned,data);
                    if(object.ReferenceEquals(Owned.Data,original.Data)) throw new InvalidOperationException("Noise data clone aliases source");
                    Scale(strength); Set(Owned); return;
                }
                Scale(strength);
            }
            static void ScaleArray(TransformNoiseParams[] baseline,TransformNoiseParams[] target,float strength)
            {
                if(baseline==null) return;
                for(int i=0;i<baseline.Length;i++)
                {
                    TransformNoiseParams value=baseline[i];
                    value.X.Amplitude*=strength; value.Y.Amplitude*=strength; value.Z.Amplitude*=strength;
                    target[i]=value;
                }
            }
            void Scale(float strength)
            {
                if(applied==strength) return;
                ScaleArray(basePosition,privatePosition,strength); ScaleArray(baseOrientation,privateOrientation,strength); applied=strength;
            }
            void Drop(bool restore)
            {
                if(Owned!=null)
                {
                    if(restore && owner!=null && Current==Owned) Set(original);
                    UnityEngine.Object.Destroy(Owned);
                }
                Owned=null; original=null; basePosition=null; baseOrientation=null; privatePosition=null; privateOrientation=null; applied=Single.NaN;
            }
            internal void Restore() { Drop(true); }
        }
    }

    // Plain arithmetic, independent of Unity and safe to validate offline.
    public static class SmallCameraNoiseMath
    {
        static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        public static bool ValidSettings(float near,float start,float end)
        { return Finite(near)&&Finite(start)&&Finite(end)&&near>=0f&&near<=1f&&start>=0f&&end>start; }
        public static float Strength(float distance,bool preserveNative,float near,float start,float end)
        {
            if(preserveNative || !Finite(distance) || !ValidSettings(near,start,end)) return 1f;
            if(distance<=start) return near;
            if(distance>=end) return 1f;
            float t=(distance-start)/(end-start); t=t*t*(3f-2f*t);
            return near+(1f-near)*t;
        }
    }
}
