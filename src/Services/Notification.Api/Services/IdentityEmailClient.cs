
using System.Net;
using System.Net.Http.Json;

namespace Notification.Api.Services;

public interface IIdentityEmailClient
{
    Task<string?> GetEmailAsync(Guid userId, CancellationToken ct);
}

public class IdentityEmailClient(
    HttpClient httpClient,
    IConfiguration configuration) : IIdentityEmailClient
{
    public async Task<string?> GetEmailAsync(
        Guid userId,
        CancellationToken ct)
    {
        var key = configuration["InternalApi:Key"];

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Chưa cấu hình InternalApi:Key.");
        }

        httpClient.DefaultRequestHeaders.Remove("X-Internal-Api-Key");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Internal-Api-Key", key);

        var response = await httpClient.GetAsync(
            $"api/v1/users/internal/{userId}/email", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmailResponse>(
            cancellationToken: ct);

        return result?.Email;
    }

    private sealed class EmailResponse
    {
        public string? Email { get; set; }
    }
}
