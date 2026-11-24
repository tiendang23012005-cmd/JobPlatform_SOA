using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Payment.Api.Controllers;
using Payment.Api.Data;
using Payment.Api.Services;
using Polly;
using Shared.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(o =>
    {
        o.ModelBindingMessageProvider.SetMissingRequestBodyRequiredValueAccessor(() => "Vui lòng gửi dữ liệu yêu cầu.");
    })
    .AddJsonOptions(o => o.AllowInputFormatterExceptionMessages = false)
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context =>
    {
        var error = context.ModelState.Values.SelectMany(x => x.Errors)
            .FirstOrDefault(x => x.Exception is null && !string.IsNullOrWhiteSpace(x.ErrorMessage));
        return new BadRequestObjectResult(new { error = error?.ErrorMessage ?? "Dữ liệu gửi lên không hợp lệ." });
    });
builder.Services.AddSwaggerWithJwt();
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition(WalletCallbackOperationFilter.Scheme, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = PaymentCallbackSignature.Header,
        Description = "Chữ ký HMAC-SHA256 cho nội dung callback. Cổng mô phỏng chỉ dùng ở Development và Test."
    });
    o.OperationFilter<WalletCallbackOperationFilter>();
});
builder.Services.AddDbContext<PaymentDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHealthChecks().AddDbContextCheck<PaymentDbContext>()
    .AddCheck<RabbitMqHealthCheck>("RabbitMQ", HealthStatus.Degraded);
builder.Services.AddJobPlatformAuth(builder.Configuration);
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddOutboxPublisher<PaymentDbContext>();
builder.Services.AddHostedService<WalletReconciliationService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddSingleton<PaymentSimulator>();

builder.Services.AddHttpClient<WalletAccountFilter>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Services:Identity"] ?? "http://localhost:5001/");
        client.Timeout = TimeSpan.FromSeconds(6);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    .AddStandardResilienceHandler(o =>
    {
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
        o.Retry.MaxRetryAttempts = 2;
        o.Retry.Delay = TimeSpan.FromMilliseconds(200);
        o.Retry.BackoffType = DelayBackoffType.Exponential;
        o.CircuitBreaker.MinimumThroughput = 5;
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(15);
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(WalletAccountFilter.RetryAfterSeconds);
    });
builder.Services.AddHttpClient<MockPaymentGateway>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["PaymentSimulator:CallbackBaseUrl"] ?? "http://localhost:5004/");
        client.Timeout = TimeSpan.FromSeconds(12);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

var app = builder.Build();
app.Services.GetRequiredService<PaymentSimulator>().Validate();

// Áp dụng migration khi khởi động. Thử lại vì SQL Server có thể chưa sẵn sàng.
if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    for (var i = 1; i <= 10; i++)
    {
        try { db.Database.Migrate(); break; }
        catch (Exception ex) when (i < 10)
        {
            log.LogWarning("Chưa áp dụng được migration Payment (lần {Try}/10): {Msg}", i, ex.Message);
            Thread.Sleep(3000);
        }
    }
}

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = "Không xử lý được yêu cầu ví. Vui lòng thử lại." });
}));
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
