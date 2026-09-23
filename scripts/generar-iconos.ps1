Add-Type -AssemblyName System.Drawing

$root = "c:\Proyectos\TefOgloba"
$src  = [System.Drawing.Bitmap]::new("$root\ogloba_logo.jpeg")

# 1. Recorte del margen blanco. El JPEG viene con el logo flotando en un cuadro blanco; si se
#    usa tal cual, el ícono adaptativo de Android lo vuelve a encoger dentro de su zona segura y
#    el logo termina siendo una mancha en el centro.
$minX = $src.Width; $minY = $src.Height; $maxX = -1; $maxY = -1
for ($y = 0; $y -lt $src.Height; $y++) {
    for ($x = 0; $x -lt $src.Width; $x++) {
        $p = $src.GetPixel($x, $y)
        if ($p.R -lt 245 -or $p.G -lt 245 -or $p.B -lt 245) {
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$w = $maxX - $minX + 1
$h = $maxY - $minY + 1
"Tinta del logo: ${w}x${h} en ($minX,$minY)"

# 2. El blanco pasa a transparente, con rampa en los bordes para no dejar el contorno dentado.
#    Así el logo se apoya sobre el fondo del ícono en vez de traer su propio recuadro.
$trim = [System.Drawing.Bitmap]::new($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $p = $src.GetPixel($minX + $x, $minY + $y)
        $m = [Math]::Max($p.R, [Math]::Max($p.G, $p.B))
        if ($m -ge 250)    { $a = 0 }
        elseif ($m -le 235) { $a = 255 }
        else                { $a = [int](255 * (250 - $m) / 15) }
        $trim.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($a, $p.R, $p.G, $p.B))
    }
}

# La zona segura del ícono adaptativo de Android es un CÍRCULO de 66dp sobre una capa de 108dp
# (61%), no un cuadrado. El logo de Ogloba es una tira ancha y baja (183x69), así que medirlo por
# el ancho engaña: a 60% de ancho las esquinas —la "o" inicial y la "a" final— caen fuera del
# círculo y el launcher se las come. Lo que tiene que caber es la DIAGONAL.
function Get-CircleSafeFill([double]$w, [double]$h, [double]$circle) {
    $diagonal = [Math]::Sqrt($w * $w + $h * $h)
    return ($w / $diagonal) * $circle
}

function Write-Icon([int]$size, [double]$fill, [string]$path) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $box   = $size * $fill
    $scale = [Math]::Min($box / $script:trim.Width, $box / $script:trim.Height)
    $dw    = $script:trim.Width  * $scale
    $dh    = $script:trim.Height * $scale
    $g.DrawImage($script:trim, ($size - $dw) / 2, ($size - $dh) / 2, $dw, $dh)
    $g.Dispose()

    $dir = Split-Path -Parent $path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "  $path  ($size px, relleno $fill)"
}

# Ícono del launcher. 0.95 del círculo seguro: el 5% restante es margen para que las letras no
# queden lamiendo el borde del recorte.
$fill = Get-CircleSafeFill $w $h (0.611 * 0.95)
"Relleno seguro del ícono: {0:N3} (tira {1}x{2})" -f $fill, $w, $h
Write-Icon 1024 $fill "$root\src\Permoda.Pay.Maui\Resources\AppIcon\appiconfg.png"

# Splash: mismo logo, un poco más chico porque ahí no hay recorte pero sí bordes de pantalla.
Write-Icon 1024 0.55 "$root\src\Permoda.Pay.Maui\Resources\Splash\splash.png"

# Logo del botón de la forma de pago en HiPOS (GET_CUSTOM_PARAMS). Acá no hay máscara: llena.
Write-Icon 256 0.92 "$root\src\Permoda.Pay.Maui\Resources\Raw\tef_logo.png"

$trim.Dispose()
$src.Dispose()
