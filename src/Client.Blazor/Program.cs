using Client.Blazor;
using Client.Blazor.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var gateway = new Uri(builder.Configuration["GatewayUrl"]!);

builder.Services.AddScoped<TokenStorage>();
// Mọi lời gọi API đi qua API Gateway và tự gắn JWT.
builder.Services.AddScoped(sp => new HttpClient(
    new AuthHeaderHandler(sp.GetRequiredService<TokenStorage>()) { InnerHandler = new HttpClientHandler() })
{ BaseAddress = gateway });

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<JwtAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthStateProvider>());
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<JobApiService>();
builder.Services.AddScoped<WalletApiService>();

await builder.Build().RunAsync();
