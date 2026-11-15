using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BBI.Core.Events;
using BBI.Unity.Core.World;
using BBI.Unity.Game;
using BBI.Unity.Game.Events;
using BBI.Unity.Game.ToReplace;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Stage4 and PresentationEventSystem only. No Update component, scene searches,
    // camera movement, clear flags, culling masks, fog assets or shared-material edits.
    public static class EndfieldSensorTheme
    {
        static readonly Color Ring = new Color(.34f,.36f,.35f,1f);
        static readonly Color Tick = new Color(.66f,.69f,.66f,1f);
        static readonly Color White = new Color(236f/255f,237f/255f,231f/255f,1f);
        static readonly Color Yellow = new Color(238f/255f,229f/255f,46f/255f,1f);
        static readonly Color Shadow = new Color(.025f,.027f,.029f,1f);
        static ThemeState state;
        static IEventSystem events;
        static int transitionLogs;
        static bool bindErrorLogged;
        static float retryAt;
        static string status = "not bound";

        public static bool Bound { get { return state != null; } }
        public static string Status { get { return status; } }

        // Repeated calls after success perform only the theme flag and reference tests.
        public static void MainTick(object main)
        {
            if(!EndfieldHudTheme.Active)
            {
                if(state != null || events != null) Release();
                return;
            }
            IEventSystem currentEvents = ShipbreakersMain.PresentationEventSystem;
            if(state != null && object.ReferenceEquals(state.Refs.Main,main) && object.ReferenceEquals(events,currentEvents)) return;
            if(state != null || events != null) Release();
            if(main == null || currentEvents == null || Time.unscaledTime < retryAt) return;
            retryAt = Time.unscaledTime + .5f;
            try
            {
                SceneRefs refs;
                if(!SceneRefs.TryResolve(main,out refs)) { status = "waiting for native camera/compass"; return; }
                ThemeState prepared = new ThemeState(refs);
                try
                {
                    prepared.ApplyCompass();
                    currentEvents.AddHandler<SensorsManagerEvent>(OnSensorsChanged);
                }
                catch { prepared.Restore(); throw; }
                state = prepared;
                events = currentEvents;
                status = "bound: " + prepared.LineCount + " compass lines + " + prepared.LabelCount + " labels";
                Log("ENDFIELD_SENSOR_BOUND: LineCount=" + prepared.LineCount + "; LabelCount=" + prepared.LabelCount +
                    "; events=True; compassReady=" + prepared.CompassReady + "; inSensors=" + refs.InSensors + "; background=NativeUntouched; backgroundFSMEdits=False");
            }
            catch(Exception error)
            {
                status = "sensor theme bind: " + error.GetType().Name + ": " + error.Message;
                if(!bindErrorLogged) { bindErrorLogged=true; Log("ENDFIELD_SENSOR_BIND_FAILED: " + status); }
            }
        }

        static void OnSensorsChanged(SensorsManagerEvent message)
        {
            ThemeState active = state;
            if(active == null) return;
            try
            {
                if(!EndfieldHudTheme.Active) { active.RestoreVisuals(); return; }
                // Follow the same known native references when a later session rebuilt
                // the camera or compass. This is per transition, never a global search.
                SceneRefs latest;
                if(SceneRefs.TryResolve(active.Refs.Main,out latest)) active.Refresh(latest);
                active.ApplyCompass();
                if(transitionLogs < 4)
                {
                    transitionLogs++;
                    Log("ENDFIELD_SENSOR_TRANSITION: active=" + message.Active + "; LineCount=" + active.LineCount +
                        "; LabelCount=" + active.LabelCount + "; compassReady=" + active.CompassReady);
                }
            }
            catch(Exception error) { status = "sensor transition: " + error.GetType().Name + ": " + error.Message; }
        }

        // Root may call this on session teardown or plugin shutdown. Disabling the
        // HUD theme also calls it on the next Stage4 MainTick.
        public static void Release()
        {
            if(events != null)
            {
                try { events.RemoveHandler<SensorsManagerEvent>(OnSensorsChanged); }
                catch(Exception error) { status = "sensor event release: " + error.Message; }
                events = null;
            }
            if(state != null) { state.Restore(); state = null; }
            transitionLogs = 0;
            bindErrorLogged = false;
            retryAt = 0f;
            status = "released";
        }

        static void Log(string message)
        {
            try { Runtime.Log(message); } catch { }
        }

        sealed class SceneRefs
        {
            static readonly BindingFlags Instance = BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            static readonly FieldInfo MainHUD = typeof(ShipbreakersMain).GetField("mHUDSystemInstance",Instance);
            static readonly FieldInfo MainCamera = typeof(ShipbreakersMain).GetField("mGameCamera",Instance);
            static readonly FieldInfo HUDCamera = typeof(BBI.Unity.Game.HUD.HUDSystem).GetField("mActiveCamera",Instance);
            static readonly FieldInfo HUDCompass = typeof(BBI.Unity.Game.HUD.HUDSystem).GetField("mSensorsCompassObject",Instance);
            internal static readonly FieldInfo CompassLines = typeof(CompassDrawer).GetField("mLineRenderers",Instance);
            internal static readonly FieldInfo CompassLabels = typeof(CompassDrawer).GetField("mLabels",Instance);
            internal static readonly FieldInfo CompassMaterial = typeof(CompassDrawer).GetField("mLineMaterial",Instance);
            internal object Main;
            internal CameraBase CameraBase;
            internal Camera Camera;
            internal CompassDrawer Compass;
            internal bool InSensors { get { return UserCamera.IsSensorsModeActive; } }

            internal static bool TryResolve(object main,out SceneRefs refs)
            {
                refs = null;
                if(main == null || MainHUD == null || HUDCamera == null || HUDCompass == null ||
                    CompassLines == null || CompassLabels == null || CompassMaterial == null) return false;
                object hud = MainHUD.GetValue(main);
                if(hud == null) return false;
                CameraBase cameraBase = HUDCamera.GetValue(hud) as CameraBase;
                if(cameraBase == null && MainCamera != null) cameraBase = MainCamera.GetValue(main) as CameraBase;
                Camera camera = cameraBase == null ? null : cameraBase.ActiveCamera;
                GameObject compassObject = HUDCompass.GetValue(hud) as GameObject;
                CompassDrawer compass = compassObject == null ? null : compassObject.GetComponent<CompassDrawer>();
                if(camera == null || compass == null) return false;
                // Subscribe as soon as the known native objects exist. The compass
                // geometry may still be empty; a later sensors event will apply it.
                refs = new SceneRefs(); refs.Main=main; refs.CameraBase=cameraBase; refs.Camera=camera; refs.Compass=compass;
                return true;
            }
        }

        sealed class LineState
        {
            internal LineRenderer Renderer;
            internal Material Original;
            internal Material Themed;
        }
        sealed class LabelState
        {
            internal UILabel Label;
            internal Color Colour,Effect,Top,Bottom;
            internal bool Gradient;
        }
        sealed class ThemeState
        {
            internal SceneRefs Refs;
            readonly List<LineState> lines = new List<LineState>();
            readonly List<LabelState> labels = new List<LabelState>();
            Material ringMaterial,tickMaterial,accentMaterial;
            object lineList,labelList;
            bool visualApplied;
            internal int LineCount { get { return lines.Count; } }
            internal int LabelCount { get { return labels.Count; } }
            internal bool CompassReady { get { return visualApplied; } }
            internal ThemeState(SceneRefs refs) { Refs=refs; }

            internal void Refresh(SceneRefs refs)
            {
                if(Refs.Camera != refs.Camera || Refs.Compass != refs.Compass)
                {
                    Restore(); Refs=refs;
                }
                else Refs=refs;
            }
            static Material MakeMaterial(Material original,string name,Color colour)
            {
                Material result = new Material(original);
                result.name=name; result.hideFlags=HideFlags.DontSave;
                result.SetColor("_Color",colour);
                return result;
            }
            static bool IsNorth(LineRenderer line)
            {
                string name=line.gameObject.name;
                if(!name.StartsWith("Compass-",StringComparison.Ordinal)) return false;
                float angle;
                return float.TryParse(name.Substring(8),NumberStyles.Float,CultureInfo.InvariantCulture,out angle) && Mathf.Abs(Mathf.DeltaAngle(angle,0f)) < .1f;
            }
            internal void ApplyCompass()
            {
                List<LineRenderer> currentLines=SceneRefs.CompassLines.GetValue(Refs.Compass) as List<LineRenderer>;
                List<UILabel> currentLabels=SceneRefs.CompassLabels.GetValue(Refs.Compass) as List<UILabel>;
                if(currentLines==null || currentLabels==null || currentLines.Count==0 || currentLabels.Count==0) return;
                if(visualApplied && object.ReferenceEquals(lineList,currentLines) && object.ReferenceEquals(labelList,currentLabels)) return;
                RestoreCompass();
                Material original=SceneRefs.CompassMaterial.GetValue(Refs.Compass) as Material;
                if(original==null || !original.HasProperty("_Color")) throw new InvalidOperationException("Native compass material _Color absent");
                // Original shader ignores vertex colour. Keep shader, pass state and
                // render queue, and change only uniforms of these private clones.
                ringMaterial=MakeMaterial(original,"EndfieldSensor_CompassRing",Ring);
                tickMaterial=MakeMaterial(original,"EndfieldSensor_CompassTicks",Tick);
                accentMaterial=MakeMaterial(original,"EndfieldSensor_CompassNorth",Yellow);
                foreach(LineRenderer line in currentLines)
                {
                    if(line==null) continue;
                    LineState snapshot=new LineState(); snapshot.Renderer=line; snapshot.Original=line.sharedMaterial;
                    snapshot.Themed=line.gameObject.name=="Compass Ring" ? ringMaterial : IsNorth(line) ? accentMaterial : tickMaterial;
                    lines.Add(snapshot); line.sharedMaterial=snapshot.Themed;
                }
                for(int i=0;i<currentLabels.Count;i++)
                {
                    UILabel label=currentLabels[i]; if(label==null) continue;
                    LabelState snapshot=new LabelState(); snapshot.Label=label; snapshot.Colour=label.color; snapshot.Effect=label.effectColor;
                    snapshot.Gradient=label.applyGradient; snapshot.Top=label.gradientTop; snapshot.Bottom=label.gradientBottom; labels.Add(snapshot);
                    Color colour=i==0 ? Yellow : White; colour.a=snapshot.Colour.a; label.color=colour;
                    label.applyGradient=false; Color shadow=Shadow; shadow.a=snapshot.Effect.a; label.effectColor=shadow;
                }
                lineList=currentLines; labelList=currentLabels; visualApplied=true;
            }
            void RestoreCompass()
            {
                foreach(LineState item in lines)
                    if(item.Renderer!=null && item.Renderer.sharedMaterial==item.Themed) item.Renderer.sharedMaterial=item.Original;
                foreach(LabelState item in labels)
                    if(item.Label!=null)
                    {
                        item.Label.color=item.Colour; item.Label.effectColor=item.Effect; item.Label.applyGradient=item.Gradient;
                        item.Label.gradientTop=item.Top; item.Label.gradientBottom=item.Bottom;
                    }
                lines.Clear(); labels.Clear(); lineList=null; labelList=null; visualApplied=false;
                if(ringMaterial!=null) UnityEngine.Object.Destroy(ringMaterial);
                if(tickMaterial!=null) UnityEngine.Object.Destroy(tickMaterial);
                if(accentMaterial!=null) UnityEngine.Object.Destroy(accentMaterial);
                ringMaterial=null; tickMaterial=null; accentMaterial=null;
            }
            internal void RestoreVisuals() { RestoreCompass(); }
            internal void Restore() { RestoreVisuals(); }
        }
    }
}
