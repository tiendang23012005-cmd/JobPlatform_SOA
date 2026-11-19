using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Notification.Api.Services;

public class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "no-reply@jobplatform.local";
    public bool EnableSsl { get; set; } = true;
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>Chế độ phát triển (chưa cấu hình SMTP): ghi nội dung email, gồm mã OTP, vào log.</summary>
public class LogEmailSender(ILogger<LogEmailSender> log) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        log.LogWarning("[EMAIL-DEV] Tới: {To} | {Subject} | {Body}", to, subject, body);
        return Task.CompletedTask;
    }
}

public class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var o = options.Value;
        using var client = new SmtpClient(o.Host, o.Port)
        {
            Credentials = new NetworkCredential(o.User, o.Password),
            EnableSsl = o.EnableSsl
        };
        using var msg = new MailMessage(o.From, to, subject, body);
        await client.SendMailAsync(msg, ct);
    }
}
