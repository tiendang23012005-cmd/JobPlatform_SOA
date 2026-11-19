using Identity.Api.Data;
using Identity.Api.Services;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Constants;
using Shared.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSwaggerWithJwt();
builder.Services.AddDbContext<IdentityDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHealthChecks().AddDbContextCheck<IdentityDbContext>();

builder.Services.AddJobPlatformAuth(builder.Configuration);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddOutboxPublisher<IdentityDbContext>();

var app = builder.Build();

// Áp dụng migration và tạo tài khoản Admin mặc định. Tắt khi chạy "dotnet ef" bằng Database__AutoMigrate=false.
if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    for (var i = 1; i <= 10; i++)
    {
        try
        {
            db.Database.Migrate();
            if (!db.Users.Any(u => u.Role == Roles.Admin))
            {
                db.Users.Add(new User
                {
                    Email = app.Configuration["Seed:AdminEmail"]!.ToLowerInvariant(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(app.Configuration["Seed:AdminPassword"]!),
                    Role = Roles.Admin, IsVerified = true, Status = UserStatus.Active,
                    Profile = new Profile { FullName = "Quản trị viên" }
                });
                db.SaveChanges();
            }
            break;
        }
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
