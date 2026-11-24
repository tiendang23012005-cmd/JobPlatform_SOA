using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Job.Api.Services;

public class IdentityClient(HttpClient http)
{
    public async Task<bool> IsActiveAsync(
        string authorization,
        CancellationToken ct = default)
    {
        if (!AuthenticationHeaderValue.TryParse(
            authorization, out var header))
        {
            return false;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "api/v1/users/me");

        request.Headers.Authorization = header;

        using var response = await http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        var user = await response.Content
            .ReadFromJsonAsync<UserStatusResponse>(
                cancellationToken: ct);

        return string.Equals(
            user?.Status,
            "Active",
            StringComparison.OrdinalIgnoreCase);
    }

    private class UserStatusResponse
    {
        public string Status { get; set; } = string.Empty;
    }
}