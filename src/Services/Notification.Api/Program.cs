using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notification.Api.Consumers;
using Notification.Api.Data;
using Notification.Api.Services;
using Shared.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSwaggerWithJwt();
builder.Services.AddDbContext<NotificationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHealthChecks().AddDbContextCheck<NotificationDbContext>();

builder.Services.AddJobPlatformAuth(builder.Configuration);
builder.Services.AddRabbitMq(builder.Configuration);

// Chưa cấu hình Smtp:Host thì chỉ ghi email (có OTP) vào log.
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<IEmailSender>(sp =>
    string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<SmtpOptions>>().Value.Host)
        ? ActivatorUtilities.CreateInstance<LogEmailSender>(sp)
        : ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp));

builder.Services.AddHostedService<UserRegisteredConsumer>();

var app = builder.Build();

if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    for (var i = 1; i <= 10; i++)
    {
        try { db.Database.Migrate(); break; }
        catch (Exception ex)
        {
            log.LogWarning("Chưa khởi tạo được CSDL (lần {Try}/10): {Msg}", i, ex.Message);
            Thread.Sleep(3000);
        }
    }
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
