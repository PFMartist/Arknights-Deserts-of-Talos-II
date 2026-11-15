using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EndfieldDoK.MapUnitLab
{
    internal static class RhodesNetworkFingerprint
    {
        sealed class Entry{internal string Path,Hash;}
        static readonly string[] RequiredModFiles={
            ".doorstop_version","doorstop_config.ini","winhttp.dll",
            "bepinex/core/bepinex.dll","bepinex/core/bepinex.preloader.dll","bepinex/core/mono.cecil.dll",
            "bepinex/core/endfielddok.mapunitlab.dll","bepinex/patchers/endfielddok.mapunitlab.patcher.dll","bepinex/rhodesmod.skirmish"
        };
        // The same five supported native binaries already pinned by the
        // installer. Read/hash only: no Steam, EOS or authorization calls.
        static readonly Entry[] NativeFiles={
            new Entry{Path="desertsofkharak64.exe",Hash="44085bc0c6d4cd59e9ce681160784d7bd024cd1c85e43edc9ab68b3c3d925e0c"},
            new Entry{Path="data/managed/bbi.game.dll",Hash="1216502eff7958a99fe305b439f81709bf340610c782ce449929d13b36ae45cc"},
            new Entry{Path="data/managed/bbi.unity.game.dll",Hash="b4c2c7bdbf9737140ac18d6191e50ddd27030a669f050374e8f7e3da8c6ec0e5"},
            new Entry{Path="data/managed/bbi.game.data.dll",Hash="24a3fbf19e5435ec24295554e12f0ad2451d722623daaccc03bea280b33e9d40"},
            new Entry{Path="data/managed/bbi.unity.game.data.dll",Hash="f3a60df1ab8377492451181a957d1f1df7e3d47265bc0221e1dee83107e14c40"}
        };
        static readonly object Gate=new object();
        static bool Started,Complete;
        static string Digest,Error;
        internal static bool Read(out string digest,out string error)
        {
            lock(Gate){digest=Digest;error=Error;return Complete;}
        }
        internal static void Begin()
        {
            lock(Gate){if(Started)return;Started=true;}
            try{
                string root=Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                if(!ThreadPool.QueueUserWorkItem(ComputeWorker,root))throw new InvalidOperationException("Cannot schedule mod verification");
            }catch(Exception e){lock(Gate){Error=e.Message;Complete=true;}}
        }
        static void ComputeWorker(object value)
        {
            string digest=null,error=null;
            try{digest=Compute((string)value);}catch(Exception e){error=e.Message;}
            lock(Gate){Digest=digest;Error=error;Complete=true;}
        }
        // Pure System/streaming IO, deliberately kept off the Unity thread.
        internal static string Compute(string directory)
        {
            if(string.IsNullOrEmpty(directory))throw new InvalidDataException("模组安装目录为空");
            string root=Path.GetFullPath(directory).TrimEnd('\\','/')+Path.DirectorySeparatorChar;
            string receipt=Path.Combine(root,"RhodesMod.install.json");
            if(!File.Exists(receipt))throw new InvalidDataException("未找到模组安装记录，请用当前版本启动器安装");
            if(new FileInfo(receipt).Length>2*1024*1024)throw new InvalidDataException("模组安装记录异常");
            Dictionary<string,object> document=new ReceiptJson(File.ReadAllText(receipt,new UTF8Encoding(false,true))).Parse() as Dictionary<string,object>;
            object product,files;
            if(document==null||!document.TryGetValue("product",out product)||!(product is string)||(string)product!="RhodesDoK")
                throw new InvalidDataException("模组安装记录类型不符");
            if(!document.TryGetValue("files",out files)||!(files is List<object>))throw new InvalidDataException("模组文件清单缺失");
            List<object> records=(List<object>)files;
            if(records.Count==0||records.Count>4096)throw new InvalidDataException("模组文件数量异常");
            List<Entry> entries=new List<Entry>(records.Count+NativeFiles.Length);
            HashSet<string> seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(object record in records){
                Dictionary<string,object> fields=record as Dictionary<string,object>;object pathValue,hashValue;
                if(fields==null||!fields.TryGetValue("path",out pathValue)||!(pathValue is string)||
                    !fields.TryGetValue("sha256",out hashValue)||!(hashValue is string)||!ValidHash((string)hashValue))
                    throw new InvalidDataException("模组文件记录格式异常");
                string path=CanonicalPath((string)pathValue);
                if(!seen.Add(path))throw new InvalidDataException("模组文件清单含重复路径");
                if(path!="winhttp.dll"&&path!="doorstop_config.ini"&&path!=".doorstop_version"&&!path.StartsWith("bepinex/",StringComparison.Ordinal))
                    throw new InvalidDataException("模组安装记录路径不符");
                entries.Add(new Entry{Path=path,Hash=((string)hashValue).ToLowerInvariant()});
            }
            foreach(string required in RequiredModFiles)if(!seen.Contains(required))throw new InvalidDataException("模组关键文件未纳入校验："+required);
            VerifyExecutableInventory(root,seen);
            entries.AddRange(NativeFiles);
            entries.Sort(delegate(Entry a,Entry b){return StringComparer.Ordinal.Compare(a.Path,b.Path);});
            StringBuilder canonical=new StringBuilder(RhodesNetworkCompatibility.Protocol+"\n");
            canonical.Append("research_profile=").Append(RhodesResearchProfile.Name).Append('\n');
            HashSet<string> checkedFolders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(Entry entry in entries){
                string file=Path.GetFullPath(Path.Combine(root,entry.Path.Replace('/',Path.DirectorySeparatorChar)));
                if(!file.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("模组文件越出安装目录");
                // A lexical root check alone does not stop a child junction or
                // symbolic link from resolving outside the selected install.
                string parent=Path.GetDirectoryName(file);
                while(parent!=null&&parent.Length>=root.Length&&checkedFolders.Add(parent)){
                    if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("模组目录包含重解析链接");
                    parent=Path.GetDirectoryName(parent);
                }
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("模组文件包含重解析链接");
                string actual;
                using(SHA256 hash=SHA256.Create())using(FileStream stream=File.OpenRead(file))actual=Hex(hash.ComputeHash(stream));
                if(actual!=entry.Hash)throw new InvalidDataException("模组文件已变化："+entry.Path+"，请检查或修复安装");
                canonical.Append(entry.Path).Append('=').Append(actual).Append('\n');
            }
            using(SHA256 hash=SHA256.Create())return RhodesNetworkCompatibility.Protocol+":"+Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())));
        }
        static void VerifyExecutableInventory(string root,HashSet<string> registered)
        {
            // Other preloader/core/plugin assemblies can change execution even
            // when every receipt entry matches. Scan once on this background
            // worker; an unsupported mixed installation cannot join the lobby.
            Stack<string> pending=new Stack<string>();int visited=0;
            foreach(string relative in new string[]{"bepinex/core","bepinex/patchers","bepinex/plugins"}){
                string directory=Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar));
                if(Directory.Exists(directory))pending.Push(directory);
            }
            while(pending.Count!=0){
                string directory=pending.Pop();
                if(++visited>16384)throw new InvalidDataException("模组执行目录文件数量异常");
                if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("模组执行目录包含重解析链接");
                foreach(string child in Directory.GetFileSystemEntries(directory)){
                    if(++visited>16384)throw new InvalidDataException("模组执行目录文件数量异常");
                    FileAttributes attributes=File.GetAttributes(child);
                    if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("模组执行目录包含重解析链接");
                    if((attributes&FileAttributes.Directory)!=0){pending.Push(child);continue;}
                    if(!string.Equals(Path.GetExtension(child),".dll",StringComparison.OrdinalIgnoreCase))continue;
                    string full=Path.GetFullPath(child);
                    if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("模组执行文件越出安装目录");
                    string relative=CanonicalPath(full.Substring(root.Length));
                    if(!registered.Contains(relative))throw new InvalidDataException("发现未登记的额外模组 DLL："+relative+"。请使用独立、已修复的模组安装后联机");
                }
            }
        }
        static bool ValidHash(string value)
        {
            if(value==null||value.Length!=64)return false;
            foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F')))return false;
            return true;
        }
        static string CanonicalPath(string path)
        {
            if(string.IsNullOrEmpty(path)||path.Length>512||Path.IsPathRooted(path))throw new InvalidDataException("模组文件路径异常");
            string normalized=path.Replace('\\','/');
            foreach(string part in normalized.Split('/')){
                if(part.Length==0||part=="."||part==".."||part.EndsWith(".",StringComparison.Ordinal)||part.EndsWith(" ",StringComparison.Ordinal))
                    throw new InvalidDataException("模组文件路径含别名或越界段");
                foreach(char c in part)if(c<32||c==127||"<>:\"|?*".IndexOf(c)>=0)throw new InvalidDataException("模组文件路径字符异常");
                string stem=part.Split('.')[0].ToUpperInvariant();
                if(stem=="CON"||stem=="PRN"||stem=="AUX"||stem=="NUL"||
                    (stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&stem[3]>='1'&&stem[3]<='9'))
                    throw new InvalidDataException("模组文件路径含设备别名");
            }
            return normalized.ToLowerInvariant();
        }
        static string Hex(byte[] data){return BitConverter.ToString(data).Replace("-","").ToLowerInvariant();}

        // A bounded JSON reader avoids optional modern framework dependencies.
        // Every files[] entry is parsed and validated; unrelated receipt fields
        // may change order or contain nested standard JSON metadata.
        sealed class ReceiptJson
        {
            sealed class NumberToken { }
            readonly string Text;int Position,Tokens;
            internal ReceiptJson(string text){Text=text;}
            internal object Parse(){object value=Value(0);Space();if(Position!=Text.Length)Bad();return value;}
            static void Bad(){throw new InvalidDataException("模组安装记录 JSON 格式异常");}
            void Space(){while(Position<Text.Length&&(Text[Position]==' '||Text[Position]=='\t'||Text[Position]=='\r'||Text[Position]=='\n'))Position++;}
            bool Take(char c){Space();if(Position<Text.Length&&Text[Position]==c){Position++;return true;}return false;}
            void Need(char c){if(!Take(c))Bad();}
            object Value(int depth)
            {
                if(depth>64||++Tokens>65536)Bad();Space();if(Position>=Text.Length)Bad();char c=Text[Position];
                if(c=='"')return String();
                if(c=='{'){
                    Position++;Dictionary<string,object> values=new Dictionary<string,object>(StringComparer.Ordinal);
                    if(Take('}'))return values;
                    do{Space();if(Position>=Text.Length||Text[Position]!='"')Bad();string key=String();Need(':');
                        if(values.ContainsKey(key))Bad();values.Add(key,Value(depth+1));if(Take('}'))return values;
                    }while(Take(','));Bad();return null;
                }
                if(c=='['){Position++;List<object> values=new List<object>();if(Take(']'))return values;
                    do{values.Add(Value(depth+1));if(Take(']'))return values;}while(Take(','));Bad();return null;}
                if(c=='t'){Literal("true");return true;}if(c=='f'){Literal("false");return false;}if(c=='n'){Literal("null");return null;}
                if(c=='-'||(c>='0'&&c<='9')){Number();return new NumberToken();}Bad();return null;
            }
            void Literal(string value)
            {if(Position+value.Length>Text.Length||string.CompareOrdinal(Text,Position,value,0,value.Length)!=0)Bad();Position+=value.Length;}
            bool Digit(){return Position<Text.Length&&Text[Position]>='0'&&Text[Position]<='9';}
            void Digits(){if(!Digit())Bad();while(Digit())Position++;}
            void Number()
            {
                if(Text[Position]=='-'){Position++;if(Position==Text.Length)Bad();}
                if(Text[Position]=='0')Position++;else Digits();
                if(Position<Text.Length&&Text[Position]=='.'){Position++;Digits();}
                if(Position<Text.Length&&(Text[Position]=='e'||Text[Position]=='E')){Position++;if(Position<Text.Length&&(Text[Position]=='+'||Text[Position]=='-'))Position++;Digits();}
            }
            string String()
            {
                Need('"');StringBuilder result=new StringBuilder();
                while(Position<Text.Length){
                    char c=Text[Position++];
                    if(c=='"'){
                        string value=result.ToString();for(int i=0;i<value.Length;i++){
                            if(char.IsHighSurrogate(value[i])){if(i+1>=value.Length||!char.IsLowSurrogate(value[++i]))Bad();}
                            else if(char.IsLowSurrogate(value[i]))Bad();
                        }return value;
                    }
                    if(c<32)Bad();
                    if(c=='\\'){
                        if(Position==Text.Length)Bad();c=Text[Position++];
                        switch(c){
                            case '"':case '\\':case '/':break;
                            case 'b':c='\b';break;case 'f':c='\f';break;case 'n':c='\n';break;case 'r':c='\r';break;case 't':c='\t';break;
                            case 'u':
                                int code=0;for(int i=0;i<4;i++){
                                    if(Position==Text.Length)Bad();char hex=Text[Position++];int digit=hex>='0'&&hex<='9'?hex-'0':hex>='a'&&hex<='f'?hex-'a'+10:hex>='A'&&hex<='F'?hex-'A'+10:-1;
                                    if(digit<0)Bad();code=code*16+digit;
                                }c=(char)code;break;
                            default:Bad();break;
                        }
                    }
                    result.Append(c);
                }Bad();return null;
            }
        }
    }

    internal static class RhodesNetworkPeerPolicy
    {
        static bool ValidDigest(string value)
        {
            string prefix=RhodesNetworkCompatibility.Protocol+":";
            if(value==null||value.Length!=prefix.Length+64||!value.StartsWith(prefix,StringComparison.Ordinal))return false;
            for(int i=prefix.Length;i<value.Length;i++)if(!((value[i]>='0'&&value[i]<='9')||(value[i]>='a'&&value[i]<='f')))return false;
            return true;
        }
        internal static bool Compatible(string local,IList<string> peers,out string reason)
        {
            if(string.IsNullOrEmpty(local)){reason="正在核对模组文件…";return false;}
            if(!ValidDigest(local)){reason="本机模组校验结果异常，请重新安装后重试";return false;}
            if(peers==null||peers.Count==0){reason="等待房间玩家信息…";return false;}
            foreach(string peer in peers){
                if(string.IsNullOrEmpty(peer)){reason="等待所有玩家完成模组校验…";return false;}
                if(!string.Equals(peer,local,StringComparison.Ordinal)){reason="玩家的模组版本、文件或科研预设不一致，暂不能开始";return false;}
            }
            reason="模组一致，可以选择罗德岛并开始自定义对战";return true;
        }
    }

}
