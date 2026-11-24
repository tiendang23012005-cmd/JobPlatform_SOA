using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Polly;

namespace Payment.Api.Services;

public sealed class WalletAccountFilter(HttpClient identity, ILogger<WalletAccountFilter> log) : IAsyncResourceFilter
{
    public const int RetryAfterSeconds = 10;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!Guid.TryParse(http.User.FindFirstValue("sub"), out var userId) || userId == Guid.Empty ||
            !AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization.ToString(), out var bearer))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var ct = http.RequestAborted;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/users/me");
            request.Headers.Authorization = bearer;
            using var response = await identity.SendAsync(request, ct);
            Account? account = null;
            if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            {
                response.EnsureSuccessStatusCode();
                account = await response.Content.ReadFromJsonAsync<Account>(ct)
                    ?? throw new JsonException("Identity trả về trạng thái tài khoản rỗng.");
            }

            if (account is null || account.Id != userId || account.Status != "Active" ||
                account.Role != http.User.FindFirstValue("role"))
                context.Result = new ObjectResult(new { error = "Tài khoản chưa xác minh hoặc không còn hoạt động." })
                { StatusCode = StatusCodes.Status403Forbidden };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or ExecutionRejectedException ||
                                   ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Không kiểm tra được tài khoản từ Identity ({FailureType}).", ex.GetType().Name);
            http.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            context.Result = new ObjectResult(new { error = "Chưa kiểm tra được tài khoản. Vui lòng thử lại sau." })
            { StatusCode = StatusCodes.Status503ServiceUnavailable };
        }

        if (context.Result is null) await next();
    }

    private record Account(Guid Id, string Role, string Status);
}
