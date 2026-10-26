# School Network Manager - agent installer (run as Administrator; install.bat does this for you)
$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = Join-Path $env:ProgramData 'SchoolNetworkManager\Agent'
$taskName = 'SchoolNetworkManager Agent'

New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $source 'snm-agent.ps1') -Destination $target -Force
Copy-Item -Path (Join-Path $source 'config.json') -Destination $target -Force

# Only SYSTEM and Administrators may read the folder (it contains the agent key).
& icacls.exe $target /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' /T | Out-Null

$script = Join-Path $target 'snm-agent.ps1'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`""

$atStartup = New-ScheduledTaskTrigger -AtStartup
$atStartup.Delay = 'PT2M'
$atLogon = New-ScheduledTaskTrigger -AtLogOn
$atLogon.Delay = 'PT1M'
$every2h = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(5) -RepetitionInterval (New-TimeSpan -Hours 2)

$principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 15) -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($atStartup, $atLogon, $every2h) `
    -Principal $principal -Settings $settings -Description 'Sends hardware/software information to School Network Manager' -Force | Out-Null

# First report right now
Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -Wait `
    -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$script`" -now"

$log = Join-Path $target 'agent.log'
if (Test-Path $log) { Get-Content $log -Tail 1 }
