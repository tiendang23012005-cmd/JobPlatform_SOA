using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Shared.Contracts.Constants;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Outbox;

namespace Shared.Infrastructure.Extensions;

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
    public const string EmployerOnly = "EmployerOnly";
    public const string FreelancerOnly = "FreelancerOnly";
    public const string VerifiedUser = "VerifiedUser";
}

public static class InfrastructureExtensions
{
    /// <summary>Xác thực JWT Bearer + các Authorization Policy dùng chung (BR-19).</summary>
    public static IServiceCollection AddJobPlatformAuth(this IServiceCollection services, IConfiguration config)
    {
        var key = config["Jwt:Key"] ?? throw new InvalidOperationException("Thiếu cấu hình Jwt:Key");
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = config["Jwt:Issuer"],
                ValidateAudience = true, ValidAudience = config["Jwt:Audience"],
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
                RoleClaimType = "role", NameClaimType = "sub"
            };
        });
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin))
            .AddPolicy(Policies.EmployerOnly, p => p.RequireRole(Roles.Employer))
            .AddPolicy(Policies.FreelancerOnly, p => p.RequireRole(Roles.Freelancer))
            .AddPolicy(Policies.VerifiedUser, p => p.RequireAuthenticatedUser().RequireClaim("email_verified", "true"));
        return services;
    }

    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<RabbitMqOptions>(config.GetSection("RabbitMq"));
        services.AddSingleton<RabbitMqConnection>();
        return services;
    }

    public static IServiceCollection AddOutboxPublisher<TContext>(this IServiceCollection services)
        where TContext : DbContext, IOutboxDbContext
        => services.AddHostedService<OutboxPublisherService<TContext>>();

    /// <summary>Swagger có nút Authorize để dán JWT khi thử API.</summary>
    public static IServiceCollection AddSwaggerWithJwt(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer",
                BearerFormat = "JWT", In = ParameterLocation.Header
            });
            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
            });
        });
        return services;
    }
}
