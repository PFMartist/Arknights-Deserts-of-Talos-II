using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EndfieldDoK.ModelIntake
{
    public sealed class FactionTrackSegment
    {
        public int Kind;
        public float AF,AU,BF,BU,Radius,Angle,Sweep,Length;
    }
    public sealed class FactionTrackWheel
    {
        public string Node;
        public int Lod;
        public float Radius;
    }
    public sealed class FactionTrackSubmesh
    {
        public int Material;
        public int[] Indices;
    }
    public sealed class FactionTrackSkin
    {
        public int Lod;
        public float[] Vertices;
        public int[] Bones;
        public FactionTrackSubmesh[] Submeshes;
    }
    public sealed class FactionTrackBelt
    {
        public string Name,Node;
        public float LocalRight,Perimeter;
        public float[] Bounds,Phases;
        public FactionTrackSegment[] Path;
        public FactionTrackWheel[] Wheels;
        public FactionTrackSkin[] Skins;
    }
    // Pure managed FTK1 parser. The RHS flag makes a missing/mismatched skin
    // package fail at registration, rather than displaying a trackless vehicle.
    public sealed class FactionTrackPackage
    {
        public const string FileName="faction_tracks.ftk";
        public string UnitKey,ModelSha256;
        public FactionTrackBelt[] Belts;
        static readonly Encoding Utf8=new UTF8Encoding(false,true);
        public static FactionTrackPackage Read(string file)
        {using(FileStream stream=File.OpenRead(file))return Read(stream);}
        public static FactionTrackPackage Read(Stream stream)
        {
            if(stream==null||!stream.CanRead||!stream.CanSeek||stream.Length<32||stream.Length>33554432L)
                throw new InvalidDataException("Invalid FTK stream");
            BinaryReader r=new BinaryReader(stream,Utf8);
            if(Encoding.ASCII.GetString(r.ReadBytes(4))!="FTK1"||r.ReadInt32()!=1)throw new InvalidDataException("Unsupported FTK version");
            FactionTrackPackage p=new FactionTrackPackage();p.UnitKey=Text(r);p.ModelSha256=Text(r);
            int expected=p.UnitKey=="RI_U06_RAILGUN"?3:p.UnitKey=="RI_U05_SUPPRESSION"||p.UnitKey=="RI_U07_AIR_DEFENCE"||p.UnitKey=="RI_U09_ARTILLERY"?4:0;
            if(expected==0||p.ModelSha256.Length!=64)throw new InvalidDataException("FTK unit/hash contract");
            foreach(char c in p.ModelSha256)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))throw new InvalidDataException("FTK hash encoding");
            int count=Count(r,8);if(count!=expected)throw new InvalidDataException("FTK belt roster mismatch");
            p.Belts=new FactionTrackBelt[count];HashSet<string> nodes=new HashSet<string>(StringComparer.Ordinal),wheels=new HashSet<string>(StringComparer.Ordinal);
            int totalVertices=0,totalIndices=0;
            for(int i=0;i<count;i++)
            {
                FactionTrackBelt b=new FactionTrackBelt();b.Name=Text(r);b.Node=Text(r);b.LocalRight=Number(r);b.Perimeter=Number(r);b.Bounds=Floats(r,6);
                if(!nodes.Add(b.Node)||!b.Node.StartsWith("L0::PIVOT_V03_Track_",StringComparison.Ordinal)||b.Perimeter<10f||b.Perimeter>50f||Math.Abs(b.LocalRight)>30f)
                    throw new InvalidDataException("FTK belt binding/range");
                for(int axis=0;axis<3;axis++)if(b.Bounds[axis]>=b.Bounds[axis+3])throw new InvalidDataException("FTK swept bounds");
                int segments=Count(r,16);if(segments!=9)throw new InvalidDataException("FTK v03 path has nine exact tangent/arc segments");
                b.Path=new FactionTrackSegment[segments];double length=0;
                for(int k=0;k<segments;k++)
                {
                    FactionTrackSegment s=new FactionTrackSegment();s.Kind=r.ReadInt32();s.AF=Number(r);s.AU=Number(r);s.BF=Number(r);s.BU=Number(r);s.Radius=Number(r);s.Angle=Number(r);s.Sweep=Number(r);s.Length=Number(r);
                    if((s.Kind!=0&&s.Kind!=1)||s.Length<=0f||(s.Kind==1&&(s.Radius<=0f||Math.Abs(s.Sweep)<1e-6f||Math.Abs(s.Radius*Math.Abs(s.Sweep)-s.Length)>.0001f)))
                        throw new InvalidDataException("FTK invalid path segment");
                    if(s.Kind==0&&Math.Abs(Math.Sqrt((s.BF-s.AF)*(s.BF-s.AF)+(s.BU-s.AU)*(s.BU-s.AU))-s.Length)>.0001)
                        throw new InvalidDataException("FTK line length mismatch");
                    length+=s.Length;b.Path[k]=s;
                }
                if(Math.Abs(length-b.Perimeter)>.0001)throw new InvalidDataException("FTK perimeter mismatch");
                int bones=Count(r,255);if(bones!=86)throw new InvalidDataException("FTK requires 43 shoes and 43 intervening pin stations");
                b.Phases=Floats(r,bones);
                for(int k=0;k<bones;k++)if(Math.Abs(b.Phases[k]-b.Perimeter*k/bones)>.0001f)throw new InvalidDataException("FTK phase spacing");
                int wc=Count(r,40);if(wc!=24)throw new InvalidDataException("FTK wheel roster needs eight wheels per LOD");
                b.Wheels=new FactionTrackWheel[wc];int[] wheelLods=new int[3];
                for(int k=0;k<wc;k++)
                {
                    FactionTrackWheel w=new FactionTrackWheel();w.Node=Text(r);w.Lod=r.ReadInt32();w.Radius=Number(r);
                    if(w.Lod<0||w.Lod>2||!wheels.Add(w.Node)||!w.Node.StartsWith("L"+w.Lod+"::",StringComparison.Ordinal)||w.Radius<.1f||w.Radius>2f)
                        throw new InvalidDataException("FTK wheel binding");
                    wheelLods[w.Lod]++;b.Wheels[k]=w;
                }
                for(int k=0;k<3;k++)if(wheelLods[k]!=8)throw new InvalidDataException("FTK wheel LOD roster mismatch");
                int skins=Count(r,3);if(skins!=3)throw new InvalidDataException("FTK needs three explicit LOD skins");
                b.Skins=new FactionTrackSkin[skins];
                for(int k=0;k<skins;k++)
                {
                    FactionTrackSkin s=new FactionTrackSkin();s.Lod=r.ReadInt32();int vertices=Count(r,60000),submeshes=Count(r,16);
                    if(s.Lod!=k||vertices<3||submeshes<1)throw new InvalidDataException("FTK skin roster");
                    s.Vertices=new float[vertices*10];s.Bones=new int[vertices];
                    for(int v=0;v<vertices;v++)
                    {
                        for(int c=0;c<10;c++)s.Vertices[v*10+c]=Number(r);
                        int bi=r.ReadInt32();if(bi<0||bi>=bones)throw new InvalidDataException("FTK vertex bone index");s.Bones[v]=bi;
                        float x=s.Vertices[v*10+3],y=s.Vertices[v*10+4],z=s.Vertices[v*10+5],norm=x*x+y*y+z*z;
                        if(norm<.5f||norm>1.5f)throw new InvalidDataException("FTK supplied normal");
                        for(int axis=0;axis<3;axis++)if(s.Vertices[v*10+axis]<b.Bounds[axis]||s.Vertices[v*10+axis]>b.Bounds[axis+3])throw new InvalidDataException("FTK vertex outside swept bounds");
                    }
                    s.Submeshes=new FactionTrackSubmesh[submeshes];HashSet<int> materials=new HashSet<int>();
                    for(int sm=0;sm<submeshes;sm++)
                    {
                        FactionTrackSubmesh sub=new FactionTrackSubmesh();sub.Material=r.ReadInt32();int indices=Count(r,180000);
                        if(sub.Material<0||!materials.Add(sub.Material)||indices<3||indices%3!=0)throw new InvalidDataException("FTK submesh binding");
                        sub.Indices=new int[indices];for(int index=0;index<indices;index++){int vi=r.ReadInt32();if(vi<0||vi>=vertices)throw new InvalidDataException("FTK geometry index");sub.Indices[index]=vi;}
                        totalIndices+=indices;s.Submeshes[sm]=sub;
                    }
                    totalVertices+=vertices;b.Skins[k]=s;
                }
                // Closure and tangent continuity are checked from the exact serialized path.
                for(int k=0;k<b.Path.Length;k++)
                {
                    FactionTrackSegment a=b.Path[k],next=b.Path[(k+1)%b.Path.Length];double af,au,aa,bf,bu,ba;
                    FactionTrackMath.SampleSegment(a,1,out af,out au,out aa);FactionTrackMath.SampleSegment(next,0,out bf,out bu,out ba);
                    if(Math.Abs(af-bf)>.00001||Math.Abs(au-bu)>.00001||Math.Abs(Math.Sin(aa-ba))>.00001||Math.Cos(aa-ba)<.99999)
                        throw new InvalidDataException("FTK discontinuous path/tangent");
                }
                p.Belts[i]=b;
            }
            if(totalVertices>300000||totalIndices>900000||stream.Position!=stream.Length)throw new InvalidDataException("FTK aggregate/trailing data budget");
            return p;
        }
        public void RequireModel(RhodesModelPackage model,string sha)
        {
            if(model==null||!model.ExternalTrackGeometry||sha!=ModelSha256)throw new InvalidDataException("FTK/RHS model hash mismatch");
            Dictionary<string,int> nodes=new Dictionary<string,int>(StringComparer.Ordinal);
            double[][] world=new double[model.Nodes.Length][];
            for(int i=0;i<model.Nodes.Length;i++)
            {
                RhodesNode n=model.Nodes[i];nodes.Add(n.Name,i);double[] local=RhodesModelPackage.Matrix(n);
                world[i]=n.Parent<0?local:RhodesModelPackage.Multiply(world[n.Parent],local);
            }
            RhodesBounds bounds=RhodesModelPackage.CalculateVisualBounds(model);
            foreach(FactionTrackBelt b in Belts)
            {
                int index;if(!nodes.TryGetValue(b.Node,out index))throw new InvalidDataException("FTK belt node missing");
                if(Math.Abs(world[index][3]-b.LocalRight)>.0001)throw new InvalidDataException("FTK differential offset mismatch");
                for(int row=0;row<3;row++)for(int col=0;col<3;col++)
                    if(Math.Abs(world[index][row*4+col]-(row==col?1:0))>.00001)
                        throw new InvalidDataException("FTK v03 track axes/scale require the authored unit basis");
                foreach(FactionTrackWheel wheel in b.Wheels)if(!nodes.ContainsKey(wheel.Node))throw new InvalidDataException("FTK wheel node missing");
                foreach(FactionTrackSkin s in b.Skins)
                {
                    foreach(FactionTrackSubmesh sub in s.Submeshes)if(sub.Material>=model.Materials.Length)throw new InvalidDataException("FTK material outside RHS table");
                    double[] m=world[index];
                    for(int v=0;v<s.Vertices.Length;v+=10)for(int a=0;a<3;a++)
                    {
                        float value=(float)(m[a*4]*s.Vertices[v]+m[a*4+1]*s.Vertices[v+1]+m[a*4+2]*s.Vertices[v+2]+m[a*4+3]);
                        bounds.Min[a]=Math.Min(bounds.Min[a],value);bounds.Max[a]=Math.Max(bounds.Max[a],value);
                    }
                }
            }
            for(int a=0;a<3;a++)if(Math.Abs(bounds.Min[a]-model.Visual.Min[a])>.01f||Math.Abs(bounds.Max[a]-model.Visual.Max[a])>.01f)
                throw new InvalidDataException("RHS plus FTK complete visual bounds mismatch");
        }
        static int Count(BinaryReader r,int max){int n=r.ReadInt32();if(n<0||n>max)throw new InvalidDataException("FTK count budget");return n;}
        static string Text(BinaryReader r){int n=Count(r,512);byte[] bytes=r.ReadBytes(n);if(bytes.Length!=n)throw new EndOfStreamException();string s=Utf8.GetString(bytes);if(s.IndexOf('\0')>=0||s.Length==0)throw new InvalidDataException("FTK text");return s;}
        static float Number(BinaryReader r){float n=r.ReadSingle();if(float.IsNaN(n)||float.IsInfinity(n)||Math.Abs(n)>100000f)throw new InvalidDataException("FTK finite number");return n;}
        static float[] Floats(BinaryReader r,int count){float[] v=new float[count];for(int i=0;i<count;i++)v[i]=Number(r);return v;}
    }
    // No Unity dependency: the runtime and managed motion fixtures use this
    // exact sampler and signed differential formula.
    public static class FactionTrackMath
    {
        public static double Wrap(double value,double period)
        {
            if(double.IsNaN(value)||double.IsInfinity(value)||period<=0||double.IsNaN(period)||double.IsInfinity(period))throw new ArgumentOutOfRangeException("period/value");
            double n=value%period;if(n<0)n+=period;
            // Adding a tiny negative remainder to a large positive period can
            // round to the endpoint. Canonicalize it to preserve [0, period).
            return n>=period?0:n;
        }
        public static double Travel(double forwardDistance,double yawRadians,double localRight)
        {return forwardDistance-yawRadians*localRight;}
        public static void SampleSegment(FactionTrackSegment s,double fraction,out double forward,out double up,out double angle)
        {
            fraction=Math.Max(0,Math.Min(1,fraction));
            if(s.Kind==0){forward=s.AF+(s.BF-s.AF)*fraction;up=s.AU+(s.BU-s.AU)*fraction;angle=Math.Atan2(s.BF-s.AF,s.BU-s.AU);}
            else
            {
                double a=s.Angle+s.Sweep*fraction;forward=s.AF+s.Radius*Math.Cos(a);up=s.AU+s.Radius*Math.Sin(a);
                double sign=Math.Sign(s.Sweep);angle=Math.Atan2(-Math.Sin(a)*sign,Math.Cos(a)*sign);
            }
        }
        public static void Sample(FactionTrackBelt belt,double distance,out double forward,out double up,out double angle)
        {
            double s=Wrap(distance,belt.Perimeter);
            for(int i=0;i<belt.Path.Length;i++)
            {
                FactionTrackSegment part=belt.Path[i];
                if(s<=part.Length||i==belt.Path.Length-1){SampleSegment(part,s/part.Length,out forward,out up,out angle);return;}
                s-=part.Length;
            }
            throw new InvalidOperationException("FTK sampler overflow");
        }
    }
}
