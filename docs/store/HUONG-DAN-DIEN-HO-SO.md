# Hướng dẫn điền hồ sơ Microsoft Store — HabitGrid 1.0

Làm theo thứ tự. Mọi nội dung dán sẵn nằm trong `store-listing.md`. Tên nút/mục trên Partner Center có thể khác chút tùy bản giao diện; nếu không thấy đúng tên thì tìm mục gần nghĩa nhất.

## Chuẩn bị (5 phút)
1. Mở `docs/store/privacy-policy.md`, thay `[điền email hỗ trợ của bạn]` (hai chỗ, bản Việt và Anh) bằng email thật.
2. Đưa chính sách lên một địa chỉ công khai, ví dụ: vào https://gist.github.com → dán nội dung → chọn **Create public gist** → copy link. Link này dùng ở bước 3.
3. Tải gói: `artifacts\store\HabitGrid_1.0.0.0_x64.msix` (chạy `pwsh build\package-store.ps1` nếu cần tạo lại).
4. Ảnh chụp: thư mục `docs/store/screenshots/vi/` và `docs/store/screenshots/en/` (mỗi nơi 5 ảnh 1582×890–990, đạt chuẩn tối thiểu 1366×768). Logo: `src/HabitGrid.Package/Images/StoreListing-300x300.png` và `StoreListing-1080x1080.png`.

## Bước 1. Bắt đầu đợt gửi
Partner Center → Apps and games → **HabitGrid** → **Start your submission**. Sẽ thấy các phần: Pricing and availability, Properties, Age ratings, Packages, Store listings, Submission options.

## Bước 2. Pricing and availability
- **Markets:** để mặc định (tất cả thị trường), hoặc bỏ bớt nếu muốn thử ở vài nước.
- **Visibility:** Public.
- **Base price:** Free. **Free trial:** không có.
- **Schedule:** "Publish as soon as certified" (mặc định).
- Bỏ qua "Sales"/"Organizational licensing" ở bản đầu.

## Bước 3. Properties
- **Category:** Productivity. **Subcategory:** để trống hoặc chọn mục gần nhất.
- **Privacy policy URL:** dán link gist ở bước chuẩn bị 2.
- **Website / Support contact info:** email hỗ trợ (website có thể bỏ trống).
- **Display mode, System requirements:** để mặc định. Với "Does your app require..." tick các mục không áp dụng; app không cần mạng, không cần phần cứng đặc biệt (camera chỉ tùy chọn).
- **Product declarations:** chỉ tick những mục đúng sự thật. Bản này **không** có mua hàng trong app, không có quảng cáo, không thu thập dữ liệu cá nhân.

## Bước 4. Age ratings
Bấm **Get started / Edit** (bảng câu hỏi IARC):
1. Email của bạn → chọn loại **"Utility, Productivity, Collaboration, or Other"**.
2. Trả lời **No** cho: bạo lực, máu me, nội dung tình dục, ngôn ngữ thô tục, ma túy/rượu/thuốc lá, cờ bạc, nội dung đáng sợ.
3. Câu hỏi về tương tác với người dùng khác, chia sẻ vị trí, mua hàng kỹ thuật số, quảng cáo: **No**.
4. Lưu, xem kết quả: mong đợi **mọi lứa tuổi**. Bấm **Apply rating**.

## Bước 5. Packages
- Kéo thả file `.msix`. Đợi Store đọc (vài chục giây).
- Nếu có lỗi màu đỏ về danh tính (Identity name/publisher) hoặc phiên bản: **copy nguyên văn gửi cho tôi**.
- **Device family / Supported languages:** để Store tự đọc từ gói (en-us, vi-vn).
- Không cần tải thêm gói khác.

## Bước 6. Store listings
Bấm **Vietnamese (vi-vn)** (thêm ngôn ngữ nếu chưa có) rồi điền; sau đó thêm **English (en-us)**.

| Ô | Dán gì |
|---|---|
| Description | "Mô tả đầy đủ" ở mục 2 của `store-listing.md` (EN: mục 3) |
| What's new in this version | "Bản phát hành đầu tiên của HabitGrid." (EN: "First release of HabitGrid.") |
| Screenshots | Kéo 5 ảnh trong `screenshots/vi` (EN: `screenshots/en`) theo thứ tự 01 → 05; có ô chú thích cho mỗi ảnh: dùng gợi ý ở mục 4 của `store-listing.md` |
| Store logos → 1:1 Logo 300×300 | `StoreListing-300x300.png` |
| Store logos → 1:1 Logo 1080×1080 | `StoreListing-1080x1080.png` (nếu có ô) |
| App features (Highlights) | 8 dòng ở mục 2/3 |
| Short description | Mô tả ngắn (nếu có ô) |
| Search terms | 7 từ khóa ở mục 2/3 |
| Copyright and trademark info | `© 2026 MTD Studio` |
| Additional license terms | để trống (dùng điều khoản Store chuẩn) |
| Developed by | `MTD Studio` |

Ảnh poster 720×1080 / hộp 9:16 là tùy chọn; có thể bỏ ở bản đầu.

## Bước 7. Submission options
- **Publishing hold options:** chọn "Publish immediately" hoặc "Manually publish" nếu muốn tự bấm sau khi duyệt.
- **Notes for certification:** dán khối ở mục 7 của `store-listing.md`.
- Mục "Restricted capabilities": gói có `runFullTrust`; Store hỏi lý do, trả lời: *"HabitGrid is a desktop (WinUI 3) application that needs full-trust process to access its local database and user-chosen backup folders."*

## Bước 8. Gửi duyệt
Kiểm tra mọi mục đều có dấu tích xanh → **Submit to the Store**. Duyệt thường vài giờ đến vài ngày; kết quả báo qua email và trong Partner Center.

## Nếu bị từ chối
- Mở mục **Certification report** của đợt gửi, copy nguyên văn lý do và gửi tôi.
- Lỗi hay gặp: danh tính gói không khớp, thiếu chính sách quyền riêng tư công khai, ảnh chụp sai kích thước, thiếu giải thích cho `runFullTrust`, hoặc app crash khi cài thử (lúc đó tôi sẽ sửa và tạo gói 1.0.1.0).

## Sau khi lên Store
- Hồ sơ thuế/thanh toán + sản phẩm Plus (add-on) có thể làm sau; khi có mã sản phẩm, tôi làm bản cập nhật bán Plus (gói mới tăng version, ví dụ 1.1.0.0).
- Khuyến mãi Plus 30 ngày tính riêng cho từng người dùng từ lần mở app đầu, nên cần phát hành bản có mua hàng trước khi người dùng đầu tiên hết hạn.
