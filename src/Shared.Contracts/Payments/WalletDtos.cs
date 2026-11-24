using System.ComponentModel.DataAnnotations;

namespace Shared.Contracts.Payments;

public static class TopUpStatus
{
    public const string Pending = "Pending";
    public const string Success = "SUCCESS";
    public const string Failed = "FAILED";

}

public sealed class TopUpRequest : IValidatableObject
{
    public const decimal MinAmount = 50_000m;
    public const decimal MaxAmount = 1_000_000_000m;
    public const int MaxKeyLength = 100;
    public const string Currency = "VND";

    [Range(typeof(decimal), "50000", "1000000000", ErrorMessage = "Số tiền nạp phải từ 50.000 đến 1.000.000.000 VNĐ.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Thiếu mã yêu cầu nạp tiền."),
     StringLength(MaxKeyLength, ErrorMessage = "Mã yêu cầu nạp tiền tối đa 100 ký tự.")]
    public string IdempotencyKey { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount != decimal.Truncate(Amount))
            yield return new("Số tiền nạp phải là số nguyên VNĐ.", [nameof(Amount)]);
        if (IdempotencyKey != IdempotencyKey.Trim() || IdempotencyKey.Any(char.IsControl))
            yield return new("Mã yêu cầu nạp tiền chứa ký tự không hợp lệ.", [nameof(IdempotencyKey)]);
    }
}

public sealed class MockPaymentRequest
{
    [Required(ErrorMessage = "Vui lòng chọn kết quả thanh toán mô phỏng.")]
    public bool? Success { get; set; }
}

public record WalletItem(Guid Id, Guid UserId, decimal Balance, decimal LockedBalance, bool CanTopUp);

public record WalletTopUpItem(Guid Id, decimal Amount, string Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public record WalletTransactionItem(Guid Id, string Type, decimal Amount, Guid? RefId, string IdempotencyKey,
    string Status, DateTimeOffset CreatedAt);

public record WalletTransactionPage(IReadOnlyList<WalletTransactionItem> Items, int Page, int PageSize, int TotalCount);
