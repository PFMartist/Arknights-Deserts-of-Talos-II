using System;
using System.Globalization;
using System.IO;

namespace EndfieldDoK.MapUnitLab
{
    internal static class HumanoidActionSettings
    {
        internal static HumanoidAnimationProfile Load(string folder, HumanoidData data)
        {
            HumanoidAnimationProfile p = new HumanoidAnimationProfile();
            p.IdleDuration = data.Idle.Duration; p.WalkDuration = data.Walk.Duration;
            p.HasRun = data.Run != null; p.HasSprint=data.Sprint!=null;if(p.HasSprint)p.SprintDuration=data.Sprint.Duration; p.HasAttack = data.Attack != null;
            if (p.HasRun) p.RunDuration = data.Run.Duration;
            if (p.HasAttack) p.AttackDuration = data.Attack.Duration;
            string path = Path.Combine(folder, "humanoid_actions.ini");
            if (File.Exists(path)) foreach (string source in File.ReadAllLines(path))
            {
                string line = source.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
                int split = line.IndexOf('=');
                if (split <= 0) throw new InvalidDataException("Malformed humanoid action setting");
                string key = line.Substring(0, split).Trim(), value = line.Substring(split + 1).Trim();
                if (key == "walkReferenceSpeed") p.WalkReferenceSpeed = Number(value);
                else if (key == "runReferenceSpeed") p.RunReferenceSpeed = Number(value);
                else if(key=="sprintReferenceSpeed")p.SprintReferenceSpeed=Number(value);
                else if(key=="sprintEnterSpeed")p.SprintEnterSpeed=Number(value);
                else if(key=="sprintExitSpeed")p.SprintExitSpeed=Number(value);
                else if (key == "runEnterSpeed") p.RunEnterSpeed = Number(value);
                else if (key == "runExitSpeed") p.RunExitSpeed = Number(value);
                else if (key == "minPlaybackRate") p.MinimumPlaybackRate = Number(value);
                else if (key == "maxPlaybackRate") p.MaximumPlaybackRate = Number(value);
                else if (key == "attackReleaseTime") p.AttackReleaseTime = Number(value);
                else if (key == "attackReleaseTimeKnown") p.AttackReleaseTimeKnown = bool.Parse(value);
                else if (key == "source" || key == "referenceSpeedSource" || key == "attackTimingSource") { }
                else throw new InvalidDataException("Unknown humanoid action setting: " + key);
            }
            p.Validate(); return p;
        }
        static float Number(string text)
        {
            float value = float.Parse(text, CultureInfo.InvariantCulture);
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Nonfinite humanoid action setting");
            return value;
        }
    }
}
