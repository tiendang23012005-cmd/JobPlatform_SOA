using Microsoft.EntityFrameworkCore;
using Job.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<JobDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHealthChecks().AddDbContextCheck<JobDbContext>();

var app = builder.Build();

// Tạo database (nếu chưa có) và áp dụng migration; thử lại vì SQL Server khởi động chậm.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JobDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    for (var i = 1; i <= 10; i++)
    {
        try { db.Database.Migrate(); break; }
        catch (Exception ex)
        {
            log.LogWarning("Chưa kết nối được CSDL (lần {Try}/10): {Msg}", i, ex.Message);
            Thread.Sleep(3000);
        }
    }
}

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
