using System.Text.Json;
using Microsoft.JSInterop;
using Shared.Contracts.Auth;

namespace Client.Blazor.Services;

/// <summary>Lưu thông tin đăng nhập (kèm JWT) trong localStorage của trình duyệt.</summary>
public class TokenStorage(IJSRuntime js)
{
    private const string Key = "jp_auth";

    public async Task SaveAsync(AuthResponse auth) =>
        await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(auth));

    public async Task<AuthResponse?> GetAsync()
    {
        try
        {
            var s = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            return string.IsNullOrEmpty(s) ? null : JsonSerializer.Deserialize<AuthResponse>(s);
        }
        catch { return null; }
    }

    public async Task ClearAsync() => await js.InvokeVoidAsync("localStorage.removeItem", Key);
}
