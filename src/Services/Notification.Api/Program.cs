
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http.Resilience;
using Notification.Api.Consumers;
using Notification.Api.Data;
using Notification.Api.Services;
using Shared.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSwaggerWithJwt();

// Database
builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<NotificationDbContext>();

// Authentication và RabbitMQ
builder.Services.AddJobPlatformAuth(builder.Configuration);
builder.Services.AddRabbitMq(builder.Configuration);

// Email sender
builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection("Smtp"));

builder.Services.AddSingleton<IEmailSender>(sp =>
{
    var smtp = sp.GetRequiredService<IOptions<SmtpOptions>>().Value;

    if (string.IsNullOrWhiteSpace(smtp.Host))
        return ActivatorUtilities.CreateInstance<LogEmailSender>(sp);

    return ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp);
});

// HttpClient gọi Identity API, có Timeout, Retry và Circuit Breaker
builder.Services
    .AddHttpClient<IIdentityEmailClient, IdentityEmailClient>(client =>
    {
        var identityUrl =
            builder.Configuration["Services:Identity"]
            ?? "http://localhost:5001/";

        client.BaseAddress = new Uri(identityUrl);
        client.Timeout = Timeout.InfiniteTimeSpan;
    })
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);

        options.Retry.MaxRetryAttempts = 3;

        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.SamplingDuration =
            TimeSpan.FromSeconds(30);
        options.CircuitBreaker.BreakDuration =
            TimeSpan.FromSeconds(15);
    });

// Consumer hiện có
builder.Services.AddHostedService<UserRegisteredConsumer>();
builder.Services.AddHostedService<JobApprovedConsumer>();
builder.Services.AddHostedService<ApplicationSubmittedConsumer>();
builder.Services.AddHostedService<ApplicationAcceptedConsumer>();
builder.Services.AddHostedService<ApplicationRejectedConsumer>();
builder.Services.AddHostedService<WalletTopUpCompletedConsumer>();

var app = builder.Build();

// Tự động cập nhật database
if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();

    var db = scope.ServiceProvider
        .GetRequiredService<NotificationDbContext>();

    var logger = scope.ServiceProvider
        .GetRequiredService<ILogger<Program>>();

    for (var attempt = 1; attempt <= 10; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            break;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Chưa khởi tạo được CSDL (lần {Attempt}/10)",
                attempt);

            if (attempt == 10)
                throw;

            await Task.Delay(3000);
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
