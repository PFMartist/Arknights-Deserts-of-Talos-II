using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Presentation only. Legacy geometry composition leaves native joints alone;
    // R19 uses bounded view-joint parking only during fire-gated transport.
    // Neither path changes weapon flags, research, simulated angles or movement.
    internal sealed class RhodesGunneryDeploymentPose
    {
        RhodesCarrierBinding Binding;
        Bounds OriginalGeometryBounds,OriginalVisualBounds;
        bool FoldBoundsExpanded;
        float LastFold=float.NaN;
        bool HadGunOffset;
        Transform Column,Bucket;
        Quaternion ColumnRest,BucketRest;
        readonly List<Hydraulic> Rams=new List<Hydraulic>();
        readonly List<GunGeometry> Guns=new List<GunGeometry>();
        RhodesR19NativeAimParking Parking;
        sealed class Hydraulic
        {public Transform Root,Target;public Quaternion LocalRest;}
        sealed class GunGeometry
        {public Transform Root,Yaw;public int Module;public Vector3 Position;public Quaternion Rotation;public RhodesGunTransitPath Path;}

        public void Initialize(RhodesCarrierBinding binding)
        {
            Restore();Column=null;Bucket=null;Rams.Clear();Guns.Clear();Parking=null;
            if(binding==null||!binding.IsV05)return;
            Binding=binding;OriginalGeometryBounds=binding.GeometryBounds;OriginalVisualBounds=binding.VisualBounds;
            Column=Require(binding,"STUDY46_COLUMN_AXIS");Bucket=Require(binding,"STUDY46_BUCKET_AXIS");
            ColumnRest=Column.localRotation;BucketRest=Bucket.localRotation;
            RequireGeometry(Column);RequireGeometry(Bucket);
            foreach(string joint in new string[]{"Column","Bucket"})foreach(string side in new string[]{"L","R"})
            {
                AddRam(binding,"STUDY46_RAM_"+joint+"_"+side+"_body_root","STUDY46_MOV_"+joint+"_"+side+"_anchor");
                AddRam(binding,"STUDY46_RAM_"+joint+"_"+side+"_rod_root","STUDY46_FIX_"+joint+"_"+side+"_anchor");
            }
            Transform[] yaw={Require(binding,"V03_PIVOT_GN_Yaw_FORE"),Require(binding,"V03_PIVOT_GN_Yaw_AFT")};
            if(RhodesR19DeploymentData.IsR19(binding)){Parking=new RhodesR19NativeAimParking(binding);return;}
            for(int i=0;i<binding.ModuleRoots.Length;i++)
            {
                if(binding.ModuleBranches[i]!="gunnery")continue;
                Transform root=binding.ModuleRoots[i];Transform owner=null;
                foreach(Transform pivot in yaw)if(root!=pivot&&root.IsChildOf(pivot)){owner=pivot;break;}
                if(owner==null)continue;
                Guns.Add(new GunGeometry{Root=root,Yaw=owner,Module=i,Position=root.localPosition,Rotation=root.localRotation,Path=new RhodesGunTransitPath(binding,root,owner)});
            }
            if(Guns.Count==0)throw new InvalidOperationException("Gunnery deployment has no articulated geometry roots");
        }
        static Transform Require(RhodesCarrierBinding binding,string name)
        {Transform node=binding.FindNode(name);if(node==null)throw new InvalidOperationException("Gunnery deployment node missing: "+name);return node;}
        static void RequireGeometry(Transform node)
        {
            if(node.GetComponentsInChildren<MeshFilter>(true).Length==0)
                throw new InvalidOperationException("Miner anchor has no geometry; fold-anchor RHS2 export is required: "+node.name);
        }
        void AddRam(RhodesCarrierBinding binding,string rootName,string targetName)
        {
            Transform root=Require(binding,rootName);RequireGeometry(root);
            Rams.Add(new Hydraulic{Root=root,Target=Require(binding,targetName),LocalRest=root.localRotation});
        }
        public void ApplyFold(float amount)
        {
            if(Column==null||Bucket==null)return;
            amount=Mathf.Clamp01(amount);
            if(amount==LastFold)return;
            if(amount>0f&&!FoldBoundsExpanded)
            {
                // Source fold0..1 was independently checked at 1025 positions.
                // Expand presentation only when GN actually folds. The AV path,
                // strict static header, selection and navigation stay unchanged.
                Bounds presentation=Binding.GeometryBounds;
                // Round the 0.05m padded limits outward to millimetres.
                presentation.Encapsulate(new Vector3(-149.745f,-.187f,-321.874f));
                presentation.Encapsulate(new Vector3(142.072f,184.208f,311.584f));
                Binding.GeometryBounds=presentation;Binding.VisualBounds=presentation;FoldBoundsExpanded=true;
            }
            // Source drivers are -60*f and -20*f degrees on Blender local X.
            // D*R*D^-1 preserves these local-X signs in the Unity-LH package.
            Column.localRotation=ColumnRest*Quaternion.AngleAxis(-60f*amount,Vector3.right);
            Bucket.localRotation=BucketRest*Quaternion.AngleAxis(-20f*amount,Vector3.right);
            foreach(Hydraulic ram in Rams)
            {
                Vector3 direction=ram.Target.position-ram.Root.position;
                if(direction.sqrMagnitude<1e-10f)throw new InvalidOperationException("Miner hydraulic endpoints coincide: "+ram.Root.name);
                direction.Normalize();Vector3 up=ram.Target.up;
                Vector3 forward=Vector3.Cross(direction,up);
                if(forward.sqrMagnitude<1e-10f)throw new InvalidOperationException("Miner hydraulic TrackTo up axis is degenerate: "+ram.Root.name);
                // Source TRACK_Z / UP_X / target-Z becomes local +Y toward
                // the target and local -X toward projected target.up.
                ram.Root.rotation=Quaternion.LookRotation(forward.normalized,direction);
            }
            LastFold=amount;
        }
        public void ApplyGunTransit(float weight,float[] moduleOffsets)
        {
            bool offset=weight>1e-6f;
            if(!offset&&!HadGunOffset)return;
            foreach(GunGeometry gun in Guns)
                gun.Path.Apply(gun.Root,gun.Position,gun.Rotation,moduleOffsets[gun.Module],weight);
            HadGunOffset=offset;
        }
        internal void ApplyR19NativeAimHold(float fore,float aft)
        {
            if(Parking!=null)Parking.Apply(fore,aft);
        }
        public void Restore()
        {
            LastFold=float.NaN;HadGunOffset=false;
            if(FoldBoundsExpanded&&Binding!=null){Binding.GeometryBounds=OriginalGeometryBounds;Binding.VisualBounds=OriginalVisualBounds;}
            FoldBoundsExpanded=false;
            if(Column!=null)Column.localRotation=ColumnRest;if(Bucket!=null)Bucket.localRotation=BucketRest;
            foreach(Hydraulic ram in Rams)if(ram.Root!=null)ram.Root.localRotation=ram.LocalRest;
            foreach(GunGeometry gun in Guns)if(gun.Root!=null){gun.Root.localPosition=gun.Position;gun.Root.localRotation=gun.Rotation;}
            if(Parking!=null)Parking.Restore();
        }
    }
    // R19 source collision paths explicitly require neutral native joints during
    // transport. Override only ten exact UnitView joints while their simulation
    // firing gate is closed. The six lift/transport parents remain R19D-owned;
    // no simulated turret angle, target, status or weapon flag is modified.
    internal sealed class RhodesR19NativeAimParking
    {
        sealed class Joint {internal Transform Node;internal Vector3 Position;internal Quaternion Rotation;internal int Station;internal bool Held;}
        readonly Joint[] Joints;
        internal RhodesR19NativeAimParking(RhodesCarrierBinding binding)
        {
            List<Joint> joints=new List<Joint>();
            for(int station=0;station<2;station++)
            {
                string tag=station==0?"FORE":"AFT";
                foreach(string suffix in new string[]{"Yaw_"+tag,"Pitch_"+tag+"_LEFT","Pitch_"+tag+"_RIGHT","Recoil_"+tag+"_LEFT","Recoil_"+tag+"_RIGHT"})
                {
                    Transform bone=binding.FindNode("V03_PIVOT_GN_"+suffix);int index=Array.IndexOf(binding.Nodes,bone);
                    if(bone==null||index<0||!bone.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("R19 native parking exact joint missing: "+suffix);
                    joints.Add(new Joint{Node=bone,Position=binding.AuthoredLocalPositions[index],Rotation=binding.AuthoredLocalRotations[index],Station=station});
                }
            }
            Joints=joints.ToArray();
        }
        internal void Apply(float fore,float aft)
        {
            foreach(Joint joint in Joints)
            {
                float weight=Mathf.Clamp01(joint.Station==0?fore:aft);
                if(weight<=1e-6f){joint.Held=false;continue;}
                if(!joint.Node.gameObject.activeInHierarchy)continue;
                Vector3 p=weight>=.999999f?joint.Position:Vector3.Lerp(joint.Node.localPosition,joint.Position,weight);
                Quaternion q=weight>=.999999f?joint.Rotation:Quaternion.Slerp(joint.Node.localRotation,joint.Rotation,weight);
                if(joint.Node.localPosition!=p)joint.Node.localPosition=p;
                if(joint.Node.localRotation!=q)joint.Node.localRotation=q;
                joint.Held=true;
            }
        }
        internal void Restore(){foreach(Joint joint in Joints)if(joint.Held&&joint.Node!=null){joint.Node.localPosition=joint.Position;joint.Node.localRotation=joint.Rotation;joint.Held=false;}}
    }
    // Virtual articulated chain: cancel native yaw/pitch/recoil for transport,
    // without writing any of those native ancestors. Interpolation is per joint,
    // so each rendered part remains rigid during the turn in/out.
    internal sealed class RhodesGunTransitPath
    {
        sealed class Joint {internal Transform Node;internal Vector3 RestPosition;internal Quaternion RestRotation;}
        readonly RhodesCarrierBinding Binding;
        readonly Joint[] Joints;
        internal RhodesGunTransitPath(RhodesCarrierBinding binding,Transform leaf,Transform yaw)
        {
            Binding=binding;List<Transform> reverse=new List<Transform>();
            for(Transform p=leaf.parent;p!=null&&p!=binding.GeometryRoot;p=p.parent)reverse.Add(p);
            if(leaf==binding.GeometryRoot||!leaf.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("Transit leaf outside owned geometry");
            reverse.Reverse();Joints=new Joint[reverse.Count];Quaternion parentRest=Quaternion.identity;bool sawYaw=false;
            for(int i=0;i<reverse.Count;i++)
            {
                Transform node=reverse[i];int index=Array.IndexOf(binding.Nodes,node);
                if(index<0||binding.AuthoredLocalPositions==null||binding.AuthoredLocalRotations==null||index>=binding.AuthoredLocalPositions.Length||index>=binding.AuthoredLocalRotations.Length)
                    throw new InvalidOperationException("Canonical transit ancestor not captured: "+node.name);
                Quaternion rest=binding.AuthoredLocalRotations[index],target=rest;
                if(node==yaw)
                {
                    // D Rz(-90) D^-1 = Ry(+90), D=(-sourceX,+sourceZ,-sourceY).
                    target=Quaternion.Inverse(parentRest)*Quaternion.AngleAxis(90f,Vector3.up)*parentRest*rest;sawYaw=true;
                }
                Joints[i]=new Joint{Node=node,RestPosition=binding.AuthoredLocalPositions[index],RestRotation=target};
                parentRest=parentRest*rest;
            }
            if(!sawYaw)throw new InvalidOperationException("Canonical transit yaw absent from leaf ancestors: "+leaf.name);
        }
        internal void Apply(Transform leaf,Vector3 localPosition,Quaternion localRotation,float lift,float weight)
        {
            weight=Mathf.Clamp01(weight);Matrix4x4 frame=Binding.GeometryRoot.localToWorldMatrix;Quaternion rotation=Binding.GeometryRoot.rotation;
            foreach(Joint joint in Joints)
            {
                Vector3 p=Vector3.Lerp(joint.Node.localPosition,joint.RestPosition,weight);
                Quaternion q=Quaternion.Slerp(joint.Node.localRotation,joint.RestRotation,weight);
                frame=frame*Matrix4x4.TRS(p,q,joint.Node.localScale);rotation=rotation*q;
            }
            leaf.position=frame.MultiplyPoint3x4(localPosition)+Binding.GeometryRoot.TransformVector(Vector3.up*lift);
            leaf.rotation=rotation*localRotation;
        }
    }

}
