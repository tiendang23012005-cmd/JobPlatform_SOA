using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace Client.Blazor.Services;

public class JwtAuthStateProvider(TokenStorage storage) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var auth = await storage.GetAsync();
        if (auth is null) return Anonymous;
        if (auth.ExpiresAt.ToUniversalTime() <= DateTime.UtcNow)
        {
            await storage.ClearAsync();
            return Anonymous;
        }
        try { return new AuthenticationState(BuildPrincipal(auth.Token)); }
        catch { await storage.ClearAsync(); return Anonymous; }
    }

    public void NotifyChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    // Đọc claim từ phần payload của JWT (không kiểm tra chữ ký ở client; server mới là nơi kiểm tra).
    private static ClaimsPrincipal BuildPrincipal(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Convert.FromBase64String(payload))!;

        var claims = new List<Claim>();
        foreach (var (key, value) in dict)
        {
            if (value.ValueKind == JsonValueKind.Array)
                claims.AddRange(value.EnumerateArray().Select(e => new Claim(key, e.ToString())));
            else
                claims.Add(new Claim(key, value.ToString()));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt", "name", "role"));
    }
}
