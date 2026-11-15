using System;
using System.Collections.Generic;
using System.IO;

namespace EndfieldDoK.MapUnitLab
{
    internal sealed class RhodesVoiceRoute
    {
        internal string Key, Clip, UnitType, EventKey, Role, Job, Sha256, CustomKey;
        internal bool Enabled;
    }

    internal sealed class RhodesVoiceCatalog
    {
        sealed class UnitVoicePool
        {
            internal readonly List<RhodesVoiceRoute> Choices=new List<RhodesVoiceRoute>();
            internal readonly HashSet<string> Clips=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int Previous=-1;

            internal RhodesVoiceRoute Choose(Random random)
            {
                int count=Choices.Count;
                if(count==1)return Choices[0];
                int next=random.Next(Previous<0?count:count-1);
                if(Previous>=0&&next>=Previous)next++;
                Previous=next;return Choices[next];
            }
        }

        internal const string Header="key\tclip\texactUnitType\teventKey\trole\tjob\tenabled\tsha256\tcustomEventKey";
        readonly Dictionary<string,RhodesVoiceRoute> Routes=new Dictionary<string,RhodesVoiceRoute>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,Dictionary<string,UnitVoicePool>> UnitEvents=new Dictionary<string,Dictionary<string,UnitVoicePool>>(StringComparer.Ordinal);
        readonly Dictionary<string,Dictionary<string,UnitVoicePool>> CarrierForms=new Dictionary<string,Dictionary<string,UnitVoicePool>>(StringComparer.Ordinal);
        // Presentation-only randomness: never consumes Unity or simulation RNG.
        readonly Random SelectionRandom=new Random();
        internal readonly List<RhodesVoiceRoute> PrivateRoutes=new List<RhodesVoiceRoute>();
        internal int Count {get{return Routes.Count;}}

        internal static RhodesVoiceCatalog Parse(string[] lines)
        {
            if(lines==null||lines.Length<2||lines.Length>2001||lines[0]!=Header)
                throw new InvalidDataException("Unexpected Rhodes voice route table");
            RhodesVoiceCatalog catalog=new RhodesVoiceCatalog();
            HashSet<string> privateKeys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int index=1;index<lines.Length;index++)
            {
                string[] fields=lines[index].Split('\t');
                if(fields.Length!=9||string.IsNullOrEmpty(fields[0])||string.IsNullOrEmpty(fields[3])||
                    fields[6]!="0"&&fields[6]!="1"||fields[7].Length!=64||catalog.Routes.ContainsKey(fields[0]))
                    throw new InvalidDataException("Invalid or duplicate Rhodes voice row "+index);
                if(fields[4]!="Carrier"&&fields[4]!="OpsFleet"&&fields[4]!="OpsCombat"&&fields[4]!="Engineering"&&fields[4]!="Sensors")
                    throw new InvalidDataException("Unknown Rhodes voice role");
                ValidateRelativeClip(fields[1]);
                RhodesVoiceRoute route=new RhodesVoiceRoute{Key=fields[0],Clip=fields[1],UnitType=fields[2],EventKey=fields[3],
                    Role=fields[4],Job=fields[5],Enabled=fields[6]=="1",Sha256=fields[7],CustomKey=fields[8]};
                catalog.Routes.Add(route.Key,route);
                if(route.Enabled&&(!string.IsNullOrEmpty(route.CustomKey)||!string.IsNullOrEmpty(route.UnitType))&&privateKeys.Add(route.EventKey))
                    catalog.PrivateRoutes.Add(route);
            }
            foreach(RhodesVoiceRoute route in catalog.PrivateRoutes)
            {catalog.AddUnitRoute(route);catalog.AddCarrierFormRoute(route);}
            return catalog;
        }

        void AddCarrierFormRoute(RhodesVoiceRoute route)
        {
            if(route.UnitType!=RhodesCarrierUnits.UnitKey||route.Role!="Carrier"||
                !route.EventKey.StartsWith(RhodesCarrierFormVoice.Prefix,StringComparison.Ordinal))return;
            string[] parts=route.EventKey.Substring(RhodesCarrierFormVoice.Prefix.Length).Split('.');
            int ordinal;
            if(parts.Length!=3||(parts[0]!="base"&&parts[0]!="aviation"&&parts[0]!="gunnery")||
                !RhodesCarrierFormVoice.ActionSupported(parts[1])||parts[2].Length!=2||
                !int.TryParse(parts[2],out ordinal)||ordinal<1||ordinal>(parts[1]=="atm"?3:4))return;
            Dictionary<string,UnitVoicePool> actions;
            if(!CarrierForms.TryGetValue(parts[0],out actions))
            {actions=new Dictionary<string,UnitVoicePool>(StringComparer.Ordinal);CarrierForms.Add(parts[0],actions);}
            UnitVoicePool pool;
            if(!actions.TryGetValue(parts[1],out pool)){pool=new UnitVoicePool();actions.Add(parts[1],pool);}
            if(pool.Clips.Add(route.Clip))pool.Choices.Add(route);
        }

        void AddUnitRoute(RhodesVoiceRoute route)
        {
            const string prefix="rhodes.opscombat.vo_coalition_opscombat_";
            if(string.IsNullOrEmpty(route.UnitType)||route.Role!="OpsCombat"||
                !route.EventKey.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return;
            int end=route.EventKey.IndexOf('_',prefix.Length);
            if(end<=prefix.Length)return;
            string action=route.EventKey.Substring(prefix.Length,end-prefix.Length);
            Dictionary<string,UnitVoicePool> actions;
            if(!UnitEvents.TryGetValue(route.UnitType,out actions))
            {actions=new Dictionary<string,UnitVoicePool>(StringComparer.OrdinalIgnoreCase);UnitEvents.Add(route.UnitType,actions);}
            UnitVoicePool pool;
            if(!actions.TryGetValue(action,out pool)){pool=new UnitVoicePool();actions.Add(action,pool);}
            // Multiple table aliases and event keys for one recording must not
            // give that recording extra selection weight.
            if(pool.Clips.Add(route.Clip))pool.Choices.Add(route);
        }

        internal static void ValidateRelativeClip(string relative)
        {
            if(string.IsNullOrEmpty(relative)||!relative.StartsWith("clips/",StringComparison.Ordinal)||
                !relative.EndsWith(".wav",StringComparison.Ordinal)||relative.IndexOf('\\')>=0||relative.IndexOf(':')>=0||Path.IsPathRooted(relative))
                throw new InvalidDataException("Invalid Rhodes voice clip path");
            foreach(string part in relative.Split('/'))
                if(part.Length==0||part=="."||part=="..")throw new InvalidDataException("Invalid Rhodes voice path segment");
        }

        internal static string ClipKey(string path)
        {
            if(string.IsNullOrEmpty(path))return string.Empty;
            string normalized=path.Replace('\\','/');
            int slash=normalized.LastIndexOf('/');
            string key=normalized.Substring(slash+1);
            if(key.EndsWith(".wav",StringComparison.OrdinalIgnoreCase)||key.EndsWith(".ogg",StringComparison.OrdinalIgnoreCase))
                key=key.Substring(0,key.Length-4);
            return key;
        }

        internal bool TryGet(string key,out RhodesVoiceRoute route)
        {return Routes.TryGetValue(key,out route)&&route.Enabled;}

        internal bool TryLoadRoute(string key,out RhodesVoiceRoute route)
        {
            return TryGet(key,out route)&&(route.UnitType.Length==0||
                key.StartsWith("rhodes.",StringComparison.OrdinalIgnoreCase));
        }

        internal RhodesVoiceRoute UnitEvent(string unitType,string action)
        {
            if(string.IsNullOrEmpty(unitType)||string.IsNullOrEmpty(action))return null;
            Dictionary<string,UnitVoicePool> actions;UnitVoicePool pool;
            return UnitEvents.TryGetValue(unitType,out actions)&&actions.TryGetValue(action,out pool)?pool.Choose(SelectionRandom):null;
        }

        internal RhodesVoiceRoute CarrierFormEvent(string form,string action)
        {
            if(string.IsNullOrEmpty(form)||!RhodesCarrierFormVoice.ActionSupported(action))return null;
            Dictionary<string,UnitVoicePool> actions;UnitVoicePool pool;
            return CarrierForms.TryGetValue(form,out actions)&&actions.TryGetValue(action,out pool)?pool.Choose(SelectionRandom):null;
        }
    }
}
