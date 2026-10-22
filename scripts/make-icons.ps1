# web/public altina icon-192.png, icon-512.png (yuvarlak koseli, "any") ile icon-maskable-512.png ve
# apple-touch-icon.png (180) (tam kaplayan kare: seffaf kose yok, isletim sistemi kendi maskesini uygular) uretir.
# icon.svg ile ayni cizim: kahverengi yuvarlak kare + krem somun + uc cizik.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'web\public'

function New-Icon([int]$size, [string]$name, [bool]$fullBleed = $false) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $s = $size / 512.0
    $brown = [System.Drawing.ColorTranslator]::FromHtml('#b5651d')
    $cream = [System.Drawing.ColorTranslator]::FromHtml('#fff5e6')

    $r = 96 * $s; $d = 2 * $r
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    if ($fullBleed) { $g.Clear($brown) } else { $g.FillPath((New-Object System.Drawing.SolidBrush $brown), $path) }

    $g.FillEllipse((New-Object System.Drawing.SolidBrush $cream), (86 * $s), (168 * $s), (340 * $s), (216 * $s))

    $pen = New-Object System.Drawing.Pen $brown, (22 * $s)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    foreach ($l in @(@(176, 236, 212, 316), @(238, 226, 274, 326), @(300, 236, 336, 316))) {
        $g.DrawLine($pen, ($l[0] * $s), ($l[1] * $s), ($l[2] * $s), ($l[3] * $s))
    }

    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "Yazildi: $name"
}

New-Icon 192 'icon-192.png'
New-Icon 512 'icon-512.png'
New-Icon 512 'icon-maskable-512.png' $true
New-Icon 180 'apple-touch-icon.png' $true
