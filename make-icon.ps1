# Dessine l'icône de Stretch Switch (src\app.ico) en plusieurs tailles.
# Usage : .\make-icon.ps1   (à relancer seulement si on veut changer l'icône)
Add-Type -AssemblyName System.Drawing
Set-Location $PSScriptRoot

function New-IconPng([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $k = $s / 256.0

    # carré arrondi, dégradé rouge
    $m = 8 * $k; $w = $s - 2 * $m; $r = 56 * $k
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $r, $r, 180, 90)
    $path.AddArc($m + $w - $r, $m, $r, $r, 270, 90)
    $path.AddArc($m + $w - $r, $m + $w - $r, $r, $r, 0, 90)
    $path.AddArc($m, $m + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $rect = New-Object Drawing.RectangleF $m, $m, $w, $w
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush $rect, ([Drawing.Color]::FromArgb(255, 105, 115)), ([Drawing.Color]::FromArgb(205, 30, 60)), 90
    $g.FillPath($grad, $path)

    # barres verticales aux bords + double flèche (= image étirée)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), ([Math]::Max(2.0, 20 * $k))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $c = $s / 2.0
    $g.DrawLine($pen, 58 * $k, 78 * $k, 58 * $k, 178 * $k)
    $g.DrawLine($pen, 198 * $k, 78 * $k, 198 * $k, 178 * $k)
    $g.DrawLine($pen, 90 * $k, $c, 166 * $k, $c)
    $g.DrawLines($pen, [Drawing.PointF[]]@((New-Object Drawing.PointF (114 * $k), (104 * $k)), (New-Object Drawing.PointF (90 * $k), $c), (New-Object Drawing.PointF (114 * $k), (152 * $k))))
    $g.DrawLines($pen, [Drawing.PointF[]]@((New-Object Drawing.PointF (142 * $k), (104 * $k)), (New-Object Drawing.PointF (166 * $k), $c), (New-Object Drawing.PointF (142 * $k), (152 * $k))))
    $g.Dispose()

    $ms = New-Object IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)   # grande taille : PNG
    } else {
        # petites tailles : bitmap 32 bits classique (lu partout, y compris par .NET)
        $bw = New-Object IO.BinaryWriter $ms
        $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32]($s * 2))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
        $bw.Write([UInt32]($s * $s * 4 + $maskRow * $s))
        $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        $rect2 = New-Object Drawing.Rectangle 0, 0, $s, $s
        $data = $bmp.LockBits($rect2, 'ReadOnly', [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $px = New-Object byte[] ($s * $s * 4)
        [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $px, 0, $px.Length)
        $bmp.UnlockBits($data)
        for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($px, $y * $s * 4, $s * 4) }
        $bw.Write((New-Object byte[] ($maskRow * $s)))
        $bw.Flush()
    }
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) { , (New-IconPng $s) }

$out = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[IO.File]::WriteAllBytes("$PSScriptRoot\src\app.ico", $out.ToArray())
Write-Host "Icône créée : src\app.ico"
