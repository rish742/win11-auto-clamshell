#Requires -RunAsAdministrator
# Builds C:\Scripts\AutoLidAction.cs and (re)registers it to run at your logon.
# Run in an *Administrator* PowerShell:  powershell -ExecutionPolicy Bypass -File .\Install-AutoLidAction.ps1

$ErrorActionPreference = 'Stop'
$dir  = 'C:\Scripts'
$src  = Join-Path $dir 'AutoLidAction.cs'
$exe  = Join-Path $dir 'AutoLidAction.exe'
$task = 'Auto Lid Action'
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $src)) { throw "Copy AutoLidAction.cs to $dir first." }
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }

# Stop the old version so the .exe can be overwritten
Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
Get-Process AutoLidAction -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

& $csc /nologo /target:winexe /optimize+ /out:$exe /reference:System.Windows.Forms.dll /reference:System.Management.dll $src
if ($LASTEXITCODE -ne 0) { throw 'Compile failed - see errors above.' }

$user      = "$env:USERDOMAIN\$env:USERNAME"
$action    = New-ScheduledTaskAction -Execute $exe
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
               -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew `
               -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable

Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $task

Write-Host "Installed and running."
Write-Host "Log: $env:LOCALAPPDATA\AutoLidAction\AutoLidAction.log"
