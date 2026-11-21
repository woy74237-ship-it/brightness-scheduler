# 紹介ページ・記事用のスクリーンショットを docs\images\ に撮る（docs\images\ は手元だけの素材）
#   main.png / edit.png / correction.png
# サンプルのスケジュールと架空のモニター名（メインモニター・サブモニター）で撮影し、モニターの明るさは変えません。
# 配色は Windows のダーク／ライトモードとアクセントカラーに従います。
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$tmp = Join-Path ([IO.Path]::GetTempPath()) 'BrightnessScheduler-Screenshots.exe'
$out = Join-Path $root 'docs\images'
New-Item -ItemType Directory -Force $out | Out-Null

$src = @((Join-Path $PSScriptRoot 'Screenshots.cs')) + (Get-ChildItem (Join-Path $root 'src') -Filter *.cs).FullName
& $csc /nologo /target:winexe /codepage:65001 /main:Screenshots `
    "/win32manifest:$root\assets\app.manifest" "/resource:$root\assets\app.ico,BrightnessScheduler.app.ico" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Runtime.Serialization.dll /r:System.Core.dll /r:System.Management.dll `
    "/out:$tmp" $src
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$p = Start-Process $tmp -ArgumentList "`"$out`"" -PassThru -Wait
Remove-Item $tmp
Get-ChildItem $out -Filter *.png | ForEach-Object { '{0,-16} {1,8:N0} bytes' -f $_.Name, $_.Length }
