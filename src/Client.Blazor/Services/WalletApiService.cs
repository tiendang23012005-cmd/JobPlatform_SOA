using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shared.Contracts.Payments;

namespace Client.Blazor.Services;

public class WalletApiService(HttpClient http)
{
    private const string Route = "api/v1/wallet";

    public Task<WalletItem> GetWalletAsync() => SendAsync<WalletItem>(HttpMethod.Get, $"{Route}/me");
    public Task<WalletTopUpItem> StartTopUpAsync(TopUpRequest request) =>
        SendAsync<WalletTopUpItem>(HttpMethod.Post, $"{Route}/top-up", request);
    public Task<WalletTopUpItem> GetTopUpAsync(Guid id) => SendAsync<WalletTopUpItem>(HttpMethod.Get, $"{Route}/top-up/{id}");
    public Task<WalletTopUpItem> CompleteMockAsync(Guid id, bool success) =>
        SendAsync<WalletTopUpItem>(HttpMethod.Post, $"{Route}/mock-payments/{id}/complete", new MockPaymentRequest { Success = success });
    public Task<WalletTransactionPage> GetTransactionsAsync(int page) =>
        SendAsync<WalletTransactionPage>(HttpMethod.Get, $"{Route}/transactions?page={page}");

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode == HttpStatusCode.Unauthorized
                ? "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại."
                : await AuthService.ReadError(response);
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new JsonException("Phản hồi trống.");
    }

    public static string Error(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: not null } request => request.Message,
        HttpRequestException or OperationCanceledException => "Không kết nối được dịch vụ ví. Vui lòng thử lại.",
        JsonException => "Không đọc được dữ liệu ví.",
        _ => "Không lưu hoặc đọc được yêu cầu nạp tiền trong trình duyệt. Vui lòng thử lại."
    };
}
