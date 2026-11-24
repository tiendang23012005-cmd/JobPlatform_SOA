using System.Net;
using Shared.Contracts.Payments;

namespace Payment.Api.Services;

public class MockPaymentGateway(HttpClient http, PaymentSimulator simulator, ILogger<MockPaymentGateway> log)
{
    public record Result(HttpStatusCode Status, WalletTopUpItem? Payment);

    public async Task<Result> CompleteAsync(WalletCallbackRequest callback, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/wallet/top-up/callback")
        {
            Content = JsonContent.Create(callback)
        };
        request.Headers.Add(PaymentCallbackSignature.Header, PaymentCallbackSignature.Sign(simulator.SigningKey, callback));

        try
        {
            using var response = await http.SendAsync(request, ct);
            var payment = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<WalletTopUpItem>(ct)
                : null;
            return new(response.StatusCode, payment);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException ||
                                   ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Chưa nhận được phản hồi callback mô phỏng cho yêu cầu {TopUpId}.", callback.TopUpId);
            return new(HttpStatusCode.ServiceUnavailable, null);
        }
    }
}
