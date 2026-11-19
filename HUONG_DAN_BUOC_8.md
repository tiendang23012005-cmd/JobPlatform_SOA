# Hướng dẫn bước 8: Giao diện đăng ký, đăng nhập trên Blazor

Gói chép đè lên `src/Client.Blazor`. Mã chưa được biên dịch thử trong môi trường tạo gói; nếu `dotnet build` lỗi, gửi lại nguyên văn lỗi.

## Luồng giao diện
`/register` (đăng ký) → `/verify` (nhập OTP) → `/login` (đăng nhập) → `/profile`. Trang `/admin` chỉ Admin vào được. Chưa đăng nhập mà vào trang cần quyền thì tự chuyển tới `/login`; đã đăng nhập nhưng sai vai trò thì báo 403. JWT lưu trong localStorage, tự gắn vào mọi lời gọi API qua Gateway, và tự hết hiệu lực khi quá hạn (60 phút).

## Bước A. Kiểm tra thư mục layout
```powershell
cd D:\C_vscode_tim\JobPlatform
Get-ChildItem src\Client.Blazor\Layout
```
Phải thấy `MainLayout.razor`. Nếu thư mục tên là `Shared` (SDK cũ), chép `MainLayout.razor` và `RedirectToLogin.razor` vào `Shared` thay vì `Layout`, rồi đổi dòng `@using Client.Blazor.Layout` trong `_Imports.razor` thành `@using Client.Blazor.Shared`.

## Bước B. Chép gói và thêm package
Giải nén zip bằng File Explorer, chép nội dung thư mục `JobPlatform` đè vào `D:\C_vscode_tim\JobPlatform` (Replace). Rồi:
```powershell
dotnet add src/Client.Blazor package Microsoft.AspNetCore.Components.Authorization --version 8.0.*
dotnet build
```
Kết quả đúng: `Build succeeded`.

## Bước C. Chạy
Đảm bảo backend đang chạy (`docker compose ps`), sau đó:
```powershell
dotnet run --project src/Client.Blazor
```
Mở http://localhost:5100.

## Bước D. Kiểm thử trên giao diện
1. **Đăng ký:** bấm Đăng ký, nhập họ tên, email mới (ví dụ `employer1@test.com`), mật khẩu `Passw0rd!`, chọn Employer. Thử trước vài lỗi: mật khẩu yếu, nhập lại không khớp, email trùng.
2. **Lấy OTP:** `docker compose logs notification-api --no-log-prefix | Select-String "EMAIL-DEV" | Select-Object -Last 1`
3. **Xác minh:** nhập 6 số OTP ở trang `/verify`. Thành công thì chuyển tới đăng nhập kèm thông báo xanh.
4. **Đăng nhập:** thanh trên cùng hiện tên và vai trò, trang chủ chào người dùng, `/profile` hiển thị thông tin.
5. **Phân quyền:** gõ http://localhost:5100/admin khi đang là Employer → báo 403, không có mục "Quản trị" trên thanh menu.
6. **Admin:** Đăng xuất, đăng nhập `admin@jobplatform.local` / `Admin@12345` → thấy mục "Quản trị", vào `/admin` hiện `admin ok`.
7. **Chưa đăng nhập:** đăng xuất rồi vào `/profile` → tự chuyển tới `/login`, đăng nhập xong quay lại `/profile`.
8. **Lỗi đăng nhập:** nhập sai mật khẩu 5 lần → thông báo khóa 15 phút; tài khoản chưa xác minh → thông báo chưa xác minh email.

## Lỗi thường gặp
| Hiện tượng | Cách xử lý |
|---|---|
| Trình duyệt báo lỗi CORS | Client phải chạy ở cổng 5100 (đúng với `Cors:Origins` của Gateway); Gateway đang chạy |
| "Không kết nối được máy chủ" | Docker chưa chạy hoặc `GatewayUrl` trong `wwwroot/appsettings.json` sai |
| Lỗi biên dịch về `AuthorizeView`, `CascadingAuthenticationState` | Chưa thêm package ở Bước B |
| Trùng `MainLayout` khi build | Layout nằm ở thư mục `Shared` nhưng đã chép thêm vào `Layout`; xóa bản trùng |
| Đăng nhập xong vẫn thấy "Đăng nhập" trên menu | Xóa localStorage của trang (F12 → Application) rồi thử lại |
| Tiếng Việt hiển thị sai | Lưu các tệp `.razor` ở dạng UTF-8 |

## Mốc hoàn thành bước 8 (bàn giao nền tảng)
- [ ] Đăng ký, xác minh OTP, đăng nhập thực hiện được hoàn toàn trên giao diện
- [ ] Menu thay đổi theo trạng thái đăng nhập và vai trò
- [ ] Trang cần quyền chuyển hướng đăng nhập / báo 403 đúng
- [ ] Admin vào được `/admin`, Employer và Freelancer thì không
