using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Payment.Api.Services;
using Shared.Contracts.Payments;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Payment.Api.Controllers;

[ApiController]
[Route("api/v1/wallet/top-up/callback")]
public class WalletCallbackController(WalletService wallets, PaymentSimulator simulator,
    ILogger<WalletCallbackController> log) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost]
    [RequestSizeLimit(4096)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WalletTopUpItem>> Callback(WalletCallbackRequest request,
        [FromHeader(Name = PaymentCallbackSignature.Header)] string? signature, CancellationToken ct)
    {
        if (!simulator.Enabled) return NotFound();
        if (!PaymentCallbackSignature.Verify(simulator.SigningKey, request, signature))
        {
            log.LogWarning("Callback nạp tiền sai chữ ký (request {TraceId}).", HttpContext.TraceIdentifier);
            return Unauthorized(new { error = "Chữ ký callback không hợp lệ." });
        }

        var result = await wallets.SettleAsync(request, ct);
        if (result.Payment is not null) return Ok(result.Payment);
        if (result.BalanceLimit) return Conflict(new { error = "Số dư sau khi nạp sẽ vượt giới hạn của ví." });

        log.LogWarning("Callback không khớp yêu cầu nạp tiền {TopUpId}.", request.TopUpId);
        return BadRequest(new { error = "Callback không khớp yêu cầu nạp tiền." });
    }
}

internal sealed class WalletCallbackOperationFilter : IOperationFilter
{
    public const string Scheme = "WalletCallbackHmac";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(WalletCallbackController)) return;

        operation.Security = [new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = Scheme } }]
                = Array.Empty<string>()
        }];
        var signature = operation.Parameters.FirstOrDefault(x => x.In == ParameterLocation.Header &&
            x.Name == PaymentCallbackSignature.Header);
        if (signature is not null) operation.Parameters.Remove(signature);
    }
}
