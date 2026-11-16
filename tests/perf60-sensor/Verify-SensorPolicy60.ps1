param(
 [string]$AssemblyPath='',
 [Parameter(Mandatory=$true)][string]$CecilPath,
 [string]$OutputDirectory='',
 [string]$ReportName='SENSOR_POLICY60'
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'TestSupport.psm1') -Force
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$AssemblyPath=Get-TestRuntime $root $AssemblyPath
if([string]::IsNullOrEmpty($OutputDirectory)){$OutputDirectory=Join-Path $root 'out\tests\perf60-sensor'}
Add-Type -Path $CecilPath
[void][System.IO.Directory]::CreateDirectory($OutputDirectory)
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($AssemblyPath)
$type = $assembly.MainModule.Types | Where-Object FullName -eq 'EndfieldDoK.MapUnitLab.FactionUnitModels'
$method = $type.Methods | Where-Object Name -eq 'ConfigureView'
if ($null -eq $method -or -not $method.HasBody) { throw 'Compiled ConfigureView missing' }
$instructions = @($method.Body.Instructions)
$byOffset = [System.Collections.Generic.Dictionary[int,int]]::new()
for ($i = 0; $i -lt $instructions.Count; $i++) { $byOffset.Add($instructions[$i].Offset, $i) }
$checks = [System.Collections.Generic.List[string]]::new()
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw "FAIL $message" }
    $checks.Add($message)
}
function Reach([hashtable]$forcedBranches) {
    $seen = [System.Collections.Generic.HashSet[int]]::new()
    $pending = [System.Collections.Generic.Stack[int]]::new()
    $pending.Push(0)
    while ($pending.Count -gt 0) {
        $index = $pending.Pop()
        if ($index -lt 0 -or $index -ge $instructions.Count -or -not $seen.Add($index)) { continue }
        $instruction = $instructions[$index]
        if ($forcedBranches.ContainsKey($instruction.Offset)) {
            $pending.Push($forcedBranches[$instruction.Offset]); continue
        }
        $flow = $instruction.OpCode.FlowControl.ToString()
        if ($flow -eq 'Return' -or $flow -eq 'Throw') { continue }
        if ($flow -eq 'Branch' -or $flow -eq 'Cond_Branch') {
            if ($instruction.Operand -is [Mono.Cecil.Cil.Instruction]) {
                $pending.Push($byOffset[$instruction.Operand.Offset])
            } elseif ($instruction.Operand -is [Mono.Cecil.Cil.Instruction[]]) {
                foreach ($target in $instruction.Operand) { $pending.Push($byOffset[$target.Offset]) }
            }
            if ($flow -eq 'Branch') { continue }
        }
        $pending.Push($index + 1)
    }
    return ,$seen
}
$success = $false
$failure = $null
try {
    $keys = @(for ($i = 0; $i -lt $instructions.Count; $i++) {
        if ($instructions[$i].OpCode.Name -eq 'ldstr' -and $instructions[$i].Operand -eq 'ForceIconOnlyInSensors') { $i }
    })
    Check ($keys.Count -eq 1) 'Compiled ConfigureView has exactly one ForceIconOnlyInSensors write key'
    $key = $keys[0]
    Check ($instructions[$key+1].OpCode.Name -eq 'ldc.i4.0') 'Compiled sensor override value is false'
    Check ($instructions[$key+2].OpCode.Name -eq 'box' -and $instructions[$key+2].Operand.FullName -eq 'System.Boolean') 'Compiled sensor override boxes a Boolean'
    $call = $instructions[$key+3]
    Check ($call.OpCode.Name -eq 'call' -and $call.Operand.FullName -like '*NativeRead::Set(System.Object,System.String,System.Object)') 'Compiled write uses the expected property setter'
    $airField = $instructions[$key-5]
    Check ($airField.OpCode.Name -eq 'ldfld' -and $airField.Operand.FullName -eq 'System.Boolean EndfieldDoK.MapUnitLab.FactionModelBinding::Air') 'Sensor write is preceded by the exact per-binding Air guard'
    $airBranch = $instructions[$key-4]
    $tuningBranch = $instructions[$key-2]
    Check ($airBranch.OpCode.Name -like 'brfalse*' -and $tuningBranch.OpCode.Name -like 'brfalse*') 'Air and non-null tuning guards use explicit conditional branches'
    $after = $byOffset[$airBranch.Operand.Offset]
    Check ($after -eq ($key+4) -and $tuningBranch.Operand.Offset -eq $airBranch.Operand.Offset) 'Either false guard skips exactly the sensor write block'
    Check ($instructions[$key-3].OpCode.Name -like 'ldloc*' -and $instructions[$key-1].OpCode.Name -like 'ldloc*' -and $instructions[$key-3].Operand.Index -eq $instructions[$key-1].Operand.Index) 'Guarded tuning object is the same local passed to NativeRead.Set'
    $ground = Reach @{ $airBranch.Offset = $after }
    Check (-not $ground.Contains($key+3)) 'Whole-method CFG: Air=false cannot reach the sensor override call'
    $air = Reach @{ $airBranch.Offset = $key-3; $tuningBranch.Offset = $key-1 }
    Check ($air.Contains($key+3)) 'Whole-method CFG: Air=true and tuning non-null reaches the override call'
    $missingTuning = Reach @{ $airBranch.Offset = $key-3; $tuningBranch.Offset = $after }
    Check (-not $missingTuning.Contains($key+3)) 'Whole-method CFG: null tuning cannot reach the override call'
    $success = $true
} catch {
    $failure = $_.Exception.Message
}
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add($method.FullName)
foreach ($instruction in $instructions) { $lines.Add($instruction.ToString()) }
[System.IO.File]::WriteAllLines((Join-Path $OutputDirectory ($ReportName+'.il.txt')), $lines, [System.Text.UTF8Encoding]::new($false))
$report = [ordered]@{
    status = $(if($success){'PASS_COMPILED_IL_CONTROL_FLOW_ONLY'}else{'FAIL_COMPILED_IL_CONTROL_FLOW_ONLY'})
    assembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
    sha256 = (Get-FileHash -LiteralPath $AssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
    assertions = @($checks)
    failure = $failure
    runtimeCodeExecuted = $false
    unityFunctionsInvoked = $false
    gameStarted = $false
    boundary = 'Reads Cecil metadata and traverses IL CFG; proves branch reachability only, not Unity sensor pixels, selection behavior or frame time.'
}
[System.IO.File]::WriteAllText((Join-Path $OutputDirectory ($ReportName+'.json')), ($report | ConvertTo-Json -Depth 5), [System.Text.UTF8Encoding]::new($false))
$report | ConvertTo-Json -Depth 4
if (-not $success) { exit 1 }
