using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Data;
using Shared.Contracts.Events;
using Shared.Contracts.Payments;
using Shared.Infrastructure.Outbox;

namespace Payment.Api.Services;

public class WalletService(PaymentDbContext db)
{
    public record CallbackResult(WalletTopUpItem? Payment, bool BalanceLimit = false);

    private static readonly Expression<Func<TopUpPayment, WalletTopUpItem>> PaymentItem = x =>
        new WalletTopUpItem(x.Id, x.Amount, x.Status, x.CreatedAt, x.CompletedAt);

    public async Task<WalletItem> GetAsync(Guid userId, bool canTopUp, CancellationToken ct)
    {
        var wallet = await db.Wallets.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (wallet is null)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            wallet = await GetOrCreateLockedAsync(userId, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        return new WalletItem(wallet.Id, userId, wallet.Balance, wallet.LockedBalance, canTopUp);
    }

    public async Task<WalletTopUpItem?> StartTopUpAsync(Guid userId, TopUpRequest request,
        string correlationId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var wallet = await GetOrCreateLockedAsync(userId, ct);
        var existing = await db.TopUpPayments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.WalletId == wallet.Id && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
            return existing.Amount == request.Amount ? ToItem(existing) : null;

        var payment = new TopUpPayment
        {
            WalletId = wallet.Id,
            Amount = request.Amount,
            IdempotencyKey = request.IdempotencyKey,
            CorrelationId = correlationId
        };
        db.TopUpPayments.Add(payment);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToItem(payment);
    }

    public Task<WalletTopUpItem?> GetTopUpAsync(Guid userId, Guid id, CancellationToken ct) =>
        db.TopUpPayments.AsNoTracking().Where(x => x.Id == id && x.Wallet.UserId == userId)
            .Select(PaymentItem).SingleOrDefaultAsync(ct);

    public Task<WalletCallbackRequest?> GetMockCallbackAsync(Guid userId, Guid id, bool success, CancellationToken ct) =>
        db.TopUpPayments.AsNoTracking().Where(x => x.Id == id && x.Wallet.UserId == userId)
            .Select(x => new WalletCallbackRequest
            {
                TopUpId = x.Id,
                ProviderTransactionId = x.ProviderTransactionId,
                Amount = x.Amount,
                Currency = TopUpRequest.Currency,
                IdempotencyKey = x.IdempotencyKey,
                Status = success ? TopUpStatus.Success : TopUpStatus.Failed
            }).SingleOrDefaultAsync(ct);

    public async Task<CallbackResult> SettleAsync(WalletCallbackRequest callback, CancellationToken ct)
    {
        var userId = await db.TopUpPayments.Where(x => x.Id == callback.TopUpId)
            .Select(x => (Guid?)x.Wallet.UserId).SingleOrDefaultAsync(ct);
        if (userId is null) return new(null);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.LockWalletAsync(userId.Value, ct);
        var payment = await db.TopUpPayments.Include(x => x.Wallet)
            .SingleAsync(x => x.Id == callback.TopUpId, ct);

        if (payment.ProviderTransactionId != callback.ProviderTransactionId ||
            payment.Amount != callback.Amount || payment.IdempotencyKey != callback.IdempotencyKey)
            return new(null);

        // Chỉ kết quả hợp lệ đến đầu tiên được chuyển trạng thái.
        if (payment.Status != TopUpStatus.Pending)
            return new(ToItem(payment));

        var now = DateTimeOffset.UtcNow;
        if (callback.Status == TopUpStatus.Success)
        {
            if (payment.Wallet.Balance > Wallet.MaxBalance - payment.Amount)
                return new(null, BalanceLimit: true);

            payment.Wallet.Balance += payment.Amount;
            var entry = new WalletTransaction
            {
                WalletId = payment.WalletId,
                Amount = payment.Amount,
                RefId = payment.Id,
                IdempotencyKey = payment.IdempotencyKey,
                CreatedAt = now
            };
            db.Transactions.Add(entry);
            db.AddOutbox(new WalletTopUpCompletedEvent(payment.WalletId, userId.Value, entry.Id, payment.Id, entry.Amount)
            {
                CorrelationId = payment.CorrelationId
            });
        }

        payment.Status = callback.Status;
        payment.CompletedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(ToItem(payment));
    }

    public async Task<WalletTransactionPage> GetTransactionsAsync(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Transactions.AsNoTracking().Where(x => x.Wallet.UserId == userId);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new WalletTransactionItem(x.Id, x.Type, x.Amount, x.RefId, x.IdempotencyKey, x.Status, x.CreatedAt))
            .ToListAsync(ct);
        return new(items, page, pageSize, count);
    }

    private async Task<Wallet> GetOrCreateLockedAsync(Guid userId, CancellationToken ct)
    {
        await db.LockWalletAsync(userId, ct);
        var wallet = await db.Wallets.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (wallet is not null) return wallet;

        wallet = new Wallet { UserId = userId };
        db.Wallets.Add(wallet);
        return wallet;
    }

    private static WalletTopUpItem ToItem(TopUpPayment payment) =>
        new(payment.Id, payment.Amount, payment.Status, payment.CreatedAt, payment.CompletedAt);
}
