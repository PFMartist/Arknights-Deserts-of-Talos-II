function Get-LegacyCsc {
 $windows=[Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
 $compiler=Join-Path $windows 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 if(-not(Test-Path -LiteralPath $compiler -PathType Leaf)){throw 'Windows .NET Framework 4.x compiler required'}
 return $compiler
}
function Get-TestRuntime([string]$Root,[string]$RuntimeDll) {
 if([string]::IsNullOrWhiteSpace($RuntimeDll)){$RuntimeDll=Join-Path $Root 'out\EndfieldDoK.MapUnitLab.dll'}
 if(-not(Test-Path -LiteralPath $RuntimeDll -PathType Leaf)){throw 'Build the Runtime or supply -RuntimeDll'}
 return [IO.Path]::GetFullPath($RuntimeDll)
}
Export-ModuleMember -Function Get-LegacyCsc,Get-TestRuntime
