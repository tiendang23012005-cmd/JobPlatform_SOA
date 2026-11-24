namespace Client.Blazor.Services;

public record NotificationItem(Guid Id, string EventType, string Content, bool IsRead, DateTime CreatedAt);

public record MenuItem(string Icon, string Text, string Href, bool Exact = false);
public record MenuSection(string Title, MenuItem[] Items);

public static class Ui
{
    private static readonly System.Globalization.NumberFormatInfo VndFormat = new() { NumberGroupSeparator = "." };

    public static string Vnd(decimal amount) => amount.ToString("N0", VndFormat) + " ₫";

    public static string TopUpStatusLabel(string status) => status switch
    {
        Shared.Contracts.Payments.TopUpStatus.Pending => "Đang chờ thanh toán",
        Shared.Contracts.Payments.TopUpStatus.Success => "Thành công",
        Shared.Contracts.Payments.TopUpStatus.Failed => "Thất bại",
        _ => "Không xác định"
    };

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var p = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length == 1 ? p[0][..1].ToUpper() : (p[0][..1] + p[^1][..1]).ToUpper();
    }

    public static string RoleLabel(string? role) => role switch
    {
        "Freelancer" => "Freelancer",
        "Employer" => "Nhà tuyển dụng",
        "Admin" => "Quản trị viên",
        _ => role ?? ""
    };

    public static string TimeAgo(DateTime utc)
    {
        var d = DateTime.UtcNow - utc.ToUniversalTime();
        if (d.TotalMinutes < 1) return "Vừa xong";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} phút trước";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} giờ trước";
        return utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }
}

/// <summary>Menu sidebar theo vai trò. Các mục dạng /soon/... là tính năng sẽ làm ở các bước sau.</summary>
public static class MenuCatalog
{
    private static readonly MenuSection Overview = new("Tổng quan", new[] { new MenuItem("bi-grid-1x2-fill", "Bảng điều khiển", "", true) });
    private static readonly MenuSection Account = new("Tài khoản", new[]
    {
        new MenuItem("bi-bell", "Thông báo", "notifications"),
        new MenuItem("bi-person-circle", "Hồ sơ", "profile"),
    });

    public static IEnumerable<MenuSection> For(string role) => role switch
    {
        "Freelancer" => new[]
        {
            Overview,
            new MenuSection("Công việc", new[]
            {
                new MenuItem("bi-search", "Tìm việc", "tim-viec"),
                new MenuItem("bi-send", "Đơn ứng tuyển", "don-ung-tuyen"),
                new MenuItem("bi-file-earmark-text", "Hợp đồng", "soon/hop-dong"),
            }),
            new MenuSection("Tài chính", new[] { new MenuItem("bi-wallet2", "Ví điện tử", "soon/vi") }),
            Account
        },
        "Employer" => new[]
        {
            Overview,
            new MenuSection("Tuyển dụng", new[]
            {
                new MenuItem("bi-plus-square", "Đăng tin tuyển dụng", "dang-tin"),
                new MenuItem("bi-briefcase", "Tin của tôi", "tin-cua-toi"),
                new MenuItem("bi-people", "Ứng viên", "ung-vien"),
                new MenuItem("bi-file-earmark-text", "Hợp đồng", "soon/hop-dong"),
            }),
            new MenuSection("Tài chính", new[] { new MenuItem("bi-wallet2", "Ví điện tử", "soon/vi") }),
            Account
        },
        "Admin" => new[]
        {
            Overview,
            new MenuSection("Quản trị", new[]
            {
                new MenuItem("bi-speedometer2", "Hệ thống", "admin"),
                new MenuItem("bi-patch-check", "Duyệt tin tuyển dụng", "duyet-tin"),
                new MenuItem("bi-people", "Quản lý người dùng", "soon/nguoi-dung"),
                new MenuItem("bi-shield-exclamation", "Tranh chấp", "soon/tranh-chap"),
                new MenuItem("bi-receipt", "Giao dịch", "soon/giao-dich"),
                new MenuItem("bi-wallet2", "Ví điện tử", "soon/vi"),
            }),
            Account
        },
        _ => new[] { Overview, Account }
    };
}
