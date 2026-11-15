using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace EndfieldDoK.MapUnitLab
{
    // Optional per-actor source release markers; no weapon/HP/stat changes.
    internal sealed class HumanoidComboSettings
    {
        public readonly float[] Releases;
        public readonly float ResetGapSeconds,NativeWindupSeconds;
        public readonly bool ReleaseKnown;
        HumanoidComboSettings(float[] releases,float gap,float windup,bool known)
        {Releases=releases;ResetGapSeconds=gap;NativeWindupSeconds=windup;ReleaseKnown=known;}
        public static HumanoidComboSettings Load(string folder,HumanoidData data)
        {
            string path=Path.Combine(folder,"humanoid_combo.ini");if(!File.Exists(path))return null;
            FileInfo file=new FileInfo(path);
            if(file.Length>4096||(file.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Invalid humanoid combo configuration file");
            Dictionary<string,string> fields=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string raw in File.ReadAllLines(path))
            {
                string line=raw.Trim();if(line.Length==0||line.StartsWith("#")||line.StartsWith(";")||line.StartsWith("["))continue;
                int split=line.IndexOf('=');if(split<1)throw new InvalidDataException("Malformed humanoid combo setting");
                string key=line.Substring(0,split).Trim(),value=line.Substring(split+1).Trim();
                if(fields.ContainsKey(key))throw new InvalidDataException("Duplicate humanoid combo setting: "+key);
                fields.Add(key,value);
            }
            string valueText;float gap=8f,windup=.25f;
            if(fields.TryGetValue("resetGapSeconds",out valueText)){gap=Number(valueText);fields.Remove("resetGapSeconds");}
            if(fields.TryGetValue("nativeWindupSeconds",out valueText)){windup=Number(valueText);fields.Remove("nativeWindupSeconds");}
            bool known=true;if(fields.TryGetValue("releaseKnown",out valueText)){known=bool.Parse(valueText);fields.Remove("releaseKnown");}
            fields.Remove("source");
            if(gap<=0f||gap>60f||windup<=0f||windup>5f||data.AttackVariants.Length<2)throw new InvalidDataException("Humanoid combo requires valid timing and at least two clips");
            float[] releases=new float[data.AttackVariants.Length];
            for(int i=0;i<releases.Length;i++)
            {
                HumanoidClip clip=data.AttackVariants[i];
                if(!fields.TryGetValue(clip.Role,out valueText))throw new InvalidDataException("Missing combo release marker: "+clip.Role);
                float release=Number(valueText);if(release<0f||release>=clip.Duration)throw new InvalidDataException("Invalid release marker for "+clip.Role);
                releases[i]=release;fields.Remove(clip.Role);
            }
            if(fields.Count!=0)throw new InvalidDataException("Unknown humanoid combo setting");
            return new HumanoidComboSettings(releases,gap,windup,known);
        }
        static float Number(string text)
        {
            float value=float.Parse(text,CultureInfo.InvariantCulture);
            if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("Non-finite humanoid combo time");return value;
        }
    }
}
