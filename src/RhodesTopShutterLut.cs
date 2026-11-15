using System;
using System.IO;
using System.Security.Cryptography;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // One immutable LUT for the installed model generation; all ship instances share it.
    // Loaded once, never during Step. No Unity object is retained in this data cache.
    internal sealed class RhodesTopShutterData
    {
        internal const int SampleCount=513,NodeCount=88,Stride=7;
        internal const string ExpectedHash="90edc1cf40a2b3cc9a976154efac74ece1e15aa734c66bf2efcfa1e54487e23b";
        internal readonly float[] Values;
        static RhodesTopShutterData Cached;
        static string CachedPath;
        RhodesTopShutterData(float[] values){Values=values;}
        internal static RhodesTopShutterData Load(string file)
        {
            file=Path.GetFullPath(file);
            if(Cached!=null)
            {if(!String.Equals(CachedPath,file,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Different V08 shutter LUT generation in one process");return Cached;}
            byte[] raw=File.ReadAllBytes(file);
            if(raw.Length!=1264044)throw new InvalidDataException("V08 shutter LUT length mismatch");
            string hash;using(SHA256 sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(raw)).Replace("-","").ToLowerInvariant();
            if(hash!=ExpectedHash)throw new InvalidDataException("V08 shutter LUT SHA256 mismatch");
            float[] values=new float[SampleCount*NodeCount*Stride];
            using(BinaryReader reader=new BinaryReader(new MemoryStream(raw,false)))
            {
                if(new string(reader.ReadChars(4))!="RS07"||reader.ReadUInt16()!=1||reader.ReadUInt16()!=SampleCount||reader.ReadUInt16()!=NodeCount||reader.ReadUInt16()!=Stride)
                    throw new InvalidDataException("V08 shutter LUT header mismatch");
                for(int i=0;i<values.Length;i++)
                {float x=reader.ReadSingle();if(float.IsNaN(x)||float.IsInfinity(x)||Math.Abs(x)>1000000f)throw new InvalidDataException("V08 shutter LUT invalid value");values[i]=x;}
            }
            CachedPath=file;Cached=new RhodesTopShutterData(values);return Cached;
        }
    }

    // The R23 author moved BOTH complete fore stations 0.25 DU toward the bow.
    // The immutable RS07 path remains canonical, so translate its WHOLE path by
    // the verified station-anchor delta. Never infer a path from one loose slat.
    internal sealed class RhodesShutterBindPose
    {
        internal string Name,Parent;
        internal Vector3 Position,Scale;
        internal Quaternion Rotation;
    }
    internal static class RhodesShutterPathBinding
    {
        internal static readonly string[] Anchors={"MOUNT_CIWS_StarboardFore","MOUNT_CIWS_StarboardAft","MOUNT_CIWS_PortFore","MOUNT_CIWS_PortAft"};
        static readonly Vector3[] CanonicalAnchors={new Vector3(110.877555847168f,104.023300170898f,78.622268676758f),new Vector3(110.877555847168f,104.023300170898f,-94.749908447266f),new Vector3(-80.638221740723f,104.023300170898f,78.622268676758f),new Vector3(-80.638221740723f,104.023300170898f,-94.749908447266f)};
        static readonly Vector3 ForeMigration=new Vector3(0f,0f,5.039886474609375f);
        static void RequirePose(RhodesShutterBindPose pose,string name,Quaternion rotation)
        {
            if(pose==null||pose.Name!=name||pose.Parent!="ROOT_Rhodes")throw new InvalidDataException("V08 shutter parent/name mismatch: "+name);
            float norm=pose.Rotation.x*pose.Rotation.x+pose.Rotation.y*pose.Rotation.y+pose.Rotation.z*pose.Rotation.z+pose.Rotation.w*pose.Rotation.w;
            float reference=rotation.x*rotation.x+rotation.y*rotation.y+rotation.z*rotation.z+rotation.w*rotation.w;
            float dot=pose.Rotation.x*rotation.x+pose.Rotation.y*rotation.y+pose.Rotation.z*rotation.z+pose.Rotation.w*rotation.w;
            // q and -q describe the same orientation. Explicit finite checks also
            // reject NaN, whose comparison would otherwise bypass a tolerance.
            if(float.IsNaN(norm)||float.IsInfinity(norm)||Math.Abs(norm-1f)>.0001f||Math.Abs(reference-1f)>.0001f||1f-dot*dot/(norm*reference)>.000001f)
                throw new InvalidDataException("V08 shutter closed rotation differs from LUT: "+name);
            Vector3 scale=pose.Scale;
            if(float.IsNaN(scale.x)||float.IsNaN(scale.y)||float.IsNaN(scale.z)||Math.Abs(scale.x-1f)>.00001f||Math.Abs(scale.y-1f)>.00001f||Math.Abs(scale.z-1f)>.00001f)
                throw new InvalidDataException("V08 shutter scale mismatch: "+name);
        }
        static bool Near(Vector3 a,Vector3 b)
        {Vector3 d=a-b;float squared=d.x*d.x+d.y*d.y+d.z*d.z;return !float.IsNaN(squared)&&!float.IsInfinity(squared)&&squared<=.0001f;}
        internal static Vector3[] Validate(RhodesTopShutterData data,RhodesShutterBindPose[] nodes,RhodesShutterBindPose[] anchors)
        {
            if(data==null||nodes==null||nodes.Length!=RhodesTopShutterData.NodeCount||anchors==null||anchors.Length!=4)throw new InvalidDataException("V08 shutter bind array mismatch");
            Vector3[] stations=new Vector3[4],offsets=new Vector3[nodes.Length];
            for(int i=0;i<4;i++)
            {
                RequirePose(anchors[i],Anchors[i],new Quaternion(0f,0f,0f,1f));stations[i]=anchors[i].Position-CanonicalAnchors[i];
                if(!Near(stations[i],Vector3.zero)&&!((i==0||i==2)&&Near(stations[i],ForeMigration)))
                    throw new InvalidDataException("V08 shutter station migration is not authorized: "+Anchors[i]);
            }
            if(!Near(stations[0],stations[2]))throw new InvalidDataException("V08 fore shutter stations migrated inconsistently");
            for(int i=0;i<nodes.Length;i++)
            {
                int k=i*7;string name=RhodesShutterContract.Names[i];
                RequirePose(nodes[i],name,new Quaternion(data.Values[k+3],data.Values[k+4],data.Values[k+5],data.Values[k+6]));
                int station=i<40?-1:(i-40)/12;Vector3 offset=station<0?Vector3.zero:stations[station];
                Vector3 canonical=new Vector3(data.Values[k],data.Values[k+1],data.Values[k+2]);
                if(!Near(nodes[i].Position,canonical+offset))throw new InvalidDataException("V08 shutter closed bind differs from station-relative LUT: "+name);
                offsets[i]=offset;
            }
            return offsets;
        }
        internal static Vector3 SamplePosition(RhodesTopShutterData data,Vector3[] offsets,int node,int lo,int hi,float t)
        {
            int x=(lo*RhodesTopShutterData.NodeCount+node)*7,y=(hi*RhodesTopShutterData.NodeCount+node)*7;float[] f=data.Values;Vector3 d=offsets[node];
            return new Vector3(f[x]+(f[y]-f[x])*t+d.x,f[x+1]+(f[y+1]-f[x+1])*t+d.y,f[x+2]+(f[y+2]-f[x+2])*t+d.z);
        }
    }

    // Existing host calls this; no new Update/LateUpdate or per-frame file/name scan.
    internal sealed class RhodesTopShutterSet
    {
        readonly Transform[] Nodes;
        readonly Vector3[] BindPosition;
        readonly Quaternion[] BindRotation;
        readonly int[] Groups;
        readonly Vector3[] PathOffsets;
        readonly RhodesTopShutterData Data;
        readonly float[] Last={float.NaN,float.NaN,float.NaN};
        internal RhodesTopShutterSet(RhodesCarrierBinding binding)
        {
            int n=RhodesShutterContract.Names.Length;
            Nodes=new Transform[n];BindPosition=new Vector3[n];BindRotation=new Quaternion[n];Groups=new int[n];
            Data=RhodesTopShutterData.Load(Path.Combine(Path.Combine(CustomUnits.AssetDirectory,"rhodes_v05"),"shutter_path_r07.lut"));
            RhodesShutterBindPose[] poses=new RhodesShutterBindPose[n],anchors=new RhodesShutterBindPose[4];
            for(int i=0;i<n;i++)
            {
                Transform node=binding.FindNode(RhodesShutterContract.Names[i]);
                if(node==null)throw new InvalidDataException("V08 shutter node missing: "+RhodesShutterContract.Names[i]);
                Nodes[i]=node;BindPosition[i]=node.localPosition;BindRotation[i]=node.localRotation;
                string slot=RhodesShutterContract.Slots[i];Groups[i]=slot=="DECK"?0:slot=="SHARED_VT"?1:2;
                poses[i]=Pose(node);
            }
            for(int i=0;i<anchors.Length;i++)anchors[i]=Pose(binding.FindNode(RhodesShutterPathBinding.Anchors[i]));
            PathOffsets=RhodesShutterPathBinding.Validate(Data,poses,anchors);
        }
        static RhodesShutterBindPose Pose(Transform node)
        {return node==null?null:new RhodesShutterBindPose{Name=node.name,Parent=node.parent==null?null:node.parent.name,Position=node.localPosition,Rotation=node.localRotation,Scale=node.localScale};}
        internal void Set(float deck,float vt,float ciws,bool driveDeck=true)
        {
            float a=Mathf.Clamp01(deck),b=Mathf.Clamp01(vt),c=Mathf.Clamp01(ciws);
            bool ca=driveDeck&&a!=Last[0],cb=b!=Last[1],cc=c!=Last[2];
            if(!ca&&!cb&&!cc)return;
            for(int i=0;i<Nodes.Length;i++)
            {
                int group=Groups[i];if(!(group==0?ca:group==1?cb:cc))continue;
                float fraction=(group==0?a:group==1?b:c)*(RhodesTopShutterData.SampleCount-1);
                int lo=Math.Min(RhodesTopShutterData.SampleCount-1,(int)fraction),hi=Math.Min(RhodesTopShutterData.SampleCount-1,lo+1);
                int x=(lo*Nodes.Length+i)*7,y=(hi*Nodes.Length+i)*7;float t=fraction-lo;float[] f=Data.Values;
                Nodes[i].localPosition=RhodesShutterPathBinding.SamplePosition(Data,PathOffsets,i,lo,hi,t);
                Nodes[i].localRotation=Quaternion.Slerp(new Quaternion(f[x+3],f[x+4],f[x+5],f[x+6]),new Quaternion(f[y+3],f[y+4],f[y+5],f[y+6]),t);
            }
            Last[0]=a;Last[1]=b;Last[2]=c;
        }
        internal void Restore()
        {
            for(int i=0;i<Nodes.Length;i++)if(Nodes[i]!=null){Nodes[i].localPosition=BindPosition[i];Nodes[i].localRotation=BindRotation[i];}
            Last[0]=Last[1]=Last[2]=float.NaN;
        }
    }

    // Only the ten exact emission leaves move; native aim/recoil ancestors do not.
    internal sealed class RhodesModuleEmissionMarkers
    {
        sealed class Marker
        {internal Transform Node,Yaw;internal Vector3 Bind;internal Quaternion Rotation;internal int Module;internal bool Inherits;internal RhodesGunTransitPath Path;}
        readonly System.Collections.Generic.List<Marker> Markers=new System.Collections.Generic.List<Marker>();
        readonly RhodesCarrierBinding Binding;
        bool HadMotion;
        internal RhodesModuleEmissionMarkers(UnitView view,RhodesCarrierBinding binding)
        {
            Binding=binding;
            foreach(string station in new string[]{"FORE","AFT"})
            {
                Transform yaw=binding.FindNode("V03_PIVOT_GN_Yaw_"+station);
                foreach(string side in new string[]{"LEFT","RIGHT"})Add(binding.FindNode("V03_HP_GN_Muzzle_"+station+"_"+side),"MOD_DECK_GN_L1__V03_PIVOT_GN_Recoil_"+station+"_"+side,yaw);
                string name="EF_RHODES_MAIN_"+station+"_CENTER";Transform center=null;
                foreach(Transform node in view.GetComponentsInChildren<Transform>(true))if(node.name==name)
                {if(center!=null)throw new InvalidDataException("Duplicate native emission center "+name);center=node;}
                Add(center,"MOD_DECK_GN_L1__V03_PIVOT_GN_Pitch_"+station+"_LEFT",yaw);
            }
            foreach(string station in new string[]{"PortFore","PortAft","StarboardFore","StarboardAft"})
                Add(binding.FindNode("HP_CIWS_"+station+"_Muzzle"),"MOD_CIWS_L1__PIVOT_CIWS_"+station+"_Pitch",null);
        }
        void Add(Transform node,string moduleID,Transform yaw)
        {
            if(node==null||Binding.GeometryRoot==null||!node.IsChildOf(Binding.GeometryRoot))throw new InvalidDataException("V08 owned emission leaf missing: "+moduleID);
            int module=Array.IndexOf(Binding.ModuleIds,moduleID);
            if(module<0||Binding.ModuleRoots[module]==null)throw new InvalidDataException("V08 emission module missing: "+moduleID);
            Transform root=Binding.ModuleRoots[module];
            if(root.IsChildOf(node))throw new InvalidDataException("Emission node is an ancestor of moving module");
            Markers.Add(new Marker{Node=node,Yaw=yaw,Bind=node.localPosition,Rotation=node.localRotation,Module=module,Inherits=node.IsChildOf(root),Path=yaw==null||node.IsChildOf(root)?null:new RhodesGunTransitPath(Binding,node,yaw)});
        }
        internal void Apply(float[] offsets,float transitWeight)
        {
            bool moving=transitWeight>1e-6f;
            foreach(Marker marker in Markers)if(offsets[marker.Module]!=0f){moving=true;break;}
            if(!moving&&!HadMotion)return;
            foreach(Marker marker in Markers)if(marker.Node!=null&&!marker.Inherits)
            {
                if(marker.Path!=null)
                    marker.Path.Apply(marker.Node,marker.Bind,marker.Rotation,offsets[marker.Module],transitWeight);
                else
                {
                    Vector3 world=Binding.GeometryRoot.TransformVector(Vector3.up*offsets[marker.Module]);
                    marker.Node.localPosition=marker.Bind+(marker.Node.parent==null?world:marker.Node.parent.InverseTransformVector(world));
                    marker.Node.localRotation=marker.Rotation;
                }
            }
            HadMotion=moving;
        }
        internal void Restore()
        {foreach(Marker marker in Markers)if(marker.Node!=null){marker.Node.localPosition=marker.Bind;marker.Node.localRotation=marker.Rotation;}HadMotion=false;}
    }
}
