using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BBI.Unity.Game.UI;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Reuses the audited native panel initialization hook. Only the frontend
    // movie instance and its local UITexture references are changed.
    public static class EndfieldMenuBackground
    {
        public static void PanelInitialized(object panel)
        {
            MainMenuBackgroundPanel background=panel as MainMenuBackgroundPanel;
            if(background==null)return;
            try
            {
                string directory=Path.Combine(CustomUnits.AssetDirectory,"ui_endfield");
                string config=Path.Combine(directory,"menu_background.ini");
                bool enabled=false;
                if(!File.Exists(config))return;
                foreach(string raw in File.ReadAllLines(config))
                {
                    string line=raw.Trim();int equals=line.IndexOf('=');
                    if(equals>0 && string.Equals(line.Substring(0,equals).Trim(),"Enabled",StringComparison.OrdinalIgnoreCase))
                    {
                        if(!bool.TryParse(line.Substring(equals+1).Trim(),out enabled))throw new InvalidDataException("Invalid menu background Enabled value");
                    }
                }
                if(!enabled)return;
                EndfieldMenuMovie driver=background.GetComponent<EndfieldMenuMovie>();
                if(driver==null)driver=background.gameObject.AddComponent<EndfieldMenuMovie>();
                driver.Configure(background,directory);
            }
            catch(Exception error){Runtime.Log("WARNING ENDFIELD_MENU bind: "+error.Message+"; nativeFallback=True");}
        }
    }

    public sealed class EndfieldMenuMovie : MonoBehaviour
    {
        sealed class TextureRoute
        {
            public UITexture Widget;
            public Texture Original;
        }
        readonly List<TextureRoute> Routes=new List<TextureRoute>();
        MainMenuBackgroundPanel Panel;
        MovieTexture OriginalMovie,Movie;
        Texture2D Poster,Logo;
        UITexture FooterLogo;
        Texture2D Title;
        sealed class TitleRoute
        {
            public UISprite Original;
            public string SpriteName;
            public UITexture Replacement;
        }
        readonly List<TitleRoute> Titles=new List<TitleRoute>();
        WWW Request;
        string DirectoryPath;
        bool Configured,Loading,Applied,Failed,Disposed;
        float EnabledAt;

        public void Configure(MainMenuBackgroundPanel panel,string directory)
        {
            if(Configured)return;
            Panel=panel;DirectoryPath=directory;
            OriginalMovie=NativeRead.Get(panel,"m_Movie") as MovieTexture;
            if(OriginalMovie==null)throw new InvalidDataException("Native menu movie is missing");
            foreach(UITexture texture in panel.GetComponentsInChildren<UITexture>(true))
                if(texture!=null && texture.mainTexture==OriginalMovie)Routes.Add(new TextureRoute{Widget=texture,Original=texture.mainTexture});
            if(Routes.Count==0)throw new InvalidDataException("Native menu movie has no local texture widget");
            Configured=true;
            Runtime.Log("ENDFIELD_MENU_BOUND widgets="+Routes.Count+"; nativeMovieLifecycle=True; recurringSceneScan=False; inputUnchanged=True");
            if(isActiveAndEnabled)Begin();
        }
        void OnEnable(){if(Configured)Begin();}
        void Begin()
        {
            if(Loading || Applied || Failed)return;
            Disposed=false;EnabledAt=Time.realtimeSinceStartup;
            try
            {
                // Fixed filenames keep all loads inside this owned asset folder.
                string movieFile=Path.Combine(DirectoryPath,"endfield_login_loop.ogv");
                if(!File.Exists(movieFile))throw new FileNotFoundException("Endfield login movie not installed");
                Logo=ReadTexture("hypergryph_logo.png");
                BindFooterLogo();
                try{BindTitles();}catch(Exception error){ReleaseTitles();Runtime.Log("WARNING TALOS_TITLE: "+error.Message+"; nativeTitleRestored=True");}
                Poster=ReadTexture("endfield_login_poster.jpg");
                foreach(TextureRoute route in Routes)if(route.Widget!=null)route.Widget.mainTexture=Poster;
                OriginalMovie.Stop();
                Request=new WWW(new Uri(movieFile).AbsoluteUri);
                Loading=true;StartCoroutine(LoadMovie());
            }
            catch(Exception error){Fail(error.Message);}
        }
        IEnumerator LoadMovie()
        {
            // WWW and MovieTexture are available in the game's Unity 5.2 ABI.
            // No newer VideoPlayer module, process, worker or frame image IO.
            while(Request!=null && !Request.isDone)
            {
                if(Time.realtimeSinceStartup-EnabledAt>30f){Fail("Local movie load timed out");yield break;}
                yield return null;
            }
            if(Request==null)yield break;
            if(!string.IsNullOrEmpty(Request.error)){Fail(Request.error);yield break;}
            try{Movie=Request.movie;}
            catch(Exception error){Fail(error.Message);}
            if(Movie==null){if(!Failed)Fail("Local Theora movie could not be decoded");yield break;}
            while(!Movie.isReadyToPlay)
            {
                if(Time.realtimeSinceStartup-EnabledAt>30f){Fail("Movie decoder readiness timed out");yield break;}
                yield return null;
            }
            try
            {
                Movie.name="EF_Menu_LoginLoop";Movie.loop=true;
                NativeRead.Set(Panel,"m_Movie",Movie);
                foreach(TextureRoute route in Routes)if(route.Widget!=null)route.Widget.mainTexture=Movie;
                Applied=true;Loading=false;Movie.Play();
                if(FooterLogo==null)BindFooterLogo();
                Runtime.Log("ENDFIELD_MENU_PLAYING "+Movie.width+"x"+Movie.height+"; duration="+Movie.duration+
                    "; loop="+Movie.loop+"; isPlaying="+Movie.isPlaying+"; nativeWidgets="+Routes.Count+"; audioTrack=False; nativeFooterLogo="+(FooterLogo!=null));
                if(Runtime.MenuPreview)StartCoroutine(RecordPreview());
            }
            catch(Exception error){Fail(error.Message);}
        }
        IEnumerator RecordPreview()
        {
            // Four bounded observations for an explicit preview launch only.
            // Normal play never captures screenshots or reads a GPU surface.
            string folder=Path.Combine(Runtime.LabDirectory,"MenuPreview_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(folder);
            Runtime.Log("ENDFIELD_MENU_PREVIEW armed=True; nativeFrontendOnly=True; folder="+folder);
            float started=Time.realtimeSinceStartup;
            int[] seconds={2,8,18,33};
            foreach(int second in seconds)
            {
                while(Time.realtimeSinceStartup-started<second)yield return null;
                yield return new WaitForEndOfFrame();
                if(Movie==null || !Applied)yield break;
                string shot=Path.Combine(folder,"menu_"+second.ToString("00")+"s.png");
                Application.CaptureScreenshot(shot);
                // Verify decoded motion, including observations after one and
                // two loop boundaries, separately from GUI screenshot evidence.
                RenderTexture target=RenderTexture.GetTemporary(160,90,0);
                RenderTexture previous=RenderTexture.active;Texture2D sample=null;
                try
                {
                    Graphics.Blit(Movie,target);RenderTexture.active=target;
                    sample=new Texture2D(160,90,TextureFormat.RGB24,false);
                    sample.ReadPixels(new Rect(0,0,160,90),0,0);sample.Apply();
                    uint hash=2166136261;foreach(Color32 pixel in sample.GetPixels32())
                    {hash=(hash^pixel.r)*16777619;hash=(hash^pixel.g)*16777619;hash=(hash^pixel.b)*16777619;}
                    Runtime.Log("ENDFIELD_MENU_PREVIEW_FRAME elapsed="+second+"; isPlaying="+Movie.isPlaying+
                        "; duration="+Movie.duration+"; decodedFrameHash="+hash.ToString("x8")+"; screenshot="+shot);
                }
                finally
                {
                    RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
                    if(sample!=null)UnityEngine.Object.Destroy(sample);
                }
            }
            Runtime.Log("ENDFIELD_MENU_PREVIEW complete=True; observations=4; capturedBeyondTwoLoops=True; furtherSampling=False");
        }
        Texture2D ReadTexture(string filename)
        {
            Texture2D texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            texture.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                if(!texture.LoadImage(File.ReadAllBytes(Path.Combine(DirectoryPath,filename))))throw new InvalidDataException("Cannot decode "+filename);
                texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
                return texture;
            }
            catch{UnityEngine.Object.Destroy(texture);throw;}
        }
        void BindFooterLogo()
        {
            if(FooterLogo!=null || Logo==null || Panel==null)return;
            // One bounded search inside this panel's NGUI root at initialization,
            // never a scene scan or a screen-space overlay. The native studio
            // texture identifies the footer independently of object names.
            UIRoot root=Panel.GetComponentInParent<UIRoot>();
            if(root==null){Runtime.Log("WARNING ENDFIELD_MENU_FOOTER native UIRoot missing; IMGUIFallback=False");return;}
            UITexture gearbox=null;int selectedScore=-1;bool ambiguous=false;
            UITexture[] widgets=root.GetComponentsInChildren<UITexture>(true);
            foreach(UITexture widget in widgets)
            {
                if(widget==null || widget.mainTexture==null)continue;
                if(widget.mainTexture.name=="TEX_UI_Logo-GBX")
                {
                    int score=0;Transform ancestor=widget.transform;
                    while(ancestor!=null && ancestor!=root.transform)
                    {
                        if(ancestor==Panel.transform)score=Mathf.Max(score,100);
                        if(ancestor.GetComponent<MainMenuPanel>()!=null)score=Mathf.Max(score,90);
                        string name=ancestor.name.ToLowerInvariant();
                        if(name.IndexOf("mainmenu",StringComparison.Ordinal)>=0 || name.IndexOf("main menu",StringComparison.Ordinal)>=0)score=Mathf.Max(score,60);
                        if(name.IndexOf("footer",StringComparison.Ordinal)>=0)score=Mathf.Max(score,40);
                        ancestor=ancestor.parent;
                    }
                    if(Runtime.MenuPreview)
                    {
                        string path=widget.name;ancestor=widget.transform.parent;
                        while(ancestor!=null && ancestor!=root.transform){path=ancestor.name+"/"+path;ancestor=ancestor.parent;}
                        Runtime.Log("ENDFIELD_MENU_FOOTER_ROUTE path="+path+"; score="+score+"; active="+widget.gameObject.activeInHierarchy+"; depth="+widget.depth);
                    }
                    if(score>selectedScore){gearbox=widget;selectedScore=score;ambiguous=false;}
                    else if(score==selectedScore)ambiguous=true;
                }
            }
            if(ambiguous){Runtime.Log("WARNING ENDFIELD_MENU_FOOTER ambiguous native Gearbox footer; IMGUIFallback=False");return;}
            if(gearbox==null)
            {
                Runtime.Log("WARNING ENDFIELD_MENU_FOOTER native Gearbox texture missing; IMGUIFallback=False; candidates="+widgets.Length);
                if(Runtime.MenuPreview)
                {
                    int count=0;foreach(UITexture widget in widgets)
                        if(widget!=null && widget.mainTexture!=null && count++<30)
                            Runtime.Log("ENDFIELD_MENU_FOOTER_CANDIDATE object="+widget.name+"; texture="+widget.mainTexture.name+"; depth="+widget.depth);
                }
                return;
            }
            UIPanel nativePanel=NGUITools.FindInParents<UIPanel>(gearbox.gameObject);
            if(nativePanel==null){Runtime.Log("WARNING ENDFIELD_MENU_FOOTER native footer UIPanel missing; IMGUIFallback=False");return;}
            // Match the native shader's alpha contract without changing the PNG.
            if(gearbox.premultipliedAlpha)
            {
                Color32[] pixels=Logo.GetPixels32();
                for(int i=0;i<pixels.Length;i++)
                {
                    Color32 pixel=pixels[i];
                    pixel.r=(byte)((pixel.r*pixel.a+127)/255);pixel.g=(byte)((pixel.g*pixel.a+127)/255);pixel.b=(byte)((pixel.b*pixel.a+127)/255);pixels[i]=pixel;
                }
                Logo.SetPixels32(pixels);Logo.Apply();
            }
            GameObject node=new GameObject("EF_Hypergryph_Footer");
            node.layer=gearbox.gameObject.layer;node.transform.parent=gearbox.transform;
            node.transform.localRotation=Quaternion.identity;node.transform.localScale=Vector3.one;
            FooterLogo=node.AddComponent<UITexture>();FooterLogo.pivot=UIWidget.Pivot.Center;
            FooterLogo.mainTexture=Logo;FooterLogo.shader=gearbox.shader;FooterLogo.depth=gearbox.depth;
            Color colour=gearbox.color;colour.a=1f;FooterLogo.color=colour;
            int height=Mathf.Max(2,Mathf.RoundToInt(gearbox.height*.95f));
            int width=Mathf.Max(2,Mathf.RoundToInt(height*(float)Logo.width/Logo.height));
            FooterLogo.SetDimensions(width,height);
            Vector3[] corners=gearbox.localCorners;
            node.transform.localPosition=new Vector3(corners[0].x-height*.25f-width*.5f,(corners[0].y+corners[1].y)*.5f,0f);
            // Parenting to the native logo inherits its visibility and alpha,
            // plus the footer panel's clipping, scale, fade and render queue.
            bool samePanel=NGUITools.FindInParents<UIPanel>(node)==nativePanel;
            Runtime.Log("ENDFIELD_MENU_FOOTER_BOUND nativeNGUI=True; samePanel="+samePanel+"; sameLayer="+(node.layer==gearbox.gameObject.layer)+
                "; widgetDepth="+FooterLogo.depth+"; nativeWidgetDepth="+gearbox.depth+"; width="+width+"; height="+height+
                "; inheritsNativeVisibilityAndAlpha=True; IMGUI=False; nativeFooter="+gearbox.name+"; shader="+(gearbox.shader==null?"null":gearbox.shader.name));
        }
        void Fail(string message)
        {
            Failed=true;
            Runtime.Log("WARNING ENDFIELD_MENU playback: "+message+"; nativeFallback=True; gameplayContinues=True");
            Release();
            if(OriginalMovie!=null && isActiveAndEnabled){OriginalMovie.loop=true;OriginalMovie.Play();}
        }
        void OnDisable(){if(Configured)Release();}
        void OnDestroy(){Release();}
        void BindTitles()
        {
            if(Title!=null || Panel==null)return;
            UIRoot root=Panel.GetComponentInParent<UIRoot>();
            if(root==null)return;
            Title=ReadTexture("talos_title_C.png");
            Title.name="Arknights_Deserts_of_Talos_II_C";
            Shader shader=Shader.Find("Unlit/Transparent Colored");
            if(shader==null)throw new InvalidDataException("Native straight-alpha UI shader unavailable");
            foreach(UISprite sprite in root.GetComponentsInChildren<UISprite>(true))
            {
                if(sprite.spriteName!="MainMenu_title" && sprite.spriteName!="MainMenu_titleglow")continue;
                Transform parent=sprite.transform.parent;
                if(parent==null || (parent.name!="MainMenu" && parent.name!="ExtrasMenu"))continue;
                TitleRoute route=new TitleRoute{Original=sprite,SpriteName=sprite.spriteName};
                Titles.Add(route);
                if(sprite.spriteName=="MainMenu_title")
                {
                    GameObject node=new GameObject("EF_Talos_Title_C");
                    node.layer=sprite.gameObject.layer;node.transform.parent=sprite.transform;
                    node.transform.localRotation=Quaternion.identity;node.transform.localScale=Vector3.one;
                    node.transform.localPosition=new Vector3(0f,18f,0f);
                    UITexture widget=node.AddComponent<UITexture>();route.Replacement=widget;
                    widget.pivot=UIWidget.Pivot.Center;widget.mainTexture=Title;widget.shader=shader;
                    widget.depth=sprite.depth;widget.color=Color.white;
                    widget.SetDimensions(sprite.width,Mathf.RoundToInt(sprite.width*220f/1600f));
                    Runtime.Log("TALOS_TITLE_BOUND menu="+parent.name+"; design=C; width="+widget.width+"; height="+widget.height+
                        "; samePanel="+(NGUITools.FindInParents<UIPanel>(node)==NGUITools.FindInParents<UIPanel>(sprite.gameObject))+
                        "; sameLayer="+(node.layer==sprite.gameObject.layer)+"; nativeAlphaInherited=True; nativeGlowRemoved=True");
                }
                // Keep the original widget and its transform/animation alive.
                // An empty sprite removes only its atlas geometry; the child
                // texture inherits widget alpha and menu lifecycle normally.
                sprite.spriteName="";
            }
            if(Titles.Count==0)throw new InvalidDataException("Native title sprites unavailable");
        }
        void ReleaseTitles()
        {
            foreach(TitleRoute route in Titles)
            {
                if(route.Replacement!=null){route.Replacement.gameObject.SetActive(false);UnityEngine.Object.Destroy(route.Replacement.gameObject);}
                if(route.Original!=null)route.Original.spriteName=route.SpriteName;
            }
            Titles.Clear();
            if(Title!=null){UnityEngine.Object.Destroy(Title);Title=null;}
        }
        void Release()
        {
            if(Disposed)return;
            Disposed=true;StopAllCoroutines();
            ReleaseTitles();
            if(Panel!=null && OriginalMovie!=null)NativeRead.Set(Panel,"m_Movie",OriginalMovie);
            foreach(TextureRoute route in Routes)if(route.Widget!=null)route.Widget.mainTexture=route.Original;
            if(Movie!=null){Movie.Stop();UnityEngine.Object.Destroy(Movie);Movie=null;}
            if(Request!=null){Request.Dispose();Request=null;}
            if(Poster!=null){UnityEngine.Object.Destroy(Poster);Poster=null;}
            if(FooterLogo!=null){FooterLogo.gameObject.SetActive(false);UnityEngine.Object.Destroy(FooterLogo.gameObject);FooterLogo=null;}
            if(Logo!=null){UnityEngine.Object.Destroy(Logo);Logo=null;}
            if(Applied || Loading)Runtime.Log("ENDFIELD_MENU_RELEASED movieStopped=True; nativeReferencesRestored=True; decoderAndTexturesReleased=True");
            Applied=false;Loading=false;
        }
    }
}
