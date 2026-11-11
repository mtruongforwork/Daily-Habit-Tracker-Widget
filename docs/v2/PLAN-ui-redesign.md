# Kế hoạch làm lại giao diện HabitGrid: kính mờ, chuyển động, nhiều tính năng

> 11/11/2026. Quyết định của người dùng: **kính mờ Acrylic**, làm **nền tảng + trang Hôm nay trước**, thêm **lịch tháng, nhật ký ngày, trang Focus trong app, bảng điều khiển tùy biến**.

## Nguyên tắc
- Kính mờ thật của Windows (`DesktopAcrylicBackdrop`), cường độ chỉnh được 0–100 (0 = nền đặc, cũng là phương án dự phòng khi hệ thống không hỗ trợ).
- Chuyển động bằng Composition (mượt, chạy trên luồng dựng hình), tôn trọng "giảm chuyển động" của Windows và của app.
- Mọi tính năng mới theo nhịp cũ: lõi thuần + test → dịch vụ → giao diện → smoke → CI. Chỉ nói "đã kiểm chứng" cho những gì CI hoặc ảnh thật xác nhận.
- **Hạn chế đã biết:** ảnh chụp trong smoke test vẽ XAML ngoài màn hình nên **không có lớp kính**; hiệu ứng kính chỉ xác nhận được bằng mắt trên máy thật.

## UI-1 — Nền tảng (đang làm)
| Việc | Trạng thái |
|---|---|
| Backdrop Acrylic cho cửa sổ chính + lớp canvas trong suốt theo cường độ (`GlassStyle`, có test) | Xong mã, chờ xem trên máy thật |
| Thanh trượt "Kính mờ" trong Cài đặt | Xong mã |
| Hệ thống chuyển động `Motion`: xuất hiện lần lượt, nâng khi rê chuột, nhún khi bấm | Xong mã; đã áp cho các khối của trang Hôm nay |
| Hệ thống token: thẻ kính (độ đục, viền sáng góc trên), bóng, bo góc thống nhất | Tiếp theo |
| Chuyển trang (drill-in, cùng hướng với thao tác) và vi hiệu ứng check-in | Tiếp theo |

## UI-2 — Trang Hôm nay dạng bảng điều khiển tùy biến
Thẻ (Buddy, tiến độ, thử thách, tâm trạng, tổng kết tuần, routine, thống kê nhanh) kéo-thả sắp xếp, bật/tắt từng thẻ, nhớ bố cục. Lõi: mô hình bố cục thuần (thứ tự, ẩn/hiện, kích cỡ ô) + test.

## UI-3 — Tính năng mới
1. **Lịch tháng/năm** + xem lại và sửa một ngày (tái dùng `DayEditorFlyout`, `YearHeatmap`).
2. **Nhật ký ngày**: một trang ghi chú mỗi ngày gắn tâm trạng, tìm kiếm. Cần bảng lưu mới (không dùng settings vì dữ liệu lớn và cần đồng bộ sau này).
3. **Trang Focus trong app**: dùng lại `FocusService`/`FocusTimer`, thêm hiển thị lớn, chọn thói quen, lịch sử phiên; âm thanh nền (Plus, đã có trong bảng giá).

## UI-4 — Áp phong cách cho các trang còn lại
Thói quen, Insights, Buddy, Cài đặt, hộp thoại; duyệt từng trang bằng ảnh thật.

## Rủi ro
- Hiệu năng trên máy yếu: Acrylic + đốm màu trôi + animation. Cần tắt đốm màu khi kính bật cường độ cao hoặc trên máy yếu, đo thật.
- Tương phản chữ trên nền kính sáng: cường độ tối đa được chặn để chữ luôn đọc được (đã có test cho độ đục tối thiểu).
