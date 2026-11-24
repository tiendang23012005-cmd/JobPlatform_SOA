using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Outbox;
using JobEntity = Job.Api.Entities.Job;
using ApplicationEntity = Job.Api.Entities.Application;

namespace Job.Api.Data;

public class JobDbContext(
    DbContextOptions<JobDbContext> options)
    : DbContext(options), IOutboxDbContext
{
    public DbSet<JobEntity> Jobs
        => Set<JobEntity>();

    public DbSet<ApplicationEntity> Applications
        => Set<ApplicationEntity>();

    public DbSet<OutboxMessage> OutboxMessages
        => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // =========================
        // JOB
        // =========================
        b.Entity<JobEntity>(e =>
        {
            e.ToTable("Jobs");

            e.HasKey(x => x.Id);

            e.Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(200);

            e.Property(x => x.Description)
                .IsRequired()
                .HasMaxLength(5000);

            e.Property(x => x.Skills)
                .IsRequired()
                .HasMaxLength(1000);

            e.Property(x => x.BudgetMin)
                .HasPrecision(18, 2);

            e.Property(x => x.BudgetMax)
                .HasPrecision(18, 2);

            e.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            e.Property(x => x.RejectionReason)
                .HasMaxLength(1000);

            e.HasIndex(x => x.EmployerId);

            e.HasIndex(x => x.Status);

            e.HasIndex(x => x.CreatedAt);
        });

        // =========================
        // APPLICATION
        // =========================
        b.Entity<ApplicationEntity>(e =>
        {
            e.ToTable("Applications");

            e.HasKey(x => x.Id);

            e.Property(x => x.CoverLetter)
                .IsRequired()
                .HasMaxLength(5000);

            e.Property(x => x.ProposedBudget)
                .HasPrecision(18, 2);

            e.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            e.Property(x => x.RejectionReason)
                .HasMaxLength(1000);

            e.HasOne(x => x.Job)
                .WithMany(x => x.Applications)
                .HasForeignKey(x => x.JobId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.FreelancerId);

            e.HasIndex(x => x.Status);

            e.HasIndex(x => new
            {
                x.JobId,
                x.FreelancerId
            }).IsUnique();
        });

        // =========================
        // OUTBOX
        // =========================
        b.Entity<OutboxMessage>(e =>
        {
            e.Property(x => x.EventType)
                .HasMaxLength(100);

            e.HasIndex(x => x.PublishedAt);
        });
    }
}