<#
  School Network Manager - screen helper.

  Runs INSIDE the logged-in user's session (not as SYSTEM), because only there can it
  capture what the user sees. It asks the server every few seconds whether a teacher is
  currently viewing the screen wall; only then does it capture a small thumbnail of the
  primary screen and upload it. Nothing is captured otherwise, and nothing is stored on disk.
#>

$ErrorActionPreference = 'SilentlyContinue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

try {
    $cfg = Get-Content -Raw -Path (Join-Path $here 'screen.json') -Encoding UTF8 | ConvertFrom-Json
} catch {
    try { Add-Content -Path (Join-Path $env:TEMP 'snm-screen.log') -Value ("{0:yyyy-MM-dd HH:mm:ss}  screen.json: {1}" -f (Get-Date), $_.Exception.Message) } catch { }
    exit 1
}

$server = ([string]$cfg.server).TrimEnd('/')
$key = [string]$cfg.screenKey
$hostName = $env:COMPUTERNAME
$headers = @{ 'X-Agent-Key' = $key }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

function Get-Thumbnail([int]$maxWidth = 480, [int]$quality = 45) {
    try {
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        if ($bounds.Width -le 0) { return $null }

        $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $g.Dispose()

        $ratio = [double]$maxWidth / $bounds.Width
        if ($ratio -gt 1) { $ratio = 1 }
        $w = [int]($bounds.Width * $ratio)
        $h = [int]($bounds.Height * $ratio)

        $thumb = New-Object System.Drawing.Bitmap $w, $h
        $tg = [System.Drawing.Graphics]::FromImage($thumb)
        $tg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $tg.DrawImage($bmp, 0, 0, $w, $h)
        $tg.Dispose()
        $bmp.Dispose()

        $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' } | Select-Object -First 1
        $params = New-Object System.Drawing.Imaging.EncoderParameters 1
        $params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality, [long]$quality)

        $ms = New-Object System.IO.MemoryStream
        $thumb.Save($ms, $codec, $params)
        $thumb.Dispose()
        $bytes = $ms.ToArray()
        $ms.Dispose()
        return $bytes
    } catch { return $null }
}

while ($true) {
    $wanted = $false
    try {
        $resp = Invoke-RestMethod -Method Get -Uri "$server/api/agent/screen-wanted?hostname=$hostName" -Headers $headers -TimeoutSec 15 -UseBasicParsing
        $wanted = [bool]$resp.wanted
    } catch { $wanted = $false }

    if ($wanted) {
        $bytes = Get-Thumbnail
        if ($bytes -and $bytes.Length -gt 0) {
            try {
                Invoke-RestMethod -Method Post -Uri "$server/api/agent/screen?hostname=$hostName" `
                    -Body $bytes -ContentType 'image/jpeg' -Headers $headers -TimeoutSec 20 -UseBasicParsing | Out-Null
            } catch { }
        }
        Start-Sleep -Seconds 2
    } else {
        Start-Sleep -Seconds 6
    }
}
