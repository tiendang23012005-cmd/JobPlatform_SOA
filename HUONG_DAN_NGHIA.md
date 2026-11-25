# Phần công việc của Nghĩa – Hồ sơ, tài khoản và chính sách

## Các thay đổi

### Identity Service
- `PUT /api/v1/users/me`: cập nhật họ tên, số điện thoại, kỹ năng và giới thiệu.
- `PUT /api/v1/users/me/password`: đổi mật khẩu, yêu cầu mật khẩu hiện tại và kiểm tra quy tắc mật khẩu hiện có.
- `GET /api/v1/users`: Admin tìm kiếm theo họ tên/email, lọc vai trò/trạng thái và phân trang (`page`, `pageSize`).
- API khóa/mở khóa cũ được giữ lại; bổ sung kiểm tra trạng thái hợp lệ và phát sự kiện qua Outbox.
- Sự kiện mới: `UserLocked`, `UserUnlocked` trong `Shared.Contracts/Events`.

### Blazor
- `/profile`: chỉnh sửa hồ sơ và đổi mật khẩu.
- `/admin/users`: Admin tìm kiếm, lọc, phân trang, khóa/mở khóa người dùng.
- Menu Admin chuyển mục Quản lý người dùng từ `soon/nguoi-dung` sang `admin/users`.
- Sửa đăng ký dịch vụ và khởi chạy Blazor bị lặp trong `Program.cs`.

### API Gateway
- Fixed-window rate limit theo IP cho các request đi qua reverse proxy.
- Mặc định 100 request / 60 giây / IP; có thể cấu hình bằng `RateLimiting:PermitLimit` và `RateLimiting:WindowSeconds`.
- Khi vượt giới hạn trả HTTP 429.

## Database / Migration
Không thêm hoặc thay đổi cột/bảng nên không cần tạo migration mới. Các sự kiện dùng bảng Outbox hiện có.

## Kiểm thử sau khi chạy dự án
1. Đăng nhập bằng tài khoản Freelancer/Employer, mở `/profile`, cập nhật họ tên, số điện thoại, kỹ năng và giới thiệu; tải lại trang để xác nhận dữ liệu được lưu.
2. Đổi mật khẩu bằng mật khẩu hiện tại đúng; thử mật khẩu hiện tại sai và mật khẩu mới không đạt quy tắc.
3. Đăng nhập bằng Admin, mở `/admin/users`; tìm theo email/họ tên, lọc vai trò/trạng thái, chuyển trang.
4. Khóa một tài khoản đã xác minh; kiểm tra trạng thái chuyển `Locked` và đăng nhập tài khoản đó bị từ chối. Mở khóa và kiểm tra đăng nhập lại.
5. Kiểm tra bảng `OutboxMessages` có EventType `UserLocked` / `UserUnlocked` sau thao tác tương ứng.
6. Gửi quá 100 request trong 60 giây tới một endpoint API qua Gateway để xác nhận phản hồi HTTP 429. Có thể giảm ngưỡng trong `src/ApiGateway/appsettings.json` khi thử nghiệm, sau đó trả lại giá trị phù hợp.

## Lưu ý kiểm tra
Gói thay đổi được tạo từ source ZIP Nghĩa cung cấp. Môi trường đóng gói không có .NET SDK nên chưa thể chạy `dotnet build` tại đây. Cần mở `JobPlatform.sln` hoặc chạy `dotnet build JobPlatform.sln` trên máy có .NET 8 SDK trước khi commit/push.
