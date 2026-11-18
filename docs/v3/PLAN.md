# HabitGrid 3.0.0: kế hoạch triển khai

Nguồn: "Tài liệu Refactor UI HabitGrid (Master Spec v3.0)". Tài liệu này chuyển spec (viết theo CSS/web) sang cách làm thật của app: WinUI 3, giao diện dựng bằng C# trong `src/HabitGrid.WinUI`. Mỗi sprint là một PR riêng, CI xanh và được duyệt mới merge.

## 0. Hiện trạng đã kiểm tra trong code

| Spec nói | Thực tế trong code | Cách sửa |
|---|---|---|
| Trang xếp 1 cột, lệch trái, bỏ trống 35–45% | Mọi trang dùng `StackPanel _root` có `MaxWidth` 860–1100. Settings và Buddy còn `HorizontalAlignment.Left` | Thay bằng lưới Bento dùng chung (mục 1), full chiều rộng |
| Cắt chữ "Be Vietnam Pro (mặ", "Luôn trên cùng (khuyên dùn" | `ChipGroup` dùng `UniformGridLayout`: mọi chip rộng bằng nhau, chữ dài bị cắt | Chip tự co theo chữ, cho xuống dòng |
| Thanh cuộn ngang kệ phụ kiện Buddy, cắt "Hoa tulip" | `BuddyPage.xaml.cs:163` bọc hàng phụ kiện trong `ScrollViewer` ngang | Tab Đầu / Mặt / Cổ + lưới tự xuống dòng |
| Huy hiệu bị "..." | `BuddyPage.xaml.cs:234` tiêu đề 1 dòng `CharacterEllipsis` | Tiêu đề 2 dòng (`MaxLines = 2`, wrap) |
| Đè nhãn "Thg 11 / Thg 12" | `HeatmapGrid.cs:151` vẽ mọi nhãn tháng, kể cả tháng đầu chỉ có 1–2 cột | Ẩn nhãn tháng có < 3 cột tuần (logic thuần, có test) |
| Hàng chủ đề thẻ chia sẻ tràn | `ShareCardDialog.cs:130` hàng khung trong `ScrollViewer` ngang | Lưới tự xuống dòng, modal 2 cột |
| Nút "Chọn gói nà" | `PaywallDialog.cs` thẻ giá hẹp | Nút rộng 100% thẻ, chữ không cắt |
| Giá $2.99 / $19.99 / $39.99 | Đây chỉ là **giá tham khảo** khi Store không trả giá (`PaywallViewModel.ReferencePrices`). Giá thật đặt trong Partner Center | Sửa giá tham khảo; **bạn đổi giá thật trong Partner Center** |
| — | **Lỗi phát hiện thêm:** khi dùng giá tham khảo, gói Năm luôn hiện "Dùng thử 7 ngày" dù add-on không bật dùng thử (`PaywallViewModel.cs:143`) | Bỏ. Đây cũng là rủi ro chính sách 10.8.4 vừa bị nhắc |
| Toast xanh đè "Buổi tối" | `InfoBar` trải ngang trang (Habits, Insights) | Thông báo dạng viên thuốc nổi giữa đáy, rộng tối đa 460 |
| Ngôn ngữ chỉ vi / en | `Localizer` chỉ biết `vi`/`en`; widget dùng `HostText.IsVietnamese` (bool) | Thêm `ja`, `ko`; đổi widget sang chọn theo mã ngôn ngữ |

## 1. Quyết định kỹ thuật (dịch từ CSS sang WinUI)

1. **Lưới Bento:** control `BentoGrid` (Panel tự viết) thay cho `display: grid`.
   - Bên trong chia **8 cột** để làm được các ô "1.5 cột" của spec. Ví dụ: 1 cột spec = 2 cột lưới, 1.5 cột spec = 3 cột lưới.
   - Mỗi ô khai `Span` và `RowSpan`. Khoảng cách 16, lề 24/32 như spec.
   - **Co giãn theo bề rộng:** từ 1100 px trở lên dùng 4 cột; 720–1100 px dùng 2 cột; dưới 720 px dùng 1 cột. Ô rộng hơn số cột hiện có thì tự thu lại vừa hàng.
   - Logic xếp ô là hàm thuần trong Core nên có unit test.
2. **Glassmorphism kiểu macOS:** làm bằng vật liệu sẵn có của Windows, gồm Acrylic (đã có thanh trượt độ kính mờ) và viền sáng 1 px. Thẻ kính là một style dùng chung (`GlassCard`) với token màu nền, viền và bóng. Kết quả giống tinh thần spec nhưng đúng chất Windows; không giả lập blur kiểu web.
3. **Mục 3 "Design System" trong spec để trống.** Mình tự định nghĩa token:
   - Bo góc 16 cho thẻ, 10 cho chip.
   - Khoảng cách theo bậc 4/8/12/16/24.
   - Nền thẻ kính: sáng 72% / tối 55% độ phủ.
   - Viền 1 px trắng 8–12%.
   - Bạn sửa được sau khi xem.
4. **Không đổi dữ liệu, không đổi logic nghiệp vụ.** Chỉ đổi lớp giao diện, ngoại trừ: nhãn tháng heatmap, xếp ô Bento, giá tham khảo, ngôn ngữ và âm thanh Focus.
5. **Phiên bản:** gói 3.0.0.0. Phải lớn hơn bản đã đăng (1.1.x).

## 2. Các điểm cần bạn quyết (mình đã chọn mặc định, đổi được)

| # | Vấn đề | Mặc định mình làm | Lý do |
|---|---|---|---|
| Q1 | Giá mới $1.99 / $9.99 / $19.99, VN 29.000 / 149.000 / 299.000 ₫ (ra mắt 249.000 ₫) | Sửa giá tham khảo trong app. **Bạn tự đổi giá thật** trong Partner Center (từng add-on → Pricing). Giá ra mắt dùng "Schedule a price change" / sale | App không tự đặt giá được; Store mới là nguồn giá |
| Q2 | Spec muốn "Dùng thử 7 ngày" cho gói Năm (Store trial) | **Không bật.** Giữ dùng thử 30 ngày trong app như hiện tại | Hai kiểu dùng thử chồng nhau gây rối. Mô tả Store vừa phải sửa vì 10.8.4; bật thêm trial thì phải sửa mô tả lần nữa |
| Q3 | Thêm 日本語 / 한국어 | Làm đủ: 795 chuỗi app + chuỗi widget, phông chữ dự phòng, lưới chọn 4 ngôn ngữ | **Bản dịch do mình viết, chưa có người bản xứ duyệt.** Nên nhờ người biết tiếng duyệt trước khi thêm ngôn ngữ vào trang Store |
| Q4 | "Ambient Sound Mixer" ở trang Focus | Tự tạo âm thanh bằng code (tiếng ồn trắng/hồng/nâu, mưa, sóng tổng hợp), có thanh âm lượng từng kênh và sóng âm chạy | Không cần file âm thanh có bản quyền. Nếu bạn có file âm thanh riêng muốn dùng, đưa mình |
| Q5 | Thứ tự phát hành | 3.0.0 phát hành sau khi bản 1.1 hiện tại đã lên Store | Tránh hai bản nộp chồng nhau trong Partner Center |

## 3. Lộ trình (4 sprint, mỗi sprint 1 PR)

### Sprint 1: Nền tảng + sửa 10 lỗi cắt chữ (`v3/s1-foundation`)
1. `BentoGrid` (Core: xếp ô + test; WinUI: Panel) và style `GlassCard` + token.
2. Bỏ căn trái (Cài đặt, Buddy căn giữa). `MaxWidth` được bỏ khi từng trang chuyển sang Bento ở sprint của nó: bỏ sớm khi trang còn xếp 1 cột sẽ làm dòng chữ và hàng chip dài hết 1920 px, xấu hơn hiện tại.
3. Mười lỗi trong mục 4 của spec:
   1. Nút chọn gói Paywall bị cắt: nút rộng 100% thẻ.
   2. Chip phông chữ bị cắt: `ChipGroup` co theo chữ, cho xuống dòng.
   3. Chip kiểu ghim widget bị cắt: như trên, rút gọn nhãn.
   4. Đè nhãn tháng heatmap: ẩn tháng có < 3 cột (`HeatmapBuilder.MonthLabels`, có test).
   5. Cắt chân chữ "mỗi năm": bỏ cắt trong thẻ giá, giãn dòng.
   6. Thẻ preview chia sẻ bị cắt đáy: chuyển sang tỷ lệ vừa khung (làm cùng modal 2 cột ở S4; S1 sửa tạm cho không cắt).
   7. Hàng chủ đề chia sẻ tràn: lưới tự xuống dòng.
   8. Kệ phụ kiện Buddy cuộn ngang: lưới tự xuống dòng (tab ở S4).
   9. Huy hiệu bị "...": tiêu đề 2 dòng.
   10. Toast đè nội dung: viên thuốc nổi giữa đáy, tối đa 460.
4. Sửa giá tham khảo và bỏ nhãn dùng thử sai (Q1, Q2).

### Sprint 2: Hôm nay, Lịch, Nhật ký, Focus (`v3/s2-today-calendar-focus`)
- **Hôm nay:**
  - Hàng 1: lịch tuần (3) + tâm trạng (1).
  - Hàng 2: Mochi (2) + chuỗi & freeze (1) + Focus nhanh / mini heatmap (1).
  - Hàng 3: thẻ thói quen theo buổi.
  - Bỏ nút `+` trùng.
- **Lịch:** lịch tháng (2.5 × 2 hàng) + checklist trong ngày (1.5) + thẻ tổng kết tháng có vòng tiến độ (1.5).
- **Nhật ký:** lịch sử + tìm kiếm (1 × 2) | vùng viết + chọn mood (2 × 2) | biểu đồ cảm xúc 30 ngày (1) + ảnh khoảnh khắc trong ngày (1).
- **Focus:**
  - Đồng hồ lớn vòng sáng (2 × 2) với chip 15/25/45/60 phút thay ô nhập số.
  - Thống kê và biểu đồ cột tuần (2).
  - Bàn trộn âm thanh (2) (Q4).

### Sprint 3: Thói quen, Chi tiết thói quen, Insights (`v3/s3-habits-insights`)
- **Thói quen:** lưới thẻ + mini heatmap 30 ngày. Nửa dưới hiện thẳng Routine (2) và Thử thách đang mở (2), không cần popup.
- **Chi tiết thói quen (không cuộn ở 1080p):**
  - Hàng 1: banner (2) + 3 chỉ số (2).
  - Hàng 2: heatmap 52 tuần (3) + khoảnh khắc kiểu polaroid (1).
  - Hàng 3: theo tháng (1.5) + theo thứ (1.5) + nhật ký thói quen (1).
- **Insights 4 tầng:**
  - Tầng 1: KPI + sparkline (2) | gợi ý (1) | tâm trạng (1).
  - Tầng 2: xu hướng tuần dạng biểu đồ vùng (2) | theo thứ (1) | giờ check-in (1).
  - Tầng 3: tiến độ từng thói quen (2) | so sánh A/B + "đi cùng nhau" (2).
  - Tầng 4: lịch cả năm (4), bấm ô để xem nhanh.

### Sprint 4: Buddy, Cài đặt, Modal, giá, 4 ngôn ngữ (`v3/s4-buddy-settings-i18n`)
- **Buddy:** sân khấu Mochi cố định bên trái (1.5) có đèn nền. Bên phải (2.5): tủ đồ dạng tab Đầu / Mặt / Cổ, lưới 4 × 2, cùng 23 huy hiệu dạng lưới tự co, tiêu đề 2 dòng.
- **Cài đặt 3 cột:**
  - Cột 1: giao diện (chủ đề, màu nhấn, phông, sáng/tối, kính mờ, giảm chuyển động).
  - Cột 2: widget, nhắc nhở, giờ yên lặng, phím tắt.
  - Cột 3: Plus, sao lưu local/cloud, tạm nghỉ, ngôn ngữ, hỗ trợ, quyền riêng tư.
- **Thẻ chia sẻ:** modal 760 px, preview trọn vẹn bên trái, điều khiển bên phải.
- **Paywall Bento:** lưới 6 ô tính năng + 3 thẻ giá cân nhau, nút rộng 100%.
- **Đa ngôn ngữ:**
  - `ja`, `ko` cho app và widget.
  - Phông dự phòng: Segoe UI → Yu Gothic UI → Malgun Gothic → Microsoft YaHei UI.
  - Lưới chọn 4 ngôn ngữ, đổi tức thì (đã có cơ chế đổi tức thì).

## 4. Kiểm chứng

- **Unit test** cho mọi logic mới: xếp ô Bento, ẩn nhãn tháng, giá tham khảo và nhãn dùng thử, chọn ngôn ngữ, đủ khóa ở 4 file `.resx`.
- **Ảnh render:** SmokeRunner đã render các trang ra PNG ngoài màn hình (không chụp màn hình người dùng). Mỗi sprint render ở 1366 × 768 và 1920 × 1080 để mình tự xem bố cục, cắt chữ và khoảng trống trước khi đưa bạn.
- **CI:** build, test, smoke, MSIX install cho mỗi PR.
- **Không kiểm được bằng máy, cần bạn xem:** cảm giác thẩm mỹ, độ mượt, chất lượng bản dịch ja/ko, âm thanh Focus.

## 5. Rủi ro

- Khối lượng lớn: 12 màn hình dựng lại. Mỗi sprint giữ nguyên hành vi, chỉ đổi bố cục; smoke test hiện có bảo vệ các luồng chính.
- Nhánh `main` đang có thay đổi chưa commit của một phiên khác (sửa sao lưu, check-in đồng thời, bản 1.1.2). v3 làm trong worktree riêng (`D:\Go-Live\MS\HabitGrid-v3`) và sẽ rebase khi phần đó vào `main`.
