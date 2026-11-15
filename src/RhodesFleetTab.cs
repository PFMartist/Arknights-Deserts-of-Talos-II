using System;
using System.IO;
using BBI.Game.Data;
using BBI.Unity.Game.UI;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // A presentation-only fifth page. The native group deliberately retains
    // its four valid FactionID values and two-slot customization/skin storage.
    // It uses the same NGUI radio group for visual exclusion, never adds a
    // fabricated FactionID or out-of-range CustomizationFactionSetting.
    public sealed class RhodesFleetTab:IDisposable
    {
        readonly PlayerCustomizationPanel Panel;
        readonly Action<bool> PageChanged;
        readonly RhodesDefaultColours DefaultColours;
        readonly UIToggle[] Originals=new UIToggle[4];
        readonly Vector3[] Positions=new Vector3[4],Scales=new Vector3[4];
        ToggleGroup NativeGroup;
        GameObject Root;
        UIToggle Toggle;
        Texture2D Logo;
        bool Disposed,Changing,Active;
        bool Listening;
        public bool Selected{get{return !Disposed&&Active&&Toggle!=null&&Toggle.value;}}

        RhodesFleetTab(PlayerCustomizationPanel panel,Action<bool> pageChanged)
        {Panel=panel;PageChanged=pageChanged;DefaultColours=new RhodesDefaultColours(panel);}

        public static RhodesFleetTab TryCreate(PlayerCustomizationPanel panel,Action<bool> pageChanged)
        {
            RhodesFleetTab tab=new RhodesFleetTab(panel,pageChanged);
            try{if(tab.Build())return tab;tab.Dispose();return null;}
            catch{tab.Dispose();throw;}
        }

        bool Build()
        {
            // ToggleGroup is a serializable plain object, not a Component.
            NativeGroup=NativeRead.Get(Panel,"m_FactionToggleGroup") as ToggleGroup;
            if(NativeGroup==null)return false;
            Array containers=NativeRead.Get(NativeGroup,"m_Toggles") as Array;
            if(containers==null||containers.Length!=4)return false;
            Transform parent=null;
            for(int i=0;i<4;i++)
            {
                UIToggle original=NativeRead.Get(containers.GetValue(i),"Toggle") as UIToggle;
                if(original==null||original.transform.parent==null)return false;
                if(parent==null)parent=original.transform.parent;else if(parent!=original.transform.parent)return false;
                Originals[i]=original;Positions[i]=original.transform.localPosition;Scales[i]=original.transform.localScale;
            }
            float distance=Positions[1].x-Positions[0].x;
            if(distance<=0f||Math.Abs((Positions[3].x-Positions[0].x)/3f-distance)>.1f)return false;
            string file=Path.Combine(Path.Combine(CustomUnits.AssetDirectory,"ui_endfield"),"rhodes_faction_white.png");
            FileInfo metadata=new FileInfo(file);
            if(!metadata.Exists||metadata.Length<24||metadata.Length>4194304)throw new InvalidDataException("Rhodes fleet-tab logo missing or outside image budget");
            Logo=new Texture2D(2,2,TextureFormat.RGBA32,false);Logo.name="EF_Rhodes_FleetTab_Logo";
            Logo.hideFlags=HideFlags.HideAndDontSave;
            if(!Logo.LoadImage(File.ReadAllBytes(file))||Logo.width<1||Logo.height<1||Logo.width>2048||Logo.height>2048)
                throw new InvalidDataException("Invalid Rhodes fleet-tab logo dimensions");
            Logo.wrapMode=TextureWrapMode.Clamp;Logo.filterMode=FilterMode.Bilinear;Logo.Apply(false,true);

            Root=(GameObject)UnityEngine.Object.Instantiate(Originals[0].gameObject);
            Root.name="EF_Rhodes_FleetCustomization_Tab";Root.SetActive(false);
            Root.transform.parent=parent;Root.transform.localRotation=Originals[0].transform.localRotation;
            Toggle=Root.GetComponent<UIToggle>();
            if(Toggle==null)throw new InvalidDataException("Native faction-tab clone lacks its toggle");
            // Clone-owned callbacks are discarded. Native original delegates
            // are never cleared or reinitialized.
            Toggle.onChange.Clear();Toggle.group=Originals[0].group;
            NativeRead.Set(Toggle,"startsActive",false);Toggle.Set(false);
            foreach(Behaviour behaviour in Root.GetComponentsInChildren<Behaviour>(true))
                if(behaviour.GetType().Name=="UILocalize")behaviour.enabled=false;
            foreach(UILabel label in Root.GetComponentsInChildren<UILabel>(true))label.text="RHODES ISLAND";
            UISprite normal=null,selected=null;
            foreach(UISprite sprite in Root.GetComponentsInChildren<UISprite>(true))
            {
                if(sprite.gameObject.name=="Sprite-Btn-Background")normal=sprite;
                else if(sprite.gameObject.name=="Sprite-Btn-IconCheck")selected=sprite;
            }
            if(normal==null||selected==null)throw new InvalidDataException("Native faction-tab icon hierarchy changed");
            AddLogo(normal,new Color(.50f,.50f,.50f,1f));AddLogo(selected,Color.white);
            EventDelegate.Add(Toggle.onChange,OnChanged);
            NativeRead.Set(NativeGroup,"Changed",(Action<ToggleGroup>)Delegate.Combine(
                NativeRead.Get(NativeGroup,"Changed") as Delegate,new Action<ToggleGroup>(OnNativeChanged)));
            Listening=true;
            float step=distance*.8f,centre=(Positions[0].x+Positions[3].x)*.5f;
            for(int i=0;i<4;i++)
            {
                Originals[i].transform.localScale=Scales[i]*.8f;
                Originals[i].transform.localPosition=new Vector3(centre+(i-2)*step,Positions[i].y,Positions[i].z);
            }
            Root.transform.localScale=Scales[0]*.8f;
            Root.transform.localPosition=new Vector3(centre+2*step,Positions[0].y,Positions[0].z);
            Root.SetActive(true);
            Runtime.Log("RHODES_FLEET_TAB_READY nativeTabs=4; independentPage=5; nativeFactionStorageUnchanged=True; visualRadioGroup="+Toggle.group+"; nativeColourDefaultAccept=True; visualAcceptancePending=True");
            return true;
        }

        void AddLogo(UISprite original,Color colour)
        {
            // Retain the native checked sprite as an alpha/selection parent;
            // its native edge child and UIToggle fade remain authoritative.
            GameObject child=new GameObject("EF_Rhodes_FactionLogo");child.layer=original.gameObject.layer;
            child.transform.parent=original.transform;child.transform.localPosition=Vector3.zero;
            child.transform.localRotation=Quaternion.identity;child.transform.localScale=Vector3.one;
            UITexture image=child.AddComponent<UITexture>();image.mainTexture=Logo;image.color=colour;
            image.width=130;image.height=Math.Max(1,(130*Logo.height)/Logo.width);image.depth=original.depth+1;
            // Keep the native selection/alpha widget enabled. An empty sprite
            // suppresses only its old faction artwork; descendants and the
            // native checked-frame fade continue through the original widget.
            original.spriteName=string.Empty;
        }

        public void Select(){if(!Disposed&&Toggle!=null)Toggle.value=true;}

        void OnChanged()
        {
            if(Disposed||Changing||Toggle==null)return;
            bool selected=Toggle.value;
            if(selected==Active)return;
            Active=selected;
            if(selected)
            {
                Changing=true;
                try
                {
                    DefaultColours.Apply();
                    // A valid no-DLC native page is the persistence proxy.
                    // OnClickAccept can safely keep its existing [0..1] array
                    // access, shared colour storage, SaveToFile and event chain.
                    NativeRead.Set(Panel,"mSelectedFactionID",FactionID.Coalition);
                    NativeRead.Call(Panel,"SetUIPreviewFaction",FactionID.Coalition);
                }
                catch
                {
                    DefaultColours.Restore();Active=false;Toggle.Set(false);throw;
                }
                finally{Changing=false;}
            }
            else DefaultColours.Restore();
            if(PageChanged!=null)PageChanged(selected);
        }

        void OnNativeChanged(ToggleGroup group)
        {
            if(Disposed||Changing||!Active||Toggle==null)return;
            DefaultColours.Restore();
            // Native initialization installs its callback before this listener.
            // Its four original faction/skin switches execute normally first.
            Changing=true;try{Toggle.Set(false);Active=false;}finally{Changing=false;}
            if(PageChanged!=null)PageChanged(false);
        }

        public void Dispose()
        {
            if(Disposed)return;Disposed=true;Active=false;
            DefaultColours.Dispose();
            if(Listening&&NativeGroup!=null)NativeRead.Set(NativeGroup,"Changed",(Action<ToggleGroup>)Delegate.Remove(
                NativeRead.Get(NativeGroup,"Changed") as Delegate,new Action<ToggleGroup>(OnNativeChanged)));
            Listening=false;
            if(Toggle!=null)Toggle.onChange.Clear();
            for(int i=0;i<4;i++)if(Originals[i]!=null)
            {Originals[i].transform.localPosition=Positions[i];Originals[i].transform.localScale=Scales[i];}
            if(Root!=null){Root.SetActive(false);UnityEngine.Object.Destroy(Root);}Root=null;Toggle=null;
            if(Logo!=null)UnityEngine.Object.Destroy(Logo);Logo=null;NativeGroup=null;
        }
    }
}
