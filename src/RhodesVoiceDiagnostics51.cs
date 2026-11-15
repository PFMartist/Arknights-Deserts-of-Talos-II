using System;

namespace EndfieldDoK.MapUnitLab
{
    // Presentation-only bounded observation. It neither selects clips nor
    // posts VO events, and deliberately cannot claim that a loaded clip played.
    internal sealed class RhodesVoiceDiagnostics51
    {
        internal const int MaximumChatterReportsPerAction=4;
        internal const int MaximumRoleLoadReports=12;
        internal const int MaximumReportsPerLoader=36;
        readonly int[] ChatterReports=new int[3];
        readonly int[] RoleReports=new int[2];

        internal string ChatterRequest(string action,string speaker,string unitCode,bool direct,bool scopeMatched)
        {
            int slot=action=="cid"?0:action=="cmo"?1:action=="cuf"?2:-1;
            if(slot<0||ChatterReports[slot]>=MaximumChatterReportsPerAction)return null;
            ChatterReports[slot]++;
            return "RHODES_VOICE_CHATTER_REQUEST action="+action+"; speaker="+Token(speaker)+
                "; unitCode="+Token(unitCode)+"; direct="+direct+"; scopeMatched="+scopeMatched+
                "; fabricLeafSelectionPending=True; playbackConfirmed=False; perActionCap="+MaximumChatterReportsPerAction+
                "; totalDiagnosticCapPerLoader="+MaximumReportsPerLoader;
        }

        internal static string RoleForClipKey(string key)
        {
            if(key!=null&&key.StartsWith("vo_coalition_engineering_",StringComparison.OrdinalIgnoreCase))return "Engineering";
            if(key!=null&&key.StartsWith("vo_coalition_sensors_",StringComparison.OrdinalIgnoreCase))return "Sensors";
            return null;
        }

        internal string RoleLoad(string key,bool scopeMatched,bool catalogReady,bool routeMatched,
            bool modClipReady,bool cacheHit,bool nativeFallbackRequested,string errorType)
        {
            string role=RoleForClipKey(key);
            int slot=role=="Engineering"?0:role=="Sensors"?1:-1;
            if(slot<0||RoleReports[slot]>=MaximumRoleLoadReports)return null;
            RoleReports[slot]++;
            // A caller may report a failed/mismatched attempt, but no inconsistent
            // set of flags can turn it into a successful custom clip load.
            bool ready=scopeMatched&&catalogReady&&routeMatched&&modClipReady&&!nativeFallbackRequested&&errorType==null;
            return "RHODES_VOICE_ROLE_LOADER role="+role+"; key="+Token(key)+
                "; nativeClipRequest=True; scopeMatched="+scopeMatched+"; catalogReady="+catalogReady+
                "; routeMatched="+routeMatched+"; modClipReady="+ready+"; cacheHit="+(ready&&cacheHit)+
                "; nativeFallbackRequested="+nativeFallbackRequested+"; error="+Token(errorType)+
                "; playbackConfirmed=False; nativePlaybackNotificationObserved=False; perRoleCap="+MaximumRoleLoadReports+
                "; totalDiagnosticCapPerLoader="+MaximumReportsPerLoader;
        }

        static string Token(string value)
        {
            if(string.IsNullOrEmpty(value))return "none";
            value=value.Replace(';','_').Replace('\r','_').Replace('\n','_');
            return value.Length<=160?value:value.Substring(0,160);
        }
    }
}
