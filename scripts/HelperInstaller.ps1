[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [switch]$Uninstall,
    [string]$StreamerDirectory=(Join-Path $env:ProgramFiles 'Virtual Desktop Streamer')
)
$ErrorActionPreference='Stop'
$StreamerDirectory=[IO.Path]::GetFullPath($StreamerDirectory)
$exe=Join-Path $StreamerDirectory 'VirtualDesktop.Streamer.exe'
$dll=Join-Path $StreamerDirectory 'VirtualDesktop.LibraryOverrides.dll'
$config=Join-Path $StreamerDirectory 'VirtualDesktop.Streamer.exe.config'
$recordPath=Join-Path $StreamerDirectory 'VirtualDesktop.LibraryOverrides.install.json'
$payload=Join-Path $PSScriptRoot 'runtime-override'
$manifest=Get-Content -LiteralPath (Join-Path $payload 'manifest.json') -Raw | ConvertFrom-Json
$action=if($Uninstall){'Remove the optional RE3 startup helper and restart Streamer'}else{'Install the optional RE3 startup helper and restart Streamer'}
if(!$PSCmdlet.ShouldProcess($StreamerDirectory,$action)){return}

$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
    if($StreamerDirectory.Contains('"')){throw 'Invalid installation path.'}
    $arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'" -StreamerDirectory "'+$StreamerDirectory+'"'
    if($Uninstall){$arguments+=' -Uninstall'}
    $elevated=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -Verb RunAs -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $elevated.WaitForExit()
    if($elevated.ExitCode -ne 0){throw 'Elevated installer failed. See the installation log in ProgramData\Virtual Desktop Unlock.'}
    return
}

$logFolder=Join-Path $env:ProgramData 'Virtual Desktop Unlock'
New-Item -ItemType Directory -Path $logFolder -Force | Out-Null
Start-Transcript -LiteralPath (Join-Path $logFolder 'installer.log') -Append | Out-Null
try{
    if(!(Test-Path -LiteralPath $exe)){throw 'Virtual Desktop Streamer was not found.'}
    $supportedHashes=if($manifest.SupportedStreamers){@($manifest.SupportedStreamers | ForEach-Object {$_.SHA256})}else{@($manifest.StreamerSHA256)}
    if(!$Uninstall -and (Get-FileHash -LiteralPath $exe).Hash -notin $supportedHashes){throw 'Unsupported Streamer binary. Library integration supports verified 1.34.23.0 and 1.34.25.0 builds. No installation files were changed.'}
    if((Get-AuthenticodeSignature -LiteralPath $exe).Status -ne 'Valid'){throw 'Streamer must have a valid developer signature.'}
    $record=if(Test-Path -LiteralPath $recordPath){Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json}else{$null}
    if($Uninstall){
        if(!$record){throw 'No managed installation record was found. No files were removed.'}
        if($record.Product -ne 'Virtual Desktop Unlock'){throw 'Unknown installation record.'}
        foreach($item in @(@($dll,$record.HelperSHA256),@($config,$record.ConfigSHA256))){
            if((Test-Path -LiteralPath $item[0]) -and (Get-FileHash -LiteralPath $item[0]).Hash -ne $item[1]){throw ('File has changed since installation; refusing to remove it: '+$item[0])}
        }
    }else{
        foreach($item in @(@((Join-Path $payload 'VirtualDesktop.LibraryOverrides.dll'),$manifest.HelperSHA256),@((Join-Path $payload 'VirtualDesktop.Streamer.exe.config'),$manifest.ConfigSHA256))){
            if((Get-FileHash -LiteralPath $item[0]).Hash -ne $item[1]){throw 'Release payload hash check failed.'}
        }
        if($record -and $record.Product -ne 'Virtual Desktop Unlock'){throw 'Unknown existing installation record.'}
        foreach($item in @(@($dll,$manifest.HelperSHA256),@($config,$manifest.ConfigSHA256))){
            if((Test-Path -LiteralPath $item[0]) -and (!$record -or (Get-FileHash -LiteralPath $item[0]).Hash -ne $item[1])){throw ('An unrelated or different-version file already exists: '+$item[0]+'. Remove that helper with its original uninstaller before installing this release.')}
        }
    }
    $processes=@(Get-Process -Name VirtualDesktop.Streamer -ErrorAction SilentlyContinue | Where-Object { $_.Path -and [IO.Path]::GetFullPath($_.Path) -eq $exe })
    $processes | Stop-Process -ErrorAction Stop
    if($processes.Count){$processes | Wait-Process -Timeout 15 -ErrorAction Stop}
    if($Uninstall){
        foreach($path in @($config,$dll,$recordPath)){if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}}
        Start-Process -FilePath $exe -WorkingDirectory $StreamerDirectory -WindowStyle Hidden
        'Removed the startup helper. The signed Streamer executable is unchanged.'
        return
    }
    $process=$null
    try{
        Copy-Item -LiteralPath (Join-Path $payload 'VirtualDesktop.LibraryOverrides.dll') -Destination $dll -Force
        Copy-Item -LiteralPath (Join-Path $payload 'VirtualDesktop.Streamer.exe.config') -Destination $config -Force
        $process=Start-Process -FilePath $exe -WorkingDirectory $StreamerDirectory -WindowStyle Hidden -PassThru
        if($process.WaitForExit(4000)){throw ('Streamer exited during startup: '+$process.ExitCode)}
        if((Get-FileHash -LiteralPath $exe).Hash -notin $supportedHashes){throw 'Streamer executable changed unexpectedly.'}
        [pscustomobject]@{Product='Virtual Desktop Unlock';Version=$manifest.Version;HelperSHA256=$manifest.HelperSHA256;ConfigSHA256=$manifest.ConfigSHA256;Installed=[DateTimeOffset]::Now.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath $recordPath
        'Installed RE3 integration. Reconnect the headset and reopen Virtual Desktop Games.'
    }catch{
        $failure=$_
        if($process){Get-Process -Id $process.Id -ErrorAction SilentlyContinue | Stop-Process -ErrorAction SilentlyContinue}
        foreach($path in @($config,$dll,$recordPath)){if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}}
        Start-Process -FilePath $exe -WorkingDirectory $StreamerDirectory -WindowStyle Hidden
        throw $failure
    }
}finally{Stop-Transcript | Out-Null}
