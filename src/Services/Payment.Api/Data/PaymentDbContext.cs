using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Payments;
using Shared.Infrastructure.Outbox;

namespace Payment.Api.Data;

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<TopUpPayment> TopUpPayments => Set<TopUpPayment>();
    public DbSet<WalletTransaction> Transactions => Set<WalletTransaction>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // Khóa theo UserId trong transaction hiện tại, kể cả khi ví chưa tồn tại.
    internal Task LockWalletAsync(Guid userId, CancellationToken ct) =>
        Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = {$"Payment.Wallet:{userId:D}"},
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51001, N'Không lấy được khóa xử lý ví.', 1;
            """, ct);

    protected override void OnModelCreating(ModelBuilder b)
    {
        const string binaryCollation = "Latin1_General_100_BIN2";

        b.Entity<Wallet>(e =>
        {
            e.ToTable(t => t.HasCheckConstraint("CK_Wallets_Balance", "[Balance] >= 0 AND [LockedBalance] >= 0"));
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Balance).HasPrecision(18, 0);
            e.Property(x => x.LockedBalance).HasPrecision(18, 0);
        });

        b.Entity<TopUpPayment>(e =>
        {
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_TopUpPayments_Amount", "[Amount] BETWEEN 50000 AND 1000000000");
                t.HasCheckConstraint("CK_TopUpPayments_Status", """
                    ([Status] = N'Pending' AND [CompletedAt] IS NULL) OR
                    ([Status] IN (N'SUCCESS', N'FAILED') AND [CompletedAt] IS NOT NULL)
                    """);
            });
            e.HasOne(x => x.Wallet).WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ProviderTransactionId).IsUnique();
            e.HasIndex(x => new { x.WalletId, x.IdempotencyKey }).IsUnique();
            e.Property(x => x.Amount).HasPrecision(18, 0);
            e.Property(x => x.IdempotencyKey).HasMaxLength(TopUpRequest.MaxKeyLength).UseCollation(binaryCollation);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.CorrelationId).HasMaxLength(100);
        });

        b.Entity<WalletTransaction>(e =>
        {
            e.ToTable(t =>
            {
                t.HasTrigger("TR_Transactions_AppendOnly");
                t.HasCheckConstraint("CK_Transactions_Amount", "[Amount] > 0");
            });
            e.HasOne(x => x.Wallet).WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.WalletId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.WalletId, x.CreatedAt, x.Id });
            e.Property(x => x.Amount).HasPrecision(18, 0);
            e.Property(x => x.IdempotencyKey).HasMaxLength(TopUpRequest.MaxKeyLength).UseCollation(binaryCollation);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Type).HasMaxLength(30);
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.Property(x => x.EventType).HasMaxLength(100);
            e.HasIndex(x => x.PublishedAt);
        });
    }
}
