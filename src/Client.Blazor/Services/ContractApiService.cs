using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Client.Blazor.Services;

public class ContractRow { public Guid Id { get; set; } public string Title { get; set; } = ""; public decimal TotalAmount { get; set; } public string Status { get; set; } = ""; public int CurrentVersion { get; set; } public DateTime ExpiresAt { get; set; } }
public class AcceptedApp { public Guid ApplicationId { get; set; } public string JobTitle { get; set; } = ""; public decimal ProposedBudget { get; set; } }
public class MilestoneInput { public string Title { get; set; } = ""; public decimal Amount { get; set; } public DateTime DueDate { get; set; } = DateTime.Today.AddDays(14); }
public class ContractForm
{
    public Guid ApplicationId { get; set; }
    public string Scope { get; set; } = ""; public string Deliverables { get; set; } = ""; public string AcceptanceCriteria { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today; public DateTime EndDate { get; set; } = DateTime.Today.AddDays(30);
    public int ProgressIntervalDays { get; set; } = 7; public int? RevisionLimit { get; set; } = 2;
    public decimal DepositRate { get; set; } = 0.10m;
    public List<MilestoneInput> Milestones { get; set; } = [new()];
}
public class SubmissionVm { public int Version { get; set; } public string Note { get; set; } = ""; public string ArtifactUrl { get; set; } = ""; public bool IsLate { get; set; } public string? RevisionReason { get; set; } }
public class UpdateVm { public int Percent { get; set; } public string Note { get; set; } = ""; public DateTime CreatedAt { get; set; } }
public class MilestoneVm
{
    public Guid Id { get; set; } public int Order { get; set; } public string Title { get; set; } = ""; public decimal Amount { get; set; }
    public DateTime DueDate { get; set; } public string Status { get; set; } = ""; public int ProgressPercent { get; set; }
    public int RevisionCount { get; set; } public bool AutoApproved { get; set; }
    public List<SubmissionVm> Submissions { get; set; } = []; public List<UpdateVm> Updates { get; set; } = [];
}
public class ContractVm
{
    public Guid Id { get; set; } public Guid ApplicationId { get; set; } public string Title { get; set; } = "";
    public string Scope { get; set; } = ""; public string Deliverables { get; set; } = ""; public string AcceptanceCriteria { get; set; } = "";
    public decimal TotalAmount { get; set; } public DateTime StartDate { get; set; } public DateTime EndDate { get; set; }
    public int ProgressIntervalDays { get; set; } public int RevisionLimit { get; set; }
    public decimal FeeRate { get; set; } public decimal DepositRate { get; set; } public decimal DepositAmount { get; set; } public bool DepositHeld { get; set; }
    public string Status { get; set; } = ""; public int CurrentVersion { get; set; } public DateTime ExpiresAt { get; set; }
    public bool EmployerConfirmed { get; set; } public bool FreelancerConfirmed { get; set; }
    public List<MilestoneVm> Milestones { get; set; } = [];
}

public class ContractApiService(HttpClient http)
{
    public Task<List<ContractRow>> MineAsync() => Send<List<ContractRow>>(HttpMethod.Get, "api/v1/contracts/mine");
    public Task<List<AcceptedApp>> AcceptedAppsAsync() => Send<List<AcceptedApp>>(HttpMethod.Get, "api/v1/contracts/accepted-applications");
    public Task<ContractVm> GetAsync(Guid id) => Send<ContractVm>(HttpMethod.Get, $"api/v1/contracts/{id}");
    public Task<JsonElement> CreateAsync(ContractForm f) => Send<JsonElement>(HttpMethod.Post, "api/v1/contracts", f);
    public Task<JsonElement> ReviseAsync(Guid id, ContractForm f) => Send<JsonElement>(HttpMethod.Put, $"api/v1/contracts/{id}", f);
    public Task<JsonElement> ConfirmAsync(Guid id, int version) => Send<JsonElement>(HttpMethod.Post, $"api/v1/contracts/{id}/confirm", new { Version = version });
    public Task<JsonElement> RejectAsync(Guid id) => Send<JsonElement>(HttpMethod.Post, $"api/v1/contracts/{id}/reject");
    public Task<JsonElement> ProgressAsync(Guid m, int percent, string? note) => Send<JsonElement>(HttpMethod.Post, $"api/v1/milestones/{m}/progress", new { Percent = percent, Note = note });
    public Task<JsonElement> SubmitAsync(Guid m, string url, string? note) => Send<JsonElement>(HttpMethod.Post, $"api/v1/milestones/{m}/submit", new { ArtifactUrl = url, Note = note });
    public Task<JsonElement> ApproveAsync(Guid m) => Send<JsonElement>(HttpMethod.Post, $"api/v1/milestones/{m}/approve");
    public Task<JsonElement> RevisionAsync(Guid m, string reason) => Send<JsonElement>(HttpMethod.Post, $"api/v1/milestones/{m}/request-revision", new { Reason = reason });
    // TODO(LUOT-5): XÓA – giả lập Funded
    public Task<JsonElement> DevFundAllAsync(Guid id) => Send<JsonElement>(HttpMethod.Post, $"api/v1/contracts/dev-mock/{id}/fund-all");

    private async Task<T> Send<T>(HttpMethod method, string url, object? body = null)
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
        HttpRequestException { StatusCode: not null } r => r.Message,
        HttpRequestException or OperationCanceledException => "Không kết nối được dịch vụ hợp đồng. Vui lòng thử lại.",
        JsonException => "Không đọc được dữ liệu hợp đồng.",
        _ => "Có lỗi xảy ra. Vui lòng thử lại."
    };

    public static string StatusText(string s) => s switch
    {
        "Draft" => "Chờ ký", "Active" => "Đang hiệu lực", "Completed" => "Hoàn thành", "Cancelled" => "Đã hủy", "Expired" => "Hết hạn",
        "Pending" => "Chờ ký quỹ", "Funded" => "Đã ký quỹ", "InProgress" => "Đang làm", "Submitted" => "Đã nộp",
        "RevisionRequested" => "Cần chỉnh sửa", "Approved" => "Đã nghiệm thu", "Released" => "Đã giải ngân", _ => s
    };
}

