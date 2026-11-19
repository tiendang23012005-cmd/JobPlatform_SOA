# Hướng dẫn xây dựng nền tảng JobPlatform (bước 1–5)

Gói này chứa toàn bộ mã cho bước 1–5. Bạn chỉ cần tạo Solution và project Blazor bằng lệnh `dotnet`, phần còn lại đã có sẵn trong thư mục.
Lưu ý: mã chưa được biên dịch thử trong môi trường tạo gói (không có .NET SDK). Nếu `dotnet build` báo lỗi, gửi lại nguyên văn lỗi để sửa.

## Bước 0. Chuẩn bị
1. Cài .NET 8 SDK (kiểm tra: `dotnet --version` ra 8.x trở lên).
2. Cài Docker Desktop và bật (kiểm tra: `docker --version`).
3. Giải nén gói này, bạn có thư mục `JobPlatform` (đã chứa sẵn `src`, `Dockerfile`, `docker-compose.yml`, `.env`).

## Bước 1. Tạo Solution và cấu trúc dự án
Mở PowerShell trong thư mục `JobPlatform`:

```powershell
dotnet new sln -n JobPlatform
dotnet new blazorwasm -n Client.Blazor -o src/Client.Blazor -f net8.0
```
Lệnh thứ hai tạo project Blazor với đầy đủ tệp mẫu. Các tệp trong gói (`Program.cs`, `Pages/Home.razor`, `wwwroot/appsettings.json`, `Properties/launchSettings.json`) đã được đặt sẵn trong `src/Client.Blazor`, nên nếu hệ thống hỏi ghi đè thì chọn ghi đè bằng tệp của gói. Cách đơn giản nhất: chạy lệnh tạo Blazor trước, sau đó chép đè thư mục `src/Client.Blazor` của gói lên.

Nếu có tệp `Pages/Index.razor` trong project Blazor (SDK cũ), xóa nó để không trùng route `/` với `Home.razor`.

Thêm tham chiếu và đưa mọi project vào Solution:

```powershell
dotnet add src/Client.Blazor reference src/Shared.Contracts
Get-ChildItem src -Recurse -Filter *.csproj | ForEach-Object { dotnet sln add $_.FullName }
dotnet build
```
Kết quả: `dotnet build` thành công với 8 project (Shared.Contracts, 5 service, ApiGateway, Client.Blazor).

## Bước 2. Năm Backend API
Đã có sẵn trong `src/Services`: `Identity.Api`, `Job.Api`, `Contract.Api`, `Payment.Api`, `Notification.Api`. Mỗi service gồm:
- `*.csproj`: EF Core SQL Server, health check, Swagger, tham chiếu `Shared.Contracts`.
- `Data/<Tên>DbContext.cs`: DbContext riêng (chưa có bảng).
- `Controllers/PingController.cs`: endpoint kiểm tra `GET /api/v1/<nhóm>/ping`.
- `Program.cs`: đăng ký DbContext, `/health`, Swagger; tự tạo database khi khởi động (thử lại 10 lần chờ SQL Server).
- `appsettings.json`: connection string tới `localhost,1433` và database riêng (`IdentityDb`, `JobDb`, ...).
- `Properties/launchSettings.json`: cổng 5001–5005.

| Service | Cổng | Database | Endpoint kiểm tra |
|---|---|---|---|
| Identity | 5001 | IdentityDb | /api/v1/auth/ping |
| Job | 5002 | JobDb | /api/v1/jobs/ping |
| Contract | 5003 | ContractDb | /api/v1/contracts/ping |
| Payment | 5004 | PaymentDb | /api/v1/wallet/ping |
| Notification | 5005 | NotificationDb | /api/v1/notifications/ping |

## Bước 3. Blazor Client và API Gateway (YARP)
- `src/ApiGateway`: `Program.cs` nạp YARP từ cấu hình, bật CORS cho `http://localhost:5100`. `appsettings.json` định nghĩa route theo tiền tố `/api/v1/...` tới từng cluster, có health check chủ động (`/health`), cân bằng tải RoundRobin; địa chỉ destination dùng tên container Docker. `appsettings.Development.json` ghi đè sang `localhost:500x` khi chạy bằng `dotnet run`.
- `src/Client.Blazor`: `HttpClient` trỏ tới Gateway (`GatewayUrl` trong `wwwroot/appsettings.json`), trang `Home.razor` có nút kiểm tra gọi 5 service qua Gateway.

## Bước 4. Năm database SQL Server riêng
Dùng một container SQL Server, mỗi service tự tạo database riêng khi khởi động và chỉ biết connection string của mình. Mật khẩu `sa` đặt trong `.env` (`SA_PASSWORD`). Nếu bạn đổi mật khẩu, nhớ đổi cả trong các `appsettings.json` (chạy local); mật khẩu phải đủ phức tạp (chữ hoa, thường, số, tối thiểu 8 ký tự).

## Bước 5. RabbitMQ, Docker Compose và Shared.Contracts
- `Shared.Contracts`: `IntegrationEvent` (EventId, EventType, SchemaVersion, CorrelationId, OccurredAt), `UserRegisteredEvent`, `Roles` (Freelancer, Employer, Admin). Không chứa logic nghiệp vụ.
- `docker-compose.yml`: SQL Server (có healthcheck), RabbitMQ (có healthcheck, giao diện quản trị cổng 15672), 5 service và Gateway; `restart: unless-stopped`; service chờ SQL Server và RabbitMQ sẵn sàng.
- `Dockerfile` dùng chung: build từng project bằng tham số `PROJECT` và `DLL`.

## Chạy và kiểm tra

### Cách A. Chạy toàn bộ bằng Docker (khuyến nghị để kiểm tra hạ tầng)
```powershell
docker compose up -d --build
docker compose ps
```
Lần đầu build mất vài phút. Kiểm tra:
```powershell
curl http://localhost:5000/api/v1/auth/ping         # identity ok
curl http://localhost:5000/api/v1/jobs/ping         # job ok
curl http://localhost:5001/health                   # Healthy
```
Swagger: http://localhost:5001/swagger (…5005). RabbitMQ: http://localhost:15672 (guest/guest).
Kết nối SQL Server bằng SSMS/Azure Data Studio: Server `localhost,1433`, User `sa`, mật khẩu trong `.env`, sẽ thấy 5 database.
Chạy Client (ngoài Docker): `dotnet run --project src/Client.Blazor`, mở http://localhost:5100, bấm "Kiểm tra 5 service", cả 5 dòng phải hiện `... ok`.

### Cách B. Chạy từng project để lập trình/gỡ lỗi
```powershell
docker compose up -d sqlserver rabbitmq
```
Sau đó chạy lần lượt (mỗi lệnh một cửa sổ terminal, hoặc đặt nhiều Startup Project trong Visual Studio):
```powershell
dotnet run --project src/Services/Identity.Api
dotnet run --project src/Services/Job.Api
dotnet run --project src/Services/Contract.Api
dotnet run --project src/Services/Payment.Api
dotnet run --project src/Services/Notification.Api
dotnet run --project src/ApiGateway
dotnet run --project src/Client.Blazor
```
Nếu đang chạy cách A thì chạy `docker compose down` trước để tránh trùng cổng.

## Lỗi thường gặp
| Hiện tượng | Cách xử lý |
|---|---|
| Service log "Chưa kết nối được CSDL" liên tục | SQL Server chưa sẵn sàng hoặc sai mật khẩu; kiểm tra `docker compose logs sqlserver`, mật khẩu trong `.env` và `appsettings.json` |
| SQL Server container tự thoát | Mật khẩu `sa` chưa đủ phức tạp, hoặc máy thiếu RAM (cần tối thiểu 2 GB cho Docker) |
| Trang Blazor báo lỗi CORS | Cổng client phải là 5100, hoặc thêm origin vào `Cors:Origins` trong `ApiGateway/appsettings.json` |
| Gateway trả 503/502 | Service đích chưa chạy hoặc `/health` lỗi; kiểm tra cluster tương ứng |
| Cổng đã bị chiếm (5000–5005, 1433, 5672) | Tắt chương trình đang dùng cổng đó hoặc đổi cổng trong `launchSettings.json` / compose |
| `dotnet build` lỗi trùng `Index.razor` và `Home.razor` | Xóa `Pages/Index.razor` |

## Mốc hoàn thành bước 1–5
- [ ] `dotnet build` toàn Solution không lỗi
- [ ] 5 service và Gateway chạy, `/health` trả Healthy
- [ ] Trang Blazor gọi được cả 5 service qua Gateway
- [ ] SQL Server có 5 database riêng
- [ ] RabbitMQ Management mở được
