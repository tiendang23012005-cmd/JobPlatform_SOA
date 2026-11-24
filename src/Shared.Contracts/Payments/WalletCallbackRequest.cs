using System.ComponentModel.DataAnnotations;
namespace Shared.Contracts.Payments;

public sealed class WalletCallbackRequest : IValidatableObject
{
    public Guid TopUpId { get; set; }
    public Guid ProviderTransactionId { get; set; }

    [Range(typeof(decimal), "50000", "1000000000", ErrorMessage = "Số tiền callback không hợp lệ.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Thiếu loại tiền trong callback."), StringLength(3)]
    public string Currency { get; set; } = "";

    [Required(ErrorMessage = "Thiếu mã yêu cầu trong callback."), StringLength(TopUpRequest.MaxKeyLength)]
    public string IdempotencyKey { get; set; } = "";

    [Required(ErrorMessage = "Thiếu trạng thái callback."), StringLength(20)]
    public string Status { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TopUpId == Guid.Empty || ProviderTransactionId == Guid.Empty)
            yield return new("Mã giao dịch callback không hợp lệ.");
        if (Currency != TopUpRequest.Currency || Amount != decimal.Truncate(Amount))
            yield return new("Callback chỉ chấp nhận số nguyên VNĐ.");
        if (Status is not (TopUpStatus.Success or TopUpStatus.Failed))
            yield return new("Trạng thái callback phải là SUCCESS hoặc FAILED.");
    }
}
