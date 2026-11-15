using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace EndfieldDoK.MapUnitLab
{
    // Visibility of original authored parts only; never reskins or alters race
    // geometry, native units, hitpoints or shared material colours.
    internal static class HumanoidRaceLayout
    {
        internal static bool[] Load(string folder,int parts,int members,out string[] names)
        {
            bool[] masks=new bool[parts*members];names=new string[members];
            string path=Path.Combine(folder,"squad_races.ini");
            if(!File.Exists(path))
            {for(int i=0;i<masks.Length;i++)masks[i]=true;for(int i=0;i<members;i++)names[i]="default";return masks;}
            Dictionary<string,string> rows=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string raw in File.ReadAllLines(path))
            {
                string line=raw.Trim();if(line.Length==0||line.StartsWith(";")||line.StartsWith("#")||line.StartsWith("["))continue;
                int split=line.IndexOf('=');if(split<1)throw new InvalidDataException("Invalid squad race row");
                string key=line.Substring(0,split).Trim(),value=line.Substring(split+1).Trim();
                if(rows.ContainsKey(key))throw new InvalidDataException("Duplicate squad race row");rows.Add(key,value);
            }
            Dictionary<string,bool[]> races=new Dictionary<string,bool[]>(StringComparer.Ordinal);
            string roster;
            if(rows.TryGetValue("members",out roster))
            {
                string[] entries=roster.Split(',');if(entries.Length!=members)throw new InvalidDataException("Race roster count differs");
                rows.Remove("members");
                for(int i=0;i<members;i++){string key="member"+i;if(rows.ContainsKey(key))throw new InvalidDataException("Mixed race roster formats");rows.Add(key,entries[i].Trim());}
            }
            foreach(KeyValuePair<string,string> row in rows)
            {
                if(row.Key.StartsWith("member",StringComparison.Ordinal))continue;
                bool[] mask=new bool[parts];int count=0;
                foreach(string text in row.Value.Split(','))
                {int index=int.Parse(text.Trim(),CultureInfo.InvariantCulture);if(index<0||index>=parts||mask[index])throw new InvalidDataException("Invalid/duplicate race part");mask[index]=true;count++;}
                if(count==0)throw new InvalidDataException("Empty race parts");
                string name=row.Key.EndsWith("VisibleParts",StringComparison.Ordinal)?row.Key.Substring(0,row.Key.Length-12):row.Key;
                if(races.ContainsKey(name))throw new InvalidDataException("Duplicate race visibility");races.Add(name,mask);
            }
            for(int member=0;member<members;member++)
            {
                string race;bool[] mask;if(!rows.TryGetValue("member"+member,out race)||!races.TryGetValue(race,out mask))throw new InvalidDataException("Missing member race");
                names[member]=race;Array.Copy(mask,0,masks,member*parts,parts);
            }
            foreach(string key in rows.Keys)if(key.StartsWith("member",StringComparison.Ordinal))
            {int member;if(!int.TryParse(key.Substring(6),out member)||member<0||member>=members)throw new InvalidDataException("Unexpected race member");}
            return masks;
        }
    }
}
