<#
  School Network Manager - agent for school computers
  Collects hardware, Windows, network, Veyon and installed software information
  and sends it to the School Network Manager server.

  Runs as SYSTEM from a scheduled task (see install.ps1). Reads config.json from its own folder.
  Only collects information - it never changes anything on the computer.
#>

$AgentVersion = '1.0.0'
$ErrorActionPreference = 'SilentlyContinue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$logFile = Join-Path $here 'agent.log'

function Write-Log([string]$text) {
    try {
        $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $text
        Add-Content -Path $logFile -Value $line -Encoding UTF8
        $all = Get-Content -Path $logFile -Encoding UTF8
        if ($all.Count -gt 300) { $all | Select-Object -Last 200 | Set-Content -Path $logFile -Encoding UTF8 }
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

# Small random delay so that a whole lab starting at the same time does not hit the server together.
if ($args -notcontains '-now') { Start-Sleep -Seconds (Get-Random -Minimum 0 -Maximum 45) }

# ---------------- System ----------------
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

# ---------------- Disks ----------------
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

# ---------------- Network ----------------
$data.network = @(Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=TRUE' | Where-Object { $_.MACAddress } | ForEach-Object {
    [ordered]@{
        description = $_.Description
        mac         = $_.MACAddress
        ips         = @($_.IPAddress | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' })
        gateway     = (@($_.DefaultIPGateway) | Select-Object -First 1)
        dhcp        = [bool]$_.DHCPEnabled
    }
})

# ---------------- Installed software ----------------
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
            name        = ([string]$p.DisplayName).Trim()
            version     = [string]$p.DisplayVersion
            publisher   = [string]$p.Publisher
            installDate = [string]$p.InstallDate
            scope       = $scope
        })
    }
}

Add-FromUninstallKey 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' 'machine'
Add-FromUninstallKey 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall' 'machine'

# Per-user installs (e.g. games installed by students in their own profile) - users currently signed in.
if (-not (Test-Path 'HKU:\')) { New-PSDrive -Name HKU -PSProvider Registry -Root HKEY_USERS | Out-Null }
Get-ChildItem 'HKU:\' -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^S-1-5-21-[\d-]+$' } | ForEach-Object {
    Add-FromUninstallKey ("HKU:\{0}\Software\Microsoft\Windows\CurrentVersion\Uninstall" -f $_.PSChildName) 'user'
}

# Microsoft Store apps installed by users (games etc.), without Windows' own components.
try {
    Get-AppxPackage -AllUsers -ErrorAction Stop |
        Where-Object { -not $_.IsFramework -and $_.SignatureKind -eq 'Store' -and $_.Name -notmatch '^(Microsoft|MicrosoftWindows|Windows|MicrosoftCorporationII|NVIDIA|AppUp|RealtekSemiconductor|AD2F1837)\.' } |
        ForEach-Object {
            $name = $_.Name
            try {
                $manifest = Get-AppxPackageManifest -Package $_.PackageFullName -ErrorAction Stop
                $display = [string]$manifest.Package.Properties.DisplayName
                if ($display -and $display -notmatch '^ms-resource:') { $name = $display }
            } catch { }
            [void]$software.Add([ordered]@{
                name = $name; version = [string]$_.Version; publisher = [string]$_.PublisherId; installDate = ''; scope = 'store'
            })
        }
} catch { }

$data.software = @($software)

# ---------------- Veyon ----------------
$veyonEntry = $software | Where-Object { $_.name -match '^Veyon' } | Select-Object -First 1
$veyonService = Get-Service -Name 'VeyonService' -ErrorAction SilentlyContinue
$data.veyon = [ordered]@{
    installed = [bool]($veyonEntry -or $veyonService)
    version   = if ($veyonEntry) { $veyonEntry.version } else { $null }
    service   = if ($veyonService) { [string]$veyonService.Status } else { 'Missing' }
}

# ---------------- Send ----------------
$json = $data | ConvertTo-Json -Depth 6 -Compress
$body = [System.Text.Encoding]::UTF8.GetBytes($json)

try {
    $response = Invoke-RestMethod -Method Post -Uri "$server/api/agent/report" -Body $body `
        -ContentType 'application/json; charset=utf-8' -Headers @{ 'X-Agent-Key' = $key } -TimeoutSec 60 -UseBasicParsing
    Write-Log ("OK  {0} programs  {1}" -f $software.Count, $response.message)
} catch {
    Write-Log ("ERROR sending report: {0}" -f $_.Exception.Message)
    exit 2
}

# ---------------- Self-update ----------------
try {
    if ($response.latestVersion -and ([version]$response.latestVersion -gt [version]$AgentVersion)) {
        $temp = Join-Path $here 'snm-agent.new.ps1'
        Invoke-WebRequest -Uri "$server/api/agent/script" -Headers @{ 'X-Agent-Key' = $key } -OutFile $temp -UseBasicParsing -TimeoutSec 60
        if ((Get-Content -Raw -Path $temp) -match '\$AgentVersion = ') {
            Copy-Item -Path $temp -Destination $MyInvocation.MyCommand.Path -Force
            Write-Log ("Agent updated to {0}" -f $response.latestVersion)
        }
        Remove-Item -Path $temp -Force -ErrorAction SilentlyContinue
    }
} catch {
    Write-Log ("Update failed: {0}" -f $_.Exception.Message)
}
