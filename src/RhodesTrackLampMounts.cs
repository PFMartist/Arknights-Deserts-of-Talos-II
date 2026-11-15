using System;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static class RhodesTrackLampMounts
    {
        public const string AnchorAName="V35_LIGHT_TRACK_A";
        public const string AnchorBName="V35_LIGHT_TRACK_B";
        // Current RHS2 WorkWhite front faces. The six values are metre-space
        // position and the physical lens face's outward normal.
        // This method is pure managed data access for offline DLL checks.
        public static float[] GetMountRay(int index)
        {
            if(index==0)return new float[]{127.0671730f,60.0969887f,87.2438450f,
                .0389137115f,-.9358599592f,.3502168755f};
            if(index==1)return new float[]{-133.4389725f,58.1348553f,87.1318588f,
                .6437686407f,-.7121917455f,.2799015091f};
            throw new ArgumentOutOfRangeException("index");
        }

        public static Transform[] Ensure(Transform geometryRoot)
        {
            if(geometryRoot==null)throw new ArgumentNullException("geometryRoot");
            Transform[] anchors=new Transform[2];
            for(int index=0;index<anchors.Length;index++)
            {
                string name=index==0?AnchorAName:AnchorBName;
                Transform anchor=geometryRoot.Find(name);
                if(anchor==null)
                {
                    GameObject marker=new GameObject(name);marker.layer=geometryRoot.gameObject.layer;
                    anchor=marker.transform;anchor.parent=geometryRoot;
                }
                float[] ray=GetMountRay(index);
                anchor.localPosition=new Vector3(ray[0],ray[1],ray[2]);
                anchor.localRotation=Quaternion.FromToRotation(Vector3.up,new Vector3(ray[3],ray[4],ray[5]));
                anchor.localScale=Vector3.one;anchors[index]=anchor;
            }
            return anchors;
        }
    }
}
