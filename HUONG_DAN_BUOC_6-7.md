# Hướng dẫn bước 6–7: Đăng ký, OTP, đăng nhập, JWT, phân quyền

Gói này là bản chép đè lên dự án bước 1–5. Mã chưa được biên dịch thử trong môi trường tạo gói; nếu `dotnet build` báo lỗi, gửi lại nguyên văn lỗi.

## Luồng hoạt động
1. `POST /api/v1/auth/register`: Identity lưu User (PendingVerification), OTP đã băm và bản ghi Outbox trong **một transaction**.
2. Tiến trình nền của Identity đọc Outbox và phát `UserRegistered` lên RabbitMQ (có publisher confirm).
3. Notification nhận sự kiện (queue bền vững, ack thủ công, retry 3 lần, lỗi quá hạn vào Dead-letter Queue), gửi email OTP (chưa cấu hình SMTP thì ghi vào log), ghi `Notifications` và `ProcessedEvents` (idempotent theo EventId).
4. `POST /auth/verify` xác minh OTP (6 số, hiệu lực 5 phút, sai tối đa 5 lần) rồi tài khoản chuyển Active.
5. `POST /auth/login` trả JWT (60 phút). Sai mật khẩu 5 lần khóa 15 phút. Admin khóa/mở tài khoản qua `PUT /users/{id}/lock|unlock`.
6. Phân quyền bằng Authorization Policy: `AdminOnly`, `EmployerOnly`, `FreelancerOnly`, `VerifiedUser`.

Admin mặc định (tạo khi Identity khởi động): `admin@jobplatform.local` / `Admin@12345` (đổi trong `Seed` ở `appsettings.json`).

## Bước A. Chép gói vào dự án
Giải nén `JobPlatform_Buoc6-7.zip` bằng File Explorer, mở thư mục `JobPlatform` bên trong, chọn toàn bộ nội dung và dán đè vào `D:\C_vscode_tim\JobPlatform` (chọn "Replace the files"). Các tệp bị ghi đè: `docker-compose.yml`, `Identity.Api.csproj`, `Notification.Api.csproj`, `Program.cs`, `appsettings.json`, `Data/*` của hai service. Thư mục mới: `src/Shared.Infrastructure`, `src/Shared.Contracts/Auth`, và các thư mục `Controllers`, `Services`, `Consumers`.

## Bước B. Thêm project mới và build
```powershell
cd D:\C_vscode_tim\JobPlatform
dotnet sln add src/Shared.Infrastructure/Shared.Infrastructure.csproj
dotnet build
```
Kết quả đúng: `Build succeeded`, 9 project.

## Bước C. Tạo migration cho Identity và Notification
Cài công cụ EF một lần (bỏ qua nếu đã có):
```powershell
dotnet tool install --global dotnet-ef --version 8.*
```
Tạo migration (tắt tự động migrate để lệnh chạy nhanh):
```powershell
$env:Database__AutoMigrate = "false"
dotnet ef migrations add InitialCreate -p src/Services/Identity.Api -s src/Services/Identity.Api --output-dir Migrations
dotnet ef migrations add InitialCreate -p src/Services/Notification.Api -s src/Services/Notification.Api --output-dir Migrations
Remove-Item Env:\Database__AutoMigrate
```
Hai thư mục `Migrations` mới xuất hiện trong 2 service. Các service còn lại sẽ làm tương tự khi có bảng.

## Bước D. (Tùy chọn) Gửi email thật bằng SMTP
Mặc định OTP được ghi vào log của Notification, đủ để làm đồ án. Muốn gửi email thật (ví dụ Gmail với mật khẩu ứng dụng), thêm vào `.env`:
```
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_USER=ten@gmail.com
SMTP_PASSWORD=mat-khau-ung-dung
SMTP_FROM=ten@gmail.com
```

## Bước E. Chạy
```powershell
docker compose up -d --build
docker compose ps
```
Chờ khoảng 30 giây cho các service tạo bảng.

## Bước F. Kiểm thử bằng PowerShell
```powershell
$g = "http://localhost:5000/api/v1"
$json = "application/json"

# 1. Đăng ký (dùng tên không dấu để tránh lỗi mã hóa trong PowerShell 5.1)
Invoke-RestMethod "$g/auth/register" -Method Post -ContentType $json -Body (@{
  email="freelancer1@test.com"; password="Passw0rd!"; fullName="Nguyen Van A"; role="Freelancer" } | ConvertTo-Json)

# 2. Lấy OTP trong log Notification (tìm dòng [EMAIL-DEV])
docker compose logs notification-api --tail 15

# 3. Xác minh (thay 123456 bằng OTP thật)
Invoke-RestMethod "$g/auth/verify" -Method Post -ContentType $json -Body (@{
  email="freelancer1@test.com"; otp="123456" } | ConvertTo-Json)

# 4. Đăng nhập, lấy token
$r = Invoke-RestMethod "$g/auth/login" -Method Post -ContentType $json -Body (@{
  email="freelancer1@test.com"; password="Passw0rd!" } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($r.token)" }

# 5. Thông tin cá nhân (200) và thử vào API Admin bằng Freelancer (phải 403)
Invoke-RestMethod "$g/users/me" -Headers $h
try { Invoke-RestMethod "$g/users/admin-ping" -Headers $h } catch { $_.Exception.Response.StatusCode }

# 6. Đăng nhập Admin rồi vào API Admin (200)
$a = Invoke-RestMethod "$g/auth/login" -Method Post -ContentType $json -Body (@{
  email="admin@jobplatform.local"; password="Admin@12345" } | ConvertTo-Json)
Invoke-RestMethod "$g/users/admin-ping" -Headers @{ Authorization = "Bearer $($a.token)" }

# 7. Không có token vào /users/me (phải 401)
try { Invoke-RestMethod "$g/users/me" } catch { $_.Exception.Response.StatusCode }

# 8. Thông báo của người dùng (đã gửi OTP)
Invoke-RestMethod "$g/notifications" -Headers $h
```
Cũng có thể thử trên Swagger: http://localhost:5001/swagger (nút **Authorize**, dán token, không cần chữ Bearer).

Các trường hợp nên thử thêm: đăng ký trùng email (409), mật khẩu yếu (400), OTP sai 5 lần (bị vô hiệu), đăng nhập sai 5 lần (423), đăng nhập khi chưa xác minh (403).

Kiểm tra Outbox đã phát: trong SQL, `SELECT * FROM IdentityDb.dbo.OutboxMessages` có cột `PublishedAt` khác NULL. RabbitMQ http://localhost:15672 (guest/guest) → tab Queues thấy `notification.user-registered` và `notification.user-registered.dlq`.

## Lỗi thường gặp
| Hiện tượng | Cách xử lý |
|---|---|
| `Invalid object name 'Users'` | Chưa tạo migration (Bước C) hoặc chưa build lại: chạy Bước C rồi `docker compose up -d --build` |
| Register trả 500 | Xem `docker compose logs identity-api --tail 30` |
| Không thấy OTP trong log | Xem `docker compose logs notification-api --tail 30`; kiểm tra queue `notification.user-registered` có tin chưa ack; Outbox có `PublishedAt` chưa |
| OTP vào Dead-letter Queue | Lỗi SMTP (sai mật khẩu ứng dụng); để trống `SMTP_HOST` để dùng chế độ ghi log |
| 401 dù có token | Token hết hạn (60 phút) hoặc `Jwt__Key` khác nhau giữa các service |
| `dotnet ef` báo không tìm thấy lệnh | Mở PowerShell mới sau khi cài công cụ |

## Mốc hoàn thành bước 6–7
- [ ] Đăng ký → nhận OTP (log hoặc email) → xác minh → đăng nhập nhận JWT
- [ ] Freelancer vào `admin-ping` bị 403, Admin vào được 200, không token bị 401
- [ ] Sai mật khẩu 5 lần bị khóa 15 phút; OTP sai 5 lần bị vô hiệu
- [ ] Outbox có `PublishedAt`; RabbitMQ có queue và DLQ
