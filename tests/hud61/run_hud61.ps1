$ErrorActionPreference='Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'TestSupport.psm1') -Force
Import-Module Microsoft.PowerShell.Utility
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$audit=Join-Path $root 'out\tests\hud61'
New-Item -ItemType Directory -Path $audit -Force | Out-Null
$theme=Get-Content -LiteralPath (Join-Path $root 'src\EndfieldHudTheme.cs') -Raw
function Read-ProductionMethod([string]$needle) {
 $at=$theme.IndexOf($needle,[StringComparison]::Ordinal)
 if($at -lt 0){throw ('Production method missing: '+$needle)}
 $open=$theme.IndexOf('{',$at);$depth=0
 for($i=$open;$i -lt $theme.Length;$i++) {
  if($theme[$i] -eq '{'){$depth++}
  if($theme[$i] -eq '}'){$depth--;if($depth -eq 0){return $theme.Substring($at,$i-$at+1)}}
 }
 throw 'Production method closing brace missing'
}
$probe=@'
using System; using System.Collections.Generic; using System.Reflection; using UnityEngine; using EndfieldDoK.MapUnitLab;
public static class ThemeIconProbe61 {
 public static bool Enabled=true; public static int ExternalWalks; public static Component LastExternal;
 static bool IsEnabled(){return Enabled;}
 static Color MapValue(Color value){return value.b>value.r?new Color(1,1,1,value.a):value;}
 static object ReadOptional(object value,string field){object result;return NativeRead.TryGet(value,field,out result)?result:null;}
 static void MapNamedColourField(object value,string field){FieldInfo f=value.GetType().GetField(field);Color c=(Color)f.GetValue(value);f.SetValue(value,MapValue(c));}
 static void MapAtlasField(object value,string field){value.GetType().GetField(field).SetValue(value,"ThemedAtlas");}
 static void ApplySubtree(Component widget,HashSet<GameObject> walking,bool cache){ExternalWalks++;LastExternal=widget;}
 public static void Apply(object container,Component icon){ApplyIconContainer(container,new HashSet<object>(),icon,new HashSet<GameObject>());}
'@
$probe+=[Environment]::NewLine+(Read-ProductionMethod 'static void ApplyIconContainer(')+[Environment]::NewLine+(Read-ProductionMethod 'public static Color MapIconColour(')+[Environment]::NewLine+(Read-ProductionMethod 'public static Color MapContainerColour(')+[Environment]::NewLine+'}'
$probePath=Join-Path $audit 'ProductionThemeIconMethods61.cs'
[IO.File]::WriteAllText($probePath,$probe,(New-Object Text.UTF8Encoding($false)))
$sources=@((Join-Path $root 'src\HudSerializedSkinSnapshot61.cs'),(Join-Path $root 'src\RhodesHangarStateColours.cs'),(Join-Path $PSScriptRoot 'HudNativeStubs61.cs'),(Join-Path $PSScriptRoot 'HudSkinFixture61.cs'),$probePath)
$exe=Join-Path $audit 'HudSkinFixture61.exe'
& (Get-LegacyCsc) /nologo /utf8output /warn:4 /warnaserror+ ("/out:"+$exe) @sources
if($LASTEXITCODE -ne 0){throw 'HUD61 production helper fixture compile failed'}
& $exe 2>&1 | Tee-Object -FilePath (Join-Path $audit 'HUD61_RESULT.txt')
if($LASTEXITCODE -ne 0){throw 'HUD61 helper fixture FAILED'}
$files=@('src\EndfieldHudTheme.cs','src\RhodesHangarStateColours.cs','src\HudSerializedSkinSnapshot61.cs','tests\hud61\HudSkinFixture61.cs') | ForEach-Object {Join-Path $root $_}
$dll=Join-Path $root 'out\EndfieldDoK.MapUnitLab.dll'
if(Test-Path -LiteralPath $dll){$files+=$dll}
$files | Where-Object {Test-Path -LiteralPath $_} | ForEach-Object {Get-FileHash -LiteralPath $_ -Algorithm SHA256} | Select-Object Path,Hash | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $audit 'VERIFIED_HUD_FILES_SHA256.json') -Encoding UTF8
