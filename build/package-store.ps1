param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0.0"
)

# Đóng gói app WinUI thành MSIX để tải lên Partner Center. Gói chưa ký: Store ký khi phát hành.
# Chạy: pwsh build/package-store.ps1   (cần Windows SDK có makeappx.exe)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\HabitGrid.WinUI\HabitGrid.WinUI.csproj"
$widgetsProj = Join-Path $root "src\HabitGrid.WidgetHost\HabitGrid.WidgetHost.csproj"
$pkgDir = Join-Path $root "src\HabitGrid.Package"
$out = Join-Path $root "artifacts\store"
$layout = Join-Path $out "layout"
$msix = Join-Path $out "HabitGrid_${Version}_x64.msix"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $layout | Out-Null

Write-Host "[1/4] Publish HabitGrid.WinUI (self-contained, win-x64)" -ForegroundColor Yellow
dotnet publish $proj -c $Configuration -r win-x64 --self-contained true -p:Platform=x64 -p:DebugType=none -p:DebugSymbols=false -o $layout
if ($LASTEXITCODE -ne 0) { throw "dotnet publish thất bại" }

# WidgetHost cùng thư mục: hai exe dùng chung runtime .NET và các thư viện HabitGrid.* (cùng phiên bản nên ghi đè an toàn).
Write-Host "[1b/4] Publish HabitGrid.WidgetHost vào cùng layout" -ForegroundColor Yellow
dotnet publish $widgetsProj -c $Configuration -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o $layout
if ($LASTEXITCODE -ne 0) { throw "dotnet publish WidgetHost thất bại" }
foreach ($exe in "HabitGrid.WinUI.exe", "HabitGrid.WidgetHost.exe") {
    if (-not (Test-Path (Join-Path $layout $exe))) { throw "Thiếu $exe trong layout" }
}

Write-Host "[2/4] Manifest + logo" -ForegroundColor Yellow
$manifest = (Get-Content (Join-Path $pkgDir "AppxManifest.WinUI.xml") -Raw) -replace 'Version="1\.0\.0\.0"', "Version=`"$Version`""
Set-Content -Path (Join-Path $layout "AppxManifest.xml") -Value $manifest -Encoding utf8
$images = Join-Path $layout "Images"
New-Item -ItemType Directory -Force -Path $images | Out-Null
Get-ChildItem (Join-Path $pkgDir "Images") -Filter *.png | Where-Object { $_.Name -notlike "StoreListing-*" } | Copy-Item -Destination $images

Write-Host "[3/4] makeappx" -ForegroundColor Yellow
$makeappx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Filter makeappx.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\x64\\" } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $makeappx) { throw "Không thấy makeappx.exe (cài Windows SDK)." }
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx thất bại" }

Write-Host "[4/4] Xong" -ForegroundColor Green
Get-Item $msix | Select-Object FullName, @{n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
