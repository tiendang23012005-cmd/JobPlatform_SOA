using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Outbox;
namespace Contract.Api.Data;

public class ContractDbContext(DbContextOptions<ContractDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<AcceptedApplication> AcceptedApplications => Set<AcceptedApplication>();
    public DbSet<ContractEntity> Contracts => Set<ContractEntity>();
    public DbSet<ContractVersion> ContractVersions => Set<ContractVersion>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<ProgressUpdate> ProgressUpdates => Set<ProgressUpdate>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Khóa Guid do code tự sinh: bắt buộc ValueGeneratedNever để EF coi thực thể mới thêm vào danh sách là Added (không phải Modified)
        b.Entity<ContractEntity>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<ContractVersion>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Milestone>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<ProgressUpdate>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Submission>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<AcceptedApplication>().HasKey(x => x.ApplicationId);
        b.Entity<ContractEntity>(e =>
        {
            e.ToTable("Contracts");
            e.HasIndex(x => x.ApplicationId).IsUnique();               // BR-05
            e.HasIndex(x => new { x.Status, x.ExpiresAt });
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.DepositAmount).HasPrecision(18, 2);
            e.Property(x => x.FeeRate).HasPrecision(5, 4);
            e.Property(x => x.DepositRate).HasPrecision(5, 4);
            e.HasMany(x => x.Milestones).WithOne(m => m.Contract).HasForeignKey(m => m.ContractId);
            e.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.ContractId);
        });
        b.Entity<ContractVersion>().HasIndex(x => new { x.ContractId, x.Version }).IsUnique();
        b.Entity<Milestone>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasMany(x => x.Updates).WithOne().HasForeignKey(u => u.MilestoneId);
            e.HasMany(x => x.Submissions).WithOne().HasForeignKey(s => s.MilestoneId);
        });
        b.Entity<OutboxMessage>().HasKey(x => x.Id);
    }
}