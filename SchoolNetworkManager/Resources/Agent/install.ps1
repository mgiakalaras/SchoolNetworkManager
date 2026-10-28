# School Network Manager - agent installer (run as Administrator; install.bat does this for you)
$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = Join-Path $env:ProgramData 'SchoolNetworkManager\Agent'
$taskName = 'SchoolNetworkManager Agent'
$screenTaskName = 'SchoolNetworkManager Screen'
# Ο βοηθός οθόνης τρέχει ως απλός χρήστης → δικός του φάκελος, αναγνώσιμος (χωρίς το κλειδί του agent).
$screenDir = Join-Path $env:ProgramData 'SchoolNetworkManager\Screen'
$avOk = $false

# Σταμάτα τον agent αν τρέχει ήδη (αναβάθμιση), για να μην είναι κλειδωμένο το αρχείο.
Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
Stop-ScheduledTask -TaskName $screenTaskName -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $source 'snm-agent.ps1') -Destination $target -Force
Copy-Item -Path (Join-Path $source 'config.json') -Destination $target -Force
if (Test-Path (Join-Path $source 'snm-screen.ps1')) {
    New-Item -ItemType Directory -Path $screenDir -Force | Out-Null

    # Ο Defender μπλοκάρει ευρετικά το script λήψης οθόνης ("ScriptContainedMaliciousContent").
    # Είναι δικό μας εργαλείο σε δικούς μας υπολογιστές → ρητή εξαίρεση ΜΟΝΟ για αυτόν τον φάκελο,
    # ΠΡΙΝ αντιγράψουμε το script (αλλιώς μπορεί να το μπλοκάρει την ώρα της εγγραφής).
    try {
        if (Get-Command Add-MpPreference -ErrorAction SilentlyContinue) {
            Add-MpPreference -ExclusionPath $screenDir -ErrorAction Stop
            $avOk = $true
        }
    } catch { }

    Copy-Item -Path (Join-Path $source 'snm-screen.ps1') -Destination $screenDir -Force
    if (Test-Path (Join-Path $source 'screen.json')) { Copy-Item -Path (Join-Path $source 'screen.json') -Destination $screenDir -Force }

    # Ανάγνωση για όλους τους χρήστες, αλλαγή μόνο από SYSTEM/Admins (για να μην πειράξει κανείς το script).
    & icacls.exe $screenDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
    & icacls.exe (Join-Path $screenDir '*') /reset | Out-Null

    # Παλιά θέση (2.15 πρώτη έκδοση): καθάρισμα
    Remove-Item -Path (Join-Path $target 'snm-screen.ps1'), (Join-Path $target 'screen.json') -Force -ErrorAction SilentlyContinue
}

# Only SYSTEM and Administrators may read the folder (it contains the agent key).
& icacls.exe $target /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
# The files inherit exactly these permissions from the folder
& icacls.exe (Join-Path $target '*') /reset | Out-Null

$script = Join-Path $target 'snm-agent.ps1'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`""

$atStartup = New-ScheduledTaskTrigger -AtStartup
$atStartup.Delay = 'PT1M'
$atLogon = New-ScheduledTaskTrigger -AtLogOn
# Keep-alive: every 10 λεπτά ξαναξεκινά τον agent ΜΟΝΟ αν δεν τρέχει ήδη (MultipleInstances IgnoreNew).
$keepAlive = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(5) -RepetitionInterval (New-TimeSpan -Minutes 10)

$principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartInterval (New-TimeSpan -Minutes 2) -RestartCount 3 -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($atStartup, $atLogon, $keepAlive) `
    -Principal $principal -Settings $settings -Description 'Sends hardware/software information to School Network Manager' -Force | Out-Null

# Τοίχος οθονών: τρέχει στη συνεδρία του συνδεδεμένου χρήστη (όχι SYSTEM), ώστε να «βλέπει» την οθόνη.
$screen = Join-Path $screenDir 'snm-screen.ps1'
if (Test-Path $screen) {
    $screenAction = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$screen`""
    $screenLogon = New-ScheduledTaskTrigger -AtLogOn
    $screenKeepAlive = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(5) -RepetitionInterval (New-TimeSpan -Minutes 10)
    $screenPrincipal = New-ScheduledTaskPrincipal -GroupId 'S-1-5-32-545' -RunLevel Limited
    $screenSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $screenTaskName -Action $screenAction -Trigger @($screenLogon, $screenKeepAlive) `
        -Principal $screenPrincipal -Settings $screenSettings -Description 'School Network Manager - screen wall thumbnails (runs in the user session)' -Force | Out-Null
    # Ξεκίνα τον τώρα στη συνεδρία του ήδη συνδεδεμένου χρήστη (χωρίς να περιμένει νέα σύνδεση).
    Start-ScheduledTask -TaskName $screenTaskName -ErrorAction SilentlyContinue
}

# First report right now (one-shot), then start the resident task
Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -Wait `
    -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$script`" -Once"
Start-ScheduledTask -TaskName $taskName

$log = Join-Path $target 'agent.log'
$server = (Get-Content -Raw -Path (Join-Path $target 'config.json') | ConvertFrom-Json).server
Write-Output ''
Write-Output (' Server: ' + $server)
if (Test-Path $screenDir) {
    if ($avOk) {
        Write-Output ' Τοίχος οθονών: OK (μπήκε εξαίρεση Defender μόνο για τον φάκελο Screen).'
    } else {
        Write-Output ' Τοίχος οθονών: ΠΡΟΣΟΧΗ - το antivirus ίσως μπλοκάρει τη λήψη οθόνης.'
        Write-Output ('   Βάλε χειροκίνητα εξαίρεση φακέλου στο antivirus: ' + $screenDir)
    }
}
if (Test-Path $log) {
    $last = Get-Content $log -Tail 1
    Write-Output (' First report: ' + $last)
    if ($last -notmatch '  OK  ') { Write-Output ' WARNING: the first report failed - check that this computer can open the server address above.' }
} else {
    Write-Output ' WARNING: the agent did not run. Run install.bat again as Administrator.'
}
