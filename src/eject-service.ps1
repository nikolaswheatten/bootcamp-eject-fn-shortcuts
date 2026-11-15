param([switch]$CheckOnly)
$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$usb=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'USBPcap\USBPcapCMD.exe'
Add-Type -Path @("$PSScriptRoot\EjectPcapReader.cs","$PSScriptRoot\EjectEngine.cs","$PSScriptRoot\FnShortcuts.cs")
if($CheckOnly){'COMPILE OK';return}
if(-not (Test-Path -LiteralPath $usb -PathType Leaf)){throw "USBPcapCMD.exe not found: $usb"}
New-Item -ItemType Directory -Path (Join-Path $base 'logs') -Force | Out-Null
$mutex=[Threading.Mutex]::new($false,'Local\EjectFix-6f44e2e3')
try {
 if(-not $mutex.WaitOne(0)){$mutex.Dispose();return}
}catch [Threading.AbandonedMutexException] {
 # The previous worker exited without releasing the mutex; this thread now owns it.
}
$stop=[Threading.EventWaitHandle]::new($false,[Threading.EventResetMode]::ManualReset,'Local\EjectFixStop-6f44e2e3')
$stop.Reset() | Out-Null
function UsbText([string]$arguments){
 $si=[Diagnostics.ProcessStartInfo]::new($usb,$arguments)
 $si.UseShellExecute=$false;$si.CreateNoWindow=$true;$si.RedirectStandardOutput=$true;$si.RedirectStandardError=$true
 $p=[Diagnostics.Process]::Start($si)
 $out=$p.StandardOutput.ReadToEnd();$err=$p.StandardError.ReadToEnd();$p.WaitForExit()
 if($p.ExitCode-ne 0){throw "USB enumeration failed: $err"}
 $p.Dispose();return ($out -split "`r?`n")
}
function Log($s){if((Test-Path "$base\logs\service.log") -and (Get-Item "$base\logs\service.log").Length -gt 1MB){Move-Item "$base\logs\service.log" "$base\logs\service.previous.log" -Force};Add-Content "$base\logs\service.log" "$(Get-Date -Format o) $s"}
try {
 Log 'START memory-only worker'
 while(-not $stop.WaitOne(0)){
  $cap=$null;$engine=$null;$shortcuts=$null;$observe=$null;$fnErrorLogged=$false
  try {
   $candidates=@()
   $interfaces=UsbText '--extcap-interfaces'
   foreach($line in $interfaces){
    if($line -match '^interface \{value=([^}]+)\}'){
     $hub=$Matches[1]
     $config=UsbText "--extcap-interface $hub --extcap-config"
     foreach($row in $config){if($row -match '\{display=Apple Keyboard\}.*\{parent=(\d+)\}'){$candidates+=@{Hub=$hub;Address=$Matches[1]}}}
   }
   }
   if($candidates.Count-ne 1){throw 'Exactly one Apple Keyboard capture candidate required'}
   $c=$candidates[0]
   $keys=Get-ItemProperty 'HKCU:\Control Panel\Keyboard'
   $engine=[EjectEngine]::new([int]$keys.KeyboardDelay,[int]$keys.KeyboardSpeed)
   $si=[Diagnostics.ProcessStartInfo]::new($usb,"--extcap-interface $($c.Hub) --capture --fifo - --devices $($c.Address) --inject-descriptors")
   $si.UseShellExecute=$false;$si.CreateNoWindow=$true;$si.RedirectStandardOutput=$true;$si.RedirectStandardError=$true
   $cap=[Diagnostics.Process]::Start($si)
   $stderr=$cap.StandardError.ReadToEndAsync()
   try{
    $shortcuts=[FnShortcuts]::new()
    $shortcuts.StartHook()
    $observe=[Action[bool,int,bool]][Delegate]::CreateDelegate([Action[bool,int,bool]],$shortcuts,'Observe')
    Log 'Fn shortcuts ready'
   }catch{
    if($shortcuts){$shortcuts.Dispose();$shortcuts=$null}
    $observe=$null
    Log "Fn shortcuts unavailable: $($_.Exception.Message)"
   }
   $engine.Start($cap.StandardOutput.BaseStream,$observe)
   Log "CAPTURE START pid=$($cap.Id) hub=$($c.Hub) address=$($c.Address)"
   $last=[Environment]::TickCount64
   while(-not $stop.WaitOne(1000)){
    $now=[Environment]::TickCount64
    if($now-$last-gt 4000){throw 'Resume or prolonged scheduling gap; reconnect'}
    $last=$now
    if($shortcuts -and $shortcuts.Error -and !$fnErrorLogged){Log "Fn shortcuts: $($shortcuts.Error)";$fnErrorLogged=$true}
    if($cap.HasExited -or $engine.Worker.IsCompleted -or $engine.Error){throw "Stream ended/error: $($engine.Error)"}
    $dev=Get-PnpDevice -PresentOnly -InstanceId 'USB\VID_05AC&PID_0253&MI_00\*' -ErrorAction SilentlyContinue
    if(-not $dev -or @($dev | Where-Object Status -eq 'OK').Count -ne 1){throw 'Keyboard unavailable'}
   }
  }catch{Log "RETRY $($_.Exception.Message)"}
  finally{
   if($shortcuts){$shortcuts.Dispose()}
   if($engine){$engine.Dispose()}
   if($cap){if(-not $cap.HasExited){$cap.Kill();$cap.WaitForExit()};$cap.Dispose()}
  }
  if(-not $stop.WaitOne(0)){$stop.WaitOne(3000) | Out-Null}
 }
}finally{$stop.Dispose();$mutex.ReleaseMutex();$mutex.Dispose();Log 'STOP'}
