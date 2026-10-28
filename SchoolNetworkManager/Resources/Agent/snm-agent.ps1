<#
  School Network Manager - agent for school computers.

  Two jobs:
   1) Inventory: hardware, Windows, network, Veyon and installed software (every 2 hours).
   2) Commands : asks the server every few seconds for pending actions that the
                 lab administrator sent (shutdown / restart / logoff / message /
                 lock / cancel) and carries them out. Runs as SYSTEM.

  Runs resident from a scheduled task (see install.ps1). Reads config.json from its own folder.
  It only changes the computer when an administrator explicitly sends a command from the app.
#>

param([switch]$Once)

$AgentVersion = '1.2.0'
$ErrorActionPreference = 'SilentlyContinue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$logFile = Join-Path $here 'agent.log'

function Write-Log([string]$text) {
    try {
        $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $text
        Add-Content -Path $logFile -Value $line -Encoding UTF8
        $all = Get-Content -Path $logFile -Encoding UTF8
        if ($all.Count -gt 400) { $all | Select-Object -Last 250 | Set-Content -Path $logFile -Encoding UTF8 }
    } catch { }
}

function To-Gb($bytes) { if ($bytes) { [math]::Round([double]$bytes / 1GB, 1) } else { 0 } }

# ---------------- Settings ----------------
try {
    $config = Get-Content -Raw -Path (Join-Path $here 'config.json') -Encoding UTF8 | ConvertFrom-Json
} catch {
    Write-Log 'ERROR: config.json not found or invalid.'
    exit 1
}

$server = ([string]$config.server).TrimEnd('/')
$key = [string]$config.key
$headers = @{ 'X-Agent-Key' = $key }

# How often to ask the server for pending commands (seconds).
$pollSeconds = 6
if ($config.pollSeconds) { $pollSeconds = [int]$config.pollSeconds }

# ======================================================================
#  RustDesk helpers
# ======================================================================
function Get-RustDeskId {
    # The ID lives in RustDesk2.toml under ProgramData or the user profile.
    $paths = @(
        (Join-Path $env:ProgramData 'RustDesk\config\RustDesk2.toml'),
        (Join-Path $env:APPDATA 'RustDesk\config\RustDesk2.toml')
    )
    foreach ($p in $paths) {
        if (Test-Path $p) {
            $line = (Get-Content -Path $p -ErrorAction SilentlyContinue | Where-Object { $_ -match "^\s*id\s*=" } | Select-Object -First 1)
            if ($line -match "=\s*'?""?([0-9]{6,})") { return $Matches[1] }
        }
    }
    $exe = Get-RustDeskExe
    if ($exe) {
        $out = & $exe --get-id 2>$null
        if ($out -match '([0-9]{6,})') { return $Matches[1] }
    }
    return $null
}

function Get-RustDeskExe {
    foreach ($p in @("$env:ProgramFiles\RustDesk\rustdesk.exe", "${env:ProgramFiles(x86)}\RustDesk\rustdesk.exe")) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

function Apply-RustDeskConfig($rd) {
    # Points RustDesk at the school's own server and sets the permanent password.
    # Only runs when the server asks for it (config pushed from Settings).
    if (-not $rd -or -not $rd.host) { return }
    $exe = Get-RustDeskExe
    if (-not $exe) { return }
    try {
        $marker = Join-Path $here 'rustdesk.applied'
        $want = "$($rd.host)|$($rd.key)"
        $have = if (Test-Path $marker) { Get-Content -Raw -Path $marker } else { '' }
        if ($have.Trim() -ne $want) {
            & $exe --config "$($rd.host)" 2>$null | Out-Null
            if ($rd.key) { & $exe --option custom-rendezvous-server "$($rd.host)" 2>$null | Out-Null }
            Set-Content -Path $marker -Value $want -Encoding UTF8
            Write-Log "RustDesk pointed to $($rd.host)"
        }
        if ($rd.password) { & $exe --password "$($rd.password)" 2>$null | Out-Null }
    } catch { Write-Log "RustDesk config failed: $($_.Exception.Message)" }
}

# ======================================================================
#  Inventory
# ======================================================================
function Send-Inventory {
    $cs = Get-CimInstance Win32_ComputerSystem
    $os = Get-CimInstance Win32_OperatingSystem
    $bios = Get-CimInstance Win32_BIOS
    $product = Get-CimInstance Win32_ComputerSystemProduct
    $cpu = @(Get-CimInstance Win32_Processor)
    $winKey = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'

    $displayVersion = $winKey.DisplayVersion
    if (-not $displayVersion) { $displayVersion = $winKey.ReleaseId }

    $chassis = 'desktop'
    if ($cs.PCSystemType -eq 2) { $chassis = 'laptop' }

    $serial = $bios.SerialNumber
    if (-not $serial -or $serial -match 'To be filled|Default') { $serial = $product.IdentifyingNumber }

    $data = [ordered]@{
        agentVersion = $AgentVersion
        hostname     = $env:COMPUTERNAME
        domain       = $cs.Domain
        user         = $cs.UserName
        manufacturer = $cs.Manufacturer
        model        = $cs.Model
        serial       = $serial
        chassis      = $chassis
        rustDeskId   = (Get-RustDeskId)
        os           = [ordered]@{
            name           = $os.Caption
            version        = $os.Version
            build          = [string]$os.BuildNumber
            displayVersion = $displayVersion
            arch           = $os.OSArchitecture
            lastBoot       = if ($os.LastBootUpTime) { $os.LastBootUpTime.ToString('s') } else { $null }
        }
        cpu          = [ordered]@{
            name    = ($cpu | Select-Object -First 1).Name.Trim()
            cores   = [int](($cpu | Measure-Object -Property NumberOfCores -Sum).Sum)
            threads = [int](($cpu | Measure-Object -Property NumberOfLogicalProcessors -Sum).Sum)
        }
        ramGb        = To-Gb $cs.TotalPhysicalMemory
        gpu          = @(Get-CimInstance Win32_VideoController | Where-Object { $_.Name -notmatch 'Remote|Mirage|Virtual|Basic Display' } | ForEach-Object { $_.Name.Trim() })
    }

    $data.disks = @(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | ForEach-Object {
        [ordered]@{ drive = $_.DeviceID; label = $_.VolumeName; sizeGb = (To-Gb $_.Size); freeGb = (To-Gb $_.FreeSpace) }
    })

    $physical = @()
    try {
        $physical = @(Get-PhysicalDisk | Where-Object { $_.BusType -ne 'USB' } | ForEach-Object {
            [ordered]@{ model = $_.FriendlyName; mediaType = [string]$_.MediaType; sizeGb = (To-Gb $_.Size) }
        })
    } catch { }
    if ($physical.Count -eq 0) {
        $physical = @(Get-CimInstance Win32_DiskDrive | Where-Object { $_.InterfaceType -ne 'USB' } | ForEach-Object {
            [ordered]@{ model = $_.Model; mediaType = ''; sizeGb = (To-Gb $_.Size) }
        })
    }
    $data.physicalDisks = $physical

    $virtualPattern = 'Virtual|ZeroTier|VPN|TAP-|Hyper-V|VMware|VirtualBox|Tailscale|WireGuard|Loopback|Bluetooth|WAN Miniport|Npcap'
    $data.network = @(Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=TRUE' | Where-Object { $_.MACAddress } |
        Sort-Object -Property @{ Expression = { $_.Description -match $virtualPattern } } | ForEach-Object {
        [ordered]@{
            description = $_.Description
            virtual     = [bool]($_.Description -match $virtualPattern)
            mac         = $_.MACAddress
            ips         = @($_.IPAddress | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' })
            gateway     = (@($_.DefaultIPGateway) | Select-Object -First 1)
            dhcp        = [bool]$_.DHCPEnabled
        }
    })

    $software = New-Object System.Collections.ArrayList
    function Add-FromUninstallKey([string]$path, [string]$scope) {
        Get-ChildItem -Path $path -ErrorAction SilentlyContinue | ForEach-Object {
            $p = Get-ItemProperty -Path $_.PSPath -ErrorAction SilentlyContinue
            if (-not $p.DisplayName) { return }
            if ($p.SystemComponent -eq 1) { return }
            if ($p.ParentKeyName) { return }
            if ($p.ReleaseType -match 'Update|Hotfix|Security') { return }
            if ($p.DisplayName -match '^(Security Update|Update for|Hotfix for)') { return }
            [void]$software.Add([ordered]@{
                name = ([string]$p.DisplayName).Trim(); version = [string]$p.DisplayVersion
                publisher = [string]$p.Publisher; installDate = [string]$p.InstallDate; scope = $scope
            })
        }
    }
    Add-FromUninstallKey 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' 'machine'
    Add-FromUninstallKey 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall' 'machine'
    if (-not (Test-Path 'HKU:\')) { New-PSDrive -Name HKU -PSProvider Registry -Root HKEY_USERS | Out-Null }
    Get-ChildItem 'HKU:\' -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^S-1-5-21-[\d-]+$' } | ForEach-Object {
        Add-FromUninstallKey ("HKU:\{0}\Software\Microsoft\Windows\CurrentVersion\Uninstall" -f $_.PSChildName) 'user'
    }
    try {
        Get-AppxPackage -AllUsers -ErrorAction Stop |
            Where-Object { -not $_.IsFramework -and $_.SignatureKind -eq 'Store' -and $_.Name -notmatch '^(Microsoft|MicrosoftWindows|Windows|MicrosoftCorporationII|NVIDIA|AppUp|RealtekSemiconductor|AD2F1837)\.' } |
            Where-Object { @($_.PackageUserInformation | Where-Object { [string]$_.InstallState -eq 'Installed' }).Count -gt 0 } |
            ForEach-Object {
                $name = $_.Name; $publisher = ''
                try {
                    $manifest = Get-AppxPackageManifest -Package $_.PackageFullName -ErrorAction Stop
                    $display = [string]$manifest.Package.Properties.DisplayName
                    if ($display -and $display -notmatch '^ms-resource:') { $name = $display }
                    $pubDisplay = [string]$manifest.Package.Properties.PublisherDisplayName
                    if ($pubDisplay -and $pubDisplay -notmatch '^ms-resource:') { $publisher = $pubDisplay }
                } catch { }
                if (-not $publisher -and $_.Publisher -match '(?:^|,\s*)(?:O|CN)=([^,]+)') { $publisher = $Matches[1].Trim('"') }
                [void]$software.Add([ordered]@{ name = $name; version = [string]$_.Version; publisher = $publisher; installDate = ''; scope = 'store' })
            }
    } catch { }
    $data.software = @($software)

    $veyonEntry = $software | Where-Object { $_.name -match '^Veyon' } | Select-Object -First 1
    $veyonService = Get-Service -Name 'VeyonService' -ErrorAction SilentlyContinue
    $data.veyon = [ordered]@{
        installed = [bool]($veyonEntry -or $veyonService)
        version   = if ($veyonEntry) { $veyonEntry.version } else { $null }
        service   = if ($veyonService) { [string]$veyonService.Status } else { 'Missing' }
    }

    $json = $data | ConvertTo-Json -Depth 6 -Compress
    $body = [System.Text.Encoding]::UTF8.GetBytes($json)
    try {
        $response = Invoke-RestMethod -Method Post -Uri "$server/api/agent/report" -Body $body `
            -ContentType 'application/json; charset=utf-8' -Headers $headers -TimeoutSec 60 -UseBasicParsing
        Write-Log ("OK  {0} programs  {1}" -f $software.Count, $response.message)
        if ($response.computerId) { $script:ComputerId = [int]$response.computerId }
        Update-Self $response.latestVersion
        return $true
    } catch {
        Write-Log ("ERROR sending report: {0}" -f $_.Exception.Message)
        return $false
    }
}

function Update-Self($latest) {
    try {
        if ($latest -and ([version]$latest -gt [version]$AgentVersion)) {
            $temp = Join-Path $here 'snm-agent.new.ps1'
            Invoke-WebRequest -Uri "$server/api/agent/script" -Headers $headers -OutFile $temp -UseBasicParsing -TimeoutSec 60
            if ((Get-Content -Raw -Path $temp) -match '\$AgentVersion = ') {
                Copy-Item -Path $temp -Destination $MyInvocation.MyCommand.Path -Force
                Write-Log ("Agent updated to {0} - restarting" -f $latest)
                Remove-Item -Path $temp -Force -ErrorAction SilentlyContinue
                # The scheduled task restarts the script at next trigger; exit so the new file is used.
                exit 0
            }
            Remove-Item -Path $temp -Force -ErrorAction SilentlyContinue
        }
    } catch { Write-Log ("Update failed: {0}" -f $_.Exception.Message) }
}

# ======================================================================
#  Commands
# ======================================================================
function Show-Message($text, $title) {
    if (-not $text) { return }
    # Show on the interactive desktop via msg.exe (works from SYSTEM).
    try { & "$env:WINDIR\System32\msg.exe" * /TIME:60 "$title`n`n$text" 2>$null } catch { }
}

function Invoke-Command2($cmd) {
    $delay = [int]$cmd.delay
    switch ($cmd.kind) {
        'shutdown' {
            Show-Message $cmd.text 'School Network Manager'
            & shutdown.exe /s /f /t $delay /c 'School Network Manager' | Out-Null
            return 'shutdown scheduled'
        }
        'restart' {
            Show-Message $cmd.text 'School Network Manager'
            & shutdown.exe /r /f /t $delay /c 'School Network Manager' | Out-Null
            return 'restart scheduled'
        }
        'cancel' {
            & shutdown.exe /a | Out-Null
            return 'cancelled'
        }
        'logoff' {
            & shutdown.exe /l | Out-Null
            return 'logoff'
        }
        'lock' {
            # Lock every interactive session.
            & "$env:WINDIR\System32\rundll32.exe" user32.dll,LockWorkStation | Out-Null
            return 'locked'
        }
        'message' {
            Show-Message $cmd.text 'Μήνυμα από τον διαχειριστή'
            return 'message shown'
        }
        default { return "unknown: $($cmd.kind)" }
    }
}

function Poll-Commands {
    if (-not $script:ComputerId) { return }
    try {
        $resp = Invoke-RestMethod -Method Get -Uri "$server/api/agent/commands?computerId=$($script:ComputerId)" `
            -Headers $headers -TimeoutSec 30 -UseBasicParsing
    } catch { return }

    if ($resp.rustdesk) { Apply-RustDeskConfig $resp.rustdesk }

    foreach ($cmd in @($resp.commands)) {
        $ok = $true; $msg = ''
        try { $msg = Invoke-Command2 $cmd } catch { $ok = $false; $msg = $_.Exception.Message }
        Write-Log ("CMD {0} #{1} -> {2}" -f $cmd.kind, $cmd.id, $msg)
        try {
            $rb = @{ id = [int]$cmd.id; computerId = [int]$script:ComputerId; ok = $ok; message = "$msg" } | ConvertTo-Json -Compress
            Invoke-RestMethod -Method Post -Uri "$server/api/agent/commands/result" `
                -Body ([System.Text.Encoding]::UTF8.GetBytes($rb)) `
                -ContentType 'application/json; charset=utf-8' -Headers $headers -TimeoutSec 30 -UseBasicParsing | Out-Null
        } catch { }
    }
}

# ======================================================================
#  Main
# ======================================================================
$script:ComputerId = $null

# A small random delay so a whole lab starting together does not hit the server at once.
if (-not $Once) { Start-Sleep -Seconds (Get-Random -Minimum 0 -Maximum 30) }

Send-Inventory | Out-Null

if ($Once) { return }

$lastInventory = Get-Date
while ($true) {
    Poll-Commands
    if ((Get-Date) - $lastInventory -gt [TimeSpan]::FromHours(2)) {
        if (Send-Inventory) { $lastInventory = Get-Date }
    }
    Start-Sleep -Seconds $pollSeconds
}
