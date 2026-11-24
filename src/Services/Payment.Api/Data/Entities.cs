using Shared.Contracts.Payments;

namespace Payment.Api.Data;

public class Wallet
{
    public const decimal MaxBalance = 999_999_999_999_999_999m;
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public decimal Balance { get; set; }
    public decimal LockedBalance { get; set; }
}

public class TopUpPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WalletId { get; set; }
    public Wallet Wallet { get; set; } = null!;
    public Guid ProviderTransactionId { get; set; } = Guid.NewGuid();
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string Status { get; set; } = TopUpStatus.Pending;
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public class WalletTransaction
{
    public const string TopUp = "TopUp";
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WalletId { get; set; }
    public Wallet Wallet { get; set; } = null!;
    public string Type { get; set; } = TopUp;
    public decimal Amount { get; set; }
    public Guid? RefId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string Status { get; set; } = TopUpStatus.Success;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
