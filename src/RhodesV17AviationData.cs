using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using EndfieldDoK.ModelIntake;

namespace EndfieldDoK.MapUnitLab
{
    internal struct V17AirPose
    {
        internal Vector3 Position;internal Quaternion Rotation;internal float Fold,Gear;
        internal static V17AirPose Mix(V17AirPose a,V17AirPose b,float t)
        {return new V17AirPose{Position=Vector3.Lerp(a.Position,b.Position,t),Rotation=RhodesAviationPoseMath.Blend(a.Rotation,b.Rotation,t),Fold=Mathf.Lerp(a.Fold,b.Fold,t),Gear=Mathf.Lerp(a.Gear,b.Gear,t)};}
    }
    internal sealed class V17AirPath
    {
        internal float[] Times;internal V17AirPose[] Values;
        internal V17AirPose Sample(float time)
        {
            if(time<=Times[0])return Values[0];int last=Times.Length-1;if(time>=Times[last])return Values[last];
            int low=0,high=last;while(high-low>1){int mid=(low+high)/2;if(Times[mid]<=time)low=mid;else high=mid;}
            return V17AirPose.Mix(Values[low],Values[high],Mathf.Clamp01((time-Times[low])/(Times[high]-Times[low])));
        }
    }
    internal sealed class V17NodeCurve
    {
        internal float FPS;internal Vector3[] Positions;internal Quaternion[] Rotations;
        internal void Apply(Transform node,float time)
        {
            float frame=Mathf.Clamp(time*FPS,0f,Positions.Length-1);int a=(int)frame,b=Math.Min(a+1,Positions.Length-1);float t=frame-a;
            node.localPosition=Vector3.Lerp(Positions[a],Positions[b],t);node.localRotation=Quaternion.Slerp(Rotations[a],Rotations[b],t);
        }
    }
    // Immutable source tables acquired once per model generation; never parsed
    // or hashed during frame/aircraft updates.
    internal sealed class RhodesV17AviationData
    {
        static RhodesV17AviationData Cached;static string CachedPath;
        internal readonly Dictionary<string,V17AirPath> Paths=new Dictionary<string,V17AirPath>(StringComparer.Ordinal);
        internal readonly Dictionary<string,V17NodeCurve> Curves=new Dictionary<string,V17NodeCurve>(StringComparer.Ordinal);
        static string Text(BinaryReader r)
        {int n=r.ReadInt32();if(n<1||n>256)throw new InvalidDataException("V17 aviation text budget");byte[] b=r.ReadBytes(n);if(b.Length!=n)throw new EndOfStreamException();return new UTF8Encoding(false,true).GetString(b);}
        static float Number(BinaryReader r)
        {float x=r.ReadSingle();if(float.IsNaN(x)||float.IsInfinity(x)||Math.Abs(x)>100000f)throw new InvalidDataException("V17 aviation nonfinite/range");return x;}
        static int Count(BinaryReader r,int min,int max)
        {int n=r.ReadInt32();if(n<min||n>max)throw new InvalidDataException("V17 aviation count budget");return n;}
        static Vector3 Vector(BinaryReader r){return new Vector3(Number(r),Number(r),Number(r));}
        static Quaternion Rotation(BinaryReader r)
        {Quaternion q=new Quaternion(Number(r),Number(r),Number(r),Number(r));float m=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;if(Math.Abs(m-1f)>.002f)throw new InvalidDataException("V17 aviation quaternion");return q;}
        internal static RhodesV17AviationData Load()
        {
            string folder=Path.Combine(CustomUnits.AssetDirectory,"rhodes_v05"),path=Path.Combine(folder,"v17_aviation.vap");
            if(Cached!=null&&CachedPath==path)return Cached;
            Cached=Read(folder);CachedPath=path;return Cached;
        }
        internal static RhodesV17AviationData Read(string folder)
        {
            string path=Path.Combine(folder,"v17_aviation.vap");
            RhodesV17AviationData data=new RhodesV17AviationData();
            RhodesResourceContent content=RhodesResourceContent.Read(path,4194304L);
            RhodesSuspensionV14Contract.RequireAviationPackageHash(content.Sha256);
            using(MemoryStream stream=new MemoryStream(content.Bytes,false))using(BinaryReader r=new BinaryReader(stream))
            {
                if(stream.Length<160||stream.Length>4194304||Encoding.ASCII.GetString(r.ReadBytes(4))!="VAP1"||r.ReadInt32()!=1)throw new InvalidDataException("V17 aviation format");
                if(Text(r)!=RhodesSuspensionV14Contract.GeometrySHA256)throw new InvalidDataException("V18 author source differs");
                string expected=Text(r),actual;using(SHA256 hash=SHA256.Create())using(FileStream model=File.OpenRead(Path.Combine(folder,"rhodes_v05.bin")))actual=BitConverter.ToString(hash.ComputeHash(model)).Replace("-",string.Empty).ToLowerInvariant();
                if(actual!=expected)throw new InvalidDataException("V17 path/model exact pairing differs");
                int paths=Count(r,10,10);
                for(int i=0;i<paths;i++)
                {
                    string name=Text(r);int samples=Count(r,2,1000);V17AirPath p=new V17AirPath{Times=new float[samples],Values=new V17AirPose[samples]};
                    for(int k=0;k<samples;k++)
                    {
                        float time=Number(r);if(time<0f||time>24.01f||(k>0&&time<=p.Times[k-1]))throw new InvalidDataException("V17 path time order");
                        p.Times[k]=time;V17AirPose pose=new V17AirPose();pose.Position=Vector(r);pose.Rotation=RhodesAviationPoseMath.YawDegrees(Number(r));pose.Fold=Number(r);pose.Gear=Number(r);
                        if(pose.Fold<0f||pose.Fold>1f||pose.Gear<0f||pose.Gear>1f)throw new InvalidDataException("V17 path mechanism range");p.Values[k]=pose;
                    }
                    data.Paths.Add(name,p);
                }
                int curves=Count(r,25,25);
                for(int i=0;i<curves;i++)
                {
                    string name=Text(r);float fps=Number(r);int samples=Count(r,2,1500);if(fps!=30f)throw new InvalidDataException("V17 curve clock differs");
                    V17NodeCurve c=new V17NodeCurve{FPS=fps,Positions=new Vector3[samples],Rotations=new Quaternion[samples]};
                    for(int k=0;k<samples;k++){c.Positions[k]=Vector(r);c.Rotations[k]=Rotation(r);}data.Curves.Add(name,c);
                }
                if(stream.Position!=stream.Length)throw new InvalidDataException("V17 aviation trailing bytes");
            }
            return data;
        }
        internal static void Reset(){Cached=null;CachedPath=null;}
    }
}
