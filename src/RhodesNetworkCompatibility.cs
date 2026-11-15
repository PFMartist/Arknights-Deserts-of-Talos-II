using System;
using System.Collections.Generic;
using BBI.Core.Network;
using BBI.Unity.Game.UI;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Native lobby build filtering rejects vanilla/older-protocol peers before
    // private faction indices are propagated. The per-player digest additionally
    // checks identical installed code, simulation inputs and authored footprints.
    public static class RhodesNetworkCompatibility
    {
        public const string Protocol="RI18NET1";
        internal const string PeerKey="rhodes18fingerprint";
        public static string DecorateLobbyBuildId(string original)
        {return Runtime.SkirmishMode?original+"-"+Protocol:original;}
        internal static void PanelInitialized(object value)
        {
            MultiplayerMissionPanel panel=value as MultiplayerMissionPanel;if(panel==null||!Runtime.SkirmishMode)return;
            try{Ensure(panel);}catch(Exception error){Runtime.Log("WARNING RHODES_NETWORK_COMPATIBILITY "+error.Message+"; multiplayerRhodesUnavailable=True");}
        }
        static RhodesNetworkRoomDriver Ensure(MultiplayerMissionPanel panel)
        {
            RhodesNetworkRoomDriver driver=panel.GetComponent<RhodesNetworkRoomDriver>();
            if(driver==null)driver=panel.gameObject.AddComponent<RhodesNetworkRoomDriver>();
            driver.Configure(panel);return driver;
        }
        public static bool AllowRhodesSelection(object value)
        {
            MultiplayerMissionPanel panel=value as MultiplayerMissionPanel;if(panel==null)return true;
            try{string reason;return Ensure(panel).Check(out reason);}catch{return false;}
        }
        public static bool AllowStart(object value)
        {return AllowRhodesSelection(value);}
        public static void RequireCompatibleRoom(object value)
        {
            MultiplayerMissionPanel panel=value as MultiplayerMissionPanel;if(panel==null)return;
            string reason;if(!Ensure(panel).Check(out reason))throw new InvalidOperationException(reason);
        }
    }

    public sealed class RhodesNetworkRoomDriver:MonoBehaviour
    {
        MultiplayerMissionPanel Panel;
        ILobby Lobby;
        IPlayerGroupMetaData Metadata;
        string Published,Reason;
        UILabel Label;
        float NextUpdate;
        readonly List<string> PeerDigests=new List<string>();
        internal void Configure(MultiplayerMissionPanel panel)
        {if(Panel!=null)return;Panel=panel;RhodesNetworkFingerprint.Begin();}
        internal bool Check(out string reason)
        {
            bool ready;
            try{ready=CheckCore(out reason);}
            catch(KeyNotFoundException){
                // Native lobby membership can precede the per-player metadata
                // entry. It is a pending update, not a permanent driver failure.
                Published=null;reason="等待玩家房间数据同步…";ready=false;
            }
            RhodesSkirmishFaction.RefreshNetworkChoices(Panel,ready);
            return ready;
        }
        bool CheckCore(out string reason)
        {
            if(Panel==null){reason="多人房间尚未准备好";return false;}
            ILobby current=NativeRead.Get(Panel,"mLobby") as ILobby;
            if(!object.ReferenceEquals(current,Lobby)){Lobby=current;Metadata=current as IPlayerGroupMetaData;Published=null;}
            if(Lobby==null||Metadata==null||!Lobby.IsActive){reason="等待多人房间建立…";return false;}
            string digest,error;
            if(!RhodesNetworkFingerprint.Read(out digest,out error)){reason="正在核对模组文件…";return false;}
            if(error!=null){reason=error;return false;}
            if(Published!=digest){Metadata.SetLocalPlayerStringData(RhodesNetworkCompatibility.PeerKey,digest);Published=digest;}
            PeerDigests.Clear();
            foreach(NetworkPlayerID player in Lobby.PlayerIDs)PeerDigests.Add(Metadata.GetPlayerStringData(player,RhodesNetworkCompatibility.PeerKey,string.Empty));
            return RhodesNetworkPeerPolicy.Compatible(digest,PeerDigests,out reason);
        }
        void LateUpdate()
        {
            if(Panel==null||!Panel.gameObject.activeInHierarchy||Time.unscaledTime<NextUpdate)return;
            NextUpdate=Time.unscaledTime+.25f;
            try{
                string reason;bool ready=Check(out reason);
                if(Label==null)MakeLabel();
                if(Label!=null){if(Reason!=reason){Label.text=reason;Reason=reason;}Label.color=ready?new Color(.8f,.95f,.75f):new Color(1f,.88f,.28f);}
            }catch(Exception e){
                string reason="房间校验尚未完成："+e.Message;
                if(Reason!=reason){Runtime.Log("WARNING RHODES_NETWORK_ROOM "+e.Message+"; startBlocked=True; willRetry=True");Reason=reason;}
                if(Label!=null){Label.text=reason;Label.color=new Color(1f,.88f,.28f);}
                NextUpdate=Time.unscaledTime+1f;
            }
        }
        void MakeLabel()
        {
            Component button=NativeRead.Get(Panel,"m_StartCountdownButton") as Component;
            if(button==null)return;
            UILabel source=NativeRead.Get(Panel,"m_GameStartCountdownValue") as UILabel;
            if(source==null||(source.bitmapFont==null&&source.trueTypeFont==null)){
                foreach(UILabel candidate in Panel.GetComponentsInChildren<UILabel>(true))if(candidate!=null&&(candidate.bitmapFont!=null||candidate.trueTypeFont!=null)){source=candidate;break;}
            }
            if(source==null)return;
            GameObject root=new GameObject("RI_NetworkCompatibility");root.layer=button.gameObject.layer;
            root.transform.parent=button.transform.parent;root.transform.localScale=Vector3.one;root.transform.localRotation=Quaternion.identity;
            root.transform.localPosition=button.transform.localPosition+new Vector3(0f,48f,0f);
            Label=root.AddComponent<UILabel>();if(source.bitmapFont!=null)Label.bitmapFont=source.bitmapFont;else Label.trueTypeFont=source.trueTypeFont;
            Label.fontSize=14;Label.width=640;Label.height=40;Label.alignment=NGUIText.Alignment.Center;Label.pivot=UIWidget.Pivot.Bottom;Label.depth=source.depth+10;
            Label.overflowMethod=UILabel.Overflow.ShrinkContent;Label.supportEncoding=false;
        }
        void OnDisable(){if(Label!=null)Destroy(Label.gameObject);Label=null;Reason=null;}
        void OnDestroy(){OnDisable();Panel=null;Lobby=null;Metadata=null;PeerDigests.Clear();}
    }
}
