using System;

namespace EndfieldDoK.MapUnitLab
{
    // Managed construction geometry. No Unity state, physics or simulation.
    internal struct NativeStatusVector55
    {
        internal double X,Y,Z;
        internal NativeStatusVector55(double x,double y,double z){X=x;Y=y;Z=z;}
    }
    internal struct NativeStatusRotation55
    {
        internal double X,Y,Z,W;
        internal NativeStatusRotation55(double x,double y,double z,double w){X=x;Y=y;Z=z;W=w;}
    }
    internal struct NativeStatusBounds55
    {
        internal NativeStatusVector55 Min,Max;
        internal NativeStatusBounds55(NativeStatusVector55 min,NativeStatusVector55 max){Min=min;Max=max;}
    }
    internal static class NativeGroundStatusLayout55
    {
        internal static bool Finite(double n){return !double.IsNaN(n)&&!double.IsInfinity(n);}
        internal static bool Finite(NativeStatusVector55 p){return Finite(p.X)&&Finite(p.Y)&&Finite(p.Z);}
        internal static bool Valid(NativeStatusBounds55 b)
        {return Finite(b.Min)&&Finite(b.Max)&&b.Max.X-b.Min.X>.0001&&b.Max.Y-b.Min.Y>.0001&&b.Max.Z-b.Min.Z>.0001;}
        static bool Normalize(NativeStatusRotation55 q,out NativeStatusRotation55 value)
        {
            value=new NativeStatusRotation55();
            double n=q.X*q.X+q.Y*q.Y+q.Z*q.Z+q.W*q.W;
            if(!Finite(n)||n<.000001)return false;
            double inv=1/Math.Sqrt(n);value=new NativeStatusRotation55(q.X*inv,q.Y*inv,q.Z*inv,q.W*inv);return true;
        }
        static NativeStatusRotation55 Inverse(NativeStatusRotation55 q){return new NativeStatusRotation55(-q.X,-q.Y,-q.Z,q.W);}
        static NativeStatusVector55 Add(NativeStatusVector55 a,NativeStatusVector55 b){return new NativeStatusVector55(a.X+b.X,a.Y+b.Y,a.Z+b.Z);}
        static NativeStatusVector55 Subtract(NativeStatusVector55 a,NativeStatusVector55 b){return new NativeStatusVector55(a.X-b.X,a.Y-b.Y,a.Z-b.Z);}
        internal static NativeStatusVector55 Rotate(NativeStatusRotation55 q,NativeStatusVector55 p)
        {
            double tx=2*(q.Y*p.Z-q.Z*p.Y),ty=2*(q.Z*p.X-q.X*p.Z),tz=2*(q.X*p.Y-q.Y*p.X);
            return new NativeStatusVector55(p.X+q.W*tx+q.Y*tz-q.Z*ty,p.Y+q.W*ty+q.Z*tx-q.X*tz,p.Z+q.W*tz+q.X*ty-q.Y*tx);
        }
        static NativeStatusVector55 Map(NativeStatusVector55 p,NativeStatusBounds55 source,NativeStatusBounds55 target)
        {
            return new NativeStatusVector55(
                target.Min.X+(p.X-source.Min.X)*(target.Max.X-target.Min.X)/(source.Max.X-source.Min.X),
                target.Min.Y+(p.Y-source.Min.Y)*(target.Max.Y-target.Min.Y)/(source.Max.Y-source.Min.Y),
                target.Min.Z+(p.Z-source.Min.Z)*(target.Max.Z-target.Min.Z)/(source.Max.Z-source.Min.Z));
        }
        internal static bool TryMapBone(NativeStatusBounds55 source,NativeStatusBounds55 target,
            NativeStatusVector55 sourceBone,out NativeStatusVector55 mapped)
        {
            mapped=new NativeStatusVector55();
            if(!Valid(source)||!Valid(target)||!Finite(sourceBone))return false;
            mapped=Map(sourceBone,source,target);return Finite(mapped);
        }
        // native pose = boneP + boneQ * prefabRootQ * childInPrefabRoot.
        // Preserve both rotations. Reposition private child transforms only;
        // native particle curves, size, velocity, emission and budget stay intact.
        internal static bool TryMapChild(NativeStatusBounds55 source,NativeStatusBounds55 target,
            NativeStatusVector55 sourceBone,NativeStatusRotation55 sourceBoneRotation,
            NativeStatusRotation55 prefabRootRotation,NativeStatusVector55 childInPrefabRoot,
            out NativeStatusVector55 mappedBone,out NativeStatusVector55 mappedChildInPrefabRoot)
        {
            mappedBone=mappedChildInPrefabRoot=new NativeStatusVector55();
            NativeStatusRotation55 bone,prefab;
            if(!TryMapBone(source,target,sourceBone,out mappedBone)||!Finite(childInPrefabRoot)||
                !Normalize(sourceBoneRotation,out bone)||!Normalize(prefabRootRotation,out prefab))return false;
            NativeStatusVector55 original=Add(sourceBone,Rotate(bone,Rotate(prefab,childInPrefabRoot)));
            NativeStatusVector55 delta=Subtract(Map(original,source,target),mappedBone);
            mappedChildInPrefabRoot=Rotate(Inverse(prefab),Rotate(Inverse(bone),delta));
            return Finite(mappedChildInPrefabRoot);
        }
    }
}
