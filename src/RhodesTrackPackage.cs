using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EndfieldDoK.ModelIntake
{
    // Pure managed TKR reader. Never invokes Unity, game code or source scripts.
    public sealed class RhodesTrackPath
    {
        public int Kind;
        public float AY,AZ,BY,BZ,Radius,Angle,Length;
    }
    public sealed class RhodesTrackBone
    {
        public string Name;
        public int Kind;
        public float BaseDistance,Radius,InitialAngle;
        public float[] InitialPosition;
    }
    public sealed class RhodesTrackSubmesh
    {
        public int Material;
        public int[] Indices;
    }
    public sealed class RhodesTrackGroup
    {
        public string BogieName;
        public float LocalRight,Perimeter;
        public float[] Bounds;
        public float[] RigOffset,RigScale;
        public RhodesTrackPath[] Path;
        public RhodesTrackBone[] Bones;
        public float[] Vertices;
        public int[] VertexBones;
        public RhodesTrackSubmesh[] Submeshes;
    }
    public sealed class RhodesTrackPackage
    {
        // TKR1: paired-bogie skins. TKR2: one skin per independent wide belt;
        // consecutive pairs share one suspension lift, never one pitch pivot.
        public int Version,ModuleCount;
        public bool IndependentBeltPitch { get { return Version==2; } }
        public RhodesMaterial[] Materials;
        public RhodesTrackGroup[] Groups;
        static readonly Encoding Utf8=new UTF8Encoding(false,true);
        public static RhodesTrackPackage Read(string file)
        {
            using(FileStream stream=File.OpenRead(file))return Read(stream);
        }
        public static RhodesTrackPackage Read(Stream stream)
        {
            if(stream==null||!stream.CanRead||!stream.CanSeek||stream.Length<16||stream.Length>33554432L)
                throw new InvalidDataException("Invalid TKR stream length/capabilities");
            BinaryReader reader=new BinaryReader(stream,Utf8);
            string magic=Encoding.ASCII.GetString(reader.ReadBytes(4));int version=reader.ReadInt32();
            if(!((magic=="TKR1"&&version==1)||(magic=="TKR2"&&version==2)))
                throw new InvalidDataException("Unsupported TKR magic/version");
            int materials=Count(reader,128,"materials"),groups=Count(reader,version==1?24:64,"groups");
            int modules=version==1?groups:Count(reader,32,"modules");
            if(materials<1||(version==1?(groups!=20&&groups!=24):(modules<1||groups!=modules*2)))
                throw new InvalidDataException("TKR layout must be legacy 20/24 paired bogies or two independent belt skins per declared module");
            RhodesTrackPackage package=new RhodesTrackPackage();package.Version=version;package.ModuleCount=modules;package.Materials=new RhodesMaterial[materials];package.Groups=new RhodesTrackGroup[groups];
            HashSet<string> names=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<materials;i++)
            {
                RhodesMaterial material=new RhodesMaterial();material.Name=Text(reader);material.Albedo=Asset(reader);material.Normal=Asset(reader);material.Emission=Asset(reader);
                material.BaseColour=Floats(reader,4);material.Metallic=Number(reader);material.Roughness=Number(reader);
                if(string.IsNullOrEmpty(material.Name)||!names.Add(material.Name)||material.Metallic<0f||material.Metallic>1f||material.Roughness<0f||material.Roughness>1f)
                    throw new InvalidDataException("Invalid TKR material identity/range");
                package.Materials[i]=material;
            }
            names.Clear();int totalBones=0,totalVertices=0,totalIndices=0;
            for(int i=0;i<groups;i++)
            {
                RhodesTrackGroup group=new RhodesTrackGroup();group.BogieName=Text(reader);group.LocalRight=Number(reader);group.Perimeter=Number(reader);group.Bounds=Floats(reader,6);
                string expectedName=version==1?"CHASSIS_"+i.ToString("00",System.Globalization.CultureInfo.InvariantCulture)+"_BogiePivot":"TRACK_"+i.ToString("00",System.Globalization.CultureInfo.InvariantCulture)+"_PitchPivot";
                if(group.BogieName!=expectedName||!names.Add(group.BogieName)||group.Perimeter<10f||group.Perimeter>500f)
                    throw new InvalidDataException("Invalid TKR bogie identity/perimeter");
                for(int axis=0;axis<3;axis++)if(group.Bounds[axis]>=group.Bounds[axis+3])throw new InvalidDataException("Invalid TKR swept bounds");
                group.RigOffset=version==2?Floats(reader,3):new float[3];
                group.RigScale=version==2?Floats(reader,3):new float[]{1f,1f,1f};
                for(int axis=0;axis<3;axis++)if(group.RigScale[axis]<.1f||group.RigScale[axis]>10f||Math.Abs(group.RigOffset[axis])>100f)
                    throw new InvalidDataException("TKR positive diagonal rig-space affine exceeds bounds");
                int segments=Count(reader,8,"path segments");if(segments!=8)throw new InvalidDataException("TKR path requires four lines and four arcs");
                group.Path=new RhodesTrackPath[segments];float length=0f;
                for(int k=0;k<segments;k++)
                {
                    RhodesTrackPath segment=new RhodesTrackPath();segment.Kind=reader.ReadInt32();segment.AY=Number(reader);segment.AZ=Number(reader);segment.BY=Number(reader);segment.BZ=Number(reader);segment.Radius=Number(reader);segment.Angle=Number(reader);segment.Length=Number(reader);
                    if(segment.Kind!=k%2||segment.Length<=0f||(segment.Kind==1&&segment.Radius<=0f))throw new InvalidDataException("Invalid TKR path segment");
                    length+=segment.Length;group.Path[k]=segment;
                }
                if(Math.Abs(length-group.Perimeter)>.005f)throw new InvalidDataException("TKR segment/perimeter mismatch");
                int bones=Count(reader,255,"bones");if(version==1?(bones!=69&&bones!=130):bones<1)throw new InvalidDataException("Unexpected TKR rigid skeleton size");
                group.Bones=new RhodesTrackBone[bones];HashSet<string> boneNames=new HashSet<string>(StringComparer.Ordinal);
                for(int k=0;k<bones;k++)
                {
                    RhodesTrackBone bone=new RhodesTrackBone();bone.Name=Text(reader);bone.Kind=reader.ReadInt32();bone.BaseDistance=Number(reader);bone.Radius=Number(reader);bone.InitialPosition=Floats(reader,3);bone.InitialAngle=Number(reader);
                    if(!boneNames.Add(bone.Name)||(bone.Kind!=0&&bone.Kind!=1)||Math.Abs(bone.InitialPosition[0])>.01f||bone.BaseDistance<0f||bone.BaseDistance>=group.Perimeter||(bone.Kind==1&&bone.Radius<=0f))
                        throw new InvalidDataException("Invalid TKR rigid bone");
                    group.Bones[k]=bone;
                }
                int vertices=Count(reader,60000,"vertices"),submeshes=Count(reader,materials,"submeshes");
                if(vertices<1||submeshes<1)throw new InvalidDataException("Empty TKR mesh");
                group.Vertices=FloatsWithBones(reader,vertices,bones,out group.VertexBones);group.Submeshes=new RhodesTrackSubmesh[submeshes];
                for(int k=0;k<submeshes;k++)
                {
                    RhodesTrackSubmesh submesh=new RhodesTrackSubmesh();submesh.Material=reader.ReadInt32();int indices=Count(reader,200000,"indices");
                    if(submesh.Material<0||submesh.Material>=materials||indices<3||indices%3!=0)throw new InvalidDataException("Invalid TKR submesh");
                    submesh.Indices=new int[indices];
                    for(int j=0;j<indices;j++){int index=reader.ReadInt32();if(index<0||index>=vertices)throw new InvalidDataException("TKR index outside vertex range");submesh.Indices[j]=index;}
                    totalIndices+=indices;group.Submeshes[k]=submesh;
                }
                totalBones+=bones;totalVertices+=vertices;package.Groups[i]=group;
            }
            if((version==1?totalBones!=(groups==20?1868:1900):totalBones>4096)||totalVertices>300000||totalIndices>500000||stream.Position!=stream.Length)
                throw new InvalidDataException("TKR complete-package budget or trailing bytes mismatch");
            return package;
        }
        static float[] FloatsWithBones(BinaryReader reader,int count,int bones,out int[] weights)
        {
            float[] values=new float[count*10];weights=new int[count];
            for(int i=0;i<count;i++)
            {
                for(int k=0;k<10;k++)values[i*10+k]=Number(reader);
                int bone=reader.ReadInt32();if(bone<0||bone>=bones)throw new InvalidDataException("TKR vertex bone outside skeleton");weights[i]=bone;
                float nx=values[i*10+3],ny=values[i*10+4],nz=values[i*10+5];float n=nx*nx+ny*ny+nz*nz;
                if(n<.5f||n>1.5f)throw new InvalidDataException("Invalid TKR supplied normal");
            }
            return values;
        }
        static int Count(BinaryReader reader,int maximum,string what)
        {int value=reader.ReadInt32();if(value<0||value>maximum)throw new InvalidDataException("TKR "+what+" budget exceeded");return value;}
        static string Text(BinaryReader reader)
        {int count=Count(reader,1024,"UTF8 bytes");byte[] raw=reader.ReadBytes(count);if(raw.Length!=count)throw new EndOfStreamException();return Utf8.GetString(raw);}
        static string Asset(BinaryReader reader)
        {string path=Text(reader);if(Path.IsPathRooted(path)||path.IndexOf("..",StringComparison.Ordinal)>=0)throw new InvalidDataException("Unsafe TKR material asset path");return path;}
        static float Number(BinaryReader reader)
        {float value=reader.ReadSingle();if(float.IsNaN(value)||float.IsInfinity(value)||Math.Abs(value)>1000000f)throw new InvalidDataException("Invalid TKR finite number");return value;}
        static float[] Floats(BinaryReader reader,int count)
        {float[] values=new float[count];for(int i=0;i<count;i++)values[i]=Number(reader);return values;}
    }
}
