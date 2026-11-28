// TODO(LUOT-5, Mạnh): XÓA CẢ FILE NÀY. Thay bằng consumer MilestoneFunded / DepositHeld từ Payment.
//   Consumer mới gọi lại ContractDomain.MarkMilestoneFunded / MarkDepositHeld.
using Contract.Api.Data;
using Contract.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Contract.Api.Controllers;

[ApiController, Authorize, Route("api/v1/contracts/dev-mock")]
public class DevMockController(ContractDbContext db, IWebHostEnvironment env) : ControllerBase
{
    [HttpPost("{contractId:guid}/fund-all")]   // Funded toàn bộ milestone + coi như đã đặt cọc
    public async Task<IActionResult> FundAll(Guid contractId, CancellationToken ct)
    {
        if (!env.IsDevelopment()) return NotFound();
        var c = await db.Contracts.Include(x => x.Milestones).FirstOrDefaultAsync(x => x.Id == contractId, ct);
        if (c is null) return NotFound();
        if (c.Status != ContractStatus.Active) return Conflict(new { error = "Hợp đồng chưa Active." });
        foreach (var m in c.Milestones) ContractDomain.MarkMilestoneFunded(m);
        ContractDomain.MarkDepositHeld(c);
        await db.SaveChangesAsync(ct);
        return Ok(new { c.Id, DepositHeld = c.DepositHeld, Milestones = c.Milestones.Count });
    }
}
