using System;
using System.Threading;
using UnityEngine;

namespace EndfieldDoK.ModelIntake
{
    // Only immutable uploaded meshes and texture pixels are shared. Materials,
    // transforms, renderer property blocks, module poses and rig state stay local.
    internal static class RhodesSharedResources
    {
        static readonly ContentLeaseCache<Mesh> Meshes=new ContentLeaseCache<Mesh>();
        static readonly ContentLeaseCache<Texture2D> Textures=new ContentLeaseCache<Texture2D>();
        static int MainThread;
        static void RequireThread()
        {
            int current=Thread.CurrentThread.ManagedThreadId;
            if(MainThread==0)MainThread=current;
            if(MainThread!=current)throw new InvalidOperationException("Unity resources may only be built/released on the presentation thread");
        }
        internal static Mesh Mesh(string contentKey,RhodesLoadedModel owner,Func<Mesh> build)
        {
            RequireThread();ContentLeaseCache<Mesh>.Lease lease=Meshes.Acquire(contentKey,build,
                delegate(Mesh value){EndfieldDoK.MapUnitLab.RhodesProbeLoader.Forget(value);UnityEngine.Object.DestroyImmediate(value);});
            try{owner.Shared.Add(lease);}catch{lease.Dispose();throw;}return lease.Value;
        }
        internal static Texture2D Png(RhodesResourceContent content,string role,bool linear,bool readable,int width,int height,string name,RhodesLoadedModel owner)
        {
            RequireThread();string key=role+"|rgba32|mip|wrap:repeat|filter:trilinear|"+(linear?"linear":"srgb")+"|"+(readable?"read":"gpu")+"|"+content.Sha256;
            ContentLeaseCache<Texture2D>.Lease lease=Textures.Acquire(key,delegate
            {
                Texture2D texture=new Texture2D(2,2,TextureFormat.RGBA32,true,linear);
                try
                {
                    texture.name=name;texture.hideFlags=HideFlags.HideAndDontSave;
                    if(!texture.LoadImage(content.Bytes)||texture.width!=width||texture.height!=height)throw new System.IO.InvalidDataException("PNG decode/dimension mismatch");
                    texture.wrapMode=TextureWrapMode.Repeat;texture.filterMode=FilterMode.Trilinear;texture.Apply(true,!readable);return texture;
                }
                catch{UnityEngine.Object.DestroyImmediate(texture);throw;}
            },delegate(Texture2D value){UnityEngine.Object.DestroyImmediate(value);});
            try{owner.Shared.Add(lease);}catch{lease.Dispose();throw;}return lease.Value;
        }
        internal static void Release(RhodesLoadedModel owner)
        {
            RequireThread();for(int i=owner.Shared.Count-1;i>=0;i--)owner.Shared[i].Dispose();owner.Shared.Clear();
        }
        internal static int MeshEntries{get{return Meshes.Count;}}
        internal static int TextureEntries{get{return Textures.Count;}}
        internal static int Leases{get{return Meshes.References+Textures.References;}}
    }
}
