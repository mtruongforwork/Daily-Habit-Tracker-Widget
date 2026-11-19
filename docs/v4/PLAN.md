# HabitGrid 4.0.0: Kế hoạch triển khai

Nguồn tham khảo: khảo sát thực tế **everyday**, **Habitify**, **Finch**, **Habitica** và các quyết định đã chốt ngày 20/11/2026. Mỗi sprint là một PR riêng, CI xanh (`dotnet test`, WinUI `--smoke`, WidgetHost `--selftest`) mới merge.

## 1. Quyết định đã chốt (20/11/2026)

1. **Nhánh & Phiên bản:** Triển khai từ nhánh `v4/s1-core-logic` (tách từ `v3/s4-buddy-settings-i18n`), mục tiêu phát hành **4.0.0.0**.
2. **Lưu trữ dữ liệu:** Nâng cấp SQLite v3 an toàn (idempotent migration, tương thích ngược 100% dữ liệu v3; đồng bộ đầy đủ với `SnapshotBackupService` và `EraseAll`).
3. **Phân chia Free vs Plus (kiểu Finch / everyday):**
   - **Free:** Toàn bộ trải nghiệm cốt lõi — *Goal of the Day*, *No Weekends*, *Dual Habit (`+`/`−`)*, *Quick To-Do*, *Gentle Streak Repair*, *Never Miss Twice*, *Inline Context Actions*, âm thanh & hiệu ứng check-in, Mochi tương tác cơ bản, *Matrix Board* (14 ngày), tối đa **3 Areas** và **3 Custom Rewards**.
   - **Plus:** *Matrix Board* 30–365 ngày, *Streak Timeline* nâng cao, *Custom Rewards* & *Areas* không giới hạn, *Phòng Mochi theo mùa*, *Habit Stacking* triggers.

## 2. Lộ trình 4 Sprint

### Sprint 1 — Core Logic, Schema & Rewards (`v4/s1-core-logic`)
1. **Core Domain & Logic thuần:**
   - **No Weekends:** tùy chọn `SkipWeekends` trên `Habit` (tự động coi T7/CN là ngày nghỉ hợp lệ, không làm đứt chuỗi).
   - **Dual Habit (`+` / `−`):** mở rộng `HabitKind` thêm `Dual` (hoặc hỗ trợ đếm +/-), ghi nhận tích cực tăng xu/XP, tiêu cực giảm xu nhẹ (không phạt nặng gây nản).
   - **Goal of the Day:** đánh dấu 1 thói quen tiêu điểm mỗi ngày (`goal_of_day` theo ngày), hoàn thành nhận **×2 XP** và **×2 xu**.
   - **Gentle Streak Repair & "Never Miss Twice":**
     - Hàm thuần phát hiện thói quen vừa lỡ hôm qua khi đang có chuỗi (`IsAtRiskForSecondMiss`).
     - Cơ chế **Sửa chuỗi**: nếu đứt chuỗi ≤ 48h và hoàn thành 2 ngày liên tiếp tiếp theo (hoặc dùng token sửa chuỗi từ mốc tuần), tự động nối lại chuỗi.
   - **Areas (Lĩnh vực):** `HabitArea` (Sức khỏe, Công việc, Học tập, Tinh thần, hoặc tùy chỉnh; Free tối đa 3, Plus không giới hạn).
   - **Quick To-Do:** việc cần làm 1 lần trong ngày (`quick_todos`: tiêu đề, ngày, hoàn thành, thưởng xu nhỏ).
   - **Custom Rewards Shop:** ví xu (`coins` trong `GamificationState` / settings), tạo/sửa/xóa phần thưởng tự định nghĩa (`custom_rewards`, Free tối đa 3, Plus không giới hạn) và đổi thưởng (`RedeemAsync`).
2. **Data Layer:**
   - Mở rộng SQLite schema (idempotent `CREATE TABLE IF NOT EXISTS` / `ALTER TABLE ADD COLUMN` cho `habits`, `quick_todos`, `habit_areas`, `custom_rewards`, `reward_redemptions`), cập nhật `SnapshotBackupService` và `EraseAll`.
3. **Presentation & UI cơ bản cho Sprint 1:**
   - ViewModel + UI trên *Hôm nay* (thẻ Goal of the Day, khu vực Quick To-Do gọn, nhãn "Đừng lỡ 2 ngày liên tiếp", nút Sửa chuỗi), *Thói quen* (lọc theo Area, tạo/sửa Dual Habit & No Weekends), và tab/hộp thoại *Phần thưởng* trong *Buddy*.
4. **Kiểm chứng:** Unit test cho toàn bộ logic mới (No Weekends, Dual, Goal of the Day ×2, Gentle Streak Repair, Never Miss Twice, Areas limit, Rewards wallet/redeem, SQLite migration/backup) + WinUI `--smoke`.

### Sprint 2 — Visual & Matrix Board (`v4/s2-matrix-visuals`)
1. **Matrix Board view** trên trang *Thói quen*: bảng `Thói quen × Ngày` (14 ngày Free, 30/90/365 ngày Plus), check-in 1 chạm trên ô, ô **đậm màu dần theo chuỗi liên tiếp** (kiểu everyday), kéo-thả sắp xếp thứ tự hàng.
2. **Continuous Streak Pill Bars** trên *Lịch tháng* (nối ngày liên tiếp thành dải pill bo tròn kiểu Habitify) + **Streak Timeline** ngang trên *Chi tiết thói quen*.
3. **Dynamic Task Urgency:** thẻ thói quen đổi sắc nhẹ theo độ đều đặn / bỏ quên + thưởng bonus XP "Hồi sinh" khi quay lại.
4. **Mochi Living Room:** nền phòng Mochi đổi ánh sáng theo giờ trong ngày và thời tiết theo âm thanh Focus đang phát (Plus mở khóa chủ đề phòng theo mùa).

### Sprint 3 — Motion, Audio & Mochi Interactive Loop (`v4/s3-motion-mochi`)
1. **Inline Context Actions** trên dòng thói quen: `▶ Focus` (thời lượng), `+1` / `−1` + thanh fill mượt (đếm số), checkbox lò xo (có/không).
2. **WinUI Composition Animations:** `SpringAnimation` khi check-in/tăng số, `ImplicitAnimations` hover thẻ kính, gợn sóng check-in, pháo giấy nhẹ khi đạt **Perfect Day**.
3. **Âm thanh check-in & hoàn thành ngày:** tổng hợp bằng code (pop/chime nhẹ nhàng, không dùng file có bản quyền), công tắc bật/tắt trong Cài đặt.
4. **Mochi Interactive Loop:** chạm/vuốt ve Mochi để đổi biểu cảm, nhận câu thoại phản hồi theo tiến độ ngày & "khám phá nhỏ" khi hoàn thành 100% ngày.

### Sprint 4 — Journal Prompts, Habit Stacking & Widget Sync (`v4/s4-stacking-widgets`)
1. **Guided Reflection Prompts** trong trang *Nhật ký* (chọn nhanh mẫu câu hỏi theo ngày).
2. **Habit Stacking Triggers (Plus):** *"Sau khi xong A → gợi ý làm B"* ngay trên *Hôm nay* và toast nhắc.
3. **WidgetHost Sync:** đồng bộ *Goal of the Day* + *Quick To-Do* lên Hub Widget / Today List + bản dịch đủ 4 ngôn ngữ (`vi`, `en`, `ja`, `ko`).
4. **Đóng gói & Kiểm chứng cuối:** cập nhật `4.0.0.0`, kiểm tra render 1366×768 & 1920×1080, full CI (`dotnet test`, `--smoke`, `--selftest`).
