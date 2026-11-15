using System;

namespace EndfieldDoK.MapUnitLab
{
    // The native information slot shares its left side with attribute values.
    // Anchor the silhouette to the right edge inside that same sprite canvas;
    // do not move or resize the panel, widget, labels or click targets.
    public static class RhodesInfoIconLayout
    {
        public static void Fit(int width,int height,int cropWidth,int cropHeight,out float scale,out float x,out float y)
        {
            if(width<1||height<1||cropWidth<1||cropHeight<1)throw new ArgumentOutOfRangeException("width");
            float padding=Math.Min(2f,Math.Min(width,height)*.05f);
            scale=Math.Min((width-padding*2f)/cropWidth,(height-padding*2f)/cropHeight);
            x=width-padding-cropWidth*scale;
            y=(height-cropHeight*scale)*.5f;
        }
    }
}
