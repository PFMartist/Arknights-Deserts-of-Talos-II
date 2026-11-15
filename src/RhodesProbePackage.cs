using System;
using System.IO;
using System.Text;

namespace EndfieldDoK.ModelIntake
{
    public sealed class RhodesProbeFrame
    {
        public float[] Pose; // parent-local position3/quaternion4, metres
        public float[] Wire; // geometry-root-local position3/normal3 per vertex
    }
    // Pure managed RPA1 reader: no Unity or native game dependency.
    public sealed class RhodesProbePackage
    {
        public const long MaximumBytes=33554432L;
        public const int MaximumVertices=12000,MaximumFrames=1025;
        public float Duration,FramesPerSecond;
        public int VertexCount;
        public string PivotName,CableName;
        public RhodesMaterial Material;
        public float[] WireBounds,ProbeSweptBounds,UV;
        public int[] Indices;
        public RhodesProbeFrame[] Frames;
        public float PositionLoopSeam,WirePositionLoopSeam,WireNormalLoopSeam;
        public double RotationLoopAbsDot;
        static readonly Encoding Utf8=new UTF8Encoding(false,true);
        public static RhodesProbePackage Read(string file)
        {using(FileStream stream=File.OpenRead(file))return Read(stream);}
        public static RhodesProbePackage Read(Stream stream)
        {
            if(stream==null||!stream.CanRead||!stream.CanSeek||stream.Length<32||stream.Length>MaximumBytes)
                throw new InvalidDataException("Invalid RPA stream length/capabilities");
            try{return ReadBody(new BinaryReader(stream,Utf8));}
            catch(EndOfStreamException error){throw new InvalidDataException("Truncated RPA payload",error);}
            catch(OverflowException error){throw new InvalidDataException("RPA budget arithmetic overflow",error);}
            catch(DecoderFallbackException error){throw new InvalidDataException("Invalid RPA UTF8 string",error);}
        }
        static RhodesProbePackage ReadBody(BinaryReader reader)
        {
            if(Encoding.ASCII.GetString(reader.ReadBytes(4))!="RPA1"||reader.ReadInt32()!=1)throw new InvalidDataException("Unsupported RPA magic/version");
            int frames=Count(reader,MaximumFrames,"frames"),vertices=Count(reader,MaximumVertices,"vertices"),indices=Count(reader,360000,"indices");
            if(frames<2||vertices<3||indices<3||indices%3!=0)throw new InvalidDataException("Empty/invalid RPA topology");
            RhodesProbePackage result=new RhodesProbePackage();result.Duration=Number(reader);result.FramesPerSecond=Number(reader);result.VertexCount=vertices;
            if(result.Duration<=0f||result.Duration>60f||result.FramesPerSecond<1f||result.FramesPerSecond>120f||Math.Abs((frames-1)-result.Duration*result.FramesPerSecond)>.01f)
                throw new InvalidDataException("RPA inclusive-frame timing mismatch");
            result.PivotName=Text(reader);result.CableName=Text(reader);
            if(result.PivotName!="PIVOT_ProbeSway"||result.CableName!="GEO_EF_PROBE_Tether")throw new InvalidDataException("Unknown RPA pivot/cable identity");
            RhodesMaterial material=new RhodesMaterial();material.Name=Text(reader);material.Albedo=Asset(reader);material.Normal=Asset(reader);material.Emission=Asset(reader);
            material.BaseColour=Numbers(reader,4);material.Metallic=Number(reader);material.Roughness=Number(reader);
            if(string.IsNullOrEmpty(material.Name)||material.Metallic<0f||material.Metallic>1f||material.Roughness<0f||material.Roughness>1f)throw new InvalidDataException("Invalid RPA material factors");
            foreach(float value in material.BaseColour)if(value<0f||value>1f)throw new InvalidDataException("Invalid RPA base colour");result.Material=material;
            result.WireBounds=Bounds(reader);result.ProbeSweptBounds=Bounds(reader);
            long expected=checked(vertices*8L+indices*4L+frames*(28L+vertices*24L));
            if(expected>MaximumBytes||reader.BaseStream.Length-reader.BaseStream.Position!=expected)throw new InvalidDataException("RPA payload budget, truncation or trailing bytes mismatch");
            result.UV=Numbers(reader,checked(vertices*2));result.Indices=new int[indices];
            for(int i=0;i<indices;i++){int index=reader.ReadInt32();if(index<0||index>=vertices)throw new InvalidDataException("RPA index outside vertex array");result.Indices[i]=index;}
            result.Frames=new RhodesProbeFrame[frames];
            for(int f=0;f<frames;f++)
            {
                RhodesProbeFrame frame=new RhodesProbeFrame();frame.Pose=Numbers(reader,7);double norm=0;
                for(int i=3;i<7;i++)norm+=(double)frame.Pose[i]*frame.Pose[i];if(Math.Abs(norm-1.0)>.01)throw new InvalidDataException("RPA non-unit pose quaternion");
                frame.Wire=Numbers(reader,checked(vertices*6));
                for(int i=0;i<vertices;i++)
                {
                    int at=i*6;
                    for(int axis=0;axis<3;axis++)if(frame.Wire[at+axis]<result.WireBounds[axis]-.01f||frame.Wire[at+axis]>result.WireBounds[axis+3]+.01f)throw new InvalidDataException("RPA vertex outside swept wire bounds");
                    double normal=0;for(int axis=3;axis<6;axis++)normal+=(double)frame.Wire[at+axis]*frame.Wire[at+axis];
                    if(normal<.5||normal>1.5)throw new InvalidDataException("RPA invalid wire normal");
                }
                result.Frames[f]=frame;
            }
            if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("Trailing RPA bytes");
            ValidateLoop(result);return result;
        }
        static void ValidateLoop(RhodesProbePackage data)
        {
            RhodesProbeFrame a=data.Frames[0],b=data.Frames[data.Frames.Length-1];double dot=0,na=0,nb=0;
            for(int i=0;i<3;i++)data.PositionLoopSeam=Math.Max(data.PositionLoopSeam,Math.Abs(a.Pose[i]-b.Pose[i]));
            for(int i=3;i<7;i++){dot+=(double)a.Pose[i]*b.Pose[i];na+=(double)a.Pose[i]*a.Pose[i];nb+=(double)b.Pose[i]*b.Pose[i];}
            data.RotationLoopAbsDot=Math.Min(1.0,Math.Abs(dot)/Math.Sqrt(na*nb));
            for(int i=0;i<a.Wire.Length;i++)
                if(i%6<3)data.WirePositionLoopSeam=Math.Max(data.WirePositionLoopSeam,Math.Abs(a.Wire[i]-b.Wire[i]));
                else data.WireNormalLoopSeam=Math.Max(data.WireNormalLoopSeam,Math.Abs(a.Wire[i]-b.Wire[i]));
            if(data.PositionLoopSeam>.0015f||data.WirePositionLoopSeam>.0015f||data.WireNormalLoopSeam>.0015f||data.RotationLoopAbsDot<.999999)
                throw new InvalidDataException("RPA authored loop is discontinuous");
        }
        static int Count(BinaryReader reader,int maximum,string name)
        {int value=reader.ReadInt32();if(value<0||value>maximum)throw new InvalidDataException("RPA "+name+" budget exceeded");return value;}
        static float Number(BinaryReader reader)
        {float value=reader.ReadSingle();if(float.IsNaN(value)||float.IsInfinity(value)||Math.Abs(value)>1000000f)throw new InvalidDataException("RPA nonfinite/out-of-budget number");return value;}
        static float[] Numbers(BinaryReader reader,int count)
        {float[] values=new float[count];for(int i=0;i<count;i++)values[i]=Number(reader);return values;}
        static float[] Bounds(BinaryReader reader)
        {float[] values=Numbers(reader,6);for(int i=0;i<3;i++)if(values[i]>=values[i+3])throw new InvalidDataException("RPA invalid swept bounds");return values;}
        static string Text(BinaryReader reader)
        {
            int count=Count(reader,1024,"UTF8 string");byte[] bytes=reader.ReadBytes(count);if(bytes.Length!=count)throw new EndOfStreamException();
            string value=Utf8.GetString(bytes);if(value.IndexOf('\0')>=0)throw new InvalidDataException("RPA NUL in string");return value;
        }
        static string Asset(BinaryReader reader)
        {string value=Text(reader);RhodesModelPackage.SafeAssetPath(Path.GetTempPath(),value);return value;}
    }
}
