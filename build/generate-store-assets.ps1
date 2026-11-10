# Tạo logo cho gói MSIX của app WinUI: ô bo góc gradient tím-hồng (màu thương hiệu) với lưới 3x3 trắng.
Add-Type -AssemblyName System.Drawing

$outputDir = Join-Path $PSScriptRoot "..\src\HabitGrid.Package\Images"
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

function New-Logo {
    param([int]$Width, [int]$Height, [string]$FileName, [bool]$RoundedSquareOnly = $true)

    $bmp = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Ô gradient ở giữa, chiếm ~86% cạnh ngắn (logo Store cần chừa lề nhỏ).
    $side = [Math]::Min($Width, $Height) * 0.86
    $x = ($Width - $side) / 2
    $y = ($Height - $side) / 2
    $radius = $side * 0.24
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $side - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $side - $d, $y + $side - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $side - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $rect = New-Object System.Drawing.RectangleF($x, $y, $side, $side)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(139, 92, 246), [System.Drawing.Color]::FromArgb(236, 72, 153), 45.0)
    $g.FillPath($grad, $path)

    # Lưới 3x3: ba ô sáng (đã check), sáu ô mờ.
    $grid = $side * 0.56
    $gap = $grid * 0.09
    $cell = ($grid - 2 * $gap) / 3
    $gx = $x + ($side - $grid) / 2
    $gy = $y + ($side - $grid) / 2
    $on = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $off = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(90, 255, 255, 255))
    $cr = $cell * 0.28

    for ($r = 0; $r -lt 3; $r++) {
        for ($c = 0; $c -lt 3; $c++) {
            $cx = $gx + $c * ($cell + $gap)
            $cy = $gy + $r * ($cell + $gap)
            $cd = $cr * 2
            $p = New-Object System.Drawing.Drawing2D.GraphicsPath
            $p.AddArc($cx, $cy, $cd, $cd, 180, 90)
            $p.AddArc($cx + $cell - $cd, $cy, $cd, $cd, 270, 90)
            $p.AddArc($cx + $cell - $cd, $cy + $cell - $cd, $cd, $cd, 0, 90)
            $p.AddArc($cx, $cy + $cell - $cd, $cd, $cd, 90, 90)
            $p.CloseFigure()
            $isOn = (($r -eq 0 -and $c -eq 0) -or ($r -eq 1 -and $c -eq 1) -or ($r -eq 2 -and $c -eq 2))
            $g.FillPath($(if ($isOn) { $on } else { $off }), $p)
            $p.Dispose()
        }
    }

    $g.Dispose()
    $bmp.Save((Join-Path $outputDir $FileName), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Created: $FileName ($Width x $Height)"
}

New-Logo -Width 50 -Height 50 -FileName "StoreLogo.png"
New-Logo -Width 44 -Height 44 -FileName "Square44x44Logo.png"
New-Logo -Width 256 -Height 256 -FileName "Square44x44Logo.targetsize-256.png"
New-Logo -Width 150 -Height 150 -FileName "Square150x150Logo.png"
New-Logo -Width 310 -Height 150 -FileName "Wide310x150Logo.png"
New-Logo -Width 310 -Height 310 -FileName "Square310x310Logo.png"
New-Logo -Width 620 -Height 300 -FileName "SplashScreen.png"
# Logo 1:1 để tải lên Partner Center (mục "Store logos").
New-Logo -Width 1080 -Height 1080 -FileName "StoreListing-1080x1080.png"
New-Logo -Width 300 -Height 300 -FileName "StoreListing-300x300.png"
