<#
.SYNOPSIS
Builds the approved rounded weather artwork and a review sheet using deterministic drawing primitives.
.DESCRIPTION
Requires Windows System.Drawing. All production layers use straight-alpha 64 by 64 PNG canvases.
The review sheet includes actual 24-pixel composites; it does not establish in-game visual acceptance.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$textureDirectory = Join-Path $repository 'VanillaExpanded/assets/vanillaexpanded/textures/hud/weather/composite'
$reviewDirectory = Join-Path $repository 'docs/weather-conditions'
New-Item -ItemType Directory -Force -Path $textureDirectory, $reviewDirectory | Out-Null
$ink = [Drawing.ColorTranslator]::FromHtml('#25343e')
$cloud = [Drawing.ColorTranslator]::FromHtml('#f3f1e6')
$rain = [Drawing.ColorTranslator]::FromHtml('#86e3ef')
$snow = [Drawing.ColorTranslator]::FromHtml('#f7faf7')
$hail = [Drawing.ColorTranslator]::FromHtml('#b7e6f5')
$wind = [Drawing.ColorTranslator]::FromHtml('#d7e4ed')
$fog = [Drawing.ColorTranslator]::FromHtml('#c5d3df')
$bolt = [Drawing.ColorTranslator]::FromHtml('#ffe186')

#region Private
#region Drawing primitives
function New-Canvas {
    <# .SYNOPSIS Creates an oversampled transparent canvas with drawing coordinates in 64-pixel units. #>
    $bitmap = [Drawing.Bitmap]::new(256, 256, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(4, 4)
    return @{ Bitmap = $bitmap; Graphics = $graphics }
}

function Save-Layer {
    <# .SYNOPSIS Resolves the oversampled drawing into a transparent production PNG and releases its resources. #>
    param($Canvas, [string]$Name)
    $result = [Drawing.Bitmap]::new(64, 64, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($result)
    try {
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($Canvas.Bitmap, [Drawing.Rectangle]::new(0, 0, 64, 64))
        $result.Save((Join-Path $textureDirectory "$Name.png"), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $result.Dispose(); $Canvas.Graphics.Dispose(); $Canvas.Bitmap.Dispose() }
}

function Paint-Shape {
    <# .SYNOPSIS Fills a shape and adds a narrow contrast outline that stays readable over bright scenes. #>
    param($Graphics, $Path, [Drawing.Color]$Color, [single]$Outline = 1.4)
    $brush = [Drawing.SolidBrush]::new($Color)
    $pen = [Drawing.Pen]::new($ink, $Outline)
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    try { $Graphics.FillPath($brush, $Path); $Graphics.DrawPath($pen, $Path) }
    finally { $brush.Dispose(); $pen.Dispose() }
}

function Paint-Stroke {
    <# .SYNOPSIS Draws a rounded stroke with a subtle contrasting edge, without changing its centerline. #>
    param($Graphics, $Path, [Drawing.Color]$Color, [single]$Width)
    $edge = [Drawing.Pen]::new($ink, $Width + 1.25)
    $pen = [Drawing.Pen]::new($Color, $Width)
    foreach ($item in @($edge, $pen)) {
        $item.StartCap = $item.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $item.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    }
    try { $Graphics.DrawPath($edge, $Path); $Graphics.DrawPath($pen, $Path) }
    finally { $edge.Dispose(); $pen.Dispose() }
}
#endregion

#region Layer families
function Write-SkyLayers {
    <# .SYNOPSIS Builds a stable rounded canopy whose extent grows with coverage, plus clear and unknown markers. #>
    $names = @('mostly-clear', 'partly-cloudy', 'mostly-cloudy', 'overcast')
    $widths = @(20, 32, 43, 52)
    for ($index = 0; $index -lt $names.Length; $index++) {
        $canvas = New-Canvas
        $path = [Drawing.Drawing2D.GraphicsPath]::new()
        # Keep the baseline fixed; increasing cloud coverage changes horizontal extent and lobe height only.
        $path.AddBezier(10, 18, -1, 18, -1, 6, 10, 6)
        $path.AddBezier(10, 6, 12, -3, 30, -3, 33, 6)
        $path.AddBezier(33, 6, 43, 2, 50, 7, 47, 11)
        $path.AddBezier(47, 11, 55, 13, 51, 18, 44, 18)
        $path.AddLine(44, 18, 10, 18)
        $path.CloseFigure()
        $matrix = [Drawing.Drawing2D.Matrix]::new()
        $matrix.Translate([single]((64 - $widths[$index]) / 2), 17)
        $matrix.Scale([single]($widths[$index] / 52), 1)
        $path.Transform($matrix)
        Paint-Shape $canvas.Graphics $path $cloud
        $matrix.Dispose(); $path.Dispose()
        Save-Layer $canvas "sky-$($names[$index])"
    }
    $canvas = New-Canvas
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    # The clear marker is the selected small sparkle, with no enclosing disc or ring.
    $points = [Drawing.PointF[]]@([Drawing.PointF]::new(32, 20), [Drawing.PointF]::new(34, 25),
        [Drawing.PointF]::new(40, 27), [Drawing.PointF]::new(34, 29), [Drawing.PointF]::new(32, 34),
        [Drawing.PointF]::new(30, 29), [Drawing.PointF]::new(24, 27), [Drawing.PointF]::new(30, 25))
    $path.AddPolygon($points)
    Paint-Shape $canvas.Graphics $path $cloud
    $path.Dispose()
    Save-Layer $canvas 'sky-clear'
    $canvas = New-Canvas
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddBezier(28, 24, 28, 19, 37, 19, 37, 24)
    $path.AddBezier(37, 24, 37, 27, 32, 26, 32, 29)
    Paint-Stroke $canvas.Graphics $path $cloud 2.4
    $path.Dispose()
    $brush = [Drawing.SolidBrush]::new($cloud)
    $canvas.Graphics.FillEllipse($brush, 30.7, 31.4, 2.6, 2.6)
    $brush.Dispose()
    Save-Layer $canvas 'sky-unknown'
}

function Write-PrecipitationLayers {
    <# .SYNOPSIS Builds option A streaks, six-spoke snowflakes, and round hail pellets with motion ticks. #>
    $strengths = @('light', 'moderate', 'heavy')
    # Rain and hail use two, four, and six marks. Larger snowflakes use one, two, and four to retain their shape.
    # All positions leave the central gap reserved for lightning at every level.
    $positions = @(@(@(17, 44), @(47, 44)), @(@(12, 44), @(22, 44), @(42, 44), @(52, 44)),
        @(@(12, 40), @(22, 40), @(42, 40), @(52, 40), @(17, 49), @(47, 49)))
    $snowPositions = @(@(@(17, 44), $null), @(@(17, 44), @(47, 44)),
        @(@(9, 44), @(21, 44), @(43, 44), @(55, 44)))
    foreach ($kind in @('rain', 'snow', 'hail')) {
        for ($level = 0; $level -lt 3; $level++) {
            $canvas = New-Canvas
            if ($kind -eq 'snow') { $marks = $snowPositions[$level] } else { $marks = $positions[$level] }
            foreach ($point in $marks) {
                if ($null -eq $point) { continue }
                [single]$x = $point[0]; [single]$y = $point[1]
                $path = [Drawing.Drawing2D.GraphicsPath]::new()
                switch ($kind) {
                    'rain' {
                        $path.AddLine($x + 1.5, $y - 2.7, $x - 1.5, $y + 2.7)
                        Paint-Stroke $canvas.Graphics $path $rain 1.9
                    }
                    'snow' {
                        # Three diameters give six spokes, unlike the rejected four-arm plus shape.
                        for ($arm = 0; $arm -lt 3; $arm++) {
                            $angle = [Math]::PI * $arm / 3
                            [single]$dx = 3.8 * [Math]::Sin($angle); [single]$dy = 3.8 * [Math]::Cos($angle)
                            $path.StartFigure(); $path.AddLine($x - $dx, $y - $dy, $x + $dx, $y + $dy)
                        }
                        Paint-Stroke $canvas.Graphics $path $snow 1.5
                    }
                    'hail' {
                        $path.AddEllipse($x - 1.9, $y - 1.9, 3.8, 3.8)
                        Paint-Shape $canvas.Graphics $path $hail 1
                        $tick = [Drawing.Drawing2D.GraphicsPath]::new()
                        $tick.AddLine($x - 3.8, $y - 1, $x - 5.1, $y + 1.3)
                        Paint-Stroke $canvas.Graphics $tick $hail 1.15
                        $tick.Dispose()
                    }
                }
                $path.Dispose()
            }
            Save-Layer $canvas "$kind-$($strengths[$level])"
        }
    }
}

function Write-WindAndFogLayers {
    <# .SYNOPSIS Keeps curved wind strokes above every cloud and straight fog bands below all precipitation. #>
    for ($level = 1; $level -le 3; $level++) {
        $canvas = New-Canvas
        for ($line = 0; $line -lt $level; $line++) {
            [single]$y = 5 + 4.6 * $line
            [single]$left = 18 - 2 * $line
            [single]$right = 40 + 3 * $line
            $path = [Drawing.Drawing2D.GraphicsPath]::new()
            $path.AddLine($left, $y, $right, $y)
            $path.AddBezier($right, $y, $right + 6, $y, $right + 5, $y - 4, $right + 2, $y - 3)
            Paint-Stroke $canvas.Graphics $path $wind 1.85
            $path.Dispose()
        }
        $name = @('wind-light', 'wind-moderate', 'wind-strong')[$level - 1]
        Save-Layer $canvas $name
        $canvas = New-Canvas
        for ($line = 0; $line -lt $level; $line++) {
            [single]$y = 55 + 3.2 * $line
            $path = [Drawing.Drawing2D.GraphicsPath]::new()
            $path.AddLine(13, $y, 51, $y)
            Paint-Stroke $canvas.Graphics $path $fog 1.75
            $path.Dispose()
        }
        $name = @('fog-light', 'fog-moderate', 'fog-dense')[$level - 1]
        Save-Layer $canvas $name
    }
}

function Write-ThunderLayers {
    <# .SYNOPSIS Varies lightning breadth inside the reserved center, leaving all precipitation marks legible. #>
    $names = @('light', 'moderate', 'heavy')
    for ($index = 0; $index -lt 3; $index++) {
        $canvas = New-Canvas
        [single]$width = 5.5 + 1.8 * $index
        [single]$left = 32 - $width / 2
        $points = [Drawing.PointF[]]@([Drawing.PointF]::new($left + $width * .55, 35),
            [Drawing.PointF]::new($left + $width, 35), [Drawing.PointF]::new($left + $width * .57, 42),
            [Drawing.PointF]::new($left + $width, 42), [Drawing.PointF]::new($left + $width * .08, 52),
            [Drawing.PointF]::new($left + $width * .35, 44), [Drawing.PointF]::new($left, 44))
        $path = [Drawing.Drawing2D.GraphicsPath]::new()
        $path.AddPolygon($points)
        Paint-Shape $canvas.Graphics $path $bolt 1.1
        $path.Dispose()
        Save-Layer $canvas "thunder-$($names[$index])"
    }
}
#endregion

#region Review sheet
function Write-ReviewSheet {
    <# .SYNOPSIS Composes the actual PNG layers at enlarged and native HUD sizes over bright and dark surfaces. #>
    $examples = @(
        @{ Label = 'Clear'; Layers = @('sky-clear') },
        @{ Label = 'Mostly clear'; Layers = @('sky-mostly-clear') },
        @{ Label = 'Partly cloudy'; Layers = @('sky-partly-cloudy') },
        @{ Label = 'Mostly cloudy'; Layers = @('sky-mostly-cloudy') },
        @{ Label = 'Overcast'; Layers = @('sky-overcast') },
        @{ Label = 'Unknown'; Layers = @('sky-unknown') },
        @{ Label = 'Light rain'; Layers = @('sky-partly-cloudy', 'rain-light') },
        @{ Label = 'Moderate rain'; Layers = @('sky-mostly-cloudy', 'rain-moderate') },
        @{ Label = 'Heavy rain'; Layers = @('sky-overcast', 'rain-heavy') },
        @{ Label = 'Light snow'; Layers = @('sky-partly-cloudy', 'snow-light') },
        @{ Label = 'Moderate snow'; Layers = @('sky-mostly-cloudy', 'snow-moderate') },
        @{ Label = 'Heavy snow'; Layers = @('sky-overcast', 'snow-heavy') },
        @{ Label = 'Light hail'; Layers = @('sky-partly-cloudy', 'hail-light') },
        @{ Label = 'Moderate hail'; Layers = @('sky-mostly-cloudy', 'hail-moderate') },
        @{ Label = 'Heavy hail'; Layers = @('sky-overcast', 'hail-heavy') },
        @{ Label = 'Light wind'; Layers = @('sky-partly-cloudy', 'wind-light') },
        @{ Label = 'Moderate wind'; Layers = @('sky-partly-cloudy', 'wind-moderate') },
        @{ Label = 'Strong wind'; Layers = @('sky-partly-cloudy', 'wind-strong') },
        @{ Label = 'Light fog'; Layers = @('sky-partly-cloudy', 'fog-light') },
        @{ Label = 'Moderate fog'; Layers = @('sky-partly-cloudy', 'fog-moderate') },
        @{ Label = 'Dense fog'; Layers = @('sky-partly-cloudy', 'fog-dense') },
        @{ Label = 'Light thunder'; Layers = @('sky-overcast', 'rain-light', 'thunder-light') },
        @{ Label = 'Moderate thunder'; Layers = @('sky-overcast', 'rain-moderate', 'thunder-moderate') },
        @{ Label = 'Heavy thunder'; Layers = @('sky-overcast', 'rain-heavy', 'thunder-heavy') },
        @{ Label = 'All: rain'; Layers = @('sky-overcast', 'rain-heavy', 'wind-strong', 'fog-dense', 'thunder-heavy') },
        @{ Label = 'All: snow'; Layers = @('sky-overcast', 'snow-heavy', 'wind-strong', 'fog-dense', 'thunder-heavy') },
        @{ Label = 'All: hail'; Layers = @('sky-overcast', 'hail-heavy', 'wind-strong', 'fog-dense', 'thunder-heavy') },
        @{ Label = 'Dry wind'; Layers = @('sky-clear', 'wind-strong') },
        @{ Label = 'Fog alone'; Layers = @('sky-clear', 'fog-dense') },
        @{ Label = 'Wind and fog'; Layers = @('sky-clear', 'wind-strong', 'fog-dense') }
    )
    $sheet = [Drawing.Bitmap]::new(1200, 1170)
    $graphics = [Drawing.Graphics]::FromImage($sheet)
    $titleFont = [Drawing.Font]::new('Segoe UI', 20, [Drawing.FontStyle]::Bold)
    $font = [Drawing.Font]::new('Segoe UI', 10)
    $text = [Drawing.SolidBrush]::new([Drawing.Color]::WhiteSmoke)
    $bright = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#c7c2ac'))
    try {
        $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#20282e'))
        $graphics.DrawString('Approved weather layers: actual production textures', $titleFont, $text, 20, 12)
        $graphics.DrawString('Enlarged 128px preview; actual 24px below on dark and bright backgrounds. No outer ring.', $font, $text, 22, 50)
        for ($index = 0; $index -lt $examples.Length; $index++) {
            $entry = $examples[$index]
            $composite = [Drawing.Bitmap]::new(64, 64, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $drawing = [Drawing.Graphics]::FromImage($composite)
            try {
                foreach ($name in $entry.Layers) {
                    $layer = [Drawing.Image]::FromFile((Join-Path $textureDirectory "$name.png"))
                    try { $drawing.DrawImageUnscaled($layer, 0, 0) } finally { $layer.Dispose() }
                }
                $x = 20 + 198 * ($index % 6); $y = 86 + 214 * [Math]::Floor($index / 6)
                $graphics.DrawString($entry.Label, $font, $text, $x, $y)
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                $graphics.DrawImage($composite, [Drawing.Rectangle]::new($x + 20, $y + 24, 128, 128))
                $graphics.FillRectangle($bright, $x + 86, $y + 166, 40, 36)
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear
                $graphics.DrawImage($composite, [Drawing.Rectangle]::new($x + 40, $y + 172, 24, 24))
                $graphics.DrawImage($composite, [Drawing.Rectangle]::new($x + 94, $y + 172, 24, 24))
            }
            finally { $drawing.Dispose(); $composite.Dispose() }
        }
        $sheet.Save((Join-Path $reviewDirectory 'weather-textures-preview.png'), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $sheet.Dispose(); $titleFont.Dispose(); $font.Dispose(); $text.Dispose(); $bright.Dispose() }
}
#endregion
#endregion

Write-SkyLayers
Write-PrecipitationLayers
Write-WindAndFogLayers
Write-ThunderLayers
Write-ReviewSheet
Write-Output "Created 24 weather layers in $textureDirectory and weather-textures-preview.png."
