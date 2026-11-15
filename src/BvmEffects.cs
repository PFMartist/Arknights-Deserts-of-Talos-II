using System;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // BVM-only presentation supplement. Native weapon particles, projectiles and
    // impacts stay native; this class adds no shot timers and no damage logic.
    public sealed class BvmEffects : MonoBehaviour
    {
        const int PuffCount = 36;
        const float PuffSpacing = .85f;
        // Native WT emitter WTM positions mapped as (-nativeZ,nativeY-groundY,nativeX).
        // FrontLeft/Right are emtr_lantern_01/02; RearLeft/Right are b_04/b_03.
        // These hull emitters stay on ModelRoot, not the independent turret.
        static readonly Vector3 FrontLeft = new Vector3(-.7702901f, 1.1957420f, 3.0386367f);
        static readonly Vector3 FrontRight = new Vector3(.7701213f, 1.1943464f, 2.9971509f);
        static readonly Vector3 RearLeft = new Vector3(-1.1430041f, 1.2842656f, -3.2481661f);
        static readonly Vector3 RearRight = new Vector3(1.1524684f, 1.3004924f, -3.2690570f);
        // Contact points under the rear road wheels, not the track UV-node pivot.
        static readonly Vector3 DustLeft = new Vector3(-1.4062452f, .10f, -1.9347302f);
        static readonly Vector3 DustRight = new Vector3(1.4062458f, .10f, -1.8287001f);

        sealed class Puff
        {
            public Transform Transform;
            public Renderer Renderer;
            public Vector3 Position, Velocity;
            public float Age, Lifetime, Size, Rotation;
            public bool Active;
        }

        UnitView View;
        BvmRigBinding Rig;
        GameObject EffectsRoot;
        UserCamera UserCamera;
        Light[] RunningLights;
        Renderer[] LampFaces;
        Light ShotLight;
        Mesh Quad;
        Texture2D RadialTexture;
        Material HeadMaterial, RearMaterial, DustMaterial;
        MaterialPropertyBlock PuffProperties;
        Puff[] Puffs;
        bool Initialized, HavePrevious;
        Vector3 PreviousPosition;
        float LeftTravel, RightTravel, PreviousYaw, FlashAge = 100f;
        int LastSequence, NextPuff, PuffSerial;

        // Reentrant for a pooled UnitView. Only objects created here are released.
        public void Initialize(UnitView view, BvmRigBinding rig)
        {
            Release();
            if (view == null || rig == null || rig.ModelRoot == null || rig.VisualScale <= .001f) return;
            View = view; Rig = rig;
            EffectsRoot = new GameObject("EF_BVM_OwnEffects");
            EffectsRoot.layer = rig.ModelRoot.gameObject.layer;
            EffectsRoot.transform.parent = rig.ModelRoot;
            EffectsRoot.transform.localPosition = Vector3.zero;
            EffectsRoot.transform.localRotation = Quaternion.identity;
            EffectsRoot.transform.localScale = Vector3.one;
            Quad = MakeQuad(); RadialTexture = MakeRadialTexture();
            Shader additive = Shader.Find("Particles/Additive") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            Shader alpha = Shader.Find("Particles/Alpha Blended") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            if (additive == null || alpha == null)
            {
                Runtime.Log("BVM_OWN_EFFECTS_SKIPPED missing built-in particle shaders; native weapon FX retained");
                Release(); return;
            }
            HeadMaterial = MakeMaterial(additive, new Color(.78f,.71f,.54f,.38f), "Headlamp");
            RearMaterial = MakeMaterial(additive, new Color(.48f,.018f,.009f,.34f), "Taillamp");
            DustMaterial = MakeMaterial(alpha, Color.white, "TrackDust");
            PuffProperties = new MaterialPropertyBlock();
            RunningLights = new Light[4]; LampFaces = new Renderer[4];
            MakeLamp(0, FrontLeft, new Vector3(0f,-.055f,1f), false);
            MakeLamp(1, FrontRight, new Vector3(0f,-.055f,1f), false);
            MakeLamp(2, RearLeft, Vector3.back, true);
            MakeLamp(3, RearRight, Vector3.back, true);
            GameObject flash = Child("MuzzleRoundLight");
            ShotLight = flash.AddComponent<Light>(); ShotLight.type = LightType.Point;
            ShotLight.color = new Color(1f,.70f,.34f,1f);
            ShotLight.range = 5.5f * Rig.VisualScale;
            ShotLight.shadows = LightShadows.None; ShotLight.enabled = false;
            Puffs = new Puff[PuffCount];
            for (int i = 0; i < Puffs.Length; i++)
            {
                GameObject go = Child("TrackPuff_" + i);
                go.AddComponent<MeshFilter>().sharedMesh = Quad;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = DustMaterial; renderer.enabled = false;
                Puffs[i] = new Puff(); Puffs[i].Transform = go.transform; Puffs[i].Renderer = renderer;
            }
            LastSequence = BvmNativeState.GetRoundSequence(View.EntityID);
            FlashAge = 100f; HavePrevious = false; LeftTravel = RightTravel = 0f;
            NextPuff = PuffSerial = 0; Initialized = true;
            SetVisible(false);
            Runtime.Log("BVM_OWN_EFFECTS_READY source=native_lantern_emitters; front=01/02; rear=b04/b03; groundDust=rearRoadwheelContacts; nativeMuzzleParticlesAndImpactsRetained=True; customMuzzleParticleDuplication=False; roundLightOnly=True; sensorAndFowGated=True");
        }

        // Driver invokes this after pose/aim/recoil. simulationDeltaTime must be
        // zero when the DoK controller is paused, not merely Unity's wall delta.
        public void Tick(BvmNativeState.Snapshot state, float simulationDeltaTime)
        {
            if (!Initialized || View == null || Rig == null || Rig.ModelRoot == null) return;
            float dt = float.IsNaN(simulationDeltaTime) || float.IsInfinity(simulationDeltaTime) ? 0f : Mathf.Clamp(simulationDeltaTime,0f,.20f);
            if (UserCamera == null)
                foreach (UserCamera candidate in UnityEngine.Object.FindObjectsOfType(typeof(UserCamera)))
                    if (candidate != null && candidate.gameObject.activeInHierarchy) { UserCamera = candidate; break; }
            UnitState unit = View.NonInterpolatedTickState;
            bool alive = unit != null && !unit.UnitIsDead && unit.Hitpoints > 0;
            bool sensors = UserCamera == null || UserCamera.SensorsManagerActive;
            bool visible = state.Valid && alive && View.gameObject.activeInHierarchy && Rig.ModelRoot.gameObject.activeInHierarchy
                && View.Visibility == DetectionState.Sensed && !sensors;
            SetVisible(visible);
            if (!visible)
            {
                ClearTransient(); HavePrevious = false; LastSequence = state.RoundSequence;
                return;
            }
            bool newRound=state.RoundSequence>LastSequence && dt>0f;
            if (state.RoundSequence != LastSequence)
            {
                LastSequence = state.RoundSequence;
                if (newRound) FlashAge = 0f; else FlashAge = 100f;
            }
            if (dt > 0f && !newRound) FlashAge += dt;
            if (Rig.Muzzle != null)
            {
                Vector3 direction = Rig.Bones[Rig.RecoilNode].TransformDirection(Rig.RecoilAxis).normalized;
                ShotLight.transform.position = Rig.Muzzle.position + direction * (.10f * Rig.VisualScale);
            }
            ShotLight.enabled = Rig.Muzzle != null && FlashAge < .070f;
            ShotLight.intensity = 1.65f * Mathf.Exp(-FlashAge / .024f);
            Vector3 position = Rig.ModelRoot.position;
            Vector3 forward = Rig.ModelRoot.forward; forward.y = 0f; forward.Normalize();
            float yaw = Mathf.Atan2(forward.x,forward.z);
            if (dt > 0f && HavePrevious)
            {
                Vector3 delta = position - PreviousPosition; delta.y = 0f;
                float turn = Mathf.DeltaAngle(PreviousYaw * Mathf.Rad2Deg,yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                if (delta.sqrMagnitude < 625f && Mathf.Abs(turn) < 1f)
                {
                    float travel = Vector3.Dot(delta,forward);
                    float left = travel + turn * Rig.TrackGauge * .5f;
                    float right = travel - turn * Rig.TrackGauge * .5f;
                    LeftTravel += Mathf.Abs(left); RightTravel += Mathf.Abs(right);
                    EmitTrackPuffs(ref LeftTravel,DustLeft,left,forward,dt);
                    EmitTrackPuffs(ref RightTravel,DustRight,right,forward,dt);
                }
                else { LeftTravel = RightTravel = 0f; }
            }
            PreviousPosition = position; PreviousYaw = yaw; HavePrevious = true;
            Camera renderCamera = Camera.main;
            for (int i = 0; i < Puffs.Length; i++)
            {
                Puff puff = Puffs[i]; if (!puff.Active) continue;
                if (dt > 0f)
                {
                    puff.Age += dt;
                    if (puff.Age >= puff.Lifetime) { puff.Active = false; puff.Renderer.enabled = false; continue; }
                    puff.Position += puff.Velocity * dt;
                    puff.Velocity *= Mathf.Exp(-1.8f * dt);
                }
                float life = puff.Age / puff.Lifetime;
                puff.Transform.position = puff.Position;
                if (renderCamera != null) puff.Transform.rotation = renderCamera.transform.rotation * Quaternion.AngleAxis(puff.Rotation,Vector3.forward);
                float size = puff.Size * (1f + life * 1.25f);
                puff.Transform.localScale = new Vector3(size,size,size);
                PuffProperties.Clear();
                PuffProperties.SetColor("_TintColor",new Color(.40f,.37f,.31f,.085f * Mathf.Sin(life * Mathf.PI)));
                PuffProperties.SetColor("_Color",new Color(.40f,.37f,.31f,.085f * Mathf.Sin(life * Mathf.PI)));
                puff.Renderer.SetPropertyBlock(PuffProperties);
                puff.Renderer.enabled = true;
            }
        }

        void EmitTrackPuffs(ref float distance, Vector3 anchor, float signedTravel, Vector3 forward, float dt)
        {
            float spacing = PuffSpacing * Rig.VisualScale;
            int emit = Mathf.Min(2,(int)(distance / spacing));
            if (emit == 0) return;
            distance -= emit * spacing;
            float speed = Mathf.Min(12f,Mathf.Abs(signedTravel) / Mathf.Max(dt,.001f));
            for (int i = 0; i < emit; i++)
            {
                Puff puff = Puffs[NextPuff]; NextPuff = (NextPuff + 1) % Puffs.Length;
                float noise = ((PuffSerial++ * 37) % 101) / 100f;
                puff.Position = Rig.ModelRoot.TransformPoint(anchor * Rig.VisualScale);
                puff.Velocity = -forward * Mathf.Sign(signedTravel) * (.12f + .028f * speed) + Vector3.up * (.055f * Rig.VisualScale);
                puff.Age = 0f; puff.Lifetime = .70f + .35f * noise;
                puff.Size = (.26f + .12f * noise) * Rig.VisualScale; puff.Rotation = 360f * noise;
                puff.Active = true;
            }
        }

        void MakeLamp(int index,Vector3 position,Vector3 direction,bool rear)
        {
            GameObject lamp = Child(rear ? "RearNativeLamp_"+index : "FrontNativeLamp_"+index);
            lamp.transform.localPosition = position * Rig.VisualScale;
            lamp.transform.localRotation = Quaternion.LookRotation(direction,Vector3.up);
            Light light = lamp.AddComponent<Light>(); RunningLights[index] = light;
            light.type = rear ? LightType.Point : LightType.Spot;
            light.color = rear ? new Color(.72f,.025f,.012f,1f) : new Color(1f,.90f,.73f,1f);
            light.range = (rear ? 1.15f : 14f) * Rig.VisualScale;
            light.intensity = rear ? .10f : .56f; light.spotAngle = 46f;
            light.shadows = LightShadows.None; light.enabled = false;
            GameObject face = Child("NativeLampAperture_"+index);
            face.transform.parent = lamp.transform;
            face.transform.localPosition = new Vector3(0f,0f,.018f * Rig.VisualScale);
            face.transform.localRotation = Quaternion.identity;
            float width = (rear ? .10f : .14f) * Rig.VisualScale;
            face.transform.localScale = new Vector3(width,width,1f);
            face.AddComponent<MeshFilter>().sharedMesh = Quad;
            MeshRenderer renderer = face.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = rear ? RearMaterial : HeadMaterial;
            renderer.enabled = false; LampFaces[index] = renderer;
        }

        GameObject Child(string name)
        {
            GameObject child = new GameObject("EF_BVM_"+name); child.layer = EffectsRoot.layer;
            child.transform.parent = EffectsRoot.transform;
            child.transform.localPosition = Vector3.zero; child.transform.localRotation = Quaternion.identity; child.transform.localScale = Vector3.one;
            return child;
        }
        Material MakeMaterial(Shader shader,Color tint,string name)
        {
            Material material = new Material(shader); material.name = "EF_BVM_Own"+name;
            material.hideFlags = HideFlags.HideAndDontSave; material.mainTexture = RadialTexture;
            if(material.HasProperty("_TintColor"))material.SetColor("_TintColor",tint);
            if(material.HasProperty("_Color"))material.SetColor("_Color",tint);
            return material;
        }
        static Mesh MakeQuad()
        {
            Mesh mesh = new Mesh(); mesh.name = "EF_BVM_FXQuad"; mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = new Vector3[]{new Vector3(-.5f,-.5f,0f),new Vector3(.5f,-.5f,0f),new Vector3(.5f,.5f,0f),new Vector3(-.5f,.5f,0f)};
            mesh.uv = new Vector2[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};
            mesh.triangles = new int[]{0,2,1,0,3,2}; mesh.RecalculateBounds(); return mesh;
        }
        static Texture2D MakeRadialTexture()
        {
            const int size=32;
            Texture2D texture = new Texture2D(size,size,TextureFormat.ARGB32,false);
            texture.name = "EF_BVM_SoftRadial"; texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float dx=(x+.5f)/size*2f-1f,dy=(y+.5f)/size*2f-1f;
                float radius=dx*dx+dy*dy;
                pixels[y*size+x]=new Color(1f,1f,1f,Mathf.Clamp01(1f-radius)*Mathf.Clamp01(1f-radius));
            }
            texture.SetPixels(pixels);texture.Apply(false,true);return texture;
        }
        void SetVisible(bool visible)
        {
            if (RunningLights != null) foreach(Light light in RunningLights) if(light!=null)light.enabled=visible;
            if (LampFaces != null) foreach(Renderer renderer in LampFaces) if(renderer!=null)renderer.enabled=visible;
            if (!visible && ShotLight!=null) ShotLight.enabled=false;
        }
        void ClearTransient()
        {
            FlashAge=100f;LeftTravel=RightTravel=0f;
            if(ShotLight!=null)ShotLight.enabled=false;
            if(Puffs!=null)foreach(Puff puff in Puffs)
                if(puff!=null){puff.Active=false;if(puff.Renderer!=null)puff.Renderer.enabled=false;}
        }
        public void Release()
        {
            Initialized=false;ClearTransient();SetVisible(false);
            if(EffectsRoot!=null){EffectsRoot.SetActive(false);Destroy(EffectsRoot);}
            if(HeadMaterial!=null)Destroy(HeadMaterial);
            if(RearMaterial!=null)Destroy(RearMaterial);
            if(DustMaterial!=null)Destroy(DustMaterial);
            if(RadialTexture!=null)Destroy(RadialTexture);
            if(Quad!=null)Destroy(Quad);
            View=null;Rig=null;EffectsRoot=null;UserCamera=null;
            RunningLights=null;LampFaces=null;ShotLight=null;Puffs=null;
            HeadMaterial=RearMaterial=DustMaterial=null;RadialTexture=null;Quad=null;PuffProperties=null;
            HavePrevious=false;
        }
        void OnDisable(){Release();}
        void OnDestroy(){Release();}
    }
}
