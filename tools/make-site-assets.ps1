# 紹介ページ（site/）用の画像を生成・コピーする
#   使う素材: assets\app.ico、docs\images\*.png（スクリーンショット）、promo\ogp.png・cover.png
#   ※ docs\images\ と promo\ は手元だけの素材です。先に tools\make-screenshots.ps1 と tools\make-promo.ps1 を実行しておいてください。
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'site\images'
New-Item -ItemType Directory -Force $out | Out-Null
Add-Type -AssemblyName System.Drawing

function Save-Resized([string]$src, [string]$dst, [int]$w, [int]$h) {
    $img = [System.Drawing.Image]::FromFile($src)
    try {
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'HighQuality'
        $g.DrawImage($img, 0, 0, $w, $h); $g.Dispose()
        if ($dst -like '*.jpg') {
            $enc = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
            $p = New-Object System.Drawing.Imaging.EncoderParameters 1
            $p.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), 85L
            $bmp.Save($dst, $enc, $p)
        } else { $bmp.Save($dst, [System.Drawing.Imaging.ImageFormat]::Png) }
        $bmp.Dispose()
    } finally { $img.Dispose() }
}

# アイコン（256px はアイコン生成ツールのプレビュー出力を使う）
& (Join-Path $PSScriptRoot 'make-icon.ps1') -Preview (Join-Path $out 'icon-256.png') | Out-Null
Save-Resized (Join-Path $out 'icon-256.png') (Join-Path $out 'apple-touch-icon.png') 180 180
$ico = New-Object System.Drawing.Icon (Join-Path $root 'assets\app.ico'), 32, 32
$ico.ToBitmap().Save((Join-Path $out 'favicon-32.png'), [System.Drawing.Imaging.ImageFormat]::Png); $ico.Dispose()

# スクリーンショット
foreach ($n in 'main', 'edit', 'correction') { Copy-Item (Join-Path $root "docs\images\$n.png") $out -Force }

# シェア用画像とヒーロー背景（背景は JPEG にして軽くする）
Copy-Item (Join-Path $root 'promo\ogp.png') $out -Force
Save-Resized (Join-Path $root 'promo\cover.png') (Join-Path $out 'hero.jpg') 1920 1080

Get-ChildItem $out | ForEach-Object { '{0,-22} {1,8:N0} bytes' -f $_.Name, $_.Length }
