using System.Net.Http.Json;
using System.Text.Json;
using System.Globalization;

namespace Client.Blazor.Services;

public class JobApiService(HttpClient http)
{
    // ==============================
    // JOB API
    // ==============================

    public async Task<PagedResult<JobItem>> SearchAsync(
        string? keyword,
        string? skills,
        decimal? minBudget,
        decimal? maxBudget,
        int page = 1,
        int pageSize = 10)
    {
        var query = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrWhiteSpace(keyword))
            query.Add(
                $"keyword={Uri.EscapeDataString(keyword)}");

        if (!string.IsNullOrWhiteSpace(skills))
            query.Add(
                $"skills={Uri.EscapeDataString(skills)}");

        if (minBudget.HasValue)
            query.Add("minBudget=" +
                minBudget.Value.ToString(
                    CultureInfo.InvariantCulture));

        if (maxBudget.HasValue)
            query.Add("maxBudget=" +
                maxBudget.Value.ToString(
                    CultureInfo.InvariantCulture));

        return await GetAsync<PagedResult<JobItem>>(
            "api/v1/jobs?" + string.Join("&", query));
    }

    public Task<JobItem> GetJobAsync(Guid id)
        => GetAsync<JobItem>($"api/v1/jobs/{id}");

    public Task<List<JobItem>> GetMyJobsAsync()
        => GetAsync<List<JobItem>>("api/v1/jobs/mine");

    public Task<List<JobItem>> GetPendingJobsAsync()
        => GetAsync<List<JobItem>>("api/v1/jobs/pending");

    public Task<JobItem> CreateJobAsync(JobForm form)
        => SendAsync<JobItem>(
            HttpMethod.Post,
            "api/v1/jobs",
            form);

    public Task<JobItem> UpdateJobAsync(
        Guid id, JobForm form)
        => SendAsync<JobItem>(
            HttpMethod.Put,
            $"api/v1/jobs/{id}",
            form);

    public Task<JobItem> CloseJobAsync(Guid id)
        => SendAsync<JobItem>(
            HttpMethod.Put,
            $"api/v1/jobs/{id}/close");

    public Task<JobItem> ApproveJobAsync(Guid id)
        => SendAsync<JobItem>(
            HttpMethod.Put,
            $"api/v1/jobs/{id}/approve");

    public Task<JobItem> RejectJobAsync(
        Guid id, string reason)
        => SendAsync<JobItem>(
            HttpMethod.Put,
            $"api/v1/jobs/{id}/reject",
            new { reason });

    // ==============================
    // APPLICATION API
    // ==============================

    public Task<ApplicationItem> SubmitAsync(
        Guid jobId,
        ApplicationForm form)
        => SendAsync<ApplicationItem>(
            HttpMethod.Post,
            $"api/v1/jobs/{jobId}/applications",
            form);

    public Task<List<ApplicationItem>> GetMyApplicationsAsync()
        => GetAsync<List<ApplicationItem>>(
            "api/v1/applications/mine");

    public Task<List<ApplicationItem>> GetApplicantsAsync(
        Guid jobId)
        => GetAsync<List<ApplicationItem>>(
            $"api/v1/jobs/{jobId}/applications");

    public Task<ApplicationItem> AcceptAsync(Guid id)
        => SendAsync<ApplicationItem>(
            HttpMethod.Put,
            $"api/v1/applications/{id}/accept");

    public Task<ApplicationItem> RejectAsync(
        Guid id, string reason)
        => SendAsync<ApplicationItem>(
            HttpMethod.Put,
            $"api/v1/applications/{id}/reject",
            new { reason });

    // ==============================
    // HTTP HELPERS
    // ==============================

    private async Task<T> GetAsync<T>(string url)
    {
        return await SendAsync<T>(
            HttpMethod.Get, url);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string url,
        object? body = null)
    {
        using var request =
            new HttpRequestMessage(method, url);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                await ReadErrorAsync(response));
        }

        var result =
            await response.Content.ReadFromJsonAsync<T>();

        if (result is null)
            throw new InvalidOperationException(
                "Máy chủ không trả về dữ liệu.");

        return result;
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response)
    {
        try
        {
            var raw =
                await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
                return error.GetString()
                    ?? "Có lỗi xảy ra.";

            if (root.TryGetProperty("errors", out var errors))
            {
                foreach (var property
                    in errors.EnumerateObject())
                {
                    foreach (var message
                        in property.Value.EnumerateArray())
                    {
                        return message.GetString()
                            ?? "Dữ liệu không hợp lệ.";
                    }
                }
            }
        }
        catch
        {
            // Sử dụng thông báo HTTP mặc định.
        }

        return (int)response.StatusCode switch
        {
            400 => "Dữ liệu hoặc trạng thái không hợp lệ.",
            401 => "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.",
            403 => "Bạn không có quyền thực hiện thao tác này.",
            404 => "Không tìm thấy dữ liệu.",
            409 => "Dữ liệu bị trùng hoặc có xung đột.",
            500 => "Máy chủ xảy ra lỗi.",
            502 => "Không kết nối được dịch vụ.",
            503 => "Dịch vụ tạm thời không khả dụng.",
            _ => $"Có lỗi xảy ra ({(int)response.StatusCode})."
        };
    }
}