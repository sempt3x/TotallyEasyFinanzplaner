param([string]$OutDir)
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [single]$size
    $small = $size -le 24

    # Hintergrund: abgerundetes Quadrat mit Blau-Verlauf
    $m = if ($small) { 0.0 } else { $s * 0.04 }
    $rect = New-Object System.Drawing.RectangleF $m, $m, ($s - 2 * $m), ($s - 2 * $m)
    $bg = New-RoundRect $rect.X $rect.Y $rect.Width $rect.Height ($s * 0.22)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, `
        ([System.Drawing.Color]::FromArgb(255, 0x3B, 0x82, 0xF6)), `
        ([System.Drawing.Color]::FromArgb(255, 0x1E, 0x3A, 0x8A)), 45.0
    $g.FillPath($grad, $bg)

    # Leichter Glanz oben
    if (-not $small) {
        $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, `
            ([System.Drawing.Color]::FromArgb(40, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90.0
        $g.FillPath($hl, $bg)
    }

    # Balken (Monatswerte), ab 32 px
    $base = $s * 0.80
    if ($size -ge 32) {
        $barBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(85, 255, 255, 255))
        $bw = $s * 0.13
        foreach ($b in @(@(0.21, 0.20), @(0.40, 0.30), @(0.59, 0.42))) {
            $bx = $s * $b[0]; $bh = $s * $b[1]
            $bar = New-RoundRect $bx ($base - $bh) $bw $bh ($s * 0.025)
            $g.FillPath($barBrush, $bar)
        }
    }

    # Trendlinie mit Punkt (steigender Kontostand)
    $lw = if ($small) { $s * 0.12 } else { $s * 0.065 }
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $lw
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $pts = if ($small) {
        @((New-Object System.Drawing.PointF ($s*0.18), ($s*0.72)), (New-Object System.Drawing.PointF ($s*0.42), ($s*0.50)),
          (New-Object System.Drawing.PointF ($s*0.58), ($s*0.60)), (New-Object System.Drawing.PointF ($s*0.80), ($s*0.30)))
    } else {
        @((New-Object System.Drawing.PointF ($s*0.19), ($s*0.60)), (New-Object System.Drawing.PointF ($s*0.40), ($s*0.44)),
          (New-Object System.Drawing.PointF ($s*0.56), ($s*0.52)), (New-Object System.Drawing.PointF ($s*0.78), ($s*0.27)))
    }
    $g.DrawLines($pen, [System.Drawing.PointF[]]$pts)
    $end = $pts[-1]
    $r = if ($small) { $s * 0.10 } else { $s * 0.065 }
    $g.FillEllipse([System.Drawing.Brushes]::White, $end.X - $r, $end.Y - $r, 2 * $r, 2 * $r)
    if (-not $small) {
        $ri = $r * 0.45
        $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x2F, 0x6F, 0xDB))
        $g.FillEllipse($dot, $end.X - $ri, $end.Y - $ri, 2 * $ri, 2 * $ri)
    }

    $g.Dispose()
    return $bmp
}

# PNG-Einzelbilder erzeugen und als .ico (PNG-komprimierte Einträge) zusammenfügen
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($sz in $sizes) {
    $bmp = Draw-Icon $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($sz -eq 256) { $bmp.Save((Join-Path $OutDir 'app-256.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    if ($sz -eq 64) { $bmp.Save((Join-Path $OutDir 'app-64.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $pngs[$i]
    $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $w.Write([byte[]]$data) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'app.ico'), $out.ToArray())

# Vorschau aller Größen nebeneinander (hell + dunkel)
$prev = New-Object System.Drawing.Bitmap 760, 300
$g = [System.Drawing.Graphics]::FromImage($prev)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 241, 244, 248))), 0, 0, 760, 150)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 18, 22, 27))), 0, 150, 760, 150)
$x = 10
foreach ($sz in 16, 24, 32, 48, 64, 128) {
    $b = Draw-Icon $sz
    $g.DrawImage($b, $x, 75 - $sz / 2, $sz, $sz)
    $g.DrawImage($b, $x, 225 - $sz / 2, $sz, $sz)
    $x += $sz + 24
}
$b = Draw-Icon 256
$g.DrawImage($b, 470, 10, 280, 280)
$prev.Save((Join-Path $OutDir 'preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
"ok: " + (Get-Item (Join-Path $OutDir 'app.ico')).Length + " Bytes"
