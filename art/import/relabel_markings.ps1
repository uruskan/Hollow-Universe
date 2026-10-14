# Redraws three plates of the PSG Starter Kit's markings atlas with Hollow's names (GROUND.md §9: rename the kits'
# Rust item names). Same layout, font and colours as the kit's own make_markings_windows.ps1.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File art/import/relabel_markings.ps1 -Path <T_PSG_Markings_BaseColor.png>
param([Parameter(Mandatory = $true)][string]$Path)
Add-Type -AssemblyName System.Drawing

# Plate index in the atlas -> title, subtitle. 7 was "P-250", 8 "K-47", 11 "BREACH PACK / FICTIONAL GAME PROP".
$plates = @{
    7  = @('SERVICE PISTOL', 'FIELD SIDEARM / RECOVERED');
    8  = @('FRONTIER RIFLE', 'SALVAGE PATTERN / MK.02');
    11 = @('TIMED CHARGE', 'HANDLE WITH CARE / KEEP CLEAR')
}

$source = [System.Drawing.Image]::FromFile((Resolve-Path $Path))
$bmp = New-Object System.Drawing.Bitmap($source)
$source.Dispose()
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
function C($r, $gg, $b) { return [System.Drawing.Color]::FromArgb(255, $r, $gg, $b) }
function Rect($x, $y, $w, $h, $color) {
    $br = New-Object System.Drawing.SolidBrush($color)
    $g.FillRectangle($br, [single]$x, [single]$y, [single]$w, [single]$h)
    $br.Dispose()
}
function Text($s, $x, $y, $size, $color, $bold = $false) {
    $style = [System.Drawing.FontStyle]::Regular
    if ($bold) { $style = [System.Drawing.FontStyle]::Bold }
    $font = New-Object System.Drawing.Font('Consolas', [single]$size, $style, [System.Drawing.GraphicsUnit]::Pixel)
    $br = New-Object System.Drawing.SolidBrush($color)
    $g.DrawString([string]$s, $font, $br, [single]$x, [single]$y)
    $br.Dispose(); $font.Dispose()
}
$rng = New-Object System.Random(4110)
foreach ($k in ($plates.Keys | Sort-Object)) {
    $x = ($k % 4) * 512; $y = (7 - [math]::Floor($k / 4)) * 256
    Rect $x $y 512 256 (C 32 39 38)
    Rect ($x + 8) ($y + 8) 496 240 (C 137 155 133)
    Rect ($x + 11) ($y + 11) 490 234 (C 32 39 38)
    Rect ($x + 22) ($y + 22) 468 29 (C 168 119 46)
    Text 'PSG // FRONTIER EQUIPMENT' ($x + 31) ($y + 25) 20 (C 30 36 33) $true
    $title = $plates[$k][0]
    $size = 42; if ($title.Length -gt 15) { $size = 34 }
    Text $title ($x + 27) ($y + 82) $size (C 213 216 187) $true
    Rect ($x + 28) ($y + 151) 451 2 (C 111 136 113)
    Text $plates[$k][1] ($x + 28) ($y + 170) 19 (C 163 181 156)
    Text ('R.02 / ' + (7100 + $k * 13) + ' / INSPECTED') ($x + 28) ($y + 217) 16 (C 125 145 128)
    for ($j = 0; $j -lt 37; $j++) { Rect ($x + 316 + $rng.Next(162)) ($y + 207) (1 + $rng.Next(2)) 28 (C 155 173 148) }
    for ($j = 0; $j -lt 80; $j++) { Rect ($x + 12 + $rng.Next(485)) ($y + 12 + $rng.Next(228)) (1 + $rng.Next(5)) 1 (C 51 61 52) }
}
$g.Dispose()
$bmp.Save((Resolve-Path $Path), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "RELABELLED $Path"
