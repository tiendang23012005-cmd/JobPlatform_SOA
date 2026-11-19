using System.Net.Http.Headers;

namespace Client.Blazor.Services;

/// <summary>Tự thêm header Authorization: Bearer vào mọi yêu cầu khi đã đăng nhập.</summary>
public class AuthHeaderHandler(TokenStorage storage) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var auth = await storage.GetAsync();
        if (auth is not null && auth.ExpiresAt.ToUniversalTime() > DateTime.UtcNow)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return await base.SendAsync(request, ct);
    }
}
