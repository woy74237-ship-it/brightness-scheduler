# 紹介ページ・記事・SNS 用の画像を promo\ に生成する（promo\ は手元だけの素材で、Git には含めない）
#   ogp.png          … シェア時のサムネイル（1200x630）
#   note-header.png  … note の見出し画像（1280x670）
#   cover.png        … 文字なしの背景（1920x1080、紹介ページのヒーロー背景など）
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$tmp = Join-Path ([IO.Path]::GetTempPath()) 'BrightnessScheduler-MakePromo.exe'

& $csc /nologo /codepage:65001 /r:System.Drawing.dll "/out:$tmp" (Join-Path $PSScriptRoot 'MakeIcon.cs') (Join-Path $PSScriptRoot 'Promo.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$out = Join-Path $root 'promo'
New-Item -ItemType Directory -Force $out | Out-Null
& $tmp --ogp (Join-Path $out 'ogp.png') 1200 630
& $tmp --ogp (Join-Path $out 'note-header.png') 1280 670
& $tmp --cover (Join-Path $out 'cover.png') 1920 1080
Remove-Item $tmp
Get-ChildItem $out -Filter *.png | ForEach-Object { Write-Host "生成しました: $($_.FullName)" }
