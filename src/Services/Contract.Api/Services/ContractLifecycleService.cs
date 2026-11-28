using Contract.Api.Data;
using Contract.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace Contract.Api.Services;

public class ContractLifecycleService(IServiceScopeFactory scopes, IOptionsMonitor<ContractPolicyOptions> opt,
    ILogger<ContractLifecycleService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await ScanAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogWarning("Quét hợp đồng lỗi (sẽ thử lại): {Msg}", ex.Message); } // BR-20: không làm sập service
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, opt.CurrentValue.ScanIntervalSeconds)), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task ScanAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractDbContext>();
        var now = DateTime.UtcNow;

        // Hết hạn ký sau 7 ngày
        foreach (var c in await db.Contracts.Where(c => c.Status == ContractStatus.Draft && c.ExpiresAt <= now).ToListAsync(ct))
            c.Status = ContractStatus.Expired;

        // Tự nghiệm thu sau N ngày không phản hồi và không tranh chấp
        var limit = now.AddDays(-opt.CurrentValue.AutoApproveDays);
        var due = await db.Milestones.Include(m => m.Contract)
            .Where(m => m.Status == MilestoneStatus.Submitted && m.SubmittedAt <= limit && !m.Contract.InDispute)
            .ToListAsync(ct);
        foreach (var m in due) ContractDomain.Approve(db, m, m.Contract, auto: true);

        await db.SaveChangesAsync(ct);
    }
}
