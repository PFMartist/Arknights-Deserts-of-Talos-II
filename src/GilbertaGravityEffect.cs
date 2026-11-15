using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using BBI.Unity.Game.World;

namespace EndfieldDoK.MapUnitLab
{
    // Source mesh/UV/colour adapter for Unity5.2. View-facing source shallow
    // dishes remain view-facing; the source Local release ring stays horizontal.
    // Source material UV/mask/blend/dissolve animation is reconstructed offline
    // into straight-RGBA sheets. The original private GPU shader is not ported.
    public sealed class GilbertaGravityEffect:MonoBehaviour
    {
        const float SourceRadius=5.2f;
        const float MaterialRadiance=1.35f;
        sealed class Definition
        {
            internal string Name;internal Mesh Mesh;internal Texture2D Texture;internal Material Material;
            internal Vector3 Position,Scale,Size,Euler;internal Quaternion Rotation;internal Vector4 UV;
            internal Vector2 UVSpeed;internal float Phase,Delay,Life,EmitDuration;internal int Alignment,Repeat;
            internal Color StartColor;internal float[] ColorTimes,AlphaTimes,Alpha;internal Color[] Colors;
            internal GilbertaGravityProfile Sheet;
        }
        sealed class Instance
        {
            internal Definition Data;internal Transform Transform;internal Renderer Renderer;internal float Offset;
            internal Mesh Mesh;internal Vector2[] SourceUV,AtlasUV;internal int Frame=-1;
        }
        sealed class TrailDefinition
        {
            internal Vector3 Position,Scale;
            internal Quaternion Rotation;
            internal float Radius,RadialSpeed,Lifetime,TrailLifetime;
            internal float OrbitZMin,OrbitZMax,OrbitYMin,OrbitYMax;
            internal float InitialSpeed,Width,EmitDuration,Delay;
            internal Color Color;
            internal Texture2D Texture;
            internal Material Material;
        }
        const int TrailCount=24,TrailSegments=4;
        const float TrailWidthMultiplier=1f;
        static Definition[] Definitions;
        static TrailDefinition SourceTrails;
        readonly List<Instance> Layers=new List<Instance>();
        UserCamera UserCameraRef;
        Camera CameraRef;
        Mesh TrailMesh;
        MeshRenderer TrailRenderer;
        Vector3[] TrailVertices;
        Color32[] TrailColors;
        float Age=100f;
        public bool Active {get{return Age<GilbertaGravityAssets.ActiveSeconds;}}
        static string ReadString(BinaryReader r)
        {int count=r.ReadInt32();if(count<0||count>256)throw new InvalidDataException("Gravity VFX string bound");return Encoding.UTF8.GetString(r.ReadBytes(count));}
        static string FilePath(string root,string name)
        {if(string.IsNullOrEmpty(name)||Path.GetFileName(name)!=name)throw new InvalidDataException("Gravity VFX filename invalid");return Path.Combine(root,name);}
        static Vector3 V3(BinaryReader r){return new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());}
        static Quaternion Q4(BinaryReader r){return new Quaternion(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());}
        static Color C4(BinaryReader r){return new Color(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());}
        static Mesh LoadMesh(string file)
        {
            using(BinaryReader r=new BinaryReader(File.OpenRead(file)))
            {
                int nv=r.ReadInt32(),ni=r.ReadInt32();if(nv<3||nv>65535||ni<3||ni>196608||ni%3!=0)throw new InvalidDataException("Gravity source mesh bounds");
                Vector3[] positions=new Vector3[nv];Vector2[] uv=new Vector2[nv];Color32[] colors=new Color32[nv];int[] indices=new int[ni];
                for(int i=0;i<nv;i++){positions[i]=V3(r);uv[i]=new Vector2(r.ReadSingle(),r.ReadSingle());colors[i]=new Color32(r.ReadByte(),r.ReadByte(),r.ReadByte(),r.ReadByte());}
                for(int i=0;i<ni;i++){indices[i]=r.ReadInt32();if(indices[i]<0||indices[i]>=nv)throw new InvalidDataException("Gravity source index bound");}
                if(r.BaseStream.Position!=r.BaseStream.Length)throw new InvalidDataException("Gravity source mesh trailing data");
                Mesh mesh=new Mesh();mesh.name="EF_Gilberta_Source_"+Path.GetFileNameWithoutExtension(file);mesh.hideFlags=HideFlags.HideAndDontSave;
                mesh.vertices=positions;mesh.uv=uv;mesh.colors32=colors;mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
            }
        }
        static int GradientCount(BinaryReader r){int n=r.ReadInt32();if(n<1||n>16)throw new InvalidDataException("Gravity source gradient bound");return n;}
        static void Prepare()
        {
            if(Definitions!=null)return;
            string root=Path.Combine(HumanoidModel.AssetDirectory,"humanoid_trial/gilberta/gravity_vfx");
            // The game's particle PMA variant has no tint property and expects
            // premultiplied source RGB. Its former use with white RGB textures
            // ignored the blue-green tint and produced opaque white geometry.
            // This pinned sprite shader multiplies texture and vertex RGBA,
            // then premultiplies RGB once in its fragment program.
            Shader shader=Shader.Find("Sprites/Default");
            if(shader==null||!shader.isSupported)throw new InvalidDataException("Native straight-RGBA sprite shader unavailable");
            Dictionary<string,GilbertaGravityProfile> profiles=GilbertaGravityProfile.Load(root);
            List<Definition> loaded=new List<Definition>();
            try
            {
                using(BinaryReader r=new BinaryReader(File.OpenRead(Path.Combine(root,"gravity_vfx.bin"))))
                {
                    if(r.ReadUInt32()!=0x31565247||r.ReadInt32()!=1)throw new InvalidDataException("Gravity source config signature");
                    int count=r.ReadInt32();if(count<1||count>8)throw new InvalidDataException("Gravity source layer count");
                    for(int i=0;i<count;i++)
                    {
                        Definition d=new Definition();loaded.Add(d);d.Name=ReadString(r);d.Mesh=LoadMesh(FilePath(root,ReadString(r)));ReadString(r);
                        if(!profiles.TryGetValue(d.Name,out d.Sheet))throw new InvalidDataException("Gravity material sheet missing");
                        string textureFile=FilePath(root,d.Sheet.TextureFile);
                        d.Position=V3(r);d.Rotation=Q4(r);d.Scale=V3(r);d.Size=V3(r);d.Euler=V3(r)*Mathf.Rad2Deg;
                        d.Phase=r.ReadSingle();d.Delay=r.ReadSingle();d.Life=r.ReadSingle();d.EmitDuration=r.ReadSingle();d.Alignment=r.ReadInt32();d.Repeat=r.ReadInt32();
                        if(d.Life<=0||d.Life>10||d.EmitDuration<0||d.EmitDuration>10)throw new InvalidDataException("Gravity source lifetime bound");
                        d.UV=new Vector4(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());d.UVSpeed=new Vector2(r.ReadSingle(),r.ReadSingle());d.StartColor=C4(r);
                        int nc=GradientCount(r);d.ColorTimes=new float[nc];d.Colors=new Color[nc];for(int k=0;k<nc;k++){d.ColorTimes[k]=r.ReadSingle();d.Colors[k]=new Color(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),1f);}
                        int na=GradientCount(r);d.AlphaTimes=new float[na];d.Alpha=new float[na];for(int k=0;k<na;k++){d.AlphaTimes[k]=r.ReadSingle();d.Alpha[k]=r.ReadSingle();}
                        d.Texture=new Texture2D(2,2,TextureFormat.RGBA32,true);d.Texture.name="EF_Gilberta_"+d.Name;d.Texture.hideFlags=HideFlags.HideAndDontSave;
                        if(!d.Texture.LoadImage(File.ReadAllBytes(textureFile)))throw new InvalidDataException("Gravity source PNG decode failed");
                        if(d.Texture.width!=d.Sheet.Columns*d.Sheet.Cell||d.Texture.height!=d.Sheet.Rows*d.Sheet.Cell)
                            throw new InvalidDataException("Gravity material sheet dimensions");
                        d.Texture.wrapMode=TextureWrapMode.Clamp;d.Texture.filterMode=FilterMode.Trilinear;d.Texture.anisoLevel=1;
                        d.Texture.Apply(true,true);
                        d.Material=new Material(shader);d.Material.name="EF_Gilberta_SourceAdapter_"+d.Name;d.Material.hideFlags=HideFlags.HideAndDontSave;d.Material.mainTexture=d.Texture;
                        d.Material.SetColor("_Color",new Color(MaterialRadiance,MaterialRadiance,MaterialRadiance,1f));d.Material.renderQueue=3000+i;
                    }
                    if(r.BaseStream.Position!=r.BaseStream.Length)throw new InvalidDataException("Gravity source config trailing data");
                }
                SourceTrails=LoadSourceTrails(root,shader);
                Definitions=loaded.ToArray();Runtime.Log("GILBERTA_SOURCE_VFX_READY nativeMeshes=3; meshLayers=5; sourceInwardTrails="+TrailCount+"; trailDrawCalls=1; sourceRadialVelocity="+SourceTrails.RadialSpeed+"; sourceVertexRGBA=True; sourcePixelMasks=True; sourceUVSheets=32; atlasMipMiB=25.542; sourceViewAndLocalAlignment=True; uniformXYZScale="+(GilbertaGravityAssets.RadiusMeters/SourceRadius)+"; sourceShaderReconstruction=True; shader="+shader.name+"; runtimeVisualAcceptancePending=True");
            }
            catch{foreach(Definition d in loaded)DestroyDefinition(d);throw;}
        }
        public void Emit(Vector3 target,float radius)
        {
            Prepare();Age=0;RaycastHit hit;if(Physics.Raycast(new Vector3(target.x,10000,target.z),Vector3.down,out hit,20000,1<<8))target.y=hit.point.y+.15f;
            transform.position=target;transform.rotation=Quaternion.identity;transform.localScale=Vector3.one*(radius/SourceRadius);
            UserCameraRef=null;
            foreach(UserCamera camera in UnityEngine.Object.FindObjectsOfType(typeof(UserCamera)))
            {
                if(camera==null||!camera.enabled||!camera.gameObject.activeInHierarchy||camera.ActiveCamera==null)continue;
                UserCameraRef=camera;
                CameraRef=camera.ActiveCamera;
                break;
            }
            if(UserCameraRef==null)CameraRef=Camera.main;
            if(Layers.Count==0)foreach(Definition d in Definitions)
            {
                AddLayer(d,0f);
                // The source inward-ripple emitter allows exactly two particles
                // at two per second. Preserve its stagger with two reused meshes.
                if(d.Name=="inward_ripple")AddLayer(d,.5f);
            }
            PrepareTrailMesh();
            Render();
        }
        void AddLayer(Definition d,float offset)
        {
            GameObject g=new GameObject(d.Name+(offset>0?"_Second":""));g.hideFlags=HideFlags.DontSave;g.transform.parent=transform;g.transform.localPosition=d.Position;
            Mesh mesh=UnityEngine.Object.Instantiate(d.Mesh) as Mesh;mesh.MarkDynamic();mesh.name=d.Mesh.name+"_PooledSheetUV";
            MeshFilter filter=g.AddComponent<MeshFilter>();filter.sharedMesh=mesh;MeshRenderer renderer=g.AddComponent<MeshRenderer>();renderer.sharedMaterial=d.Material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            Vector2[] sourceUV=d.Mesh.uv;
            Layers.Add(new Instance{Data=d,Transform=g.transform,Renderer=renderer,Offset=offset,Mesh=mesh,SourceUV=sourceUV,AtlasUV=new Vector2[sourceUV.Length]});
        }
        public void Step(float dt)
        {
            if(!Active)return;
            Age=Mathf.Min(GilbertaGravityAssets.ActiveSeconds,Age+dt);
            Render();
        }
        // Called by the presentation event bridge when the native field ends.
        public void Stop()
        {
            Age=GilbertaGravityAssets.ActiveSeconds;
            Render();
        }
        static void SetSheetFrame(Instance layer,float age)
        {
            GilbertaGravityProfile sheet=layer.Data.Sheet;
            int frame=Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(age)*(sheet.Frames-1)),0,sheet.Frames-1);
            if(frame==layer.Frame)return;
            layer.Frame=frame;
            for(int index=0;index<layer.AtlasUV.Length;index++)layer.AtlasUV[index]=sheet.AtlasUV(layer.SourceUV[index],frame);
            layer.Mesh.uv=layer.AtlasUV;
        }
        void Render()
        {
            float sourceClock=GilbertaGravityPolicy.SourceClock(Age,GilbertaGravityAssets.SourcePullSeconds,GilbertaGravityAssets.ActiveSeconds);
            bool shown=Active&&!UserCamera.IsSensorsModeActive;
            if(UserCameraRef!=null)CameraRef=UserCameraRef.ActiveCamera;
            foreach(Instance layer in Layers)
            {
                Definition d=layer.Data;float local=sourceClock-d.Phase-d.Delay-layer.Offset;bool emitted=local>=0;
                if(d.Repeat!=0)
                {
                    // Continue only particles emitted before the source emitter
                    // stops. Duration and particle lifespan stay distinct.
                    float birth=local<0?0:Mathf.Floor(local/d.Life)*d.Life;
                    emitted=emitted&&birth<=d.EmitDuration;local-=birth;
                }
                bool visible=shown&&emitted&&local<=d.Life;layer.Renderer.enabled=visible;if(!visible)continue;
                layer.Transform.localScale=Vector3.Scale(d.Scale,d.Size);
                Quaternion particle=Quaternion.Euler(d.Euler);
                layer.Transform.localRotation=d.Alignment==0&&CameraRef!=null?CameraRef.transform.rotation*particle:d.Rotation*particle;
                SetSheetFrame(layer,Mathf.Clamp01(local/d.Life));
            }
            RenderSourceTrails(sourceClock,shown);
        }
        static TrailDefinition LoadSourceTrails(string root,Shader shader)
        {
            TrailDefinition data=new TrailDefinition();
            using(BinaryReader reader=new BinaryReader(File.OpenRead(Path.Combine(root,"source_trails.bin"))))
            {
                if(reader.ReadUInt32()!=0x31525447||reader.ReadInt32()!=1)throw new InvalidDataException("Gravity trail signature");
                data.Position=V3(reader);
                data.Rotation=Q4(reader);
                data.Scale=V3(reader);
                data.Radius=reader.ReadSingle();
                data.RadialSpeed=reader.ReadSingle();
                data.Lifetime=reader.ReadSingle();
                data.TrailLifetime=reader.ReadSingle();
                data.OrbitZMin=reader.ReadSingle();
                data.OrbitZMax=reader.ReadSingle();
                data.OrbitYMin=reader.ReadSingle();
                data.OrbitYMax=reader.ReadSingle();
                data.InitialSpeed=reader.ReadSingle();
                data.Width=reader.ReadSingle();
                data.EmitDuration=reader.ReadSingle();
                data.Delay=reader.ReadSingle();
                data.Color=C4(reader);
                if(data.RadialSpeed>=0f||data.Radius<=0f||data.Radius>12f||data.Lifetime<=0f||data.Lifetime>2f||reader.BaseStream.Position!=reader.BaseStream.Length)
                    throw new InvalidDataException("Gravity source inward trail limits");
            }
            try
            {
                data.Texture=new Texture2D(2,2,TextureFormat.RGBA32,true);
                data.Texture.name="EF_Gilberta_SourceInwardTrail";
                data.Texture.hideFlags=HideFlags.HideAndDontSave;
                if(!data.Texture.LoadImage(File.ReadAllBytes(Path.Combine(root,"inward_source_trail.png"))))throw new InvalidDataException("Gravity source trail PNG");
                data.Texture.wrapMode=TextureWrapMode.Clamp;
                data.Texture.filterMode=FilterMode.Trilinear;
                data.Material=new Material(shader);
                data.Material.name="EF_Gilberta_SourceInwardTrailAdapter";
                data.Material.hideFlags=HideFlags.HideAndDontSave;
                data.Material.mainTexture=data.Texture;
                data.Material.SetColor("_Color",Color.white);data.Material.renderQueue=3006;
                data.Texture.Apply(true,true);
                return data;
            }
            catch
            {
                if(data.Material!=null)UnityEngine.Object.Destroy(data.Material);
                if(data.Texture!=null)UnityEngine.Object.Destroy(data.Texture);
                throw;
            }
        }
        void PrepareTrailMesh()
        {
            if(TrailMesh!=null)return;
            int quadCount=TrailCount*TrailSegments;
            TrailVertices=new Vector3[quadCount*4];
            TrailColors=new Color32[quadCount*4];
            Vector2[] uv=new Vector2[quadCount*4];
            int[] indices=new int[quadCount*6];
            for(int index=0;index<quadCount;index++)
            {
                int vertex=index*4,triangle=index*6,segment=index%TrailSegments;
                float start=segment/(float)TrailSegments,end=(segment+1)/(float)TrailSegments;
                uv[vertex]=new Vector2(start,0);
                uv[vertex+1]=new Vector2(start,1);
                uv[vertex+2]=new Vector2(end,0);
                uv[vertex+3]=new Vector2(end,1);
                indices[triangle]=vertex;
                indices[triangle+1]=vertex+2;
                indices[triangle+2]=vertex+1;
                indices[triangle+3]=vertex+1;
                indices[triangle+4]=vertex+2;
                indices[triangle+5]=vertex+3;
            }
            TrailMesh=new Mesh();
            TrailMesh.name="EF_Gilberta_BoundedSourceInwardTrails";
            TrailMesh.hideFlags=HideFlags.HideAndDontSave;
            TrailMesh.MarkDynamic();
            TrailMesh.vertices=TrailVertices;
            TrailMesh.colors32=TrailColors;
            TrailMesh.uv=uv;
            TrailMesh.triangles=indices;
            TrailMesh.bounds=new Bounds(Vector3.zero,Vector3.one*18f);
            GameObject host=new GameObject("SourceInwardTrails");
            host.transform.parent=transform;
            host.transform.localPosition=Vector3.zero;
            host.transform.localRotation=Quaternion.identity;
            host.transform.localScale=Vector3.one;
            host.AddComponent<MeshFilter>().sharedMesh=TrailMesh;
            TrailRenderer=host.AddComponent<MeshRenderer>();
            TrailRenderer.sharedMaterial=SourceTrails.Material;
            TrailRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            TrailRenderer.receiveShadows=false;
        }
        static Vector3 SourceTrailPoint(int trail,float age)
        {
            TrailDefinition source=SourceTrails;
            float fraction=(trail*.61803398875f)%1f;
            float angle=trail*2.39996323f;
            float radius=Mathf.Max(.1f,source.Radius*(.65f+.35f*fraction)+source.RadialSpeed*age);
            Vector3 point=new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius,source.InitialSpeed*age);
            float orbitZ=Mathf.Lerp(source.OrbitZMin,source.OrbitZMax,fraction)*age*Mathf.Rad2Deg;
            float orbitY=Mathf.Lerp(source.OrbitYMin,source.OrbitYMax,(fraction+.37f)%1f)*age*Mathf.Rad2Deg;
            point=Quaternion.Euler(0,orbitY,orbitZ)*point;
            return source.Position+source.Rotation*Vector3.Scale(source.Scale,point);
        }
        void RenderSourceTrails(float sourceClock,bool visible)
        {
            if(TrailRenderer==null)return;
            float emitterTime=sourceClock-.5f-SourceTrails.Delay;
            TrailRenderer.enabled=visible&&emitterTime>=0f&&emitterTime<SourceTrails.EmitDuration+SourceTrails.Lifetime;
            if(!TrailRenderer.enabled)return;
            Vector3 viewForward=Vector3.forward;
            if(CameraRef!=null)viewForward=transform.InverseTransformDirection(CameraRef.transform.forward);
            for(int trail=0;trail<TrailCount;trail++)
            {
                float phase=SourceTrails.Lifetime*trail/TrailCount;
                float birth=Mathf.Floor((emitterTime-phase)/SourceTrails.Lifetime)*SourceTrails.Lifetime+phase;
                float age=emitterTime-birth;
                float alpha=0f;
                if(birth>=0f&&birth<=SourceTrails.EmitDuration)
                    alpha=Mathf.Clamp01(age/.035f)*Mathf.Clamp01(1f-age/(SourceTrails.Lifetime*.8f));
                Color color=SourceTrails.Color;
                color.a*=alpha;
                for(int segment=0;segment<TrailSegments;segment++)
                {
                    float start=segment/(float)TrailSegments,end=(segment+1)/(float)TrailSegments;
                    Vector3 head=SourceTrailPoint(trail,Mathf.Max(0,age-SourceTrails.TrailLifetime*start));
                    Vector3 tail=SourceTrailPoint(trail,Mathf.Max(0,age-SourceTrails.TrailLifetime*end));
                    Vector3 side=Vector3.Cross(tail-head,viewForward).normalized;
                    float width=SourceTrails.Width*TrailWidthMultiplier*SourceTrails.Scale.x;
                    int vertex=(trail*TrailSegments+segment)*4;
                    TrailVertices[vertex]=head-side*width*(1f-start);
                    TrailVertices[vertex+1]=head+side*width*(1f-start);
                    TrailVertices[vertex+2]=tail-side*width*(1f-end);
                    TrailVertices[vertex+3]=tail+side*width*(1f-end);
                    TrailColors[vertex]=(Color32)color;
                    TrailColors[vertex+1]=(Color32)color;
                    Color tailColor=color;
                    tailColor.a*=1f-end;
                    TrailColors[vertex+2]=(Color32)tailColor;
                    TrailColors[vertex+3]=(Color32)tailColor;
                }
            }
            TrailMesh.vertices=TrailVertices;
            TrailMesh.colors32=TrailColors;
        }
        void OnDestroy()
        {
            foreach(Instance layer in Layers)if(layer.Mesh!=null)UnityEngine.Object.Destroy(layer.Mesh);
            Layers.Clear();
            if(TrailMesh!=null)UnityEngine.Object.Destroy(TrailMesh);
            TrailMesh=null;
        }
        static void DestroyDefinition(Definition d)
        {if(d.Material!=null)UnityEngine.Object.Destroy(d.Material);if(d.Mesh!=null)UnityEngine.Object.Destroy(d.Mesh);if(d.Texture!=null)UnityEngine.Object.Destroy(d.Texture);}
        public static void ReleaseShared()
        {
            if(Definitions!=null)foreach(Definition d in Definitions)DestroyDefinition(d);
            Definitions=null;
            if(SourceTrails!=null)
            {
                if(SourceTrails.Material!=null)UnityEngine.Object.Destroy(SourceTrails.Material);
                if(SourceTrails.Texture!=null)UnityEngine.Object.Destroy(SourceTrails.Texture);
                SourceTrails=null;
            }
        }
    }
}
