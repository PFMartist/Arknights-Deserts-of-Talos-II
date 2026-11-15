using System;

namespace EndfieldDoK.MapUnitLab
{
    public static class RhodesPreviewFraming
    {
        static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
        // For each fixed-pose corner, camera position is anchor+scale*offset.
        // Every frustum/viewport constraint is linear in that one scale. This
        // fits the actual nine boxes, with no empty sphere or rotation margin.
        public static double FitFixedScale(double ax,double ay,double az,double[] offsets,double verticalTan,double aspect,double orthoSize,
            bool orthographic,double near,double far,double left,double right,double bottom,double top)
        {
            if(offsets==null||offsets.Length==0||offsets.Length%3!=0||!Finite(ax+ay+az+verticalTan+aspect+orthoSize+near+far+left+right+bottom+top)||
                aspect<=0||near<0||far<=near||left<0||right>1||left>=right||bottom<0||top>1||bottom>=top||
                (orthographic?orthoSize<=0:verticalTan<=0))return 0;
            double minimum=0,maximum=double.PositiveInfinity;
            double l=left*2-1,r=right*2-1,b=bottom*2-1,t=top*2-1;
            double horizontal=orthographic?orthoSize*aspect:verticalTan*aspect,vertical=orthographic?orthoSize:verticalTan;
            for(int i=0;i<offsets.Length;i+=3)
            {
                double x=offsets[i],y=offsets[i+1],z=offsets[i+2];if(!Finite(x+y+z))return 0;
                if(!Constrain(az-near-.0001,z,ref minimum,ref maximum)||!Constrain(far-.0001-az,-z,ref minimum,ref maximum))return 0;
                if(orthographic)
                {
                    if(!Constrain(ax-l*horizontal,x,ref minimum,ref maximum)||!Constrain(r*horizontal-ax,-x,ref minimum,ref maximum)||
                        !Constrain(ay-b*vertical,y,ref minimum,ref maximum)||!Constrain(t*vertical-ay,-y,ref minimum,ref maximum))return 0;
                }
                else if(!Constrain(ax-l*horizontal*az,x-l*horizontal*z,ref minimum,ref maximum)||
                    !Constrain(r*horizontal*az-ax,r*horizontal*z-x,ref minimum,ref maximum)||
                    !Constrain(ay-b*vertical*az,y-b*vertical*z,ref minimum,ref maximum)||
                    !Constrain(t*vertical*az-ay,t*vertical*z-y,ref minimum,ref maximum))return 0;
            }
            if(!Finite(maximum)||maximum<=0||maximum<minimum)return 0;
            // Back off only for float transform rounding at an active plane.
            double result=maximum*.9999;return result>=minimum?result:maximum;
        }
        static bool Constrain(double constant,double rate,ref double minimum,ref double maximum)
        {
            if(Math.Abs(rate)<1e-12)return constant>=0;
            double bound=-constant/rate;
            if(rate>0)minimum=Math.Max(minimum,bound);else maximum=Math.Min(maximum,bound);
            return maximum>=minimum;
        }
    }
}
