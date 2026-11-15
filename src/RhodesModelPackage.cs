using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EndfieldDoK.ModelIntake
{
    // Pure managed format layer: no Unity, JSON library, registry or game hooks.
    public sealed class RhodesBounds
    {
        public float[] Min=new float[3],Max=new float[3];
        public bool Specified;
    }
    public sealed class RhodesNode
    {
        public string Name;
        public int Parent;
        public float[] Position,Rotation,Scale;
    }
    public sealed class RhodesMaterial
    {
        public string Name,Albedo,Normal,Emission;
        public float[] BaseColour;
        public float Metallic,Roughness;
    }
    public sealed class RhodesMesh
    {
        public string Name;
        public int Node,Material,Lod;
        public float[] Vertices; // position3, normal3, runtime UV0_2, UV1_2
        public int[] Indices;
    }
    public sealed class RhodesModule
    {
        public string Id,Branch,Slot,Role,Research;
        public int Node,Level;
    }
    public sealed class RhodesModelPackage
    {
        public int Version;
        public string ContentSha256 { get; internal set; }
        public bool ExternalTrackGeometry;
        public FactionTrackPackage Tracks;
        public RhodesBounds Visual=new RhodesBounds(),Selection=new RhodesBounds(),Navigation=new RhodesBounds();
        public RhodesNode[] Nodes;
        public RhodesMaterial[] Materials;
        public RhodesMesh[] Meshes;
        public RhodesModule[] Modules;
        public const int MaximumMeshVertices=60000;
        public const long MaximumFileBytes=268435456;
        static readonly Encoding Utf8=new UTF8Encoding(false,true);

        public static RhodesModelPackage Read(string file)
        {
            RhodesResourceContent content=RhodesResourceContent.Read(file,MaximumFileBytes);
            using(MemoryStream stream=new MemoryStream(content.Bytes,false))
            {
                RhodesModelPackage result=Read(stream);result.ContentSha256=content.Sha256;
                if(result.ExternalTrackGeometry)
                {
                    result.Tracks=FactionTrackPackage.Read(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file)),FactionTrackPackage.FileName));
                    result.Tracks.RequireModel(result,content.Sha256);
                }
                return result;
            }
        }
        public static RhodesModelPackage Read(Stream stream)
        {
            if(stream==null || !stream.CanRead || !stream.CanSeek || stream.Length<16 || stream.Length>MaximumFileBytes)
                throw new InvalidDataException("Invalid RHS stream length/capabilities");
            BinaryReader reader=new BinaryReader(stream,Utf8);
            string magic=Encoding.ASCII.GetString(reader.ReadBytes(4));
            int version=reader.ReadInt32();RhodesModelPackage result;
            if(magic=="RHS1" && version==1)result=ReadLegacy(reader);
            else if(magic=="RHS2" && version==2)result=ReadV2(reader);
            else throw new InvalidDataException("Unsupported RHS magic/version");
            if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing RHS bytes");
            Validate(result);return result;
        }
        static RhodesModelPackage ReadLegacy(BinaryReader reader)
        {
            int vertices=Count(reader,MaximumMeshVertices,"legacy vertices"),indices=Count(reader,180000,"legacy indices");
            if(vertices!=14400 || indices!=14400 || reader.BaseStream.Length!=16L+vertices*40L+indices*4L)
                throw new InvalidDataException("RHS1 legacy contract mismatch");
            RhodesMesh mesh=ReadGeometry(reader,"LegacyLOD0",0,0,0,vertices,indices);
            RhodesModelPackage result=new RhodesModelPackage();result.Version=1;
            result.Nodes=new RhodesNode[]{new RhodesNode{Name="ROOT_Rhodes",Parent=-1,Position=new float[3],Rotation=new float[]{0,0,0,1},Scale=new float[]{1,1,1}}};
            result.Materials=new RhodesMaterial[]{new RhodesMaterial{Name="Legacy_NOT_COLOR_REFERENCE",Albedo="albedo.png",Normal="",Emission="",BaseColour=new float[]{1,1,1,1},Metallic=.15f,Roughness=.8f}};
            result.Meshes=new RhodesMesh[]{mesh};result.Modules=new RhodesModule[0];result.Visual=CalculateVisualBounds(result);
            return result;
        }
        static RhodesModelPackage ReadV2(BinaryReader reader)
        {
            int nodes=Count(reader,4096,"nodes"),materials=Count(reader,128,"materials"),meshes=Count(reader,2048,"meshes"),modules=Count(reader,1024,"modules");
            if(nodes==0 || materials==0 || meshes==0)throw new InvalidDataException("Empty RHS2 geometry");
            int flags=reader.ReadInt32();if((flags&~7)!=0)throw new InvalidDataException("Unknown RHS2 bounds flags");
            RhodesModelPackage result=new RhodesModelPackage();result.Version=2;result.ExternalTrackGeometry=(flags&4)!=0;
            result.Visual=ReadBounds(reader,true);result.Selection=ReadBounds(reader,(flags&1)!=0);result.Navigation=ReadBounds(reader,(flags&2)!=0);
            result.Nodes=new RhodesNode[nodes];result.Materials=new RhodesMaterial[materials];result.Meshes=new RhodesMesh[meshes];result.Modules=new RhodesModule[modules];
            for(int index=0;index<nodes;index++)
            {
                RhodesNode node=new RhodesNode();node.Name=Text(reader);node.Parent=reader.ReadInt32();
                node.Position=Numbers(reader,3);node.Rotation=Numbers(reader,4);node.Scale=Numbers(reader,3);result.Nodes[index]=node;
            }
            for(int index=0;index<materials;index++)
            {
                RhodesMaterial material=new RhodesMaterial();material.Name=Text(reader);material.Albedo=Text(reader);material.Normal=Text(reader);material.Emission=Text(reader);
                material.BaseColour=Numbers(reader,4);material.Metallic=Number(reader);material.Roughness=Number(reader);result.Materials[index]=material;
            }
            int totalVertices=0,totalIndices=0;
            for(int index=0;index<meshes;index++)
            {
                string name=Text(reader);int node=reader.ReadInt32(),material=reader.ReadInt32(),lod=reader.ReadInt32();
                int vertices=Count(reader,MaximumMeshVertices,"vertices"),indices=Count(reader,1800000,"indices");
                totalVertices=checked(totalVertices+vertices);totalIndices=checked(totalIndices+indices);
                if(totalVertices>3000000 || totalIndices>9000000)throw new InvalidDataException("RHS2 aggregate geometry budget exceeded");
                result.Meshes[index]=ReadGeometry(reader,name,node,material,lod,vertices,indices);
            }
            for(int index=0;index<modules;index++)
            {
                RhodesModule module=new RhodesModule();module.Id=Text(reader);module.Node=reader.ReadInt32();module.Branch=Text(reader);module.Slot=Text(reader);
                module.Role=Text(reader);module.Research=Text(reader);module.Level=reader.ReadInt32();result.Modules[index]=module;
            }
            return result;
        }
        static RhodesMesh ReadGeometry(BinaryReader reader,string name,int node,int material,int lod,int vertices,int indices)
        {
            if(vertices<3 || indices<3 || indices%3!=0 || reader.BaseStream.Length-reader.BaseStream.Position<vertices*40L+indices*4L)
                throw new InvalidDataException("Invalid RHS geometry length");
            RhodesMesh mesh=new RhodesMesh();mesh.Name=name;mesh.Node=node;mesh.Material=material;mesh.Lod=lod;
            mesh.Vertices=Numbers(reader,checked(vertices*10));mesh.Indices=new int[indices];
            for(int index=0;index<indices;index++){int value=reader.ReadInt32();if(value<0 || value>=vertices)throw new InvalidDataException("RHS index outside vertex array");mesh.Indices[index]=value;}
            for(int index=0;index<vertices;index++)
            {
                int offset=index*10;float x=mesh.Vertices[offset+3],y=mesh.Vertices[offset+4],z=mesh.Vertices[offset+5];
                float length=x*x+y*y+z*z;if(length<.1f || length>4f)throw new InvalidDataException("Invalid RHS normal");
            }
            return mesh;
        }
        static RhodesBounds ReadBounds(BinaryReader reader,bool specified)
        {return new RhodesBounds{Min=Numbers(reader,3),Max=Numbers(reader,3),Specified=specified};}
        static int Count(BinaryReader reader,int maximum,string name)
        {int count=reader.ReadInt32();if(count<0 || count>maximum)throw new InvalidDataException("Invalid RHS "+name);return count;}
        static float Number(BinaryReader reader)
        {float value=reader.ReadSingle();if(float.IsNaN(value)||float.IsInfinity(value)||Math.Abs(value)>1000000f)throw new InvalidDataException("Nonfinite/out-of-budget RHS number");return value;}
        static float[] Numbers(BinaryReader reader,int count)
        {float[] values=new float[count];for(int index=0;index<count;index++)values[index]=Number(reader);return values;}
        static string Text(BinaryReader reader)
        {
            int bytes=Count(reader,1024,"UTF8 string");byte[] value=reader.ReadBytes(bytes);if(value.Length!=bytes)throw new EndOfStreamException();
            string text=Utf8.GetString(value);if(text.IndexOf('\0')>=0)throw new InvalidDataException("NUL in RHS string");return text;
        }
        public static string SafeAssetPath(string folder,string relative)
        {
            if(string.IsNullOrEmpty(relative))return null;
            if(Path.IsPathRooted(relative)||relative.IndexOf(':')>=0)throw new InvalidDataException("RHS texture must be relative");
            string[] parts=relative.Replace('\\','/').Split('/');foreach(string part in parts)if(part==".." || part=="" || part==".")throw new InvalidDataException("RHS texture traversal");
            string root=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string target=Path.GetFullPath(Path.Combine(root,relative));if(!target.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("RHS texture escaped package");return target;
        }
        public static void Validate(RhodesModelPackage package)
        {
            Dictionary<string,bool> names=new Dictionary<string,bool>(StringComparer.Ordinal);
            for(int index=0;index<package.Nodes.Length;index++)
            {
                RhodesNode node=package.Nodes[index];if(string.IsNullOrEmpty(node.Name)||names.ContainsKey(node.Name)||node.Parent>=index||node.Parent< -1)throw new InvalidDataException("Duplicate node name/cyclic hierarchy");names.Add(node.Name,true);
                float q=0;foreach(float value in node.Rotation)q+=value*value;if(Math.Abs(q-1f)>.01f)throw new InvalidDataException("Non-unit node quaternion");
                foreach(float value in node.Scale)if(value<=0f)throw new InvalidDataException("Negative/zero scale requires offline transform bake");
            }
            foreach(RhodesMaterial material in package.Materials)
            {
                foreach(float value in material.BaseColour)if(value<0f||value>1f)throw new InvalidDataException("Invalid base colour");
                if(material.Metallic<0f||material.Metallic>1f||material.Roughness<0f||material.Roughness>1f)throw new InvalidDataException("Invalid material factors");
                SafeAssetPath(Path.GetTempPath(),material.Albedo);SafeAssetPath(Path.GetTempPath(),material.Normal);SafeAssetPath(Path.GetTempPath(),material.Emission);
            }
            bool[] lods=new bool[4];int highest=-1;
            foreach(RhodesMesh mesh in package.Meshes)
            {
                if(mesh.Node<0||mesh.Node>=package.Nodes.Length||mesh.Material<0||mesh.Material>=package.Materials.Length||mesh.Lod< -1||mesh.Lod>3)throw new InvalidDataException("Invalid RHS mesh binding");
                if(mesh.Lod>=0){lods[mesh.Lod]=true;highest=Math.Max(highest,mesh.Lod);}
            }
            for(int lod=0;lod<=highest;lod++)if(!lods[lod])throw new InvalidDataException("Explicit LOD levels must be contiguous from LOD0");
            names.Clear();Dictionary<int,bool> moduleNodes=new Dictionary<int,bool>();
            foreach(RhodesModule module in package.Modules)
            {
                if(string.IsNullOrEmpty(module.Id)||names.ContainsKey(module.Id)||module.Node<0||module.Node>=package.Nodes.Length||moduleNodes.ContainsKey(module.Node)||module.Level<0)
                    throw new InvalidDataException("Invalid/duplicate module binding");
                if(module.Branch!="common"&&module.Branch!="aviation"&&module.Branch!="gunnery")throw new InvalidDataException("Unknown Rhodes branch");
                if(module.Role!="base"&&module.Role!="cap"&&module.Role!="module")throw new InvalidDataException("Unknown Rhodes module role");
                names.Add(module.Id,true);moduleNodes.Add(module.Node,true);
            }
            foreach(RhodesBounds bounds in new RhodesBounds[]{package.Visual,package.Selection,package.Navigation})if(bounds.Specified)
                for(int axis=0;axis<3;axis++)if(bounds.Min[axis]>bounds.Max[axis])throw new InvalidDataException("Reversed RHS bounds");
            RhodesBounds computed=CalculateVisualBounds(package);
            for(int axis=0;axis<3;axis++)
            {
                if(package.ExternalTrackGeometry)
                {
                    // The complete bounds are checked with the hash-paired FTK
                    // vertices by Read(file). Stream reads validate static containment.
                    if(computed.Min[axis]<package.Visual.Min[axis]-.01f||computed.Max[axis]>package.Visual.Max[axis]+.01f)
                        throw new InvalidDataException("Static RHS geometry escaped complete FTK bounds");
                }
                else if(Math.Abs(computed.Min[axis]-package.Visual.Min[axis])>.01f||Math.Abs(computed.Max[axis]-package.Visual.Max[axis])>.01f)
                    throw new InvalidDataException("Declared visual bounds differ from transformed meshes");
            }
        }
        public static RhodesBounds CalculateVisualBounds(RhodesModelPackage package)
        {
            double[][] world=new double[package.Nodes.Length][];
            for(int index=0;index<world.Length;index++)
            {
                RhodesNode node=package.Nodes[index];double[] local=Matrix(node);
                world[index]=node.Parent<0?local:Multiply(world[node.Parent],local);
            }
            RhodesBounds result=new RhodesBounds();bool first=true;
            foreach(RhodesMesh mesh in package.Meshes)
            {
                double[] matrix=world[mesh.Node];
                for(int index=0;index<mesh.Vertices.Length;index+=10)for(int axis=0;axis<3;axis++)
                {
                    float value=(float)(matrix[axis*4]*mesh.Vertices[index]+matrix[axis*4+1]*mesh.Vertices[index+1]+matrix[axis*4+2]*mesh.Vertices[index+2]+matrix[axis*4+3]);
                    if(float.IsInfinity(value)||float.IsNaN(value))throw new InvalidDataException("Invalid transformed geometry");
                    if(first){result.Min[axis]=result.Max[axis]=value;}else{result.Min[axis]=Math.Min(result.Min[axis],value);result.Max[axis]=Math.Max(result.Max[axis],value);}
                    if(axis==2)first=false;
                }
            }
            result.Specified=!first;return result;
        }
        internal static double[] Matrix(RhodesNode node)
        {
            double x=node.Rotation[0],y=node.Rotation[1],z=node.Rotation[2],w=node.Rotation[3];float[] s=node.Scale,p=node.Position;
            return new double[]{(1-2*y*y-2*z*z)*s[0],(2*x*y-2*z*w)*s[1],(2*x*z+2*y*w)*s[2],p[0],
                (2*x*y+2*z*w)*s[0],(1-2*x*x-2*z*z)*s[1],(2*y*z-2*x*w)*s[2],p[1],
                (2*x*z-2*y*w)*s[0],(2*y*z+2*x*w)*s[1],(1-2*x*x-2*y*y)*s[2],p[2],0,0,0,1};
        }
        internal static double[] Multiply(double[] left,double[] right)
        {double[] result=new double[16];for(int row=0;row<4;row++)for(int col=0;col<4;col++)for(int k=0;k<4;k++)result[row*4+col]+=left[row*4+k]*right[k*4+col];return result;}
    }
}

