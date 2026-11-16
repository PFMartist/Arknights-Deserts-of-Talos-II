$ErrorActionPreference='Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'TestSupport.psm1') -Force
Import-Module Microsoft.PowerShell.Utility
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$audit=Join-Path $root 'out\tests\operatorlayout61'
New-Item -ItemType Directory -Path $audit -Force | Out-Null
$sourcePath=Join-Path $root 'src\RhodesOperatorStatusUI.cs'
$source=Get-Content -LiteralPath $sourcePath -Raw
function Read-ProductionMethod([string]$needle) {
 $at=$source.IndexOf($needle,[StringComparison]::Ordinal)
 if($at -lt 0){throw ('Production method missing: '+$needle)}
 $open=$source.IndexOf('{',$at);$depth=0
 for($i=$open;$i -lt $source.Length;$i++) {
  if($source[$i] -eq '{'){$depth++}
  if($source[$i] -eq '}'){$depth--;if($depth -eq 0){return $source.Substring($at,$i-$at+1)}}
 }
 throw 'Production method closing brace missing'
}
$align=Read-ProductionMethod 'void AlignToNativeHangar()'
$geometryAt=$align.IndexOf('NativeRegion=WidgetBounds(',[StringComparison]::Ordinal)
if($geometryAt -lt 0){throw 'Production geometry boundary changed'}
# Retain the exact production guard prefix. Replace only the geometry body
# after those guards with an execution counter; do not reimplement the guard.
$align=$align.Substring(0,$geometryAt)+'AlignExecutions++; }'
$harness=@'
using System; using System.Collections; using System.Collections.Generic;
public sealed class LayoutNode61 { public bool activeInHierarchy=true; }
public sealed class ProductionOperatorLayout61 {
 public LayoutNode61 Root=new LayoutNode61(),gameObject=new LayoutNode61();
 public object NativeFrame=new object(),NativeFontReference=new object(),NativeHangar=new object();
 public bool Ready,AlignmentPending; public int AlignExecutions,CoroutineStarts,BuildRequests;
 readonly List<IEnumerator> Pending=new List<IEnumerator>();
 void StartCoroutine(IEnumerator routine){CoroutineStarts++;Pending.Add(routine);}
 void ScheduleBuild(){BuildRequests++;}
 public void Schedule(){ScheduleAlignment();} public void Align(){AlignToNativeHangar();} public void Resize(){OnScreenResize();}
 public void Tick(){for(int i=Pending.Count-1;i>=0;i--)if(!Pending[i].MoveNext())Pending.RemoveAt(i);}
'@
$harness+=[Environment]::NewLine+$align+[Environment]::NewLine+(Read-ProductionMethod 'void ScheduleAlignment()')+[Environment]::NewLine+(Read-ProductionMethod 'IEnumerator AfterNativeLayout()')+[Environment]::NewLine+(Read-ProductionMethod 'void OnScreenResize()')+[Environment]::NewLine+'}'
$generated=Join-Path $audit 'ProductionOperatorLayoutMethods61.cs'
[IO.File]::WriteAllText($generated,$harness,(New-Object Text.UTF8Encoding($false)))
$exe=Join-Path $audit 'OperatorLayoutFixture61.exe'
& (Get-LegacyCsc) /nologo /utf8output /warn:4 /warnaserror+ ("/out:"+$exe) $generated (Join-Path $PSScriptRoot 'OperatorLayoutFixture61.cs')
if($LASTEXITCODE -ne 0){throw 'Operator layout fixture compile failed'}
& $exe 2>&1 | Tee-Object -FilePath (Join-Path $audit 'OPERATOR_LAYOUT61_RESULT.txt')
if($LASTEXITCODE -ne 0){throw 'Operator layout fixture FAILED'}
@($sourcePath,$generated,(Join-Path $PSScriptRoot 'OperatorLayoutFixture61.cs'),(Join-Path $root 'out\EndfieldDoK.MapUnitLab.dll')) | Where-Object {Test-Path -LiteralPath $_} | ForEach-Object {Get-FileHash -LiteralPath $_ -Algorithm SHA256} | Select-Object Path,Hash | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $audit 'VERIFIED_OPERATOR_LAYOUT_SHA256.json') -Encoding UTF8
