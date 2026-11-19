using System.ComponentModel.DataAnnotations;

namespace Shared.Contracts.Auth;

public class RegisterRequest
{
    [Required(ErrorMessage = "Vui lòng nhập email"), EmailAddress(ErrorMessage = "Email không hợp lệ"), MaxLength(256)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu"), MaxLength(100)]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập họ tên"), MaxLength(100)]
    public string FullName { get; set; } = "";

    /// <summary>Freelancer hoặc Employer (Admin không tự đăng ký).</summary>
    [Required(ErrorMessage = "Vui lòng chọn vai trò")]
    public string Role { get; set; } = "";
}

public class VerifyOtpRequest
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, StringLength(6, MinimumLength = 6, ErrorMessage = "OTP gồm 6 chữ số")] public string Otp { get; set; } = "";
}

public class ResendOtpRequest
{
    [Required, EmailAddress] public string Email { get; set; } = "";
}

public class LoginRequest
{
    [Required(ErrorMessage = "Vui lòng nhập email"), EmailAddress] public string Email { get; set; } = "";
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu")] public string Password { get; set; } = "";
}

public class AuthResponse
{
    public string Token { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "";
}
