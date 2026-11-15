#Requires -Version 7.4
#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess)]
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
if (-not ($IsWindows -and [Environment]::Is64BitProcess)) {
    throw 'Run this script in 64-bit PowerShell 7 on Windows.'
}
$taskName = 'BootCampEjectFnShortcuts'
$taskDescription = 'Boot Camp Eject and Fn Shortcuts (managed by install.ps1)'
$programFiles = [Environment]::GetFolderPath('ProgramFiles')
$destination = Join-Path $programFiles 'BootCampEjectFnShortcuts'
$entryPoint = Join-Path $destination 'eject-service.ps1'
$powerShell = Join-Path $programFiles 'PowerShell\7\pwsh.exe'
$arguments = '-NoProfile -WindowStyle Hidden -File "{0}"' -f $entryPoint
$files = @('eject-service.ps1', 'stop.ps1', 'EjectPcapReader.cs', 'EjectEngine.cs', 'FnShortcuts.cs')
$existing = Get-ScheduledTask -TaskPath '\' -TaskName $taskName -ErrorAction SilentlyContinue

if ($Uninstall) {
    if (!$existing) { Write-Output 'Scheduled task is already absent.'; return }
    if ($existing.Description -ne $taskDescription -or @($existing.Actions).Count -ne 1 -or
        $existing.Actions[0].Execute -ne $powerShell -or $existing.Actions[0].Arguments -ne $arguments) {
        throw 'The existing task does not match this installer.'
    }
    if (!$PSCmdlet.ShouldProcess($taskName, 'Stop and remove the scheduled task')) { return }

    Disable-ScheduledTask -TaskPath '\' -TaskName $taskName | Out-Null
    & (Join-Path $destination 'stop.ps1')
    $entryPattern = '*-File "' + $entryPoint + '"*'
    function GetOwnedWorker {
        Get-CimInstance Win32_Process -Filter "Name='pwsh.exe'" | Where-Object {
            $_.ExecutablePath -eq $powerShell -and $_.CommandLine -like $entryPattern
        }
    }
    for ($i = 0; $i -lt 30 -and (GetOwnedWorker); $i++) {
        Start-Sleep -Milliseconds 250
    }
    if (GetOwnedWorker) {
        throw 'The worker is still active. Run uninstall from the same desktop session after it exits.'
    }
    Unregister-ScheduledTask -TaskPath '\' -TaskName $taskName -Confirm:$false
    Write-Output "Task removed. Installed files and technical logs remain in: $destination"
    return
}

if (!$PSCmdlet.ShouldProcess($destination, 'Install scripts and register an interactive elevated logon task')) { return }
if ($existing) { throw 'Task already exists. Uninstall it before reinstalling.' }
if (Get-ScheduledTask -TaskName EjectToDelete -ErrorAction SilentlyContinue) {
    throw 'An EjectToDelete task already exists. Remove the old installation before installing this package.'
}
if (Test-Path -LiteralPath $destination) {
    throw "Install directory already exists. Review and move it before reinstalling: $destination"
}
if (!(Test-Path -LiteralPath $powerShell -PathType Leaf)) {
    throw 'Install 64-bit PowerShell 7 using the Microsoft MSI before proceeding.'
}
$usb = Join-Path $programFiles 'USBPcap\USBPcapCMD.exe'
if (!(Test-Path -LiteralPath $usb -PathType Leaf)) {
    throw 'Install USBPcap in its default Program Files location and reboot first.'
}
$keyboard = Get-PnpDevice -PresentOnly -InstanceId 'USB\VID_05AC&PID_0253&MI_00\*' -ErrorAction SilentlyContinue
if (@($keyboard | Where-Object Status -eq 'OK').Count -ne 1) {
    throw 'Expected exactly one matching built-in Apple Keyboard (05AC:0253, interface 0).'
}
$account = [Security.Principal.WindowsIdentity]::GetCurrent().Name
if ((Get-Process -Id $PID).SessionId -eq 0) {
    throw 'Run from the interactive Windows account that will use the remap.'
}
$usbDriver = Get-CimInstance Win32_SystemDriver -Filter "Name='USBPcap'"
if (!$usbDriver -or $usbDriver.State -ne 'Running') {
    throw 'USBPcap driver is not running. Reboot after installing USBPcap.'
}

New-Item -ItemType Directory -Path $destination | Out-Null
try {
    foreach ($name in $files) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "src\$name") -Destination (Join-Path $destination $name)
    }
    & $powerShell -NoProfile -File $entryPoint -CheckOnly
    if ($LASTEXITCODE -ne 0) { throw 'Installed source failed the compilation check.' }
    $action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments -WorkingDirectory $destination
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $account
    $principal = New-ScheduledTaskPrincipal -UserId $account -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -StartWhenAvailable
    Register-ScheduledTask -TaskName $taskName -TaskPath '\' -Description $taskDescription -Action $action `
        -Trigger $trigger -Principal $principal -Settings $settings | Out-Null
} catch {
    foreach ($name in $files) {
        $path = Join-Path $destination $name
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
    }
    if (!(Get-ChildItem -LiteralPath $destination -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $destination
    }
    throw
}
Start-ScheduledTask -TaskPath '\' -TaskName $taskName
Write-Output "Task started for $account. See the technical log at $destination\logs\service.log if shortcuts do not work."
