<#
.SYNOPSIS
  Chạy HabitGrid Widgets (WidgetHost) với một kiểu ghim chẩn đoán, để biết widget có hiện trên máy này không.
.PARAMETER Pin
  default = ghim vào lớp desktop (Progman/WorkerW), above = cửa sổ cấp cao ngay trên lớp desktop,
  top = luôn nằm trên cùng (dùng để chứng minh widget vẽ được), bottom = đáy z-order.
.EXAMPLE
  pwsh tools/run-widgets.ps1 -Pin top
#>
param([ValidateSet("default", "above", "top", "bottom")][string]$Pin = "default")

$root = Split-Path -Parent $PSScriptRoot
Get-Process HabitGrid.WidgetHost -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
$exe = Get-ChildItem (Join-Path $root "src\HabitGrid.WidgetHost\bin") -Recurse -Filter HabitGrid.WidgetHost.exe -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw "Chưa build WidgetHost: dotnet build src/HabitGrid.WidgetHost" }

if ($Pin -eq "default") { Remove-Item Env:HABITGRID_PIN -ErrorAction SilentlyContinue } else { $env:HABITGRID_PIN = $Pin }
Start-Process $exe.FullName
Remove-Item Env:HABITGRID_PIN -ErrorAction SilentlyContinue
Write-Host "Đã chạy WidgetHost, kiểu ghim: $Pin"
