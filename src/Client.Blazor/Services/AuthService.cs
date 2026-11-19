using System.Net.Http.Json;
using System.Text.Json;
using Shared.Contracts.Auth;

namespace Client.Blazor.Services;

public class AuthService(HttpClient http, TokenStorage storage, JwtAuthStateProvider provider)
{
    public Task<(bool Ok, string Message)> RegisterAsync(RegisterRequest r) => PostAsync("api/v1/auth/register", r);
    public Task<(bool Ok, string Message)> VerifyAsync(VerifyOtpRequest r) => PostAsync("api/v1/auth/verify", r);
    public Task<(bool Ok, string Message)> ResendOtpAsync(ResendOtpRequest r) => PostAsync("api/v1/auth/resend-otp", r);

    public async Task<(bool Ok, string Message)> LoginAsync(LoginRequest r)
    {
        try
        {
            var res = await http.PostAsJsonAsync("api/v1/auth/login", r);
            if (!res.IsSuccessStatusCode) return (false, await ReadError(res));
            var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
            await storage.SaveAsync(auth!);
            provider.NotifyChanged();
            return (true, "");
        }
        catch (HttpRequestException) { return (false, "Không kết nối được máy chủ. Hãy kiểm tra API Gateway."); }
    }

    public async Task LogoutAsync()
    {
        await storage.ClearAsync();
        provider.NotifyChanged();
    }

    private async Task<(bool Ok, string Message)> PostAsync<T>(string url, T body)
    {
        try
        {
            var res = await http.PostAsJsonAsync(url, body);
            if (!res.IsSuccessStatusCode) return (false, await ReadError(res));
            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            return (true, msg);
        }
        catch (HttpRequestException) { return (false, "Không kết nối được máy chủ. Hãy kiểm tra API Gateway."); }
    }

    // Đọc thông báo lỗi từ {"error": "..."} hoặc ProblemDetails {"errors": {...}}.
    private static async Task<string> ReadError(HttpResponseMessage res)
    {
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var e)) return e.GetString() ?? "Có lỗi xảy ra.";
            if (root.TryGetProperty("errors", out var errs))
                foreach (var p in errs.EnumerateObject())
                    foreach (var m in p.Value.EnumerateArray())
                        return m.GetString() ?? "Dữ liệu không hợp lệ.";
        }
        catch { }
        return res.StatusCode switch
        {
            System.Net.HttpStatusCode.TooManyRequests => "Thao tác quá nhanh, vui lòng thử lại sau.",
            _ => $"Có lỗi xảy ra (mã {(int)res.StatusCode})."
        };
    }
}
