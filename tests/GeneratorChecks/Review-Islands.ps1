param([string]$ReviewDirectory = '.godot/island-review52', [int[]]$Versions = @(50,51,52), [int[]]$Heights = @(64,256))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$reviewRoot = (Resolve-Path -LiteralPath $ReviewDirectory).Path
$font = [System.Drawing.Font]::new('Arial', 14)
$land = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(155,176,110))
$water = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(42,81,110))
$sea = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(80,190,180))
$oceanColors = @('#335F8A','#517DA2','#356E78','#4A8EA5','#625C92','#397A94','#667FA3')
$oceanBrushes = @($oceanColors | ForEach-Object { [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($_)) })
try {
    foreach ($seed in @(1234,-77)) {
        foreach ($mapHeight in $Heights) {
            $scale = if ($mapHeight -eq 64) { 4 } else { 2 }
            $mapWidth = 2 * $mapHeight
            foreach ($mode in @('land','regions')) {
                $canvas = [System.Drawing.Bitmap]::new($mapWidth*$scale*$Versions.Count, $mapHeight*$scale+36)
                $graphics = [System.Drawing.Graphics]::FromImage($canvas)
                try {
                    $graphics.Clear([System.Drawing.Color]::FromArgb(20,30,42))
                    for ($panel = 0; $panel -lt $Versions.Count; $panel++) {
                        $version = $Versions[$panel]
                        $sample = Get-Content -LiteralPath (Join-Path $reviewRoot "$seed-$mapHeight-v$version.json") -Raw | ConvertFrom-Json
                        $graphics.DrawString("v$version - seed $seed - $mapWidth x $mapHeight", $font, [System.Drawing.Brushes]::White, $panel*$mapWidth*$scale, 5)
                        for ($row = 0; $row -lt $mapHeight; $row++) {
                            for ($col = 0; $col -lt $mapWidth; $col++) {
                                $cell = $row*$mapWidth+$col
                                $id = $sample.Regions[$cell]
                                if ($id -eq 0) { continue }
                                $brush = if ($sample.Water[$cell] -eq 0) { $land } else { $water }
                                if ($mode -eq 'regions' -and $sample.Water[$cell] -ne 0) {
                                    $kind = $sample.Kinds.([string]$id)
                                    if ($kind -eq 3) { $brush = $oceanBrushes[$id % $oceanBrushes.Count] }
                                    if ($kind -eq 4) { $brush = $sea }
                                }
                                $graphics.FillRectangle($brush, $panel*$mapWidth*$scale+$col*$scale, 36+$row*$scale, $scale, $scale)
                            }
                        }
                    }
                    $canvas.Save((Join-Path $reviewRoot "$mode-$seed-$mapHeight.png"), [System.Drawing.Imaging.ImageFormat]::Png)
                }
                finally { $graphics.Dispose(); $canvas.Dispose() }
            }
        }
    }
}
finally {
    $font.Dispose(); $land.Dispose(); $water.Dispose(); $sea.Dispose()
    foreach ($brush in $oceanBrushes) { $brush.Dispose() }
}
