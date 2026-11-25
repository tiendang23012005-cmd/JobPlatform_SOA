using System.ComponentModel.DataAnnotations;

namespace Identity.Api.Dtos;

public sealed class UpdateProfileRequest
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [StringLength(100, ErrorMessage = "Họ tên không được vượt quá 100 ký tự.")]
    public string FullName { get; set; } = "";

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    [StringLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự.")]
    public string? Phone { get; set; }

    [StringLength(2000, ErrorMessage = "Kỹ năng không được vượt quá 2000 ký tự.")]
    public string? Skills { get; set; }

    [StringLength(2000, ErrorMessage = "Giới thiệu không được vượt quá 2000 ký tự.")]
    public string? Bio { get; set; }
}

public sealed class ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 8)] public string NewPassword { get; set; } = "";
}

public sealed record UserListItem(
    Guid Id, string Email, string Role, string Status, bool IsVerified,
    string? FullName, string? Phone, DateTime CreatedAt);

public sealed record PagedUsersResponse(
    IReadOnlyList<UserListItem> Items, int Page, int PageSize, int TotalItems, int TotalPages);
