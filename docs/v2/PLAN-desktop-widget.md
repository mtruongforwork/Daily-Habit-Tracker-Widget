# Kế hoạch: Widget ghim desktop cho HabitGrid (làm lại sau bản Store v1)

> Soạn 10/11/2026. Nền: `SPEC-V2-05-widgets.md` (thiết kế), `POC-V2-report.md` (quyết định dùng WPF cho tiến trình widget).
> Mục tiêu: widget ghim màn hình nền thành tính năng khác biệt thật sự, có trong gói Store, kiểm chứng được mà không cần tắt Smart App Control trên máy dev.

## 1. Hiện trạng (đã có, không làm lại)
`src/HabitGrid.WidgetHost` (WPF, ~2.900 dòng, tiến trình riêng, đọc CSDL v3):
- Widget lưới thói quen (tối đa 3), **Today List**, **Quick check-in** (`Win+Alt+H`), khay hệ thống, widget Khoảnh khắc, cửa sổ Focus.
- Nhớ vị trí, làm mới theo `PRAGMA data_version` và khi qua ngày/ngủ dậy, dựng lại và ghim lại sau khi Explorer khởi động lại (đã kiểm chứng trên CI bằng cách kết thúc `explorer.exe` thật), đưa widget ngoài vùng nhìn về màn hình chính khi đổi màn hình.
- Cầu nối với app chính: `HostLink` (bảng settings chung, WidgetHost đọc mỗi 2 giây), bật/tắt tự chạy trong Cài đặt.

**Chưa có:** widget Buddy, Multi Grid, Thử thách; Windows 11 Widgets Board; nền kính (acrylic); popover sửa ngày cũ; chữ sáng/tối theo hình nền; snap lưới; đo RAM/CPU trên máy thật; **gói MSIX hiện tại không chứa WidgetHost** (đã bỏ khi dừng widget).

## 2. Ba rủi ro quyết định cách làm
1. **Danh tính gói và vị trí dữ liệu (nghiêm trọng nhất).** Trong MSIX, ghi vào `%LocalAppData%` bị ảo hóa theo gói *chỉ với tiến trình có danh tính gói*. Nếu WidgetHost chạy bằng khóa `Run` trỏ thẳng tới file `.exe` trong `WindowsApps`, nó **không có danh tính** và sẽ đọc một CSDL khác app chính (widget trống rỗng, không báo lỗi). Cách làm: khai báo WidgetHost là `Application` thứ hai trong manifest, có `AppExecutionAlias` và `StartupTask`; `HostLink` khởi động nó qua alias; bỏ khóa `Run` khi chạy trong gói.
2. **Smart App Control trên máy dev.** Bản build dev chưa ký bị chặn nên WidgetHost từng không chạy được tại máy. Cách kiểm chứng không cần tắt SAC: (a) CI cài MSIX **ký bằng chứng chỉ tạm** (runner có quyền admin, nạp cert vào TrustedPeople) rồi chạy app + WidgetHost thật; (b) bản Store do Microsoft ký, cài về máy dev chạy bình thường; (c) Store *package flight* để thử bản trước khi công khai.
3. **Ngân sách tài nguyên.** Hai runtime .NET (WinUI + WPF) cùng chạy. Tiêu chí SPEC-V2-05: 3 widget L + Today List dưới 60 MB RAM, CPU nhàn rỗi < 0,5%. POC WinUI đạt 89 MB cho 1 cửa sổ trống nên đã chọn WPF; cần **đo thật** trước khi hứa với người dùng.

## 3. Lộ trình (mỗi mốc đều có tiêu chí nghiệm thu)

### M0 — Đóng gói và kiểm chứng gói (nền cho mọi thứ khác)
- Thêm WidgetHost vào MSIX: `Application` thứ hai + alias `habitgrid-widgets` + StartupTask (mặc định tắt, bật trong Cài đặt).
- `HostLink`/Cài đặt: trong gói thì dùng alias và `StartupTask` API; ngoài gói giữ khóa `Run`.
- Job CI mới `msix-smoke`: dựng MSIX, ký cert tạm, `Add-AppxPackage`, chạy app (`--smoke`) và WidgetHost qua alias, xác nhận **cùng một CSDL** (check-in ở app hiện ở widget).
- *Nghiệm thu:* CI xanh; ảnh chụp có widget hiện đúng dữ liệu từ gói đã cài. Đây cũng là lần đầu **gói MSIX được cài và chạy thật**, nên bắt được lỗi đóng gói của bản Store.

### M1 — Bắt kịp v2 và hoàn thiện loại widget
- Widget **Buddy** (linh vật + phụ kiện đang đeo + vòng % hôm nay + chuỗi tổng; đọc `buddy.accessories`, cấp độ từ hồ sơ).
- Widget **Thử thách** (Ngày x/N + thanh tiến độ, đọc `challenges`) và **Ý định tuần** (đọc `weekly.intent`).
- **Multi Grid** (2–4 thói quen, mỗi dòng một heatmap mỏng).
- Phân gói: Free = 1 lưới + Today List; Plus = tối đa 6 widget, Buddy/Thử thách/Multi Grid, nền tùy biến. Kiểm bằng `IEntitlements`, hết Plus thì widget Plus ẩn nhưng cấu hình được giữ (cùng cách phụ kiện Buddy).
- Làm sạch dữ liệu liên thông: mọi thứ widget cần đã nằm trong CSDL/settings, không thêm kênh mới.
- *Nghiệm thu:* test cho từng bộ chọn dữ liệu (`WidgetMapper`), ảnh CI từng widget ở 2 theme, widget Plus không hiện khi Free.

### M2 — Giao diện và tương tác
- **Nền kính giả bằng hình nền**: lấy mẫu vùng hình nền sau widget, làm mờ rồi vẽ làm nền (acrylic thật không dùng được trên cửa sổ con của WorkerW). Cùng mẫu đó quyết định chữ sáng/tối ("thông minh với hình nền").
- Chế độ sắp xếp: kéo, snap lưới 8 px, đổi cỡ S/M/L/XL; ngoài chế độ này widget không di chuyển nhầm.
- Popover sửa ngày cũ, tooltip ô ngày, menu chuột phải (đổi cỡ/theme/ẩn/mở app).
- Vị trí nhớ theo **cấu hình màn hình** (đã có phần đưa về màn hình chính; thêm trả về vị trí cũ khi cắm lại).
- *Nghiệm thu:* tiêu chí 5 của SPEC-V2-05 (rút/cắm màn hình phụ); đo DPI 100/150/200%.

### M3 — Windows 11 Widgets Board (tùy chọn, làm sau cùng)
- `IWidgetProvider` (Windows App SDK) trong tiến trình COM riêng, đăng ký trong manifest; Adaptive Cards: **Hôm nay** (có nút check), **Streak**, **Buddy**.
- Chỉ chạy khi có danh tính gói, nên phụ thuộc M0. Không kiểm thử được trong CI thường (cần Windows 11 có Widgets Board): kiểm tay.
- *Nghiệm thu:* check-in từ Board hiện trên app và widget desktop trong 1 giây.

### M4 — Đo và khóa chất lượng
- Đo RAM/CPU 10 phút trên máy thật với 3 widget L + Today List; ghi số vào `POC-V2-report.md`. Quá ngân sách thì tối ưu (vẽ lại theo vùng, ngưng làm mới khi ẩn) trước khi mở rộng.
- Kiểm trên Windows 10 22H2 và Windows 11 24H2+ (máy ảo hoặc máy phụ).
- Cập nhật `QA-CHECKLIST-v2.0.md`, mô tả Store và ảnh chụp (widget là điểm bán).

## 4. Phiên bản phát hành
- **1.1:** M0 + M1 (widget có mặt trong gói, bán được Plus nếu đã nối mua hàng).
- **1.2:** M2. **1.3:** M3 nếu số đo M4 cho phép.
- Mỗi bản đi qua Store (kiểm duyệt có thể thêm vài ngày vì có `StartupTask` và nhiều `Application`; nêu rõ trong ghi chú kiểm duyệt rằng widget chạy nền theo ý người dùng).

## 5. Việc cần bạn quyết
1. **Thứ tự**: mình đề xuất M0 trước (vừa kiểm chứng gói Store, vừa mở đường cho widget). Làm M0 xong mới bàn M1.
2. **Cách kiểm chứng**: dùng CI ký cert tạm (không cần đụng SAC), cộng bản cài từ Store để bạn thử tay. Bạn đồng ý không tắt SAC?
3. **Giới hạn Free/Plus của widget** ở M1 (1 lưới + Today List cho Free; Plus nhiều hơn và có Buddy/Thử thách/Multi Grid).
4. Widgets Board (M3) có đáng làm không, hay dừng ở widget desktop? Mình đề xuất quyết định sau khi có số đo M4.

## Tiến độ (10/11/2026)
### M0 — xong, đã kiểm chứng trên CI
- Gói MSIX chứa cả app WinUI và **HabitGrid Widgets** (WidgetHost) như ứng dụng thứ hai: alias `habitgrid.exe` và `habitgrid-widgets.exe`, StartupTask `HabitGridWidgetsStartup` (tắt mặc định, WidgetHost tự bật/tắt theo Cài đặt), kích hoạt toast qua COM cho nút trên thông báo nhắc nhở.
- `PackageIdentity` (Core) cho biết tiến trình có danh tính gói; trong gói `HostLink` mở WidgetHost qua alias, tự chạy dùng StartupTask thay khóa Run.
- Job CI `MSIX install smoke` (`.github/workflows/msix.yml`): dựng MSIX, ký chứng chỉ tạm, **cài thật** rồi chạy: app `--package-check` (tạo CSDL trong vùng dữ liệu của gói), WidgetHost `--package-check` (cùng danh tính gói, thấy đúng CSDL đó) và `--selftest` đầu-cuối (widget, check-in, làm mới, Quick check-in, toast, Focus). Cả ba bước đạt.
- Hai phát hiện khi làm: (1) thư viện toast chỉ đọc `Application` **đầu tiên** của manifest nên WidgetHost phải đứng trước app trong manifest; (2) **nhắc nhở (toast) chỉ chạy trong WidgetHost**, nên bản Store v1 (không chứa WidgetHost) chưa có nhắc nhở dù Cài đặt có thẻ Nhắc nhở. Bản 1.1 sửa được việc này.
- Chưa kiểm chứng: bản Store thật (do Microsoft ký) có mở được qua alias như bản ký tạm hay không; nút "Mở" của Store với manifest có `Application` ẩn đứng đầu; nhấn nút trên toast bằng tay; widget hiển thị trên màn hình thật (CI không có hình nền/WorkerW thật).
