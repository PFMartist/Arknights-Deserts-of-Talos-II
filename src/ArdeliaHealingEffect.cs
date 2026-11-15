using System;
using System.IO;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Fixed-cost Unity5 presentation from the supplied original sheep mesh,
    // albedo, flying-sheep sprite and ultimate range textures. Source sheep
    // skeletal/dissolve/VAT animation was not delivered in converted form.
    // The subtle whole-mesh float below is explicitly an approximation.
    public sealed class ArdeliaHealingEffect:MonoBehaviour
    {
        static Mesh SheepMesh,RingMesh;
        static Texture2D SheepTexture,FlyTexture,GlowTexture,RingTexture;
        static Material SheepTemplate,FlyTemplate,GlowTemplate,RingTemplate;
        Transform Sheep;
        MeshRenderer SheepRenderer,FlyRenderer,GlowRenderer,RingRenderer;
        Material SheepMaterial,FlyMaterial,GlowMaterial,RingMaterial;
        Mesh FlyMesh;
        readonly Vector3[] Points=new Vector3[48];readonly Vector2[] UV=new Vector2[48];readonly Color[] Colors=new Color[48];
        bool Prepared;float NextGround,NextSprites,Ground,PreviousAge;Vector3 LastGround;
        Camera Camera;UserCamera UserCameraRef;
        static string Folder{get{return Path.Combine(HumanoidModel.AssetDirectory,"ardelia_v01/healing_vfx");}}
        static Shader ShaderFor(string first,string fallback)
        {Shader shader=Shader.Find(first);if(shader==null||!shader.isSupported)shader=Shader.Find(fallback);if(shader==null||!shader.isSupported)throw new InvalidDataException("Native healing visual shader unavailable");return shader;}
        static Texture2D Texture(string filename,int maximum)
        {
            string file=Path.Combine(Folder,filename);FileInfo info=new FileInfo(file);if(!info.Exists||info.Length<1||info.Length>4*1024*1024)throw new InvalidDataException("Healing texture absent or exceeds budget: "+filename);
            Texture2D t=new Texture2D(2,2,TextureFormat.RGBA32,true);t.hideFlags=HideFlags.HideAndDontSave;
            if(!t.LoadImage(File.ReadAllBytes(file))||t.width>maximum||t.height>maximum){UnityEngine.Object.Destroy(t);throw new InvalidDataException("Healing texture dimensions differ: "+filename);}t.name="EF_Ardelia_Source_"+filename;t.wrapMode=TextureWrapMode.Clamp;t.filterMode=FilterMode.Trilinear;return t;
        }
        static Material Template(string name,Shader shader,Texture texture)
        {Material m=new Material(shader);m.name=name;m.hideFlags=HideFlags.HideAndDontSave;m.mainTexture=texture;return m;}
        static void PrepareShared()
        {
            if(SheepMesh!=null)return;
            try
            {
                using(BinaryReader input=new BinaryReader(File.OpenRead(Path.Combine(Folder,"sheep_mesh.bin"))))
                {
                    if(input.BaseStream.Length>1024*1024||new string(input.ReadChars(4))!="ASH1")throw new InvalidDataException("Invalid source sheep mesh");
                    int count=input.ReadInt32(),indices=input.ReadInt32();if(count!=2911||indices!=14916)throw new InvalidDataException("Source sheep mesh dimensions differ");
                    Vector3[] vertices=new Vector3[count],normals=new Vector3[count];Vector2[] uv=new Vector2[count];int[] triangles=new int[indices];
                    for(int i=0;i<count;i++)vertices[i]=new Vector3(input.ReadSingle(),input.ReadSingle(),input.ReadSingle());
                    for(int i=0;i<count;i++)normals[i]=new Vector3(input.ReadSingle(),input.ReadSingle(),input.ReadSingle());
                    for(int i=0;i<count;i++)uv[i]=new Vector2(input.ReadSingle(),input.ReadSingle());
                    for(int i=0;i<indices;i++){triangles[i]=input.ReadInt32();if(triangles[i]<0||triangles[i]>=count)throw new InvalidDataException("Source sheep index outside mesh");}
                    if(input.BaseStream.Position!=input.BaseStream.Length)throw new InvalidDataException("Trailing source sheep data");
                    SheepMesh=new Mesh();SheepMesh.name="EF_Ardelia_OriginalUltimateSheep_Static";SheepMesh.hideFlags=HideFlags.HideAndDontSave;SheepMesh.vertices=vertices;SheepMesh.normals=normals;SheepMesh.uv=uv;SheepMesh.triangles=triangles;SheepMesh.RecalculateBounds();
                }
                const int segments=96;Vector3[] ring=new Vector3[(segments+1)*2];Vector2[] ringUV=new Vector2[ring.Length];int[] ringTriangles=new int[segments*6];
                for(int i=0;i<=segments;i++){float a=i*Mathf.PI*2f/segments;Vector3 direction=new Vector3(Mathf.Sin(a),0f,Mathf.Cos(a));ring[i*2]=direction*.94f;ring[i*2+1]=direction;ringUV[i*2]=new Vector2((float)i/segments,0f);ringUV[i*2+1]=new Vector2((float)i/segments,1f);if(i<segments){int v=i*2,t=i*6;ringTriangles[t]=v;ringTriangles[t+1]=v+1;ringTriangles[t+2]=v+2;ringTriangles[t+3]=v+1;ringTriangles[t+4]=v+3;ringTriangles[t+5]=v+2;}}
                RingMesh=new Mesh();RingMesh.name="EF_Ardelia_SourceRangeAnnulus";RingMesh.hideFlags=HideFlags.HideAndDontSave;RingMesh.vertices=ring;RingMesh.uv=ringUV;RingMesh.triangles=ringTriangles;RingMesh.RecalculateBounds();
                SheepTexture=Texture("sheep_albedo.png",1024);FlyTexture=Texture("flying_sheep.png",512);GlowTexture=Texture("field_glow.png",512);RingTexture=Texture("field_ring.png",512);
                SheepTemplate=Template("EF_Ardelia_SheepLegacy",ShaderFor("Legacy Shaders/Transparent/Diffuse","Unlit/Transparent"),SheepTexture);
                Shader additive=ShaderFor("Particles/Additive","Legacy Shaders/Particles/Additive");FlyTemplate=Template("EF_Ardelia_FlyingSheepLegacy",additive,FlyTexture);GlowTemplate=Template("EF_Ardelia_RangeGlowLegacy",additive,GlowTexture);RingTemplate=Template("EF_Ardelia_RangeRingLegacy",additive,RingTexture);
            }
            catch{ReleaseShared();throw;}
        }
        MeshRenderer Node(string name,Mesh mesh,Material material,out Transform node)
        {GameObject child=new GameObject(name);child.hideFlags=HideFlags.HideAndDontSave;node=child.transform;node.parent=transform;node.localPosition=Vector3.zero;node.localRotation=Quaternion.identity;node.localScale=Vector3.one;child.AddComponent<MeshFilter>().sharedMesh=mesh;MeshRenderer r=child.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;r.enabled=false;return r;}
        void Prepare()
        {
            if(Prepared)return;PrepareShared();SheepMaterial=new Material(SheepTemplate);FlyMaterial=new Material(FlyTemplate);GlowMaterial=new Material(GlowTemplate);RingMaterial=new Material(RingTemplate);
            SheepRenderer=Node("OriginalUltimateSheep_StaticFloat",SheepMesh,SheepMaterial,out Sheep);Sheep.localScale=Vector3.one*30f;
            Transform ring;RingRenderer=Node("SourceRangeRing",RingMesh,RingMaterial,out ring);ring.localScale=Vector3.one*ArdeliaHealingProfile.RadiusMetres;
            Transform glow;GlowRenderer=Node("SourceRangeGlow",RingMesh,GlowMaterial,out glow);glow.localScale=Vector3.one*(ArdeliaHealingProfile.RadiusMetres*.975f);glow.localPosition=new Vector3(0,.3f,0);
            FlyMesh=new Mesh();FlyMesh.name="EF_Ardelia_SourceFlyingSheepBatch";FlyMesh.hideFlags=HideFlags.HideAndDontSave;FlyMesh.MarkDynamic();int[] triangles=new int[72];for(int i=0;i<12;i++){int v=i*4,t=i*6;triangles[t]=v;triangles[t+1]=v+2;triangles[t+2]=v+1;triangles[t+3]=v+1;triangles[t+4]=v+2;triangles[t+5]=v+3;UV[v]=new Vector2(0,0);UV[v+1]=new Vector2(1,0);UV[v+2]=new Vector2(0,1);UV[v+3]=new Vector2(1,1);}
            FlyMesh.vertices=Points;FlyMesh.uv=UV;FlyMesh.colors=Colors;FlyMesh.triangles=triangles;FlyMesh.bounds=new Bounds(new Vector3(0,45f,0),new Vector3(850f,160f,850f));Transform flyers;FlyRenderer=Node("OriginalFlyingSheepSprites12",FlyMesh,FlyMaterial,out flyers);
            UserCameraRef=SmallUnitCamera.ActiveGameplayCamera;Camera=UserCameraRef==null?UnityEngine.Camera.main:UserCameraRef.ActiveCamera;Prepared=true;
        }
        public void Hide(){if(SheepRenderer!=null)SheepRenderer.enabled=false;if(FlyRenderer!=null)FlyRenderer.enabled=false;if(GlowRenderer!=null)GlowRenderer.enabled=false;if(RingRenderer!=null)RingRenderer.enabled=false;}
        public void Step(Vector3 center,float age,bool ending,bool visible)
        {
            if(!visible){Hide();return;}Prepare();
            if(age<PreviousAge)NextSprites=0f;PreviousAge=age;
            if(Time.realtimeSinceStartup>=NextGround||(center-LastGround).sqrMagnitude>900f)
            {RaycastHit hit;if(Physics.Raycast(new Vector3(center.x,10000f,center.z),Vector3.down,out hit,20000f,1<<8))Ground=hit.point.y;LastGround=center;NextGround=Time.realtimeSinceStartup+.5f;}
            center.y=Ground+.6f;transform.position=center;transform.rotation=Quaternion.identity;
            float fade=ending?Mathf.Clamp01(1f-age/1.125f):Mathf.Clamp01(age/.4f);if(fade<=0f){Hide();return;}
            SheepRenderer.enabled=RingRenderer.enabled=GlowRenderer.enabled=FlyRenderer.enabled=true;Sheep.localPosition=new Vector3(0,18f+Mathf.Sin(age*2.2f)*2f,0);Sheep.localRotation=Quaternion.Euler(0,age*7f,0);SheepMaterial.color=new Color(1f,1f,1f,fade*.9f);
            float pulse=.75f+.25f*Mathf.Cos(age*Mathf.PI*2f);Color tint=new Color(.48f,.32f,.5f,fade*.55f);FlyMaterial.SetColor("_TintColor",tint);RingMaterial.SetColor("_TintColor",new Color(.5f,.31f,.5f,fade*(.3f+.2f*pulse)));GlowMaterial.SetColor("_TintColor",new Color(.32f,.4f,.38f,fade*.25f));
            if(age<NextSprites&&!ending)return;NextSprites=age+1f/30f;
            if(UserCameraRef!=null&&UserCameraRef.ActiveCamera!=null)Camera=UserCameraRef.ActiveCamera;Transform cam=Camera==null?null:Camera.transform;Vector3 right=cam==null?Vector3.right:cam.right,up=cam==null?Vector3.up:cam.up;
            for(int i=0;i<12;i++)
            {float angle=i*2.39996323f+age*.25f,distance=60f+(i%4)*65f;Vector3 position=new Vector3(Mathf.Sin(angle)*distance,18f+(i%3)*13f+Mathf.Sin(age*1.7f+i)*5f,Mathf.Cos(angle)*distance);Vector3 rw=right*(12f+i%3*2f),uh=up*(12f+i%3*2f);int v=i*4;Points[v]=position-rw-uh;Points[v+1]=position+rw-uh;Points[v+2]=position-rw+uh;Points[v+3]=position+rw+uh;for(int k=0;k<4;k++)Colors[v+k]=new Color(1f,.88f,1f,fade*.65f);}
            FlyMesh.vertices=Points;FlyMesh.colors=Colors;
        }
        void OnDestroy(){if(FlyMesh!=null)UnityEngine.Object.Destroy(FlyMesh);foreach(Material m in new[]{SheepMaterial,FlyMaterial,GlowMaterial,RingMaterial})if(m!=null)UnityEngine.Object.Destroy(m);}
        public static void ReleaseShared()
        {foreach(UnityEngine.Object value in new UnityEngine.Object[]{SheepMesh,RingMesh,SheepTexture,FlyTexture,GlowTexture,RingTexture,SheepTemplate,FlyTemplate,GlowTemplate,RingTemplate})if(value!=null)UnityEngine.Object.Destroy(value);SheepMesh=RingMesh=null;SheepTexture=FlyTexture=GlowTexture=RingTexture=null;SheepTemplate=FlyTemplate=GlowTemplate=RingTemplate=null;}
    }
}
