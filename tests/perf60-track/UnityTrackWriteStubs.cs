namespace UnityEngine
{
    // Offline instrumentation only. This counts production transform writes;
    // it does not emulate rendering, native culling or Unity quaternion math.
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 right {get{return new Vector3(1,0,0);}}
    }
    public struct Quaternion
    {
        public float Angle;
        public static Quaternion AngleAxis(float angle,Vector3 axis){return new Quaternion{Angle=angle};}
        public static Quaternion operator *(Quaternion a,Quaternion b){return new Quaternion{Angle=a.Angle+b.Angle};}
    }
    public sealed class Transform
    {
        Vector3 Position;Quaternion Rotation;
        public int PositionWrites,RotationWrites;
        public Vector3 localPosition {get{return Position;}set{Position=value;PositionWrites++;}}
        public Quaternion localRotation {get{return Rotation;}set{Rotation=value;RotationWrites++;}}
    }
    public sealed class SkinnedMeshRenderer
    {
        public bool enabled=true,isVisible;
    }
}
namespace EndfieldDoK.MapUnitLab
{
    internal static class RhodesSkirmishFrameTiming
    {
        internal static long Begin(){return 0L;}
        internal static void End(int section,long start){}
    }
}
