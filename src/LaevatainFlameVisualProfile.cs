using System;
namespace EndfieldDoK.MapUnitLab
{
    // Pure bounded presentation profile. Gradients below are generated from
    // exact P_fxbat_laevat_combo_skill_hit particle ColorModules. Geometry is
    // a documented RTS / Unity5 reconstruction, not source VFXgraph playback.
    internal static class LaevatainFlameVisualProfile
    {
        internal const int Capacity=96;
        internal const float Duration=2.2f,FireRadiance=1.4f;
        internal static bool ShouldRender(float age,bool sensors)
        {
            // MeshRenderer owns the real camera frustum and bounds tests.
            // A distance sphere would incorrectly clip the screen corners.
            return !sensors&&age>=0f&&age<Duration&&!float.IsNaN(age);
        }
        internal struct Sprite
        {
            internal float X,Y,Z,Width,Height,Angle,R,G,B,A;
            internal int Role,Frame,Mode;
            internal bool Smoke;
        }
        sealed class Curve
        {
            readonly float[] Colors,Alphas;
            internal Curve(float[] colors,float[] alphas){Colors=colors;Alphas=alphas;}
            internal void Sample(float t,out float r,out float g,out float b,out float a)
            {
                int n=Colors.Length/4,i=0;while(i<n-2&&t>Colors[(i+1)*4])i++;
                int j=Math.Min(i+1,n-1);float u=Clamp((t-Colors[i*4])/Math.Max(.00001f,Colors[j*4]-Colors[i*4]));
                r=Mix(Colors[i*4+1],Colors[j*4+1],u);g=Mix(Colors[i*4+2],Colors[j*4+2],u);b=Mix(Colors[i*4+3],Colors[j*4+3],u);
                n=Alphas.Length/2;i=0;while(i<n-2&&t>Alphas[(i+1)*2])i++;j=Math.Min(i+1,n-1);u=Clamp((t-Alphas[i*2])/Math.Max(.00001f,Alphas[j*2]-Alphas[i*2]));a=Mix(Alphas[i*2+1],Alphas[j*2+1],u);
            }
        }
        static readonly Curve[] Source={
            new Curve(new float[]{0f,1f,0.7267605f,0.57254905f,0.0869306477f,1f,0.40302843f,0.081131995f,0.193240253f,1f,0.23490146f,0f,0.277409018f,0.554717f,0.21229504f,0.11617658f,0.959578851f,0.18490559f,0.04523208f,0.020583814f},new float[]{0f,0f,0.0370946822f,1f,0.452384222f,1f,1f,0.16862746f}),
            new Curve(new float[]{0f,1f,0.8484979f,0.7294118f,0.374364843f,0.93207544f,0.5757712f,0.27258798f,0.926466773f,0f,0f,0f},new float[]{0f,1f,1f,1f}),
            new Curve(new float[]{0.0419012741f,1f,0.7056867f,0.48113203f,0.250827802f,1f,0.4f,0f,0.800106813f,1f,0.26838374f,0.1641509f,1f,1f,0.10706467f,0.043396115f},new float[]{0f,0f,0.0581979095f,1f,0.431815061f,1f}),
            new Curve(new float[]{0.118822004f,1f,0.7417536f,0.2999999f,0.190005341f,1f,0.39219368f,0.15686274f,0.506889448f,0.745283f,0.17598887f,0.15819688f},new float[]{0f,0f,0.157045853f,1f,1f,0f}),
            new Curve(new float[]{0.0315556573f,1f,0.43222335f,0.09803921f,0.096894789f,1f,0.27450982f,0.09019608f},new float[]{0f,0f,0.0407568475f,0.8627451f,0.091279469f,0.19607843f,0.658365759f,0.19607843f,1f,0f}),
            new Curve(new float[]{0f,1f,1f,1f,0.269962615f,1f,1f,1f,1f,1f,0f,0f},new float[]{0f,0f,0.0735332265f,1f,0.423529412f,1f,1f,1f}),
            new Curve(new float[]{0.0200045777f,0.9245283f,0.8050292f,0.67508006f,0.323537041f,0.54901963f,0.532549f,0.5160785f},new float[]{0.714122225f,1f,1f,0f}),
            new Curve(new float[]{0f,1f,1f,1f,1f,1f,1f,1f},new float[]{0f,1f,1f,1f})
        };
        static float Clamp(float t){return Math.Max(0f,Math.Min(1f,t));}
        static float Mix(float a,float b,float u){return a+(b-a)*u;}
        static float Sin(float x){return (float)Math.Sin(x);}
        static float Cos(float x){return (float)Math.Cos(x);}
        static float Smooth(float t){t=Clamp(t);return t*t*(3f-2f*t);}
        static float Pi=(float)Math.PI;
        static int Put(Sprite[] output,int count,int role,float age,float delay,float life,float radius,float x,float y,float z,float w,float h,float angle,int mode,float opacity)
        {
            float u=(age-delay)/life;if(u<0f||u>=1f||count>=output.Length)return count;
            Sprite s=new Sprite();s.Role=role;s.Frame=role==6?Math.Min(7,(int)(u*8f)):((int)(u*12f)+count)%8;s.Mode=mode;s.Smoke=role>=6;
            s.X=x*radius;s.Y=y*radius;s.Z=z*radius;s.Width=w*radius;s.Height=h*radius;s.Angle=angle;
            Source[role].Sample(u,out s.R,out s.G,out s.B,out s.A);s.A*=opacity;
            output[count]=s;return count+1;
        }
        internal static int Evaluate(float age,float radius,bool reduced,Sprite[] output)
        {
            if(output==null||output.Length<Capacity)throw new ArgumentException("Fixed visual pool missing");
            if(age<0f||age>=Duration||radius<=0f||float.IsNaN(age)||float.IsNaN(radius)||float.IsInfinity(age)||float.IsInfinity(radius))return 0;
            int n=0;float flash=Smooth(age/.08f);
            n=Put(output,n,4,age,.05f,.5f,radius,0,.025f,0,1.7f*flash,1.7f*flash,0,1,.6f);
            n=Put(output,n,4,age,0,.3f,radius,0,.16f,0,.75f*flash,.75f*flash,0,2,.7f);
            for(int i=0;i<8;i++)
            {
                float angle=i*Pi*.25f+.17f,delay=.012f*(i%3),u=Clamp((age-delay)/.95f),rise=Smooth((age-delay)/.13f);
                if(!reduced||i%2==0)n=Put(output,n,0,age,delay,.95f,radius,Sin(angle)*.13f,.03f+u*.22f,Cos(angle)*.13f,.44f,.95f*rise,angle,0,.66f);
                u=Clamp((age-.1f)/.65f);rise=Smooth((age-.1f)/.09f);
                float jetAngle=i*2.39996323f+.31f,jetRadius=.25f+.14f*(i%3);
                n=Put(output,n,1,age,.1f+(i%3)*.009f,.65f,radius,Sin(jetAngle)*jetRadius,.02f+u*.12f,Cos(jetAngle)*jetRadius,.26f,(.85f+.1f*(i%3))*rise,jetAngle+.4f,0,1f);
                n=Put(output,n,2,age,.14f,1.15f,radius,Sin(angle)*(.45f+u*.12f),.013f,Cos(angle)*(.45f+u*.12f),.2f,.7f,angle,1,.9f);
            }
            // A low ground glow keeps the hit readable after the short burst,
            // while the damage footprint and source masks remain unchanged.
            n=Put(output,n,4,age,.32f,1.7f,radius,0,.02f,0,1.45f,1.45f,0,1,.8f);
            float ringU=Clamp(age/.65f),rr=.13f+.69f*Smooth(ringU);
            for(int i=0;i<12;i++)
            {
                float angle=i*Pi/6f;
                n=Put(output,n,3,age,0,.65f,radius,Sin(angle)*rr,.018f,Cos(angle)*rr,.48f*rr,.16f,angle,1,.95f);
            }
            for(int i=0;i<24;i++)
            {
                if(reduced&&i%2!=0)continue;
                float angle=i*2.39996323f,delay=.07f+(i%4)*.015f,life=.72f+(i%4)*.18f,u=Clamp((age-delay)/life),rrSpark=.18f+u*(.42f+.12f*(i%3));
                n=Put(output,n,5,age,delay,life,radius,Sin(angle)*rrSpark,.07f+(float)Math.Sin(u*Pi)*(.35f+.1f*(i%3))+u*.18f,Cos(angle)*rrSpark,.007f,.035f+(i%3)*.012f,angle,2,.8f);
            }
            for(int i=0;i<8;i++)
            {
                float angle=i*Pi*.25f+.3f,delay=.12f+(i%3)*.035f,u=Clamp((age-delay)/.8f);
                if(!reduced||i%2==0)n=Put(output,n,6,age,delay,.8f,radius,Sin(angle)*(.25f+u*.25f)+u*.1f,.04f+u*.33f,Cos(angle)*(.25f+u*.25f),.35f+u*.2f,.28f+u*.28f,0,2,.46f);
                u=Clamp((age-.08f)/1.5f);
                if(!reduced)n=Put(output,n,7,age,.08f,1.5f,radius,Sin(angle)*(.15f+u*.7f),.04f+(float)Math.Sin(u*Pi)*(.21f+.04f*(i%3)),Cos(angle)*(.15f+u*.7f),.018f,.026f,angle+age*5,3,.85f);
            }
            return n;
        }
    }
}
