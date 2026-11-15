using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace EndfieldDoK.MapUnitLab
{
    // Startup-only, pure System diagnostics. No Unity/Fabric calls, native
    // Steam calls, ticket requests, state changes, retries or frame updates.
    public static class SteamStartupTrace
    {
        public static void Record(bool runningResult,string stage)
        {
            try{
                using(Process process=Process.GetCurrentProcess()){
                    string executable=process.MainModule.FileName;
                    string directory=Path.Combine(Path.GetDirectoryName(executable),"BepInEx");
                    StringBuilder line=new StringBuilder();
                    Add(line,"utc",DateTime.UtcNow.ToString("o"));Add(line,"pid",process.Id.ToString());Add(line,"stage",stage);
                    Add(line,"runningResult",stage=="STEAM_RUNNING_RESULT"?runningResult.ToString():"not-sampled");
                    Add(line,"exe",executable);Add(line,"cwd",Environment.CurrentDirectory);Add(line,"baseDirectory",AppDomain.CurrentDomain.BaseDirectory);
                    foreach(string name in new string[]{"SteamAppId","SteamGameId","SteamClientLaunch","SteamPath","SteamClientDll","SteamClientDll64"})
                        Add(line,name,Environment.GetEnvironmentVariable(name));
                    foreach(string name in new string[]{"SteamUser","SteamPipe","SteamAppUser"})
                        Add(line,name+"Present",(!String.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))).ToString());
                    try{
                        using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess",false)){
                            Add(line,"registeredPid",key==null?"missing":Convert.ToString(key.GetValue("pid","missing")));
                            Add(line,"registeredClientDll64",key==null?"missing":Convert.ToString(key.GetValue("SteamClientDll64","missing")));
                        }
                    }catch(Exception error){Add(line,"registryError",error.GetType().Name);}
                    try{
                        foreach(ProcessModule module in process.Modules){
                            string name=module.ModuleName.ToLowerInvariant();
                            if(name=="steam_api64.dll"||name=="steamclient64.dll"||name=="bbisteamnative.dll")Add(line,"module_"+name,module.FileName);
                        }
                    }catch(Exception error){Add(line,"modulesError",error.GetType().Name);}
                    if(stage!="BEFORE_STEAM_INITIALIZE"){
                        try{
                            Type integration=Type.GetType("BBI.Steam.SteamAPIIntegration, BBI.Steam",false);
                            FieldInfo initialized=integration==null?null:integration.GetField("sInitialized",BindingFlags.Static|BindingFlags.NonPublic);
                            Add(line,"managedInitialized",initialized==null?"unknown":Convert.ToString(initialized.GetValue(null)));
                        }catch(Exception error){Add(line,"managedStateError",error.GetType().Name);}
                    }
                    line.AppendLine();
                    File.AppendAllText(Path.Combine(directory,"MapUnitLab.SteamStartup.log"),line.ToString(),new UTF8Encoding(false));
                }
            }catch{ } // Diagnostics may never affect the original startup path.
        }
        static void Add(StringBuilder line,string name,string value)
        {
            if(line.Length>0)line.Append(" | ");
            line.Append(name).Append('=').Append(value==null?"<unset>":value.Replace("\r"," ").Replace("\n"," ").Replace("|","/"));
        }
    }
}
