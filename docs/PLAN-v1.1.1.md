# HabitGrid 1.1.1 — kế hoạch cập nhật

Audit ngày 18/11/2026 trên commit `8271d31`. Tên phiên bản là đề xuất, lấy dòng 1.1 trong script đóng gói làm mốc. Mục tiêu là ổn định dữ liệu, Dynamic Island và quy trình phát hành Microsoft Store.

## Phát hiện

| Ưu tiên | Bằng chứng | Ảnh hưởng |
| --- | --- | --- |
| P0 | `SnapshotBackupService.RestoreAsync` commit dữ liệu rồi gọi `_profile.SaveAsync` trong giao dịch riêng. | Nếu lưu hồ sơ lỗi hoặc bị hủy, giao diện có thể báo khôi phục thất bại sau khi dữ liệu chính đã bị thay thế. |
| P1 | `BackupPayload` liệt kê thói quen, log, routine, hồ sơ và nhật ký. Export đọc qua nhiều truy vấn riêng. | Các mục như lịch sử Focus và ảnh Moment nằm ngoài payload. Ghi dữ liệu trong lúc xuất có thể khiến các phần của bản sao thuộc các thời điểm khác nhau. |
| P1 | `CheckInService.ToggleAsync` coi cả `Frozen` và `Skipped` là giá trị 0 rồi ghi log mới. `RoutineService` bỏ qua hai trạng thái này khi hoàn thành cả routine. | Quick Check-in có thể ghi đè ngày tạm nghỉ. Test hiện có chủ đích cho phép sửa ngày `Skipped`, nên cần kiểm thử riêng `Frozen`. Đọc và ghi log diễn ra riêng, tạo nguy cơ mất lượt tăng khi có nhiều nguồn check-in. |
| P1 | CI có WinUI smoke, WidgetHost self-test và kiểm tra xóa dữ liệu. MSIX smoke chỉ chạy thủ công hoặc khi push vào nhánh `store/**`, `widgets/**`. Explorer restart đang ở chế độ thử nghiệm, cho phép thất bại. | Một thay đổi có thể qua CI thường mà chưa được kiểm tra trong gói cài. Các tương tác với desktop vẫn cần xác nhận trên máy Windows. |

## Thứ tự triển khai

1. **Dữ liệu · 2–3 ngày.** Đưa hồ sơ vào cùng giao dịch khôi phục. Đọc snapshot từ trạng thái nhất quán và phối hợp với WidgetHost trong lúc restore. Kiểm tra file cũ, file lỗi, lỗi ghi hồ sơ và backup trước khôi phục. Ghi rõ phạm vi `.hgbackup` trong Cài đặt. Việc mở rộng định dạng để lưu thêm ảnh và lịch sử Focus cần thiết kế riêng.
2. **Check-in và widget · 2–3 ngày.** Bảo toàn ngày `Frozen` ở các lối check-in nhanh, giữ hành vi hiện có với `Skipped`, kiểm thử thao tác từ hai tiến trình và bảo đảm phép tăng được ghi nhất quán. QA trạng thái thu gọn, hover và panel, qua nửa đêm, màn hình có DPI khác nhau, Win+D, sleep/resume, fullscreen và Explorer restart.
3. **MSIX và Store · 1–2 ngày.** Chạy MSIX smoke cho PR ảnh hưởng đến app/host/đóng gói và trên ứng viên phát hành. Cài mới và nâng cấp từ gói trước trên Windows 10/11 x64. Kiểm tra hai tiến trình mở được từ gói và dùng chung dữ liệu. Trên bản Store thử nghiệm, xác nhận giá, luồng mua/khôi phục quyền Plus, add-on và URL chính sách. Kiểm tra bàn phím, Narrator, chữ lớn và cả tiếng Việt lẫn tiếng Anh ở các luồng chính.

## Điều kiện phát hành

- Test tự động và MSIX smoke đều xanh trên commit phát hành. Có kết quả QA thực tế cho các ca desktop trong kế hoạch.
- Khôi phục thành công cập nhật cả dữ liệu lẫn hồ sơ. Khi cố ý làm lỗi giữa chừng, trạng thái trước thao tác vẫn nguyên vẹn. File `.hgbackup` do bản trước tạo vẫn đọc được.
- Ngày `Frozen` được bảo toàn qua quick check-in. Với các lượt tăng hợp lệ đồng thời dưới ngưỡng mục tiêu, số đếm phản ánh đủ số lượt.
- Bản nâng cấp đọc được dữ liệu hiện có. App và WidgetHost nhìn thấy cùng lịch sử. Nếu bản phát hành hiển thị Plus, giá và quyền mua được xác nhận trên gói Store thử nghiệm.

Ước lượng 5–8 ngày công cho một người, chưa tính thời gian duyệt Store. Cloud sync, mobile companion và mở rộng phạm vi backup để ở backlog sau đợt ổn định.

## Xác nhận bản ứng viên

- [x] 1.050 test Release, gồm lỗi khi ghi hồ sơ, backup cũ, khôi phục cùng giây tạo backup và check-in đồng thời từ hai kết nối SQLite.
- [x] Dựng MSIX x64 và kiểm tra manifest có cả HabitGrid và HabitGrid Widgets.
- [x] WinUI smoke và WidgetHost self-test chạy từ thư mục dựng gói với dữ liệu thử riêng. WidgetHost xác nhận Quick Check-in, đồng bộ dữ liệu, lịch toast, mở panel và đặt Dynamic Island ở giữa cạnh trên.
- [x] Windows 11 x64 build 26300, ngày 18/11/2026. Kiểm tra chữ ký của hai gói Developer, cài sạch 1.1.1.0 và nâng cấp tại chỗ từ gói đối chứng 1.1.0.0 dựng từ `8271d31`. Hai alias xác nhận cùng đọc hai thói quen thử trong lượt cài sạch. Tệp SQLite riêng của bản đối chứng giữ nguyên SHA-256 qua bước nâng cấp. WidgetHost bản mới đọc và ghi được vào dữ liệu đó khi chạy self-test.
- [x] WidgetHost self-test qua trên cả bản cài sạch và bản nâng cấp đã ký. Đã xác nhận thu gọn, hover, panel, nhắc nhở, chế độ siêu gọn, Quick Check-in, toast, Focus và ẩn/hiện theo tín hiệu fullscreen mô phỏng. Sửa bài kiểm tra siêu gọn để ghi tùy chọn qua SQLite và buộc các lệnh QA dùng `HABITGRID_DATA_DIR` riêng.
- [ ] Lặp lại cài sạch và nâng cấp trên Windows 10 x64, rồi xác nhận nâng cấp từ gói Store hiện hành khi có quyền truy cập.
- [ ] Trên desktop kiểm tra Dynamic Island ở ba trạng thái, Win+D, qua nửa đêm, ngủ/thức, fullscreen, Explorer restart và hai màn hình khác DPI. Thử đồng thời bấm check-in ở app và widget cho cùng một thói quen đếm. Kiểm tra bàn phím, Narrator, cỡ chữ lớn và hai ngôn ngữ.
- [ ] Trong Partner Center, đối chiếu phiên bản ứng viên với bản đang phát hành và xác nhận ba add-on cùng các URL chính sách. Trên bản Store thử nghiệm, kiểm tra hiển thị giá, mua và khôi phục quyền Plus.

### QA trên Microsoft Store

- Store ID `9P08C5JKDZKM`. Bản cài qua Microsoft Store trên Windows 11 báo `1.1.1.0`, chữ ký `Store` và package family khớp Partner Center. Gói cập nhật có hai bản sửa Plus dùng phiên bản `1.1.2.0`.
- Paywall tải được giá Việt Nam từ Store. Tháng 39.900 ₫, năm 249.900 ₫ và trọn đời 499.900 ₫. Checkout gói tháng hiện giá khớp và yêu cầu thêm phương thức thanh toán. Đã đóng cửa sổ checkout trước khi thực hiện giao dịch.
- Restore trên tài khoản Free báo không tìm thấy giao dịch Plus. Bản Store 1.1.1 khóa mua trong thời gian tặng Plus và báo đã khôi phục chỉ dựa vào quyền tặng. Mã nguồn ứng viên đã sửa cả hai hành vi và có test hồi quy.
- Còn cần kiểm tra mua thành công và khôi phục quyền đã mua trên tài khoản thử, cùng lượt QA sau khi gói ứng viên được phân phối qua Store.

### Soak rút gọn 1.1.2

Ngày 19/11/2026, chạy WinUI và WidgetHost từ bộ file dựng MSIX trên Windows 11 với hai bản sao SQLite thử riêng. Mục tiêu 20 phút liên tục.

- Lượt 1 dừng ở mẫu phút 11. WinUI thoát mã 0.
- Lượt 2 dừng ở mẫu phút 5 phút 30 giây. WinUI cũng thoát mã 0.
- Cả hai SQLite đều trả `ok` khi chạy `PRAGMA integrity_check` sau khi dừng. Log ứng dụng và Windows chưa ghi nhận sự kiện crash HabitGrid phù hợp với hai thời điểm đó.
- Kết quả: FAIL soak 20 phút. Chưa xác định nguyên nhân WinUI thoát.
