param(
    [string]$AssetsPath = (Join-Path $PSScriptRoot '..\src\AIDrawer.App\Assets')
)

Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Bounds,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($Bounds.Left, $Bounds.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.Left, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-BrandBitmap {
    param(
        [int]$Width,
        [int]$Height
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $canvas = [Math]::Min($Width, $Height)
    $scale = $canvas / 128.0
    $offsetX = ($Width - $canvas) / 2.0
    $offsetY = ($Height - $canvas) / 2.0
    $accent = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 89, 107, 255))

    $markBounds = [System.Drawing.RectangleF]::new($offsetX + 53 * $scale, $offsetY + 12 * $scale, 22 * $scale, 104 * $scale)
    $markPath = New-RoundedRectanglePath $markBounds (11 * $scale)
    $markCenter = [System.Drawing.PointF]::new($offsetX + 64 * $scale, $offsetY + 64 * $scale)

    $graphics.FillPath($accent, $markPath)

    foreach ($angle in 60, 120) {
        $rotatedMarkPath = $markPath.Clone()
        $rotation = [System.Drawing.Drawing2D.Matrix]::new()
        $rotation.RotateAt($angle, $markCenter)
        $rotatedMarkPath.Transform($rotation)
        $graphics.FillPath($accent, $rotatedMarkPath)
        $rotation.Dispose()
        $rotatedMarkPath.Dispose()
    }

    $markPath.Dispose()
    $accent.Dispose()
    $graphics.Dispose()
    return $bitmap
}

function Save-DrawerPng {
    param(
        [string]$Name,
        [int]$Width,
        [int]$Height
    )

    $bitmap = New-BrandBitmap $Width $Height
    $bitmap.Save((Join-Path $AssetsPath $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

Save-DrawerPng 'LockScreenLogo.scale-200.png' 48 48
Save-DrawerPng 'SplashScreen.scale-200.png' 1240 600
Save-DrawerPng 'Square150x150Logo.scale-200.png' 300 300
Save-DrawerPng 'Square44x44Logo.scale-200.png' 88 88
Save-DrawerPng 'Square44x44Logo.targetsize-24_altform-unplated.png' 24 24
Save-DrawerPng 'Square44x44Logo.targetsize-48_altform-lightunplated.png' 48 48
Save-DrawerPng 'StoreLogo.png' 50 50
Save-DrawerPng 'Wide310x150Logo.scale-200.png' 620 300

$iconBitmap = New-BrandBitmap 256 256
$icon = [System.Drawing.Icon]::FromHandle($iconBitmap.GetHicon())
$stream = [System.IO.File]::Open((Join-Path $AssetsPath 'AppIcon.ico'), [System.IO.FileMode]::Create)
$icon.Save($stream)
$stream.Dispose()
$icon.Dispose()
$iconBitmap.Dispose()
