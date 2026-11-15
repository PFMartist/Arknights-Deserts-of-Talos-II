using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EndfieldDoK.ModelIntake
{
    public sealed class FactionRigJoint
    {
        public int Node,Role,Lod;
        public string Axis,Name;
        public float WheelRadius,AngleScale=1f;
    }
    // FRG1 is authored offline against one exact RHS2 package. Runtime binding
    // uses node identities and roles only; names are not interpreted as code.
    public sealed class FactionRigContract
    {
        public FactionRigJoint[] Joints;
        public static FactionRigContract Read(string file,string modelFile,string unit,RhodesNode[] nodes)
        {
            string sha;using(SHA256 hash=SHA256.Create())using(FileStream stream=File.OpenRead(modelFile))
                sha=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-",string.Empty).ToLowerInvariant();
            return ReadVerified(file,sha,unit,nodes);
        }
        public static FactionRigContract ReadVerified(string file,string verifiedModelHash,string unit,RhodesNode[] nodes)
        {
            FileInfo info=new FileInfo(file);if(!info.Exists||info.Length<16||info.Length>2097152)throw new InvalidDataException("Faction rig file budget differs");
            using(StreamReader reader=new StreamReader(file,new UTF8Encoding(false,true),true))return Read(reader,verifiedModelHash,unit,nodes);
        }
        public static FactionRigContract Read(TextReader reader,string modelHash,string unit,RhodesNode[] nodes)
        {
            if(reader==null||nodes==null||nodes.Length==0||modelHash==null||modelHash.Length!=64)throw new InvalidDataException("Invalid FRG1 input");
            string version=reader.ReadLine();bool calibrated=version=="FRG2\t2";
            if(!calibrated&&version!="FRG1\t1")throw new InvalidDataException("Unsupported faction rig contract");
            string line=reader.ReadLine();string[] header=line==null?new string[0]:line.Split('\t');
            if(header.Length!=4||header[0]!=unit||header[1]!=modelHash||Integer(header[2])!=nodes.Length)
                throw new InvalidDataException("Faction rig does not match its exact model identity");
            int count=Integer(header[3]);if(count<1||count>nodes.Length)throw new InvalidDataException("Invalid faction rig joint count");
            FactionRigContract result=new FactionRigContract();result.Joints=new FactionRigJoint[count];
            HashSet<int> seen=new HashSet<int>();int[] masks=new int[3];
            for(int i=0;i<count;i++)
            {
                line=reader.ReadLine();string[] row=line==null?new string[0]:line.Split('\t');
                if(row.Length!=(calibrated?11:9))throw new InvalidDataException("Invalid faction rig row");
                FactionRigJoint joint=new FactionRigJoint{Node=Integer(row[0]),Role=Integer(row[1]),Lod=Integer(row[2]),Axis=row[3],Name=row[4]};
                if(calibrated)
                {
                    joint.WheelRadius=Number(row[9]);joint.AngleScale=Number(row[10]);
                    if(joint.WheelRadius<0f||joint.WheelRadius>100f||joint.AngleScale<=0f||joint.AngleScale>4f||
                        (joint.Role==1?joint.WheelRadius<=0f:joint.WheelRadius!=0f))
                        throw new InvalidDataException("Invalid faction rig radius or angular scale");
                }
                if(joint.Node<0||joint.Node>=nodes.Length||!seen.Add(joint.Node)||joint.Role<1||joint.Role>6||joint.Lod< -1||joint.Lod>2||
                    (joint.Axis!="X"&&joint.Axis!="Y"&&joint.Axis!="Z"&&joint.Axis!="-X"&&joint.Axis!="-Y"&&joint.Axis!="-Z")||nodes[joint.Node].Name!=joint.Name)
                    throw new InvalidDataException("Faction rig node/role/axis differs");
                if(joint.Lod>=0&&!joint.Name.StartsWith("L"+joint.Lod.ToString(CultureInfo.InvariantCulture)+"::",StringComparison.Ordinal))
                    throw new InvalidDataException("Faction rig LOD identity differs");
                for(int q=0;q<4;q++)if(Math.Abs(Number(row[5+q])-nodes[joint.Node].Rotation[q])>.00001f)
                    throw new InvalidDataException("Faction rig authored rest rotation differs");
                for(int lod=0;lod<3;lod++)if(joint.Lod<0||joint.Lod==lod)masks[lod]|=1<<joint.Role;
                result.Joints[i]=joint;
            }
            if(reader.ReadLine()!=null)throw new InvalidDataException("Trailing faction rig rows");
            int required=RequiredRoles(unit);for(int lod=0;lod<3;lod++)if((masks[lod]&required)!=required)
                throw new InvalidDataException("Faction rig required capability is missing from an authored LOD");
            return result;
        }
        static int RequiredRoles(string unit)
        {
            if(unit=="RI_U01_HARVESTER")return 1<<1;
            if(unit=="RI_U02_FIELD_SURVEY")return (1<<1)|(1<<5);
            if(unit=="RI_U03_ENGINEERING")return (1<<1)|(1<<2)|(1<<5);
            if(unit=="RI_U04_LIGHT_ESCORT")return (1<<1)|(1<<2)|(1<<3)|(1<<5);
            if(unit=="RI_U08_TILT_STRIKE")return (1<<4)|(1<<6);
            if(unit=="RI_U05_SUPPRESSION"||unit=="RI_U06_RAILGUN"||unit=="RI_U07_AIR_DEFENCE"||unit=="RI_U09_ARTILLERY")return (1<<1)|(1<<2)|(1<<3);
            throw new InvalidDataException("Faction rig unit is not registered");
        }
        static int Integer(string value)
        {int number;if(!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out number))throw new InvalidDataException("Invalid faction rig integer");return number;}
        static float Number(string value)
        {float number;if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out number)||float.IsNaN(number)||float.IsInfinity(number))throw new InvalidDataException("Invalid faction rig number");return number;}
    }
}
