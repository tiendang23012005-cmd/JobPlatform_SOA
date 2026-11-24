using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shared.Contracts.Payments;

namespace Payment.Api.Services;

public static class PaymentCallbackSignature
{
    public const string Header = "X-Wallet-Signature";

    public static string Sign(string key, WalletCallbackRequest request) => Convert.ToHexString(Compute(key, request));

    public static bool Verify(string key, WalletCallbackRequest request, string? signature)
    {
        if (signature?.Length != 64) return false;

        try { return CryptographicOperations.FixedTimeEquals(Compute(key, request), Convert.FromHexString(signature)); }
        catch (FormatException) { return false; }
    }

    private static byte[] Compute(string key, WalletCallbackRequest request)
    {
        // Các trường ký theo thứ tự cố định, với số tiền được chuẩn hóa để bảo toàn giá trị.
        var data = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            request.TopUpId.ToString("D"), request.ProviderTransactionId.ToString("D"),
            request.Amount.ToString("G29", CultureInfo.InvariantCulture),
            request.Currency, request.IdempotencyKey, request.Status
        });
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), data);
    }
}
