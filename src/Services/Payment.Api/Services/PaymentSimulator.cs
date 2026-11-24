using System.Text;

namespace Payment.Api.Services;

public sealed class PaymentSimulator(IWebHostEnvironment environment, IConfiguration configuration)
{
    public bool Enabled { get; } = (environment.IsDevelopment() || environment.IsEnvironment("Test") ||
        environment.IsEnvironment("Testing")) && configuration.GetValue<bool>("PaymentSimulator:Enabled");

    public string SigningKey { get; } = configuration["PaymentSimulator:SigningKey"] ?? "";

    public void Validate()
    {
        if (Enabled && (string.IsNullOrWhiteSpace(SigningKey) || Encoding.UTF8.GetByteCount(SigningKey) < 32))
            throw new InvalidOperationException("PaymentSimulator:SigningKey phải dài ít nhất 32 byte.");
    }
}
