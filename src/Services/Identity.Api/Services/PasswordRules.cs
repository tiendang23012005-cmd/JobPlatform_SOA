namespace Identity.Api.Services;

public static class PasswordRules
{
    /// <summary>BR-01: tối thiểu 8 ký tự, có chữ, số và ký tự đặc biệt.</summary>
    public static bool IsValid(string p) =>
        p.Length >= 8 && p.Any(char.IsLetter) && p.Any(char.IsDigit) && p.Any(c => !char.IsLetterOrDigit(c));

    public const string Message = "Mật khẩu tối thiểu 8 ký tự, gồm chữ, số và ký tự đặc biệt.";
}
