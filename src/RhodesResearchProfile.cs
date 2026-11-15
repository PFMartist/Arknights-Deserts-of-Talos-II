using System;
using System.IO;

namespace EndfieldDoK.MapUnitLab
{
    // Captured once per process. Registered research and lobby fingerprints
    // use the same setting across registry replacements and subsequent matches.
    public static class RhodesResearchProfile
    {
        public const float DemoResearchSeconds=10f;
        public const string DemoFlagName="RhodesMod.research_demo";
        static readonly bool Demo=ReadDemoFlag();

        static bool ReadDemoFlag()
        {
            string core=Path.GetDirectoryName(typeof(RhodesResearchProfile).Assembly.Location);
            if(string.IsNullOrEmpty(core))throw new InvalidOperationException("Rhodes runtime assembly location is unavailable");
            string bepinex=Path.GetDirectoryName(core);
            if(string.IsNullOrEmpty(bepinex))throw new InvalidOperationException("Rhodes runtime parent directory is unavailable");
            return File.Exists(Path.Combine(bepinex,DemoFlagName));
        }
        public static bool DemoEnabled {get{return Demo;}}
        public static string Name {get{return Demo?"demo10":"formal";}}
        public static string DisplayName {get{return Demo?"10秒演示":"正式科研";}}
        public static float Duration(float formalSeconds)
        {return Demo?DemoResearchSeconds:formalSeconds;}
    }
}
