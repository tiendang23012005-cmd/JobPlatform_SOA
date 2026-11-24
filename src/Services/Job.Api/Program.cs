using Job.Api.Services;
using Microsoft.EntityFrameworkCore;
using Job.Api.Data;
using Shared.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Controllers và Swagger JWT
builder.Services.AddControllers();
builder.Services.AddSwaggerWithJwt();

// SQL Server
builder.Services.AddDbContext<JobDbContext>(o =>
    o.UseSqlServer(
        builder.Configuration.GetConnectionString("Default")));

// Health check
builder.Services.AddHealthChecks()
    .AddDbContextCheck<JobDbContext>();

// JWT Authentication và Authorization
builder.Services.AddJobPlatformAuth(builder.Configuration);

// RabbitMQ và Outbox
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddOutboxPublisher<JobDbContext>();
builder.Services.AddHttpClient<IdentityClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Services:Identity"]
        ?? "http://localhost:5001/");

    client.Timeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

// Tự động áp dụng migration
if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();

    var db = scope.ServiceProvider
        .GetRequiredService<JobDbContext>();

    var log = scope.ServiceProvider
        .GetRequiredService<ILogger<Program>>();

    for (var i = 1; i <= 10; i++)
    {
        try
        {
            db.Database.Migrate();
            break;
        }
        catch (Exception ex)
        {
            log.LogWarning(
                "Chưa kết nối được CSDL (lần {Try}/10): {Msg}",
                i, ex.Message);

            if (i == 10)
                throw;

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