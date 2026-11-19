namespace Identity.Api.Data;

public static class UserStatus
{
    public const string PendingVerification = "PendingVerification";
    public const string Active = "Active";
    public const string Locked = "Locked";
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsVerified { get; set; }
    public string Status { get; set; } = UserStatus.PendingVerification;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Profile? Profile { get; set; }
}

public class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Skills { get; set; }
    public string? Bio { get; set; }
}

public class EmailOtp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public int FailedCount { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
