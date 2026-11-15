using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EndfieldDoK.ModelIntake;

namespace EndfieldDoK.MapUnitLab
{
    // Immutable, bounded author motion tables. Read/hash once per resource generation;
    // the presentation host only samples cached arrays, never reads a frame-time file.
    internal sealed class RhodesR19DeploymentData
    {
        static RhodesR19DeploymentData Cached;static string CachedPath;
        internal readonly Dictionary<string,Dictionary<string,V17NodeCurve>> Clips=new Dictionary<string,Dictionary<string,V17NodeCurve>>(StringComparer.Ordinal);
        static readonly string[] ClipNames={"GN_INIT","GN_AFT","AV_DEPLOY"};
        static readonly int[] NodeCounts={70,43,3},SampleCounts={646,466,721};
        static int Count(BinaryReader r,int min,int max)
        {int n=r.ReadInt32();if(n<min||n>max)throw new InvalidDataException("R19 motion count budget");return n;}
        static string Text(BinaryReader r)
        {int n=Count(r,1,256);byte[] b=r.ReadBytes(n);if(b.Length!=n)throw new EndOfStreamException();return new UTF8Encoding(false,true).GetString(b);}
        static float Number(BinaryReader r)
        {float x=r.ReadSingle();if(float.IsNaN(x)||float.IsInfinity(x)||Math.Abs(x)>100000f)throw new InvalidDataException("R19 motion nonfinite/range");return x;}
        internal static bool IsR19(RhodesCarrierBinding binding)
        {return binding!=null&&binding.FindNode("PIVOT_GN_ModuleLift")!=null;}
        internal static RhodesR19DeploymentData Load()
        {
            string folder=Path.Combine(CustomUnits.AssetDirectory,"rhodes_v05"),path=Path.Combine(folder,"r19_deployment.r19d");
            if(Cached!=null&&CachedPath==path)return Cached;
            Cached=Read(folder);CachedPath=path;return Cached;
        }
        internal static RhodesR19DeploymentData Read(string folder)
        {
            RhodesResourceContent content=RhodesResourceContent.Read(Path.Combine(folder,"r19_deployment.r19d"),4194304L);
            RhodesSuspensionV14Contract.RequireR19DeploymentPackageHash(content.Sha256);
            RhodesR19DeploymentData result=new RhodesR19DeploymentData();
            using(MemoryStream stream=new MemoryStream(content.Bytes,false))using(BinaryReader reader=new BinaryReader(stream))
            {
                if(stream.Length<160||Encoding.ASCII.GetString(reader.ReadBytes(4))!="R19D"||reader.ReadInt32()!=1)
                    throw new InvalidDataException("R19 deployment format");
                if(Text(reader)!=RhodesSuspensionV14Contract.GeometrySHA256||Text(reader)!=RhodesSuspensionV14Contract.ModelPackageSHA256)
                    throw new InvalidDataException("R19 deployment source/model pair differs");
                Count(reader,3,3);
                for(int clip=0;clip<ClipNames.Length;clip++)
                {
                    string name=Text(reader);if(name!=ClipNames[clip])throw new InvalidDataException("R19 motion clip order/name differs");
                    Count(reader,NodeCounts[clip],NodeCounts[clip]);
                    Dictionary<string,V17NodeCurve> curves=new Dictionary<string,V17NodeCurve>(StringComparer.Ordinal);
                    for(int node=0;node<NodeCounts[clip];node++)
                    {
                        string bone=Text(reader);float fps=Number(reader);int samples=Count(reader,SampleCounts[clip],SampleCounts[clip]);
                        if(fps!=30f||!AllowedNode(clip,bone))throw new InvalidDataException("R19 motion node/clock differs: "+bone);
                        V17NodeCurve curve=new V17NodeCurve{FPS=fps,Positions=new UnityEngine.Vector3[samples],Rotations=new UnityEngine.Quaternion[samples]};
                        for(int sample=0;sample<samples;sample++)
                        {
                            curve.Positions[sample]=new UnityEngine.Vector3(Number(reader),Number(reader),Number(reader));
                            UnityEngine.Quaternion q=new UnityEngine.Quaternion(Number(reader),Number(reader),Number(reader),Number(reader));
                            float magnitude=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
                            if(Math.Abs(magnitude-1f)>.002f)throw new InvalidDataException("R19 motion quaternion");curve.Rotations[sample]=q;
                        }
                        curves.Add(bone,curve);
                    }
                    result.Clips.Add(name,curves);
                }
                if(stream.Position!=stream.Length)throw new InvalidDataException("R19 motion trailing bytes");
            }
            return result;
        }
        static bool Numbered(string name,string prefix,int maximum)
        {
            if(!name.StartsWith(prefix,StringComparison.Ordinal)||name.Length!=prefix.Length+3)return false;
            int number;return int.TryParse(name.Substring(prefix.Length),out number)&&number>=0&&number<maximum&&name==prefix+number.ToString("000",System.Globalization.CultureInfo.InvariantCulture);
        }
        static bool AllowedNode(int clip,string name)
        {
            if(clip==2)return name=="V17_PIVOT_AV_ModuleCarrier"||name=="V17_PIVOT_AV_IslandCabin"||name=="V17_PIVOT_AV_IslandMast";
            if(Numbered(name,"V19_PIVOT_GN_AFT_SHUTTER_",40))return true;
            if(name=="PIVOT_GN_AFT_Lift"||name=="PIVOT_GN_AFT_CoverLift"||name=="PIVOT_GN_AFT_TransportYaw")return true;
            return clip==0&&(name=="PIVOT_GN_ModuleLift"||name=="PIVOT_GN_FORE_Lift"||name=="PIVOT_GN_FORE_TransportYaw"||Numbered(name,"V08_PIVOT_SHUTTER_DECK_",24));
        }
        internal static void Reset(){Cached=null;CachedPath=null;}
    }
}
