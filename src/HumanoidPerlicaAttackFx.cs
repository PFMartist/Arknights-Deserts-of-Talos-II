// Original Perlica attack texture/shape translation for DoK's Unity 5.2.
// Native RoundHit/RoundMiss events only. This is presentation: no projectile,
// delayed damage, native commands, new camera/listener or global shader state.
using System;
using System.Collections.Generic;
using System.IO;
using BBI.Core.ComponentModel;
using BBI.Game.Data;
using BBI.Unity.Game;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public sealed class HumanoidPerlicaAttackFx : MonoBehaviour
    {
        const int MaximumPieces=48, MaximumPngBytes=4*1024*1024;
        // Current native Perlica range is 140 m. Allow hit-anchor offset while
        // still rejecting corrupt/zero fallback aim coordinates.
        const float MaximumTraceMeters=160f;
        static readonly Color Gold=new Color(1f,.72f,.30f,1f), WhiteGold=new Color(1f,.94f,.76f,1f);
        static readonly string[] TextureNames={"perlica_open_lightning.png","perlica_fly_lightning.png","perlica_hit_star.png","perlica_glow.png","perlica_flare.png","perlica_ray.png","perlica_ring_gradient.png"};
        readonly List<Texture2D> Textures=new List<Texture2D>();
        readonly List<Mesh> Meshes=new List<Mesh>();
        readonly List<Material> Materials=new List<Material>();
        readonly List<Piece> Pieces=new List<Piece>();
        readonly MaterialPropertyBlock Properties=new MaterialPropertyBlock();
        UnitView View;
        Entity BoundEntity;
        GameObject Root;
        Camera Lens;
        string Folder;
        Mesh Quad, Ring;
        Mesh[] OpenFrames, FlyFrames;
        long LastWindup,LastRound;
        int Cursor,Reports;
        bool Ready,Paused,Attempted;

        sealed class Piece
        {
            public GameObject Object;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public bool Active,Windup,Billboard,Ring,Trace;
            public float Age,Life,Delay,StartSize,EndSize,Width,Rotation;
            public Vector3 Position,Velocity,End;
            public Quaternion Orientation;
            public Color Tint;
            public int Atlas;
        }

        public void Initialize(UnitView view,string actorFolderAbs)
        {
            if(Attempted&&View==view&&view!=null&&BoundEntity.Equals(view.EntityID)&&string.Equals(Folder,actorFolderAbs,StringComparison.OrdinalIgnoreCase))return;
            ReleaseOwn();View=view;Folder=actorFolderAbs;Attempted=true;
            if(view!=null)BoundEntity=view.EntityID;
            try
            {
                if(view==null)throw new ArgumentNullException("view");
                if(string.IsNullOrEmpty(actorFolderAbs)||!Path.IsPathRooted(actorFolderAbs))throw new InvalidDataException("Perlica actor path must be absolute");
                string folder=Path.Combine(Path.GetFullPath(actorFolderAbs),"attack_vfx");
                Shader shader=Shader.Find("Particles/Additive");
                if(shader==null)throw new InvalidDataException("DoK Particles/Additive shader unavailable");
                for(int i=0;i<TextureNames.Length;i++)
                {
                    string path=Path.Combine(folder,TextureNames[i]);
                    using(FileStream f=File.OpenRead(path))
                    {
                        if(f.Length<24||f.Length>MaximumPngBytes)throw new InvalidDataException("Perlica texture size limit");
                        byte[] data=new byte[(int)f.Length];int at=0,read;
                        while(at<data.Length&&(read=f.Read(data,at,data.Length-at))>0)at+=read;
                        if(at!=data.Length||data[0]!=137||data[1]!=80||data[2]!=78||data[3]!=71)throw new InvalidDataException("Perlica PNG invalid");
                        int w=BigEndian32(data,16),h=BigEndian32(data,20);
                        if(w<1||h<1||w>2048||h>2048)throw new InvalidDataException("Perlica texture dimensions limit");
                        Texture2D texture=new Texture2D(2,2,TextureFormat.RGBA32,false);Textures.Add(texture);
                        if(!texture.LoadImage(data))throw new InvalidDataException("Perlica PNG decode failed");
                        texture.name="EF_Perlica_"+TextureNames[i];texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
                        Material mat=new Material(shader);Materials.Add(mat);mat.name="EF_Perlica_"+i;
                        mat.mainTexture=texture;mat.SetColor("_TintColor",Color.white);mat.renderQueue=3000;
                    }
                }
                Quad=NewQuad(0f,0f,1f,1f);Ring=NewRing();
                OpenFrames=AtlasMeshes(3,2);FlyFrames=AtlasMeshes(2,4);
                Root=new GameObject("EF_Perlica_AttackFx_"+BoundEntity);Root.hideFlags=HideFlags.HideAndDontSave;
                // A world-space owned root follows scene lifetime, not skeleton scale.
                Root.transform.position=Vector3.zero;Root.transform.rotation=Quaternion.identity;
                Lens=Camera.main;Ready=true;
                Runtime.Log("PERLICA_ATTACK_FX_READY entity="+BoundEntity+"; originalTextures=7; sourceUnity=2021.3.34f5; target=5.2; originalShaderRebuilt=False; poolCap=48; realNativeRoundsOnly=True; nativeDamageUnchanged=True; inGameVisualAcceptancePending=True");
            }
            catch(Exception error)
            {
                ReleaseResources();
                Runtime.Log("PERLICA_ATTACK_FX_DISABLED entity="+BoundEntity+"; reason="+error.GetType().Name+": "+error.Message+"; nativeCombatUnaffected=True");
            }
        }

        public void PlayWindup(long sequence,Vector3 muzzleWorld)
        {
            if(sequence<=0||sequence<=LastWindup)return;LastWindup=sequence;
            CancelWindup();if(!CanEmit()||Paused||!Finite(muzzleWorld))return;
            Vector3 direction=transform.forward;
            Quaternion q=Quaternion.LookRotation(direction.sqrMagnitude>.01f?direction:Vector3.forward,Vector3.up);
            Add(6,muzzleWorld,q,.28f,0f,.22f,.54f,WhiteGold,true,false,true,0,Vector3.zero);
            Add(6,muzzleWorld+direction*.035f,q,.25f,0f,.36f,.24f,Gold,true,false,true,0,Vector3.zero);
            Add(0,muzzleWorld,q,.15f,.05f,.34f,.42f,WhiteGold,true,true,false,1,Vector3.zero);
            Add(4,muzzleWorld,q,.15f,.05f,.7f,.32f,WhiteGold,true,true,false,0,Vector3.zero);
            Add(3,muzzleWorld,q,.15f,.03f,.28f,.12f,Gold,true,true,false,0,Vector3.zero);
        }

        public void PlayRound(long sequence,Vector3 muzzleWorld,Vector3 aimWorld,bool hit)
        {PlayRound(sequence,muzzleWorld,aimWorld,true,hit);}

        public void PlayRound(long sequence,Vector3 muzzleWorld,Vector3 aimWorld,bool hasAim,bool hit)
        {
            if(sequence<=0||sequence<=LastRound)return;LastRound=sequence;
            CancelWindup();if(!CanEmit()||Paused||!Finite(muzzleWorld))return;
            Vector3 delta=hasAim&&Finite(aimWorld)?aimWorld-muzzleWorld:Vector3.zero;
            bool validAim=hasAim&&Finite(aimWorld)&&delta.sqrMagnitude>.01f&&delta.sqrMagnitude<=MaximumTraceMeters*MaximumTraceMeters;
            Vector3 forward=validAim?delta.normalized:transform.forward;
            Quaternion orientation=Quaternion.LookRotation(forward.sqrMagnitude>.01f?forward:Vector3.forward,Vector3.up);
            Add(6,muzzleWorld,orientation,.35f,0f,.3f,.85f,Gold,false,false,true,0,Vector3.zero);
            Add(4,muzzleWorld,orientation,.15f,0f,1f,.15f,WhiteGold,false,true,false,0,Vector3.zero);
            Add(0,muzzleWorld,orientation,.15f,0f,.46f,.7f,WhiteGold,false,true,false,1,Vector3.zero);
            for(int i=0;i<3;i++)
            {
                float angle=(sequence%11)*.37f+i*2.094395f;
                Vector3 drift=new Vector3(Mathf.Cos(angle),.35f,Mathf.Sin(angle))*.4f;
                Add(3,muzzleWorld,orientation,.4f,.03f,.035f,0f,Gold,false,true,false,0,drift);
            }
            if(validAim)
            {
                Piece trace=Add(1,muzzleWorld,orientation,.055f,0f,1f,1f,WhiteGold,false,false,false,2,Vector3.zero);
                if(trace!=null){trace.Trace=true;trace.End=aimWorld;trace.Width=.13f;}
                if(hit)Impact(sequence,aimWorld,orientation);
            }
            if(Reports++<6)Runtime.Log("PERLICA_ATTACK_FX_ROUND entity="+BoundEntity+"; sequence="+sequence+"; hasAim="+hasAim+"; validAim="+validAim+"; hit="+hit+"; sameFrameNativeHitVisual=True; trajectoryIsVisualOnly=True");
        }

        void Impact(long sequence,Vector3 position,Quaternion orientation)
        {
            // Parent supplies a target centre/hit anchor. Do not infer new damage.
            Add(2,position,orientation,.3f,0f,1.35f,.45f,WhiteGold,false,true,false,0,Vector3.zero);
            Add(3,position,orientation,.35f,0f,.7f,.18f,Gold,false,true,false,0,Vector3.zero);
            Add(6,position,orientation,.5f,0f,.28f,1.05f,Gold,false,false,true,0,Vector3.zero);
            Add(6,position,orientation,.5f,0f,.4f,.82f,WhiteGold,false,true,true,0,Vector3.zero);
            Add(5,position,orientation,.25f,.025f,.62f,1.15f,WhiteGold,false,true,false,0,Vector3.zero);
            for(int i=0;i<8;i++)
            {
                float angle=i*2.399963f+(sequence%17)*.13f;
                Vector3 drift=new Vector3(Mathf.Cos(angle),.3f+(i%3)*.22f,Mathf.Sin(angle))*(.55f+(i%4)*.12f);
                Add(3,position,orientation,.45f+(i%4)*.1f,.05f,.035f,.005f,Gold,false,true,false,0,drift);
            }
        }

        Piece Add(int material,Vector3 position,Quaternion orientation,float life,float delay,float startSize,float endSize,Color tint,bool windup,bool billboard,bool ring,int atlas,Vector3 velocity)
        {
            Piece p=null;
            for(int i=0;i<Pieces.Count;i++){int n=(Cursor+i)%Pieces.Count;if(!Pieces[n].Active){p=Pieces[n];Cursor=(n+1)%Pieces.Count;break;}}
            if(p==null&&Pieces.Count<MaximumPieces)
            {
                p=new Piece();p.Object=new GameObject("FX");p.Object.transform.SetParent(Root.transform,false);
                p.Filter=p.Object.AddComponent<MeshFilter>();p.Renderer=p.Object.AddComponent<MeshRenderer>();
                p.Renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;p.Renderer.receiveShadows=false;
                p.Object.layer=View.gameObject.layer;Pieces.Add(p);
            }
            if(p==null)return null;
            p.Active=true;p.Windup=windup;p.Billboard=billboard;p.Ring=ring;p.Trace=false;
            p.Age=0f;p.Life=life;p.Delay=delay;p.StartSize=startSize;p.EndSize=endSize;p.Width=0f;
            p.Position=position;p.Velocity=velocity;p.End=position;p.Orientation=orientation;p.Tint=tint;p.Atlas=atlas;p.Rotation=0f;
            p.Filter.sharedMesh=ring?Ring:atlas==1?OpenFrames[0]:atlas==2?FlyFrames[0]:Quad;
            p.Renderer.sharedMaterial=Materials[material];p.Renderer.enabled=false;p.Object.SetActive(true);
            RenderPiece(p,0f);return p;
        }

        public void Tick(float presentationDt)
        {
            if(!Ready)return;
            if(!CanEmit()){ResetAll();return;}
            Paused=!Finite(presentationDt)||presentationDt<=0f;
            if(Lens==null||!Lens.enabled)Lens=Camera.main;
            for(int i=0;i<Pieces.Count;i++)
            {
                Piece p=Pieces[i];if(!p.Active)continue;
                if(!Paused)p.Age+=Mathf.Min(presentationDt,.25f);
                if(p.Age>=p.Delay+p.Life){Deactivate(p);continue;}
                RenderPiece(p,Mathf.Clamp01((p.Age-p.Delay)/p.Life));
            }
        }

        void RenderPiece(Piece p,float fraction)
        {
            p.Renderer.enabled=p.Age>=p.Delay;if(!p.Renderer.enabled)return;
            float age=Mathf.Max(0f,p.Age-p.Delay),size=Mathf.Lerp(p.StartSize,p.EndSize,fraction);
            Transform t=p.Object.transform;
            t.position=p.Position+p.Velocity*age;
            Quaternion face=p.Billboard&&Lens!=null?Lens.transform.rotation:p.Orientation;
            t.rotation=face*Quaternion.Euler(0f,0f,p.Rotation);
            t.localScale=new Vector3(size,size,size);
            if(p.Trace)
            {
                Vector3 delta=p.End-p.Position;
                Vector3 side=Lens!=null?Vector3.Cross(delta,Lens.transform.forward):Vector3.Cross(delta,Vector3.up);
                if(side.sqrMagnitude<.0001f)side=Vector3.Cross(delta,Vector3.right);
                // XY textured lightning strip. It is not a sim projectile or a beam weapon.
                t.position=(p.Position+p.End)*.5f;
                t.rotation=Quaternion.LookRotation(Vector3.Cross(delta,side).normalized,side.normalized);
                t.localScale=new Vector3(delta.magnitude,p.Width,1f);
            }
            if(p.Atlas!=0)
            {
                Mesh[] frames=p.Atlas==1?OpenFrames:FlyFrames;
                p.Filter.sharedMesh=frames[Math.Min(frames.Length-1,(int)(fraction*frames.Length))];
            }
            float opacity=(1f-fraction)*(1f-fraction);
            Color color=Color.Lerp(p.Tint,Gold,fraction*.65f);color.a=p.Tint.a*opacity*.65f;
            Properties.Clear();Properties.SetColor("_TintColor",color);p.Renderer.SetPropertyBlock(Properties);
        }

        public void CancelWindup()
        {for(int i=0;i<Pieces.Count;i++)if(Pieces[i].Active&&Pieces[i].Windup)Deactivate(Pieces[i]);}
        public void ResetAll()
        {for(int i=0;i<Pieces.Count;i++)Deactivate(Pieces[i]);}
        static void Deactivate(Piece p){p.Active=false;p.Renderer.enabled=false;p.Object.SetActive(false);}
        bool CanEmit()
        {
            return Ready&&View!=null&&View.gameObject.activeInHierarchy&&View.EntityID.Equals(BoundEntity)&&!UserCamera.IsSensorsModeActive&&
                View.Visibility==DetectionState.Sensed&&View.NonInterpolatedTickState!=null&&!View.NonInterpolatedTickState.UnitIsDead&&View.NonInterpolatedTickState.Hitpoints>0;
        }
        Mesh[] AtlasMeshes(int columns,int rows)
        {
            Mesh[] result=new Mesh[columns*rows];
            for(int i=0;i<result.Length;i++)result[i]=NewQuad((float)(i%columns)/columns,1f-(float)(i/columns+1)/rows,1f/columns,1f/rows);
            return result;
        }
        Mesh NewQuad(float u,float v,float w,float h)
        {
            Mesh mesh=new Mesh();mesh.name="EF_Perlica_Quad";Meshes.Add(mesh);
            mesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
            mesh.uv=new[]{new Vector2(u,v),new Vector2(u+w,v),new Vector2(u+w,v+h),new Vector2(u,v+h)};
            mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};
            // Particles/Additive is Cull Off: duplicated reverse faces would
            // double the brightness and turn a thin flare into a flat card.
            mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateBounds();return mesh;
        }
        Mesh NewRing()
        {
            const int n=40;Vector3[] vertices=new Vector3[n*2];Vector2[] uv=new Vector2[n*2];Color[] colors=new Color[n*2];int[] triangles=new int[n*6];
            for(int i=0;i<n;i++)
            {
                float a=i*Mathf.PI*2f/n;Vector3 r=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0f);
                vertices[i*2]=r*.44f;vertices[i*2+1]=r*.5f;uv[i*2]=new Vector2(.5f,0);uv[i*2+1]=new Vector2(.5f,1);colors[i*2]=colors[i*2+1]=Color.white;
                int j=(i+1)%n,a0=i*2,b=i*2+1,c=j*2,d=j*2+1,k=i*6;
                int[] ids={a0,d,b,a0,c,d};for(int q=0;q<6;q++)triangles[k+q]=ids[q];
            }
            Mesh mesh=new Mesh();mesh.name="EF_Perlica_SourceRingTranslation";Meshes.Add(mesh);mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=triangles;mesh.RecalculateBounds();return mesh;
        }
        static int BigEndian32(byte[] data,int at){return(data[at]<<24)|(data[at+1]<<16)|(data[at+2]<<8)|data[at+3];}
        static bool Finite(float f){return !float.IsNaN(f)&&!float.IsInfinity(f);}
        static bool Finite(Vector3 p){return Finite(p.x)&&Finite(p.y)&&Finite(p.z);}
        void ReleaseResources()
        {
            Ready=false;ResetAll();Pieces.Clear();if(Root!=null)Destroy(Root);Root=null;
            foreach(Material x in Materials)if(x!=null)Destroy(x);Materials.Clear();
            foreach(Texture2D x in Textures)if(x!=null)Destroy(x);Textures.Clear();
            foreach(Mesh x in Meshes)if(x!=null)Destroy(x);Meshes.Clear();
            Quad=Ring=null;OpenFrames=FlyFrames=null;Lens=null;
        }
        void ReleaseOwn(){ReleaseResources();LastWindup=LastRound=0;Reports=Cursor=0;Paused=Attempted=false;}
        void OnDisable(){ResetAll();}
        void OnDestroy(){ReleaseOwn();}
    }
}
