param([switch]$Test, [string]$Version = '0.0.0-dev')
$ErrorActionPreference = 'Stop'
if ($Version -cnotmatch '^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
    throw 'Version must be MAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH, optionally followed by a prerelease suffix.'
}
$versionParts = @($Matches[1], $Matches[2], $Matches[3])
foreach ($part in $versionParts) {
    if ($part.Length -gt 5 -or [int]$part -gt 65534) { throw 'Version components must be between 0 and 65534.' }
}
$productVersion = $Version -creplace '^v', ''
$fileVersion = ($versionParts -join '.') + '.0'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
if (-not (Test-Path $compiler)) { throw '.NET Framework 4.x compiler is required.' }
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$assemblyInfo = Join-Path $output 'AssemblyInfo.g.cs'
@"
using System.Reflection;
[assembly: AssemblyTitle("2ctrl")]
[assembly: AssemblyProduct("2ctrl")]
[assembly: AssemblyVersion("$fileVersion")]
[assembly: AssemblyFileVersion("$fileVersion")]
[assembly: AssemblyInformationalVersion("$productVersion")]
"@ | Set-Content -LiteralPath $assemblyInfo -Encoding UTF8
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/win32icon:$PSScriptRoot\src\app.ico" "/resource:$PSScriptRoot\src\app.ico,TwoCtrl.AppIcon.ico" "/out:$output\2ctrl.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.Linq.dll "$PSScriptRoot\src\TapDetector.cs" "$PSScriptRoot\src\Program.cs" $assemblyInfo
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
$builtVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $output '2ctrl.exe'))
if ($builtVersion.FileVersion -ne $fileVersion -or $builtVersion.ProductVersion -ne $productVersion) {
    throw 'Built executable version does not match the requested version.'
}
Copy-Item -LiteralPath "$PSScriptRoot\src\2ctrl.exe.config" -Destination "$output\2ctrl.exe.config"
if ($Test) {
    & $compiler /nologo /target:exe "/out:$output\2ctrl.Tests.exe" "$PSScriptRoot\src\TapDetector.cs" "$PSScriptRoot\tests\TapDetectorTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & "$output\2ctrl.Tests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Write-Output "Built: $output\2ctrl.exe (version $productVersion)"
