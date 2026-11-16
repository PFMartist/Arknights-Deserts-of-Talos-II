using System;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero{get{return new Vector3();}}
        public static Vector3 one{get{return new Vector3(1,1,1);}}
        public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public static Vector3 operator *(Vector3 a,float b){return new Vector3(a.x*b,a.y*b,a.z*b);}
        public float sqrMagnitude{get{return x*x+y*y+z*z;}}
    }
    public struct Quaternion
    {
        public float x,y,z,w;
        public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}
        public static Quaternion identity{get{return new Quaternion(0,0,0,1);}}
    }
    public struct Matrix4x4
    {
        internal double[] Data;
        internal Matrix4x4(double[] data){Data=data;}
        internal static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)
        {
            double x=q.x,y=q.y,z=q.z,w=q.w;
            return new Matrix4x4(new double[]{
                (1-2*y*y-2*z*z)*s.x,(2*x*y-2*z*w)*s.y,(2*x*z+2*y*w)*s.z,p.x,
                (2*x*y+2*z*w)*s.x,(1-2*x*x-2*z*z)*s.y,(2*y*z-2*x*w)*s.z,p.y,
                (2*x*z-2*y*w)*s.x,(2*y*z+2*x*w)*s.y,(1-2*x*x-2*y*y)*s.z,p.z,0,0,0,1});
        }
        public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)
        {
            double[] c=new double[16];for(int row=0;row<4;row++)for(int col=0;col<4;col++)for(int k=0;k<4;k++)c[row*4+col]+=a.Data[row*4+k]*b.Data[k*4+col];
            return new Matrix4x4(c);
        }
        public Vector3 MultiplyPoint3x4(Vector3 p)
        {
            return new Vector3((float)(Data[0]*p.x+Data[1]*p.y+Data[2]*p.z+Data[3]),
                (float)(Data[4]*p.x+Data[5]*p.y+Data[6]*p.z+Data[7]),
                (float)(Data[8]*p.x+Data[9]*p.y+Data[10]*p.z+Data[11]));
        }
        public Matrix4x4 inverse
        {
            get
            {
                double[,] a=new double[4,8];for(int row=0;row<4;row++)for(int col=0;col<4;col++){a[row,col]=Data[row*4+col];a[row,col+4]=row==col?1:0;}
                for(int col=0;col<4;col++)
                {
                    int pivot=col;for(int row=col+1;row<4;row++)if(Math.Abs(a[row,col])>Math.Abs(a[pivot,col]))pivot=row;
                    if(Math.Abs(a[pivot,col])<1e-20)throw new InvalidOperationException("Singular fixture transform");
                    for(int k=0;k<8;k++){double temp=a[col,k];a[col,k]=a[pivot,k];a[pivot,k]=temp;}
                    double divisor=a[col,col];for(int k=0;k<8;k++)a[col,k]/=divisor;
                    for(int row=0;row<4;row++)if(row!=col){double factor=a[row,col];for(int k=0;k<8;k++)a[row,k]-=factor*a[col,k];}
                }
                double[] result=new double[16];for(int row=0;row<4;row++)for(int col=0;col<4;col++)result[row*4+col]=a[row,col+4];return new Matrix4x4(result);
            }
        }
    }
    public sealed class Transform
    {
        public Transform parent;
        public Vector3 localPosition;
        public Quaternion localRotation=Quaternion.identity;
        public Vector3 localScale=Vector3.one;
        public Matrix4x4 localToWorldMatrix{get{return parent==null?Matrix4x4.TRS(localPosition,localRotation,localScale):parent.localToWorldMatrix*Matrix4x4.TRS(localPosition,localRotation,localScale);}}
        public Matrix4x4 worldToLocalMatrix{get{return localToWorldMatrix.inverse;}}
        public Vector3 lossyScale{get{Vector3 p=parent==null?Vector3.one:parent.lossyScale;return new Vector3(localScale.x*p.x,localScale.y*p.y,localScale.z*p.z);}}
        public Vector3 TransformPoint(Vector3 point){return localToWorldMatrix.MultiplyPoint3x4(point);}
        public Vector3 InverseTransformPoint(Vector3 point){return worldToLocalMatrix.MultiplyPoint3x4(point);}
        public bool IsChildOf(Transform other){for(Transform node=this;node!=null;node=node.parent)if(node==other)return true;return false;}
    }
    public struct Bounds
    {
        public Vector3 center,size;
        public Bounds(Vector3 center,Vector3 size){this.center=center;this.size=size;}
        public Vector3 min{get{return center-size*.5f;}}
        public Vector3 max{get{return center+size*.5f;}}
        public void Encapsulate(Vector3 point)
        {
            Vector3 lo=min,hi=max;
            lo=new Vector3(Math.Min(lo.x,point.x),Math.Min(lo.y,point.y),Math.Min(lo.z,point.z));hi=new Vector3(Math.Max(hi.x,point.x),Math.Max(hi.y,point.y),Math.Max(hi.z,point.z));
            center=(lo+hi)*.5f;size=hi-lo;
        }
    }
    public class Renderer{public Transform transform=new Transform();public Bounds LocalBounds;}
    public sealed class SkinnedMeshRenderer:Renderer{}
    public struct LOD
    {
        public float screenRelativeTransitionHeight,fadeTransitionWidth;
        public Renderer[] renderers;
        public LOD(float threshold,Renderer[] renderers){screenRelativeTransitionHeight=threshold;fadeTransitionWidth=0;this.renderers=renderers;}
    }
    public sealed class LODGroup
    {
        public readonly Transform transform;
        LOD[] Levels=new LOD[0];
        Vector3 Reference;
        float Size;
        bool Enabled;
        public int SetCalls,PropertyWrites,Recalculations,ForcedMode=-1;
        public bool ThrowOnceBeforeSet,ThrowOnceAfterSet;
        public LODGroup(Transform transform){this.transform=transform;}
        public bool enabled{get{return Enabled;}set{Enabled=value;PropertyWrites++;}}
        public Vector3 localReferencePoint{get{return Reference;}set{Reference=value;PropertyWrites++;}}
        public float size{get{return Size;}set{Size=value;PropertyWrites++;}}
        public void ForceLOD(int mode){ForcedMode=mode;}
        public LOD[] GetLODs(){return (LOD[])Levels.Clone();}
        public void SetLODs(LOD[] levels)
        {
            SetCalls++;
            if(ThrowOnceBeforeSet){ThrowOnceBeforeSet=false;throw new ApplicationException("Injected SetLODs failure before mutation");}
            Levels=(LOD[])levels.Clone();
            if(ThrowOnceAfterSet)
            {
                ThrowOnceAfterSet=false;Reference=new Vector3(400,500,600);Size=900000;Enabled=!Enabled;
                throw new ApplicationException("Injected SetLODs failure after partial mutation");
            }
        }
        public void ResetCounters(){SetCalls=PropertyWrites=Recalculations=0;}
        public void RecalculateBounds()
        {
            Recalculations++;Bounds combined=new Bounds();bool first=true;
            foreach(LOD level in Levels)foreach(Renderer renderer in level.renderers)
            {
                Matrix4x4 toLocal=transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                Bounds local=renderer.LocalBounds;
                for(int c=0;c<8;c++)
                {
                    Vector3 point=toLocal.MultiplyPoint3x4(new Vector3((c&1)==0?local.min.x:local.max.x,(c&2)==0?local.min.y:local.max.y,(c&4)==0?local.min.z:local.max.z));
                    if(first){combined=new Bounds(point,Vector3.zero);first=false;}else combined.Encapsulate(point);
                }
            }
            localReferencePoint=combined.center;size=Math.Max(combined.size.x,Math.Max(combined.size.y,combined.size.z));
        }
    }
}
namespace BBI.Unity.Core.World
{
    public static class LODGroupController{public enum ModelLOD{High=0,Medium=1,Low=2,Off=3}}
}
namespace BBI.Unity.Game.World
{
    using UnityEngine;
    using BBI.Unity.Core.World;
    public sealed class UnitView
    {
        public Transform transform;
        public LODGroup Group;
        public LODGroupController.ModelLOD Mode;
        public int GetterCalls;
        // Match the production private, nonstatic native ABI exactly.
        LODGroupController.ModelLOD CurrentModelLOD{get{GetterCalls++;return Mode;}}
        public T GetComponent<T>() where T:class{return Group as T;}
    }
}
