[CmdletBinding()]
param([string]$StreamerPath,[switch]$SkipTests)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$version='0.1.0'
$env:DOTNET_CLI_HOME=Join-Path $root '.dotnet-build'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$work=Join-Path $root ('artifacts\build-'+[guid]::NewGuid().ToString('N'))
$publish=Join-Path $work 'publish'
$hook=Join-Path $work 'runtime-override'
$tests=Join-Path $work 'runtime-tests'
$stage=Join-Path $work ('VirtualDesktopUnlock-v'+$version+'-win-x64')
$dist=Join-Path $root 'artifacts\dist'
foreach($path in @($publish,$hook,$tests,$stage,$dist)){New-Item -ItemType Directory -Path $path -Force | Out-Null}
& dotnet publish (Join-Path $root 'SteamVRLibrary\SteamVRLibrary.csproj') -c Release -o $publish --configfile (Join-Path $root 'NuGet.Config')
if($LASTEXITCODE -ne 0){throw 'Companion app build failed.'}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework=Split-Path -Parent $compiler
$references=@("/reference:$framework\WPF\PresentationFramework.dll","/reference:$framework\WPF\WindowsBase.dll",'/reference:System.Xaml.dll')
& $compiler /nologo /target:library ('/out:'+(Join-Path $hook 'VirtualDesktop.LibraryOverrides.dll')) $references (Join-Path $root 'src\RuntimeOverrides\LibraryOverrides.cs')
if($LASTEXITCODE -ne 0){throw 'Runtime helper build failed.'}
Copy-Item -LiteralPath (Join-Path $root 'src\RuntimeOverrides\RuntimeHook.config') -Destination (Join-Path $hook 'VirtualDesktop.Streamer.exe.config')
[pscustomobject]@{Version=$version;StreamerVersion='1.34.23.0';StreamerSHA256='FEAD548C57DF539006E6C97A7A29D46FDE172F756F40D65DBDB77ABAAFA3AEAD';HelperSHA256=(Get-FileHash -LiteralPath (Join-Path $hook 'VirtualDesktop.LibraryOverrides.dll')).Hash;ConfigSHA256=(Get-FileHash -LiteralPath (Join-Path $hook 'VirtualDesktop.Streamer.exe.config')).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hook 'manifest.json')
if(!$SkipTests){
    $test=Start-Process -FilePath (Join-Path $publish 'Steam VR Library.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru
    $test.WaitForExit()
    Get-Content -LiteralPath (Join-Path $publish 'self-test-result.txt') | Write-Output
    if($test.ExitCode -ne 0){throw 'Companion tests failed.'}
    & $compiler /nologo /target:exe ('/out:'+(Join-Path $tests 'RuntimeHookTest.exe')) $references (Join-Path $root 'src\RuntimeOverrides\RuntimeHookTest.cs')
    if($LASTEXITCODE -ne 0){throw 'Runtime test harness build failed.'}
    Copy-Item -LiteralPath (Join-Path $hook 'VirtualDesktop.LibraryOverrides.dll') -Destination $tests
    Copy-Item -LiteralPath (Join-Path $hook 'VirtualDesktop.Streamer.exe.config') -Destination (Join-Path $tests 'RuntimeHookTest.exe.config')
    & (Join-Path $tests 'RuntimeHookTest.exe')
    if($LASTEXITCODE -ne 0){throw 'AppDomain startup-loading smoke test failed.'}
    if($StreamerPath){
        $oldData=$env:VD_LIBRARY_TEST_DATA;$oldDelay=$env:VD_LIBRARY_TEST_DELAYED;$oldLog=$env:VD_LIBRARY_OVERRIDE_LOG
        try{
            $env:VD_LIBRARY_TEST_DATA=Join-Path $tests 'data'; New-Item -ItemType Directory -Path $env:VD_LIBRARY_TEST_DATA -Force | Out-Null
            $env:VD_LIBRARY_TEST_DELAYED='1'; $env:VD_LIBRARY_OVERRIDE_LOG=Join-Path $tests 'delayed-startup.log'
            & (Join-Path $tests 'RuntimeHookTest.exe') ([IO.Path]::GetFullPath($StreamerPath)) --delayed
            if($LASTEXITCODE -ne 0){throw 'Delayed Streamer dictionary override test failed.'}
        }finally{$env:VD_LIBRARY_TEST_DATA=$oldData;$env:VD_LIBRARY_TEST_DELAYED=$oldDelay;$env:VD_LIBRARY_OVERRIDE_LOG=$oldLog}
    }
}
foreach($name in @('Steam VR Library.exe','Steam VR Library.dll','Steam VR Library.deps.json','Steam VR Library.runtimeconfig.json')){Copy-Item -LiteralPath (Join-Path $publish $name) -Destination $stage}
Copy-Item -LiteralPath $hook -Destination $stage -Recurse
Get-ChildItem -LiteralPath (Join-Path $root 'scripts') -File | Copy-Item -Destination $stage
foreach($name in @('README.md','LICENSE','CHANGELOG.md')){Copy-Item -LiteralPath (Join-Path $root $name) -Destination $stage}
$zip=Join-Path $dist ('VirtualDesktopUnlock-v'+$version+'-win-x64.zip')
Compress-Archive -LiteralPath $stage -DestinationPath $zip -Force
$sum=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+(Split-Path -Leaf $zip)
$sum | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ASCII
Write-Output ('Release: '+$zip)
Write-Output ('Staging directory: '+$stage)
