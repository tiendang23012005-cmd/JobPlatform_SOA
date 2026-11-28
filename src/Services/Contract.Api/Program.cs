using Contract.Api.Consumers;
using Contract.Api.Data;
using Contract.Api.Options;
using Contract.Api.Services;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter())); // enum trả về dạng chữ
builder.Services.AddSwaggerWithJwt();
builder.Services.AddDbContext<ContractDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHealthChecks().AddDbContextCheck<ContractDbContext>();
builder.Services.AddJobPlatformAuth(builder.Configuration);
builder.Services.Configure<ContractPolicyOptions>(builder.Configuration.GetSection(ContractPolicyOptions.Section));

builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddOutboxPublisher<ContractDbContext>();
builder.Services.AddHostedService<ApplicationAcceptedConsumer>();
builder.Services.AddHostedService<ContractLifecycleService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())   // giữ nguyên vòng lặp Migrate() cũ (retry 10 lần)
{
    var db = scope.ServiceProvider.GetRequiredService<ContractDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    for (var i = 1; i <= 10; i++)
    {
        try { db.Database.Migrate(); break; }
        catch (Exception ex)
        {
            log.LogWarning("Chưa kết nối được CSDL (lần {Try}/10): {Msg}", i, ex.Message);
            if (i == 10) throw;
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
