// Small original Endfield NPC muzzle motifs, rebuilt for Unity 5 materials.
// Real native round presentation only: no projectile, raycast, command or damage.
using System;
using System.Collections.Generic;
using System.IO;
using BBI.Core.ComponentModel;
using BBI.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Pure managed event/lifetime plan, also used by System-only offline tests.
    internal sealed class HumanoidRifleFirePlan
    {
        public const int Slots=4;
        public const float Lifetime=.36f;
        readonly float[] Ages=new float[]{-1f,-1f,-1f,-1f};
        long LastSequence;
        public int ActiveCount { get { int result=0;for(int i=0;i<Slots;i++)if(Ages[i]>=0f)result++;return result; } }
        public float AgeFor(int slot){return slot>=0&&slot<Slots?Ages[slot]:-1f;}
        public bool TryBegin(long sequence,bool eligible,bool paused,bool budget,out int slot)
        {
            slot=-1;if(sequence<=0||sequence<=LastSequence)return false;LastSequence=sequence;
            if(!eligible||paused||!budget)return false;
            for(int i=0;i<Slots;i++)if(Ages[i]<0f){slot=i;break;}
            if(slot<0){slot=0;for(int i=1;i<Slots;i++)if(Ages[i]>Ages[slot])slot=i;}
            Ages[slot]=0f;return true;
        }
        public void Step(float delta)
        {
            if(float.IsNaN(delta)||float.IsInfinity(delta)||delta<=0f)return;
            for(int i=0;i<Slots;i++)if(Ages[i]>=0f){Ages[i]+=delta;if(Ages[i]>=Lifetime)Ages[i]=-1f;}
        }
        public void CancelSlot(int slot){if(slot>=0&&slot<Slots)Ages[slot]=-1f;}
        public void Clear(){for(int i=0;i<Slots;i++)Ages[i]=-1f;}
        public void Reset(){Clear();LastSequence=0;}
    }

    public sealed class HumanoidRifleFireFx : MonoBehaviour
    {
        const int MaximumInstances=16,MaximumGlobalBursts=32,Parts=6;
        static readonly List<HumanoidRifleFireFx> Instances=new List<HumanoidRifleFireFx>();
        readonly HumanoidRifleFirePlan Plan=new HumanoidRifleFirePlan();
        sealed class SpritePart
        {
            public GameObject Node;public Mesh Mesh;public MeshRenderer Renderer;
            public readonly Vector3[] Vertices=new Vector3[4];
            public readonly Vector2[] UV=new Vector2[4];
            public readonly Color[] Colors=new Color[4];
        }
        sealed class Burst
        {
            public int Member,Variant;public Transform Muzzle;
            public Vector3 Start,Direction;public float Distance;public bool Tracer;
            public readonly SpritePart[] Sprites=new SpritePart[Parts];
        }
        UnitView View;Entity BoundEntity;HumanoidBinding Binding;
        string RequestedFolder;GameObject Root;
        Texture2D[] Textures;Material[] Materials;
        readonly Burst[] Bursts=new Burst[HumanoidRifleFirePlan.Slots];
        bool Attempted,PausedOwn=true;int Reports;

        public void Initialize(UnitView view,string textureFolderAbs)
        {
            if(Attempted&&View==view&&view!=null&&BoundEntity.Equals(view.EntityID)&&
                string.Equals(RequestedFolder,textureFolderAbs,StringComparison.OrdinalIgnoreCase))return;
            ReleaseOwn();Attempted=true;View=view;RequestedFolder=textureFolderAbs;
            if(view!=null)BoundEntity=view.EntityID;
            try
            {
                if(view==null)throw new ArgumentNullException("view");
                Binding=view.GetComponent<HumanoidBinding>();
                if(Binding==null||Binding.MemberCount<2||Binding.MemberEmission==null||Binding.AliveMembers==null)
                    throw new InvalidDataException("Rifle muzzle VFX requires a bound infantry squad");
                if(Instances.Count>=MaximumInstances)throw new InvalidOperationException("Rifle VFX instance budget reached");
                Shader additive=Supported("Particles/Additive");
                Shader alpha=Supported("Particles/Alpha Blended")??Supported("Sprites/Default")??Supported("Unlit/Transparent");
                if(additive==null||alpha==null)throw new InvalidOperationException("Required transparent particle shaders are absent");
                Textures=new Texture2D[4];Materials=new Material[4];
                string[] names=new string[]{"flare.png","core.png","smoke.png","spark.png"};
                for(int index=0;index<names.Length;index++)
                {
                    Textures[index]=LoadTexture(PngPath(textureFolderAbs,names[index]));
                }
                for(int index=0;index<4;index++)
                {
                    Material material=new Material(index==2?alpha:additive);Materials[index]=material;
                    material.name="EF_RifleNpc_"+index;material.hideFlags=HideFlags.HideAndDontSave;
                    material.mainTexture=Textures[index];material.renderQueue=3100;
                    if(material.HasProperty("_TintColor"))material.SetColor("_TintColor",Color.white);
                    if(material.HasProperty("_Color"))material.SetColor("_Color",Color.white);
                    if(material.HasProperty("_Cull"))material.SetInt("_Cull",0);
                    if(material.HasProperty("_ZWrite"))material.SetInt("_ZWrite",0);
                    if(material.HasProperty("_ZTest"))material.SetInt("_ZTest",4);
                }
                Root=new GameObject("EF_RifleNpcFx_"+BoundEntity);Root.hideFlags=HideFlags.DontSave;
                Root.transform.parent=null;int layer=LayerMask.NameToLayer("Default");Root.layer=layer<0?0:layer;
                Instances.Add(this);PausedOwn=true;
                Runtime.Log("HUMANOID_RIFLE_FX_READY entity="+BoundEntity+"; source=original_Endfield_interactive_npc_muzzle_01; slots=4; globalBursts=32; authoredLifetime=.10-.35s; nativeRoundsOnly=True; simulationUnchanged=True");
            }
            catch(Exception error)
            {
                DestroyOwned();Binding=null;
                Runtime.Log("HUMANOID_RIFLE_FX_DISABLED entity="+BoundEntity+"; reason="+error.GetType().Name+": "+error.Message+"; nativeCombatUnaffected=True");
            }
        }

        public void PlayRound(long nativeEventSequence,int livingMember,Vector3 targetWorld,bool hasAim)
        {
            Transform muzzle=Binding!=null&&Binding.MemberEmission!=null&&livingMember>=0&&livingMember<Binding.MemberEmission.Length?Binding.MemberEmission[livingMember]:null;
            PlayRound(nativeEventSequence,muzzle,targetWorld,hasAim);
        }
        // A caller may pass the actual living member's muzzle. It must belong
        // to this binding; foreign/dead emitters consume the event silently.
        public void PlayRound(long nativeEventSequence,Transform livingMuzzle,Vector3 targetWorld,bool hasAim)
        {
            int member=FindMember(livingMuzzle),slot;
            bool eligible=Root!=null&&CanDisplay()&&MemberAlive(member)&&livingMuzzle!=null&&Finite(livingMuzzle.position);
            int active=0;foreach(HumanoidRifleFireFx item in Instances)if(item!=null)active+=item.Plan.ActiveCount;
            bool budget=active<MaximumGlobalBursts||Plan.ActiveCount==HumanoidRifleFirePlan.Slots;
            if(!Plan.TryBegin(nativeEventSequence,eligible,PausedOwn,budget,out slot))return;
            Burst burst=Bursts[slot];if(burst==null){burst=CreateBurst();Bursts[slot]=burst;}
            burst.Member=member;burst.Muzzle=livingMuzzle;burst.Start=livingMuzzle.position;
            Vector3 direction=hasAim&&Finite(targetWorld)?targetWorld-burst.Start:livingMuzzle.forward;
            burst.Distance=direction.magnitude;
            if(direction.sqrMagnitude<.0001f)direction=View.ForwardHorizontal;
            if(direction.sqrMagnitude<.0001f)direction=Vector3.forward;direction.Normalize();
            burst.Direction=direction;burst.Variant=(int)((ulong)nativeEventSequence%4UL);
            burst.Tracer=hasAim&&burst.Distance>1f&&nativeEventSequence%3==0;
            RenderBurst(burst,0f);
            if(Reports<16)
            {Reports++;Runtime.Log("HUMANOID_RIFLE_FX_ROUND entity="+BoundEntity+"; eventSequence="+nativeEventSequence+"; member="+member+"; oneNativeRoundOneMuzzle=True; simulatedProjectile=False; damageApplied=False");}
        }
        // Call once before consuming native events. No Unity particle clock or
        // auto Update is used, so a paused simulation freezes the entire burst.
        public void Tick(float presentationDt)
        {
            PausedOwn=!Finite(presentationDt)||presentationDt<=0f;
            if(!CanDisplay()){Plan.Clear();HideAll();return;}
            if(PausedOwn)return;
            Plan.Step(presentationDt);
            for(int slot=0;slot<Bursts.Length;slot++)
            {
                Burst burst=Bursts[slot];if(burst==null)continue;
                float age=Plan.AgeFor(slot);
                if(age<0f||!MemberAlive(burst.Member)||burst.Muzzle==null)
                {Plan.CancelSlot(slot);Hide(burst);continue;}
                RenderBurst(burst,age);
            }
        }
        bool CanDisplay()
        {
            return View!=null&&View.gameObject.activeInHierarchy&&View.EntityID.Equals(BoundEntity)&&Binding!=null&&
                View.Visibility==DetectionState.Sensed&&!UserCamera.IsSensorsModeActive&&View.NonInterpolatedTickState!=null&&
                !View.NonInterpolatedTickState.UnitIsDead&&View.NonInterpolatedTickState.Hitpoints>0;
        }
        int FindMember(Transform muzzle)
        {if(muzzle==null||Binding==null||Binding.MemberEmission==null)return -1;for(int i=0;i<Binding.MemberEmission.Length;i++)if(Binding.MemberEmission[i]==muzzle)return i;return -1;}
        bool MemberAlive(int member)
        {return Binding!=null&&Binding.AliveMembers!=null&&member>=0&&member<Binding.AliveMembers.Length&&Binding.AliveMembers[member];}
        Burst CreateBurst()
        {
            Burst burst=new Burst();int[] roles=new int[]{0,1,2,3,3,3};
            for(int part=0;part<Parts;part++)
            {
                SpritePart sprite=new SpritePart();burst.Sprites[part]=sprite;
                sprite.Node=new GameObject("RifleSprite_"+part);sprite.Node.layer=Root.layer;sprite.Node.transform.parent=Root.transform;
                sprite.Mesh=new Mesh();sprite.Mesh.name="EF_RifleQuad";sprite.Mesh.hideFlags=HideFlags.HideAndDontSave;
                sprite.Mesh.vertices=new Vector3[]{new Vector3(-.5f,-.5f,0f),new Vector3(.5f,-.5f,0f),new Vector3(.5f,.5f,0f),new Vector3(-.5f,.5f,0f)};
                sprite.Mesh.triangles=new int[]{0,1,2,0,2,3,2,1,0,3,2,0};sprite.Mesh.uv=new Vector2[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
                sprite.Node.AddComponent<MeshFilter>().sharedMesh=sprite.Mesh;
                sprite.Renderer=sprite.Node.AddComponent<MeshRenderer>();sprite.Renderer.sharedMaterial=Materials[roles[part]];
                sprite.Renderer.receiveShadows=false;sprite.Renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;sprite.Renderer.enabled=false;
            }
            return burst;
        }
        void RenderBurst(Burst burst,float age)
        {
            Camera camera=Camera.main;if(camera==null){Hide(burst);return;}
            Vector3 muzzle=burst.Muzzle.position,normal=(camera.transform.position-muzzle).normalized;
            if(normal.sqrMagnitude<.0001f)normal=-camera.transform.forward;
            Vector3 along=burst.Direction-normal*Vector3.Dot(burst.Direction,normal);
            if(along.sqrMagnitude<.0001f)along=camera.transform.right;along.Normalize();
            Vector3 across=Vector3.Cross(normal,along).normalized;
            float flash=Mathf.Clamp01(1f-age/.12f);flash*=flash;
            Set(burst.Sprites[0],muzzle+burst.Direction*.07f,along,across,.26f,.14f,new Color(1f,.58f,.16f,flash*.8f),burst.Variant,2,age<.12f);
            Set(burst.Sprites[1],muzzle+burst.Direction*.015f,camera.transform.right,camera.transform.up,.075f,.075f,new Color(1f,.94f,.76f,Mathf.Clamp01(1f-age/.085f)*.85f),0,1,age<.085f);
            float smokeAge=Mathf.Clamp01((age-.015f)/.335f),smokeSize=.09f+smokeAge*.22f;
            Vector3 smokePosition=burst.Start+burst.Direction*(.025f+age*.25f)+Vector3.up*(age*.19f);
            Set(burst.Sprites[2],smokePosition,camera.transform.right,camera.transform.up,smokeSize,smokeSize,
                new Color(.66f,.66f,.64f,Mathf.Sin(smokeAge*Mathf.PI)*.24f),Math.Min(3,(int)(smokeAge*4f)),2,age>=.015f&&age<.35f);
            for(int spark=0;spark<2;spark++)
            {
                float side=spark==0?-1f:1f;
                Vector3 direction=(burst.Direction+across*(side*.11f)+Vector3.up*.08f).normalized;
                Vector3 position=burst.Start+direction*(age*(spark==0?3.2f:4.4f)) + Vector3.down*(age*age*.75f);
                Set(burst.Sprites[3+spark],position,along,across,.065f,.012f,new Color(1f,.82f,.51f,Mathf.Clamp01(1f-age/.14f)*.48f),0,1,age<.14f);
            }
            float travel=age*280f,limit=Math.Min(9f,burst.Distance),length=.45f;
            Set(burst.Sprites[5],burst.Start+burst.Direction*(travel+length*.5f),along,across,length,.012f,
                new Color(1f,.86f,.6f,.45f),0,1,burst.Tracer&&age<.04f&&travel+length<limit);
        }
        static void Set(SpritePart part,Vector3 position,Vector3 right,Vector3 up,float width,float height,Color color,int frame,int tiles,bool visible)
        {
            part.Renderer.enabled=visible&&color.a>.001f;if(!part.Renderer.enabled)return;
            part.Node.transform.position=position;part.Node.transform.rotation=Quaternion.identity;part.Node.transform.localScale=Vector3.one;
            right*=width*.5f;up*=height*.5f;
            part.Vertices[0]=-right-up;part.Vertices[1]=right-up;part.Vertices[2]=right+up;part.Vertices[3]=-right+up;
            float step=1f/tiles,u=(frame%tiles)*step,v=1f-((frame/tiles)+1)*step;
            part.UV[0]=new Vector2(u,v);part.UV[1]=new Vector2(u+step,v);part.UV[2]=new Vector2(u+step,v+step);part.UV[3]=new Vector2(u,v+step);
            for(int i=0;i<4;i++)part.Colors[i]=color;
            part.Mesh.vertices=part.Vertices;part.Mesh.uv=part.UV;part.Mesh.colors=part.Colors;part.Mesh.RecalculateBounds();
        }
        // Exact fixed basenames only. Do not widen AttackAudio.SafePath, whose
        // existing public contract deliberately accepts WAV/attack INI only.
        static string PngPath(string folder,string name)
        {
            if(string.IsNullOrEmpty(folder)||!Path.IsPathRooted(folder))throw new InvalidDataException("Rifle texture folder must be absolute");
            string root=Path.GetFullPath(folder);if(!Directory.Exists(root))throw new DirectoryNotFoundException("Rifle texture folder absent");
            if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Rifle texture folder reparse rejected");
            if(name!="flare.png"&&name!="core.png"&&name!="smoke.png"&&name!="spark.png")throw new InvalidDataException("Rifle texture basename unsupported");
            string path=Path.Combine(root,name);if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Rifle texture reparse rejected");return path;
        }
        static Texture2D LoadTexture(string path)
        {
            byte[] bytes=HumanoidAttackAudio.ReadBoundedFile(path,2*1024*1024);
            if(bytes.Length<24||bytes[0]!=137||bytes[1]!=80||bytes[2]!=78||bytes[3]!=71||bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82)
                throw new InvalidDataException("Rifle texture must be PNG with IHDR");
            uint width=BigEndian(bytes,16),height=BigEndian(bytes,20);if(width<1||height<1||width>1024||height>1024)throw new InvalidDataException("Rifle texture dimensions exceed bound");
            Texture2D texture=new Texture2D(2,2,TextureFormat.ARGB32,false);texture.hideFlags=HideFlags.HideAndDontSave;texture.name="EF_RifleNpc_"+Path.GetFileNameWithoutExtension(path);
            try{if(!texture.LoadImage(bytes))throw new InvalidDataException("Rifle PNG decode failed");texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;return texture;}
            catch{UnityEngine.Object.Destroy(texture);throw;}
        }
        static uint BigEndian(byte[] bytes,int at){return (uint)(bytes[at]<<24|bytes[at+1]<<16|bytes[at+2]<<8|bytes[at+3]);}
        static Shader Supported(string name){Shader shader=Shader.Find(name);return shader!=null&&shader.isSupported?shader:null;}
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        static bool Finite(Vector3 value){return Finite(value.x)&&Finite(value.y)&&Finite(value.z);}
        static void Hide(Burst burst){foreach(SpritePart part in burst.Sprites)if(part!=null&&part.Renderer!=null)part.Renderer.enabled=false;}
        void HideAll(){foreach(Burst burst in Bursts)if(burst!=null)Hide(burst);}
        void DestroyOwned()
        {
            Plan.Clear();HideAll();
            for(int slot=0;slot<Bursts.Length;slot++)
            {Burst burst=Bursts[slot];if(burst!=null)foreach(SpritePart part in burst.Sprites)if(part!=null&&part.Mesh!=null)Destroy(part.Mesh);Bursts[slot]=null;}
            if(Root!=null)Destroy(Root);Root=null;
            if(Materials!=null)foreach(Material material in Materials)if(material!=null)Destroy(material);Materials=null;
            if(Textures!=null)foreach(Texture2D texture in Textures)if(texture!=null)Destroy(texture);Textures=null;
            Instances.Remove(this);
        }
        void ReleaseOwn(){DestroyOwned();Plan.Reset();View=null;Binding=null;RequestedFolder=null;Attempted=false;PausedOwn=true;Reports=0;}
        void OnDisable(){ReleaseOwn();}
        void OnDestroy(){ReleaseOwn();}
        public static void ResetAll(){foreach(HumanoidRifleFireFx item in Instances.ToArray())if(item!=null)item.ReleaseOwn();Instances.Clear();}
    }
}
