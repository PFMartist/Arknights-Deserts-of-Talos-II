[CmdletBinding()]
param(
    [string]$IconPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IconPath) { $IconPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'RhodesMod.ico' }
$compiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $PSScriptRoot 'RhodesModEntry.cs'
$output = Join-Path (Split-Path -Parent $PSScriptRoot) 'RhodesMod.exe'
$packageIcon = Join-Path (Split-Path -Parent $PSScriptRoot) 'RhodesMod.ico'

foreach ($inputFile in @($compiler, $source, $IconPath)) {
    if (-not [IO.File]::Exists($inputFile)) {
        throw "找不到编译所需文件 $inputFile"
    }
}

$compilerArguments = @(
    '/nologo'
    '/target:exe'
    '/platform:x64'
    '/optimize+'
    '/codepage:65001'
    ('/out:' + $output)
    ('/win32icon:' + $IconPath)
    $source
)
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "C# 编译失败，退出码 $LASTEXITCODE"
}
if ([IO.Path]::GetFullPath($IconPath) -ne [IO.Path]::GetFullPath($packageIcon)) { Copy-Item -LiteralPath $IconPath -Destination $packageIcon -Force }
Get-FileHash -LiteralPath $source, $output, $packageIcon -Algorithm SHA256
