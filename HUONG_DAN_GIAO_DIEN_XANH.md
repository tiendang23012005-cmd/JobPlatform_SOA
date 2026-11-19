# Hướng dẫn cập nhật giao diện (tông xanh dương)

Gói chép đè lên `src/Client.Blazor`. Mã chưa được biên dịch thử trong môi trường tạo gói; nếu `dotnet build` lỗi, gửi lại nguyên văn lỗi. Giao diện dùng Bootstrap 5.3, Bootstrap Icons và font Inter tải từ CDN, nên máy cần có Internet khi mở trang.

## Giao diện mới gồm
- **Trang chủ công khai:** thanh menu trên cùng, banner giới thiệu, tính năng, cách hoạt động, kêu gọi đăng ký, chân trang.
- **Đăng ký / Đăng nhập / Xác minh OTP:** màn hình chia đôi (bên trái giới thiệu màu xanh, bên phải biểu mẫu), nút hiện/ẩn mật khẩu, thanh đo độ mạnh mật khẩu, chọn vai trò bằng thẻ, hiệu ứng đang xử lý.
- **Sau đăng nhập:** sidebar xanh đậm với menu theo vai trò (Freelancer, Nhà tuyển dụng, Quản trị viên), thanh trên cùng có chuông thông báo (số chưa đọc), menu người dùng (hồ sơ, thông báo, đăng xuất). Trên điện thoại sidebar thu gọn thành nút menu.
- **Bảng điều khiển theo vai trò:** thẻ chào mừng, 4 thẻ thống kê, lối tắt, thông báo gần đây; Admin có thêm trạng thái 5 dịch vụ.
- **Trang thật:** Hồ sơ, Thông báo (đánh dấu đã đọc), Hệ thống (Admin). Các mục chưa làm (tìm việc, hợp đồng, ví...) dẫn tới trang "Tính năng đang được phát triển".

## Bước A. Chép gói
Giải nén zip bằng File Explorer, chép nội dung thư mục `JobPlatform` đè vào `D:\C_vscode_tim\JobPlatform` (Replace).

## Bước B. Xóa các tệp mẫu cũ (tránh xung đột giao diện)
```powershell
cd D:\C_vscode_tim\JobPlatform\src\Client.Blazor
Remove-Item Layout\MainLayout.razor.css, Layout\NavMenu.razor, Layout\NavMenu.razor.css -ErrorAction SilentlyContinue
Remove-Item Pages\Counter.razor, Pages\Weather.razor -ErrorAction SilentlyContinue
Remove-Item wwwroot\sample-data -Recurse -ErrorAction SilentlyContinue
cd ..\..
```

## Bước C. Build và chạy
```powershell
dotnet build
dotnet run --project src/Client.Blazor
```
Mở http://localhost:5100. Nếu trình duyệt còn giao diện cũ, nhấn `Ctrl+F5`.

## Bước D. Kiểm tra
1. Chưa đăng nhập: thấy trang chủ giới thiệu, nút Đăng nhập, Đăng ký.
2. Đăng ký thử (xem lỗi mật khẩu yếu, thanh độ mạnh), xác minh OTP, đăng nhập.
3. Đăng nhập lần lượt bằng Freelancer, Employer, `admin@jobplatform.local` / `Admin@12345`: mỗi vai trò có menu và bảng điều khiển khác nhau.
4. Với Admin: mục Hệ thống hiện 5 dịch vụ "Hoạt động" và kết quả `admin ok`.
5. Thu nhỏ cửa sổ trình duyệt: sidebar ẩn, nút menu xuất hiện.

## Lỗi thường gặp
| Hiện tượng | Cách xử lý |
|---|---|
| Mất biểu tượng hoặc font | Máy không có Internet (CDN); kết nối mạng rồi tải lại |
| Giao diện lệch, thanh bên lạ | Chưa chạy Bước B (còn `MainLayout.razor.css`) hoặc cần `Ctrl+F5` |
| Lỗi build `NavMenu` hoặc `Counter` | Chạy lại Bước B |
| Lỗi build ở `.razor` | Gửi nguyên văn dòng `error RZ...` hoặc `error CS...` |
