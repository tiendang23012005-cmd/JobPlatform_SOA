using Microsoft.EntityFrameworkCore;
using Payment.Api.Data;
using Shared.Contracts.Payments;

namespace Payment.Api.Services;

public class WalletReconciliationService(IServiceScopeFactory scopes, IConfiguration config,
    ILogger<WalletReconciliationService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = Math.Max(1, config.GetValue("WalletReconciliation:IntervalSeconds", 300));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        do
        {
            try { await ReconcileAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Không hoàn tất đối soát ví."); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var users = await db.Wallets.Select(wallet => wallet.UserId).ToListAsync(ct);
        var mismatches = 0;

        foreach (var userId in users)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.LockWalletAsync(userId, ct);
            var item = await db.Wallets.Where(w => w.UserId == userId).Select(w => new
            {
                w.Id, w.Balance, w.LockedBalance,
                LedgerTotal = db.Transactions.Where(x => x.WalletId == w.Id && x.Type == WalletTransaction.TopUp &&
                    x.Status == TopUpStatus.Success).Sum(x => x.Amount),
                Duplicate = db.Transactions.Where(x => x.WalletId == w.Id)
                    .GroupBy(x => x.IdempotencyKey).Any(g => g.Count() > 1),
                PaymentMismatch = db.TopUpPayments.Any(p => p.WalletId == w.Id &&
                    p.Status == TopUpStatus.Success && !db.Transactions.Any(x => x.WalletId == w.Id &&
                        x.RefId == p.Id && x.Type == WalletTransaction.TopUp && x.Status == TopUpStatus.Success &&
                        x.Amount == p.Amount && x.IdempotencyKey == p.IdempotencyKey)),
                UnmatchedEntry = db.Transactions.Any(x => x.WalletId == w.Id &&
                    x.Type == WalletTransaction.TopUp && !db.TopUpPayments.Any(p => p.Id == x.RefId &&
                        p.WalletId == w.Id && p.Status == TopUpStatus.Success))
            }).SingleOrDefaultAsync(ct);
            await transaction.CommitAsync(ct);

            if (item is not null && (item.Balance != item.LedgerTotal || item.LockedBalance != 0 ||
                item.Duplicate || item.PaymentMismatch || item.UnmatchedEntry))
            {
                mismatches++;
                log.LogWarning("Đối soát ví {WalletId}: số dư {Balance}, số dư khóa {LockedBalance}, tổng sổ {LedgerTotal}, trùng {Duplicate}, nạp lệch {PaymentMismatch}, bút toán thiếu tham chiếu {UnmatchedEntry}.",
                    item.Id, item.Balance, item.LockedBalance, item.LedgerTotal, item.Duplicate, item.PaymentMismatch,
                    item.UnmatchedEntry);
            }
        }

        log.LogInformation("Đối soát hoàn tất: {WalletCount} ví, {MismatchCount} ví sai lệch.", users.Count, mismatches);
    }
}
