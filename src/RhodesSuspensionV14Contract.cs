using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using EndfieldDoK.ModelIntake;

namespace EndfieldDoK.MapUnitLab
{
    // R23 r02 author hull, complete fore-CIWS stations and current staged covers; V14 mechanics and V17 aviation interfaces are
    // compared against the actual source and decoded candidate bytes offline.
    // This is a package/interface gate, not game or visual acceptance.
    public static class RhodesSuspensionV14Contract
    {
        public static readonly bool InterfaceVerified=true;
        public const int ModuleCount=14,BeltsPerModule=2,TrackCount=ModuleCount*BeltsPerModule;
        public const int ContactsPerBelt=6;
        public const string GeometrySHA256="86a435e73f8b4b795fc4aaa5809f25f53a261656dfa9a2efde1bb7a37bbe238d",InterfaceSHA256="ef61c4f3f81b0490a626e071b9da40b5865d04fb46b9b359b7fd3edc4762ed18";
        public const string ModelPackageSHA256="b1be0ef36e372c2b4415e625084d933911541e4b9f84be3915bcff83225c74ba",TrackPackageSHA256="5992bb06d6c3d4aa63b6146d812e6f4e3250e1da0f34bee7be099b6297b3b854";
        public const string ProbePackageSHA256="b32ef329393fcdc76ff20defb506c3e4682b2af94c557df4f73e59d812749e5f",PbrProfileSHA256="0368fcd3a6418c13499676bdd699070bdec461a736dcc41e9bfe5772f33ee966";
        public const string AviationPackageSHA256="e5452097945b4dbf97923a089f183f9d48b545acfaeca56f41482ca5501a092a";
        public const string R19DeploymentPackageSHA256="5a84ed4b2216eff487348ef8a9806648446ed2b0d2cc19baa3e31b865fb78c61";
        public const float DraftMetresPerDesignUnit=20.159555435180664f;
        public const float AuthoredContactDatumMetres=0f;
        public const float MainPitchDegrees=3f,RelativeBeltPitchDegrees=2f,EffectiveBeltPitchDegrees=5f;
        public static RhodesSuspensionLimits Limits
        {
            get{return new RhodesSuspensionLimits{MinHeave=-.06f*DraftMetresPerDesignUnit,MaxHeave=.06f*DraftMetresPerDesignUnit,MaxPitchDegrees=5f,
                HeaveMetresPerSecond=.65f,PitchDegreesPerSecond=.5f,ResponseSeconds=.3f,
                MissGraceSeconds=.35f,OffGroundDroopMetres=.06f*DraftMetresPerDesignUnit,ContactClearance=.24f};}
        }
        public static void RequireVerified()
        {if(!InterfaceVerified)throw new InvalidOperationException("V14 independent-belt geometry contract is not yet verified; candidate must remain inert");}
        public static void RequireTrackPackage(string path)
        {RequireVerified();RequireFileHash(path,TrackPackageSHA256);}
        public static void RequireAviationPackageHash(string actual)
        {
            RequireVerified();
            if(!string.Equals(actual,AviationPackageSHA256,StringComparison.Ordinal))
                throw new InvalidDataException("V18 aviation bytes differ from the audited model/source pair");
        }
        public static void RequireR19DeploymentPackageHash(string actual)
        {
            RequireVerified();
            if(!string.Equals(actual,R19DeploymentPackageSHA256,StringComparison.Ordinal))
                throw new InvalidDataException("R19 deployment bytes differ from the audited source/model pair");
        }
        public static void RequireModelPackage(RhodesModelPackage package,string path)
        {
            RequireVerified();ValidateModelHierarchy(package);
            // Read(file) hashes the exact bytes it decoded on the bounded
            // reader. Avoid a second 83 MB main-thread file/hash pass.
            // Stream-only/offline callers retain the strict file-hash fallback.
            if(string.IsNullOrEmpty(package.ContentSha256))RequireFileHash(path,ModelPackageSHA256);
            else if(!string.Equals(package.ContentSha256,ModelPackageSHA256,StringComparison.Ordinal))
                throw new InvalidDataException("V14 decoded package differs from the audited model/track pair");
            RequireFileHash(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),"rhodes_tracks.tkr"),TrackPackageSHA256);
            RequireFileHash(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),"rhodes_probe.rpa"),ProbePackageSHA256);
            RequireFileHash(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),"materials.rhpbr"),PbrProfileSHA256);
            RequireFileHash(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),"r19_deployment.r19d"),R19DeploymentPackageSHA256);
        }
        static void RequireFileHash(string path,string expected)
        {
            if(expected==null||expected.Length!=64)throw new InvalidDataException("V14 package hash contract is incomplete");
            byte[] hash;using(FileStream stream=File.OpenRead(path))using(SHA256 algorithm=SHA256.Create())hash=algorithm.ComputeHash(stream);
            string actual=BitConverter.ToString(hash).Replace("-",string.Empty).ToLowerInvariant();
            if(!string.Equals(actual,expected,StringComparison.Ordinal))throw new InvalidDataException("V14 package differs from the audited model/track pair: "+Path.GetFileName(path));
        }
        // The builder creates a presentation wrapper before the authored RHS2
        // root. Validate that exact data hierarchy before Unity allocation.
        public static void ValidateModelHierarchy(RhodesModelPackage package)
        {
            if(package==null||package.Nodes==null)throw new ArgumentNullException("package");
            Dictionary<string,int> ids=new Dictionary<string,int>(StringComparer.Ordinal);
            for(int i=0;i<package.Nodes.Length;i++)ids.Add(package.Nodes[i].Name,i);
            int root=Node(ids,"ROOT_Rhodes"),main=Node(ids,"PIVOT_V14_MainChassis");
            if(package.Nodes[root].Parent!=-1||package.Nodes[main].Parent!=root||ids.ContainsKey("PIVOT_V13_MainChassis"))throw new InvalidDataException("V14 authored root/main hierarchy mismatch");
            Identity(package.Nodes[root],true);Identity(package.Nodes[main],true);
            for(int module=0;module<ModuleCount;module++)
            {
                string prefix="CHASSIS_"+module.ToString("00");
                int lift=Node(ids,prefix+"_PIVOT_SuspensionLift"),carrier=Node(ids,prefix+"_PIVOT_CarrierPitch");
                if(package.Nodes[lift].Parent!=main||package.Nodes[carrier].Parent!=lift)throw new InvalidDataException("V14 carrier parent mismatch");
                Identity(package.Nodes[lift],false);Identity(package.Nodes[carrier],true);
                for(int side=0;side<BeltsPerModule;side++)
                {
                    string track="TRACK_"+(module*BeltsPerModule+side).ToString("00");
                    int pitch=Node(ids,track+"_PitchPivot");RhodesNode node=package.Nodes[pitch];
                    if(node.Parent!=carrier||Math.Abs(node.Position[1])>.0001f||Math.Abs(node.Position[2])>.0001f)throw new InvalidDataException("V14 independent track axes are not collinear");
                    Identity(node,false);
                    foreach(string tag in new string[]{"LF","LR","LC","RF","RR","RC"})
                        if(package.Nodes[Node(ids,track+"_HP_Contact_"+tag)].Parent!=pitch)throw new InvalidDataException("V14 contact does not follow its own belt");
                }
            }
            // Fixed deck/lower/storage endpoints must never inherit lift motion.
            // Moving wheel contacts, conversely, belong to their own platform.
            Parent(package,ids,"V17_PIVOT_AV_ModuleCarrier","ROOT_Rhodes");
            for(int lane=1;lane<=3;lane++)
            {
                string tag=lane.ToString("00"),prefix="V17_F"+tag;
                Parent(package,ids,prefix+"_FRAME","V17_PIVOT_AV_ModuleCarrier");
                foreach(string direction in new string[]{"Launch","Recovery"})
                {
                    string part=prefix+"_"+direction,fix=part+"_FIXED";
                    Parent(package,ids,fix,prefix+"_FRAME");
                    Parent(package,ids,part+"_PIVOT",fix);
                    Parent(package,ids,part+"_STORAGE_FIXED",fix);
                    foreach(string contact in new string[]{"DeckContact","LowerContact"})Parent(package,ids,part+"_"+contact,fix);
                    Parent(package,ids,part+"_StorageContact",part+"_STORAGE_FIXED");
                    Parent(package,ids,part+"_MovingWheelContact",part+"_PIVOT");
                }
                Parent(package,ids,prefix+"_LaunchStorageEntry",prefix+"_Launch_FIXED");
                foreach(string marker in new string[]{"RecoveryApproach","RecoveryHover","RecoveryStorageExit"})Parent(package,ids,prefix+"_"+marker,prefix+"_Recovery_FIXED");
                Parent(package,ids,prefix+"_RunwayExit",prefix+"_FRAME");
                Parent(package,ids,"V06_HP_AV_"+tag+"_DeckSurface","ROOT_Rhodes");
                Parent(package,ids,"V06_HP_AV_"+tag+"_StorageSurface","ROOT_Rhodes");
            }
            Parent(package,ids,"V17_SharedRecovery_FIXED","ROOT_Rhodes");
            Parent(package,ids,"V17_SharedSideRunway_FIXED","ROOT_Rhodes");
            foreach(string marker in new string[]{"Approach","Hover","DeckContact","LowerContact","StorageExit"})Parent(package,ids,"V17_SharedRecovery_"+marker,"V17_SharedRecovery_FIXED");
            Parent(package,ids,"V17_SharedRecovery_MovingWheelContact","V03_PIVOT_SharedTiltrotorLift");
            foreach(string marker in new string[]{"ReadyPoint"})Parent(package,ids,"V17_SharedSideRunway_"+marker,"V17_SharedSideRunway_FIXED");
            foreach(string marker in new string[]{"StorageEntry","RunwayExit"})Parent(package,ids,"V17_SharedLaunch_"+marker,"V17_SharedSideRunway_FIXED");
            foreach(string tag in new string[]{"A","B"})
            {
                Parent(package,ids,"V07_LIGHT_WORK_"+tag,"ROOT_Rhodes");
                Parent(package,ids,"V07_LIGHT_TARGET_WORK_"+tag,"ROOT_Rhodes");
            }
            Parent(package,ids,"PIVOT_GN_ModuleLift","V03_MOUNT_TaskVolume");
            foreach(string station in new string[]{"FORE","AFT"})
            {
                Parent(package,ids,"PIVOT_GN_"+station+"_Lift","PIVOT_GN_ModuleLift");
                Parent(package,ids,"V03_MOUNT_GN_Main_"+station,"PIVOT_GN_"+station+"_Lift");
                Parent(package,ids,"PIVOT_GN_"+station+"_TransportYaw","V03_MOUNT_GN_Main_"+station);
                Parent(package,ids,"V03_PIVOT_GN_Yaw_"+station,"PIVOT_GN_"+station+"_TransportYaw");
            }
            Parent(package,ids,"PIVOT_GN_AFT_CoverLift","PIVOT_GN_ModuleLift");
            foreach(string point in new string[]{"FORE_GREEN","FORE_RED","AFT_GREEN","AFT_RED"})Parent(package,ids,"V19_FX_BEACON_"+point,"ROOT_Rhodes");
        }
        static void Parent(RhodesModelPackage package,Dictionary<string,int> ids,string child,string parent)
        {
            if(package.Nodes[Node(ids,child)].Parent!=Node(ids,parent))
                throw new InvalidDataException("V18 fixed/moving aviation endpoint parent mismatch: "+child);
        }
        static int Node(Dictionary<string,int> ids,string name)
        {int index;if(!ids.TryGetValue(name,out index))throw new InvalidDataException("V14 node is missing: "+name);return index;}
        static void Identity(RhodesNode node,bool zeroPosition)
        {
            if(Math.Abs(node.Rotation[0])>.0001f||Math.Abs(node.Rotation[1])>.0001f||Math.Abs(node.Rotation[2])>.0001f||Math.Abs(Math.Abs(node.Rotation[3])-1f)>.0001f||
               Math.Abs(node.Scale[0]-1f)>.0001f||Math.Abs(node.Scale[1]-1f)>.0001f||Math.Abs(node.Scale[2]-1f)>.0001f||
               (zeroPosition&&(Math.Abs(node.Position[0])>.0001f||Math.Abs(node.Position[1])>.0001f||Math.Abs(node.Position[2])>.0001f)))
                throw new InvalidDataException("V14 rest frame is not identity: "+node.Name);
        }
    }

    // Shared vertical translation, independent transverse pitch hinges.
    // A higher supported belt may lift its neighbour clear of the surface;
    // this is a bounded kinematic display, not load balancing or physics.
    public struct RhodesSuspensionV14Angles
    {
        public float Common,FirstRelative,SecondRelative;
        public float FirstEffective { get { return Common+FirstRelative; } }
        public float SecondEffective { get { return Common+SecondRelative; } }
    }
    public static class RhodesSuspensionV14Math
    {
        public static RhodesSuspensionV14Angles DecomposePitch(float first,float second,float mainLimit,float relativeLimit,float effectiveLimit)
        {
            if(!RhodesSuspensionMath.Finite(first)||!RhodesSuspensionMath.Finite(second)||!RhodesSuspensionMath.Finite(mainLimit)||!RhodesSuspensionMath.Finite(relativeLimit)||!RhodesSuspensionMath.Finite(effectiveLimit)||mainLimit<0f||relativeLimit<0f||effectiveLimit<mainLimit||effectiveLimit>10f)
                throw new ArgumentOutOfRangeException("pitch");
            first=RhodesSuspensionMath.Clamp(first,-effectiveLimit,effectiveLimit);second=RhodesSuspensionMath.Clamp(second,-effectiveLimit,effectiveLimit);
            float common=RhodesSuspensionMath.Clamp((first+second)*.5f,-mainLimit,mainLimit);
            return new RhodesSuspensionV14Angles{Common=common,FirstRelative=RhodesSuspensionMath.Clamp(first-common,-relativeLimit,relativeLimit),SecondRelative=RhodesSuspensionMath.Clamp(second-common,-relativeLimit,relativeLimit)};
        }
        public static float SharedHeave(float first,float second,RhodesSuspensionLimits limits)
        {
            limits.Validate();
            if(!RhodesSuspensionMath.Finite(first)||!RhodesSuspensionMath.Finite(second))throw new ArgumentOutOfRangeException("heave");
            return RhodesSuspensionMath.Clamp(Math.Max(first,second),limits.MinHeave,limits.MaxHeave);
        }
    }
}
