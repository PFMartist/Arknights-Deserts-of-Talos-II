using System;

namespace EndfieldDoK.MapUnitLab
{
    // Presentation only: resolved shoe detail, not simulation/physics LOD.
    // No cameras, Unity object searches, arrays or render-state mutations here.
    internal static class RhodesTrackPresentationPolicy
    {
        internal const float StopShoePixels=1.25f,ResumeShoePixels=2f;
        internal static bool Finite(float x){return !float.IsNaN(x)&&!float.IsInfinity(x);}
        internal static double Advance(double phase,float forward,float angular,float right,float delta,float scale)
        {return phase+(forward+(double)angular*right)*delta/scale;}
        internal static bool DetailActive(bool previous,float pixels)
        {
            if(!Finite(pixels)||pixels<0f)return true; // Missing/invalid camera: conservative.
            return previous?pixels>=StopShoePixels:pixels>=ResumeShoePixels;
        }
        internal static float PixelsPerMetre(float verticalFovDegrees,int height,bool orthographic,float orthoSize)
        {
            if(height<=0)return float.PositiveInfinity;
            if(orthographic)return Finite(orthoSize)&&orthoSize>0f?height/(2f*orthoSize):float.PositiveInfinity;
            if(!Finite(verticalFovDegrees)||verticalFovDegrees<=0f||verticalFovDegrees>=179f)return float.PositiveInfinity;
            return (float)(height/(2.0*Math.Tan(verticalFovDegrees*Math.PI/360.0)));
        }
        internal static float ShoePixels(float worldPitch,float depth,float radius,float near,float pixelsPerMetre,bool orthographic)
        {
            if(!Finite(worldPitch)||worldPitch<=0f||!Finite(pixelsPerMetre))return float.PositiveInfinity;
            if(orthographic)return worldPitch*pixelsPerMetre;
            return worldPitch*pixelsPerMetre/Math.Max(Math.Max(.001f,near),depth-radius);
        }
        internal static bool SphereInView(float depth,float horizontal,float vertical,float radius,float near,float far,
            float tanHorizontal,float tanVertical,bool orthographic,float orthoHalfWidth,float orthoHalfHeight)
        {
            if(!Finite(depth)||!Finite(horizontal)||!Finite(vertical)||!Finite(radius)||radius<0f)return true;
            if(depth+radius<near||depth-radius>far)return false;
            if(orthographic)return Math.Abs(horizontal)<=orthoHalfWidth+radius&&Math.Abs(vertical)<=orthoHalfHeight+radius;
            // Unnormalised side-plane tests; radius must be multiplied by the
            // normal length. A plain radius here incorrectly culls edge spheres.
            return Math.Abs(horizontal)<=depth*tanHorizontal+radius*Math.Sqrt(1.0+tanHorizontal*tanHorizontal)&&
                Math.Abs(vertical)<=depth*tanVertical+radius*Math.Sqrt(1.0+tanVertical*tanVertical);
        }
        internal static bool RotationChanged(float previous,float next)
        {
            double difference=(next-(double)previous)%(2.0*Math.PI);
            if(difference>Math.PI)difference-=2.0*Math.PI;
            if(difference<-Math.PI)difference+=2.0*Math.PI;
            return Math.Abs(difference)>1e-6;
        }
    }

    // Only the private shared suspension controls use this input/commit cache.
    // Contact samples invalidate it even when their resulting pose is unchanged.
    internal struct RhodesSharedSuspensionCommit
    {
        internal const float Epsilon=0.000001f;
        bool Known;
        float AHeave,APitch,BHeave,BPitch;
        long ARevision,BRevision;
        internal bool HavePose;
        internal float Lift,Common,First,Second;
        internal bool SameInputs(float ah,float ap,float bh,float bp,long ar,long br)
        {return Known&&ARevision==ar&&BRevision==br&&Same(AHeave,ah)&&Same(APitch,ap)&&Same(BHeave,bh)&&Same(BPitch,bp);}
        internal void Inputs(float ah,float ap,float bh,float bp,long ar,long br)
        {Known=true;AHeave=ah;APitch=ap;BHeave=bh;BPitch=bp;ARevision=ar;BRevision=br;}
        internal static bool Same(float a,float b){return Math.Abs(a-b)<=Epsilon;}
    }
}
