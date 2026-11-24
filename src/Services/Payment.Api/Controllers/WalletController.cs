using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payment.Api.Services;
using Shared.Contracts.Payments;
using Shared.Infrastructure.Extensions;

namespace Payment.Api.Controllers;

[ApiController]
[Route("api/v1/wallet")]
[Authorize(Policy = Policies.VerifiedUser)]
[ServiceFilter(typeof(WalletAccountFilter))]
public class WalletController(WalletService wallets, PaymentSimulator simulator, MockPaymentGateway gateway) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue("sub")!);

    [HttpGet("me")]
    public async Task<ActionResult<WalletItem>> Me(CancellationToken ct) =>
        Ok(await wallets.GetAsync(UserId, simulator.Enabled, ct));

    [HttpPost("top-up")]
    public async Task<ActionResult<WalletTopUpItem>> TopUp(TopUpRequest request, CancellationToken ct)
    {
        if (!simulator.Enabled)
            return StatusCode(503, new { error = "Dịch vụ nạp tiền mô phỏng đang tắt." });

        var payment = await wallets.StartTopUpAsync(UserId, request, HttpContext.TraceIdentifier, ct);
        if (payment is null)
            return Conflict(new { error = "Mã yêu cầu nạp tiền đã được dùng cho số tiền khác." });

        return Ok(payment);
    }

    [HttpGet("top-up/{id:guid}")]
    public async Task<ActionResult<WalletTopUpItem>> TopUpStatus(Guid id, CancellationToken ct)
    {
        var payment = await wallets.GetTopUpAsync(UserId, id, ct);
        return payment is null ? NotFound(new { error = "Không tìm thấy yêu cầu nạp tiền." }) : Ok(payment);
    }

    [HttpPost("mock-payments/{id:guid}/complete")]
    public async Task<ActionResult<WalletTopUpItem>> CompleteMock(Guid id, MockPaymentRequest request, CancellationToken ct)
    {
        if (!simulator.Enabled) return NotFound();

        var callback = await wallets.GetMockCallbackAsync(UserId, id, request.Success!.Value, ct);
        if (callback is null) return NotFound(new { error = "Không tìm thấy yêu cầu nạp tiền." });

        var result = await gateway.CompleteAsync(callback, ct);
        if (result.Payment is not null) return Ok(result.Payment);
        if (result.Status == HttpStatusCode.Conflict)
            return Conflict(new { error = "Số dư sau khi nạp sẽ vượt giới hạn của ví." });

        return StatusCode(503, new { error = "Chưa xác nhận được kết quả nạp tiền. Vui lòng thử lại yêu cầu này." });
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<WalletTransactionPage>> Transactions(CancellationToken ct,
        [Range(1, 1000000, ErrorMessage = "Số trang không hợp lệ.")] int page = 1,
        [Range(1, 100, ErrorMessage = "Kích thước trang phải từ 1 đến 100.")] int pageSize = 10) =>
        Ok(await wallets.GetTransactionsAsync(UserId, page, pageSize, ct));
}
