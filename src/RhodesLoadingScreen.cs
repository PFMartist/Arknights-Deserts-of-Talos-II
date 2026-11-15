using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BBI.Unity.Game.UI;
using BBI.Unity.Game.World;
using BBI.Game.Data;
using BBI.Game.Replay;
using BBI.Core.Utility;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Optional frontend presentation only. A missing catalog keeps the native
    // loading screen; no native scene, streaming lease or progress is changed.
    internal static class RhodesLoadingScreen
    {
        internal static void PanelInitialized(object value)
        {
            Component panel=value as Component;
            if(panel==null||!(value is MultiplayerLoadingPanel))return;
            try
            {
                RhodesLoadingScreenDriver driver=panel.GetComponent<RhodesLoadingScreenDriver>();
                if(driver==null)driver=panel.gameObject.AddComponent<RhodesLoadingScreenDriver>();
                driver.Configure(panel,Path.Combine(CustomUnits.AssetDirectory,"ui_arknights_loading"));
            }
            catch(Exception error){Runtime.Log("WARNING RHODES_LOADING bind: "+error.Message+"; nativeFallback=True");}
        }
    }

    internal sealed class RhodesLoadingPage
    {
        internal readonly string Id,Image,Title,Description;
        internal RhodesLoadingPage(string id,string image,string title,string description)
        {Id=id;Image=image;Title=title;Description=description;}
    }

    internal static class RhodesLoadingCatalog
    {
        internal const int MaximumPages=32,MaximumTextBytes=32768,MaximumImageBytes=24*1024*1024;
        internal static string FilePath(string directory,string name,string extension)
        {
            if(string.IsNullOrEmpty(name)||name.Length>120||name.IndexOf("..",StringComparison.Ordinal)>=0||Path.GetFileNameWithoutExtension(name).Length==0||
                !string.Equals(Path.GetExtension(name),extension,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid loading page filename");
            foreach(char c in name)if(!((c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9')||c=='_'||c=='-'||c=='.'))
                throw new InvalidDataException("Loading files must use plain filenames");
            string root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string full=Path.GetFullPath(Path.Combine(root,name));
            if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Loading path escaped its directory");
            return full;
        }
        internal static RhodesLoadingPage[] Read(string directory)
        {
            string file=Path.Combine(directory,"pages.tsv");
            if(!File.Exists(file))return new RhodesLoadingPage[0];
            if(new FileInfo(file).Length>MaximumTextBytes)throw new InvalidDataException("Loading catalog is too large");
            List<RhodesLoadingPage> pages=new List<RhodesLoadingPage>();HashSet<string> ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(string raw in File.ReadAllLines(file,Encoding.UTF8))
            {
                string line=raw.Trim();if(line.Length==0||line[0]=='#')continue;
                string[] cells=line.Split('\t');
                if(cells.Length!=4)throw new InvalidDataException("Loading catalog must have four tab-separated columns");
                for(int i=0;i<cells.Length;i++)cells[i]=cells[i].Trim();
                if(cells[0].Length==0||cells[0].Length>80||cells[2].Length==0||cells[2].Length>160||!ids.Add(cells[0]))
                    throw new InvalidDataException("Invalid or duplicate loading page identity");
                FilePath(directory,cells[1],".png");FilePath(directory,cells[3],".txt");
                pages.Add(new RhodesLoadingPage(cells[0],cells[1],cells[2],cells[3]));
                if(pages.Count>MaximumPages)throw new InvalidDataException("Too many loading pages");
            }
            return pages.ToArray();
        }
        internal static string ReadDescription(string directory,RhodesLoadingPage page)
        {
            string file=FilePath(directory,page.Description,".txt");
            if(new FileInfo(file).Length>MaximumTextBytes)throw new InvalidDataException("Loading page text is too large");
            string text=File.ReadAllText(file,Encoding.UTF8).Trim();
            if(text.Length==0)throw new InvalidDataException("Loading page text is empty");
            return text;
        }
        internal static byte[] ReadImage(string directory,RhodesLoadingPage page)
        {
            string file=FilePath(directory,page.Image,".png");
            long length=new FileInfo(file).Length;
            if(length<24||length>MaximumImageBytes)throw new InvalidDataException("Loading page PNG size is invalid");
            byte[] bytes=File.ReadAllBytes(file);
            if(bytes[0]!=137||bytes[1]!=80||bytes[2]!=78||bytes[3]!=71||bytes[4]!=13||bytes[5]!=10||bytes[6]!=26||bytes[7]!=10||
                bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82)
                throw new InvalidDataException("Loading page is not a PNG with IHDR");
            int width=BigEndian(bytes,16),height=BigEndian(bytes,20);
            if(width<1||height<1||width>4096||height>4096)throw new InvalidDataException("Loading page exceeds 4096 pixels");
            return bytes;
        }
        static int BigEndian(byte[] bytes,int at)
        {return (bytes[at]<<24)|(bytes[at+1]<<16)|(bytes[at+2]<<8)|bytes[at+3];}
    }

    public sealed class RhodesLoadingScreenDriver:MonoBehaviour
    {
        static int Selection;
        Behaviour Panel;
        LevelManager Levels;
        UITexture Background;
        UILabel FontSource,Title,Description;
        UIPanel[] VisibilityPanels;
        UIWidget[] NativeWidgets;
        Texture2D OwnedTexture;
        string DirectoryPath;
        bool Configured,Attempted,Failed,Eligible;
        int LayoutWidth=-1,LayoutHeight=-1,CyclePage=-1;
        Rect OriginalUv;
        sealed class EmptyLoad:IEnumerator
        {public bool MoveNext(){return false;}public void Reset(){}public object Current{get{return null;}}}
        static readonly IEnumerator NoWait=new EmptyLoad();

        internal void Configure(Component panel,string directory)
        {
            if(Configured)return;
            Panel=panel as Behaviour;DirectoryPath=directory;
            Background=NativeRead.Get(panel,"m_Background") as UITexture;
            FontSource=NativeRead.Get(panel,"m_MapName") as UILabel;
            Levels=NativeRead.Get(panel,"mLevelManager") as LevelManager;
            if(Panel==null||Levels==null)throw new InvalidDataException("Native loading lifecycle is missing");
            if(Background==null)throw new InvalidDataException("Native loading background is missing");
            if(FontSource==null||(FontSource.bitmapFont==null&&FontSource.trueTypeFont==null))
            {
                foreach(UILabel candidate in panel.GetComponentsInChildren<UILabel>(true))
                    if(candidate!=null&&(candidate.bitmapFont!=null||candidate.trueTypeFont!=null)){FontSource=candidate;break;}
            }
            if(FontSource==null||(FontSource.bitmapFont==null&&FontSource.trueTypeFont==null))throw new InvalidDataException("Native loading font is missing");
            // Both arrays are captured once. LateUpdate never searches a scene
            // or a hierarchy and does no file work after the visible activation.
            VisibilityPanels=Background.GetComponentsInParent<UIPanel>(true);
            NativeWidgets=panel.GetComponentsInChildren<UIWidget>(true);
            OriginalUv=Background.uvRect;
            Levels.SceneLoadStart+=OnSceneLoadStart;Configured=true;
        }
        IEnumerator OnSceneLoadStart(BBI.Unity.Game.Data.LevelDefinition level,BBI.Unity.Core.Utility.Scene scene,GameMode mode,ReplayMode replay,DependencyContainerBase dependencies)
        {
            // A real scene-loading event defines the cycle, rather than alpha
            // fades or a temporary enable/disable of the same loading widget.
            try{ReleasePage();CyclePage=-1;Eligible=mode==GameMode.AISkirmish;}
            catch(Exception error){Eligible=false;Runtime.Log("WARNING RHODES_LOADING lifecycle: "+error.Message+"; nativeFallback=True");}
            return NoWait;
        }
        bool Visible()
        {
            if(!Configured||!Eligible||Panel==null||Background==null||!isActiveAndEnabled||!Panel.enabled||!Panel.gameObject.activeInHierarchy||
                !Background.enabled||!Background.gameObject.activeInHierarchy||Background.alpha<=.001f)return false;
            for(int i=0;i<VisibilityPanels.Length;i++)
                if(VisibilityPanels[i]!=null&&(!VisibilityPanels[i].enabled||VisibilityPanels[i].alpha<=.001f))return false;
            return true;
        }
        void OnEnable(){Attempted=Failed=false;}
        void LateUpdate()
        {
            if(!Configured)return;
            if(!Visible()){if(Attempted||OwnedTexture!=null)ReleasePage();return;}
            if(Failed)return;
            try
            {
                if(!Attempted){Attempted=true;Begin();}
                if(OwnedTexture==null)return;
                // Native streaming callbacks can replace this widget after
                // InitializePanel. Reapply only this cached pointer, while visible.
                if(Background.mainTexture!=OwnedTexture)Background.mainTexture=OwnedTexture;
                if(Background.width!=LayoutWidth||Background.height!=LayoutHeight)Layout();
            }
            catch(Exception error)
            {ReleasePage();Attempted=Failed=true;Runtime.Log("WARNING RHODES_LOADING page: "+error.Message+"; nativeFallback=True");}
        }
        void Begin()
        {
            RhodesLoadingPage[] pages=RhodesLoadingCatalog.Read(DirectoryPath);
            if(pages.Length==0)return;
            if(CyclePage<0){CyclePage=Selection;Selection=Selection==int.MaxValue?0:Selection+1;}
            int index=CyclePage%pages.Length;
            RhodesLoadingPage page=pages[index];
            string description=RhodesLoadingCatalog.ReadDescription(DirectoryPath,page);
            byte[] bytes=RhodesLoadingCatalog.ReadImage(DirectoryPath,page);
            OwnedTexture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if(!OwnedTexture.LoadImage(bytes))throw new InvalidDataException("Cannot decode loading PNG");
            OwnedTexture.name="RI_Loading_"+page.Id;OwnedTexture.wrapMode=TextureWrapMode.Clamp;OwnedTexture.filterMode=FilterMode.Bilinear;
            OwnedTexture.Apply(false,true);
            Title=CreateLabel("RI_Loading_Title",page.Title,true);
            Description=CreateLabel("RI_Loading_Story",description,false);
            Background.mainTexture=OwnedTexture;LayoutWidth=LayoutHeight=-1;Layout();
            Runtime.Log("RHODES_LOADING_APPLIED page="+page.Id+"; nativeProgressRetained=True; nativeStreamingUntouched=True; residentImages=1; recurringSceneScan=False");
        }
        UILabel CreateLabel(string name,string text,bool title)
        {
            GameObject root=new GameObject(name);root.layer=Background.gameObject.layer;
            root.transform.parent=Background.transform;root.transform.localScale=Vector3.one;root.transform.localRotation=Quaternion.identity;
            UILabel label=root.AddComponent<UILabel>();
            if(FontSource.bitmapFont!=null)label.bitmapFont=FontSource.bitmapFont;else label.trueTypeFont=FontSource.trueTypeFont;
            label.pivot=UIWidget.Pivot.TopLeft;label.alignment=NGUIText.Alignment.Left;label.overflowMethod=UILabel.Overflow.ShrinkContent;
            label.supportEncoding=false;label.maxLineCount=title?1:0;label.color=Color.white;label.depth=Background.depth+1;
            label.effectStyle=UILabel.Effect.Shadow;label.effectColor=new Color(0f,0f,0f,.95f);label.effectDistance=new Vector2(1f,-1f);
            label.text=text;return label;
        }
        void Layout()
        {
            if(Title==null||Description==null)return;
            LayoutWidth=Background.width;LayoutHeight=Background.height;
            Vector3[] corners=Background.localCorners;
            float left=corners[0].x,bottom=corners[0].y,width=Mathf.Max(1f,corners[2].x-left),height=Mathf.Max(1f,corners[2].y-bottom);
            // Search six bounded text boxes against the once-cached native
            // widgets. This preserves player/map/progress information above us.
            float w=width*.64f,h=height*.25f,mx=width*.06f,my=height*.065f;
            Rect best=new Rect(left+mx,bottom+height-my-h,w,h);float bestOverlap=float.MaxValue;
            for(int option=0;option<6;option++)
            {
                float x=option%3==0?left+mx:option%3==1?left+(width-w)*.5f:left+width-mx-w;
                float y=option<3?bottom+height-my-h:bottom+my;
                Rect candidate=new Rect(x,y,w,h);float overlap=0f;
                for(int i=0;i<NativeWidgets.Length;i++)
                {
                    UIWidget native=NativeWidgets[i];if(native==null||native==Background||!native.enabled||!native.gameObject.activeInHierarchy||native.alpha<=.001f)continue;
                    Vector3[] world=native.worldCorners;Vector3 a=Background.transform.InverseTransformPoint(world[0]),b=Background.transform.InverseTransformPoint(world[2]);
                    float nx=Mathf.Min(a.x,b.x),ny=Mathf.Min(a.y,b.y),nw=Mathf.Abs(b.x-a.x),nh=Mathf.Abs(b.y-a.y);
                    if(nw>width*.85f||nh>height*.85f)continue;
                    overlap+=Mathf.Max(0f,Mathf.Min(candidate.xMax,nx+nw)-Mathf.Max(candidate.xMin,nx))*Mathf.Max(0f,Mathf.Min(candidate.yMax,ny+nh)-Mathf.Max(candidate.yMin,ny));
                }
                if(overlap<bestOverlap){best=candidate;bestOverlap=overlap;}
            }
            int titleSize=Mathf.Clamp(Mathf.RoundToInt(height*.029f),18,48),bodySize=Mathf.Clamp(Mathf.RoundToInt(height*.020f),14,34);
            Title.fontSize=titleSize;Title.width=Mathf.Max(1,Mathf.RoundToInt(best.width));Title.height=titleSize+8;
            Title.transform.localPosition=new Vector3(best.x,best.yMax,0f);
            Description.fontSize=bodySize;Description.width=Title.width;Description.height=Mathf.Max(1,Mathf.RoundToInt(best.height-titleSize-16));
            Description.transform.localPosition=new Vector3(best.x,best.yMax-titleSize-16,0f);
            // Arknights worldViewTips are stored at 1024x1024 but authored for
            // a full widescreen widget. Preserve full UVs instead of square crop.
            Background.uvRect=new Rect(0f,0f,1f,1f);
        }
        void ReleasePage()
        {
            // A captured native texture can already have been unloaded. Detach
            // only our own texture; never restore or release a native lease.
            if(Background!=null&&OwnedTexture!=null&&Background.mainTexture==OwnedTexture)
            {Background.mainTexture=null;Background.uvRect=OriginalUv;}
            if(Title!=null)Destroy(Title.gameObject);if(Description!=null)Destroy(Description.gameObject);
            if(OwnedTexture!=null)Destroy(OwnedTexture);
            Title=Description=null;OwnedTexture=null;LayoutWidth=LayoutHeight=-1;Attempted=Failed=false;
        }
        void OnDisable(){ReleasePage();}
        void OnDestroy(){if(Levels!=null)Levels.SceneLoadStart-=OnSceneLoadStart;Levels=null;ReleasePage();Panel=null;Background=null;FontSource=null;VisibilityPanels=null;NativeWidgets=null;}
    }
}
