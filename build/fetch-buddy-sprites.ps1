# Tải hình 3D của Buddy và phụ kiện từ Microsoft Fluent Emoji (giấy phép MIT) cho widget WPF
# (WPF không vẽ được emoji màu). Chạy lại khi thêm phụ kiện mới ở Core/Domain/Gamification/BuddyAccessories.cs.
# Kết quả: src/HabitGrid.WidgetHost/Assets/Buddy/<id>.png (đóng vào WidgetHost) + docs/third-party/FLUENT-EMOJI-LICENSE.txt
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "src\HabitGrid.WidgetHost\Assets\Buddy"
New-Item -ItemType Directory -Force -Path $out | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $root "docs\third-party") | Out-Null

# id sprite -> tên thư mục trong microsoft/fluentui-emoji/assets
$map = [ordered]@{
    "stage-egg" = "Egg"; "stage-baby" = "Hatching chick"; "stage-teen" = "Front-facing baby chick"; "stage-adult" = "Owl"; "stage-legend" = "Dragon"
    "cap" = "Billed cap"; "party" = "Partying face"; "grad" = "Graduation cap"; "crown" = "Crown"; "tophat" = "Top hat"; "blossom" = "Cherry blossom"
    "shades" = "Sunglasses"; "nerd" = "Nerd face"; "monocle" = "Face with monocle"
    "scarf" = "Scarf"; "medal" = "Sports medal"; "ribbon" = "Ribbon"; "gem" = "Gem stone"
    "tulip" = "Tulip"; "butterfly" = "Butterfly"; "straw" = "Womans hat"; "lei" = "Hibiscus"
    "maple" = "Maple leaf"; "pumpkin" = "Jack-o-lantern"; "santa" = "Santa claus|Default/3D/santa_claus_3d_default.png"; "snowflake" = "Snowflake"
}

foreach ($id in $map.Keys) {
    # "Thư mục|đường dẫn" cho emoji có nhiều tông da (thư mục con Default); còn lại dùng quy ước thư mục/3D/ten_3d.png.
    $parts = $map[$id] -split '\|'
    $folder = $parts[0]
    $relative = if ($parts.Count -gt 1) { $parts[1] } else { "3D/" + ($folder.ToLowerInvariant() -replace ' ', '_') + "_3d.png" }
    $url = "https://raw.githubusercontent.com/microsoft/fluentui-emoji/main/assets/" + [uri]::EscapeDataString($folder) + "/" + $relative
    Invoke-WebRequest -Uri $url -OutFile (Join-Path $out "$id.png") -UseBasicParsing
    Write-Host "OK  $id  <- $folder"
}

Invoke-WebRequest -Uri "https://raw.githubusercontent.com/microsoft/fluentui-emoji/main/LICENSE" -OutFile (Join-Path $root "docs\third-party\FLUENT-EMOJI-LICENSE.txt") -UseBasicParsing
Write-Host "Đã tải $($map.Count) hình và giấy phép MIT."
