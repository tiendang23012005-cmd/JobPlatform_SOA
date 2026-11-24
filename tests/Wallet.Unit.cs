using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Payment.Api.Services;
using Shared.Contracts.Payments;

internal static class WalletUnitTests
{
    private static int passed;

    public static void Main()
    {
        var key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var input = new WalletCallbackRequest
        {
            TopUpId = Guid.NewGuid(), ProviderTransactionId = Guid.NewGuid(), Amount = 100_000m,
            Currency = TopUpRequest.Currency, IdempotencyKey = "nap-vi:thử|đơn", Status = TopUpStatus.Success
        };
        var signature = PaymentCallbackSignature.Sign(key, input);
        Check(PaymentCallbackSignature.Verify(key, input, signature), "Chấp nhận chữ ký đúng");
        Check(!PaymentCallbackSignature.Verify(key + "khac", input, signature), "Từ chối khóa khác");
        Check(!PaymentCallbackSignature.Verify(key, input, new string('z', 64)), "Từ chối chữ ký sai định dạng");
        Check(!PaymentCallbackSignature.Verify(key, input, null), "Từ chối thiếu chữ ký");
        input.Amount = 100_000.00m;
        Check(PaymentCallbackSignature.Verify(key, input, signature), "Chuẩn hóa số tiền tương đương");
        input.Amount = 100_001m;
        Check(!PaymentCallbackSignature.Verify(key, input, signature), "Chữ ký ràng buộc số tiền");
        input.Amount = 100_000m;
        input.Status = TopUpStatus.Failed;
        Check(!PaymentCallbackSignature.Verify(key, input, signature), "Chữ ký ràng buộc trạng thái");
        input.Status = TopUpStatus.Success;
        input.Currency = "USD";
        Check(!PaymentCallbackSignature.Verify(key, input, signature), "Chữ ký ràng buộc loại tiền");
        Check(!Valid(input), "Callback từ chối loại tiền khác VNĐ");
        input.Currency = TopUpRequest.Currency;
        input.IdempotencyKey += "-khac";
        Check(!PaymentCallbackSignature.Verify(key, input, signature), "Chữ ký ràng buộc mã idempotency");
        input.IdempotencyKey = "nap-vi:thử|đơn";
        input.ProviderTransactionId = Guid.NewGuid();
        Check(!PaymentCallbackSignature.Verify(key, input, signature), "Chữ ký ràng buộc mã giao dịch cổng");
        signature = PaymentCallbackSignature.Sign(key, input);
        input.TopUpId = Guid.NewGuid();
        Check(!PaymentCallbackSignature.Verify(key, input, signature), "Chữ ký ràng buộc mã yêu cầu");
        input.Status = TopUpStatus.Pending;
        Check(!Valid(input), "Callback từ chối Pending");
        var topUp = new TopUpRequest { Amount = 50_000m, IdempotencyKey = "nap-001" };
        Check(Valid(topUp), "Chấp nhận ngưỡng 50.000 VNĐ");
        topUp.Amount = 49_999m;
        Check(!Valid(topUp), "Từ chối dưới 50.000 VNĐ");
        topUp.Amount = 50_000.5m;
        Check(!Valid(topUp), "Từ chối tiền lẻ");
        topUp.Amount = 50_000m;
        topUp.IdempotencyKey = " nap-001";
        Check(!Valid(topUp), "Từ chối khoảng trắng ở đầu mã");
        var environment = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" }).Environment;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PaymentSimulator:Enabled"] = "true",
            ["PaymentSimulator:SigningKey"] = key
        }).Build();
        var simulator = new PaymentSimulator(environment, config);
        simulator.Validate();
        config["PaymentSimulator:SigningKey"] = new string(' ', 32);
        Check(simulator.Enabled && simulator.SigningKey == key, "Cấu hình thay đổi không thay khóa cổng đã kiểm tra");
        var rejected = false;
        try { new PaymentSimulator(environment, config).Validate(); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Từ chối khởi động cổng với khóa toàn khoảng trắng");
        environment.EnvironmentName = Environments.Production;
        Check(!new PaymentSimulator(environment, config).Enabled, "Production tắt cổng dù cấu hình bật");
        Console.WriteLine($"Unit: {passed} PASS.");
    }

    private static bool Valid(object model) =>
        Validator.TryValidateObject(model, new ValidationContext(model), new List<ValidationResult>(), true);

    private static void Check(bool result, string label)
    {
        if (!result) throw new InvalidOperationException(label);
        passed++;
        Console.WriteLine($"PASS {label}");
    }
}
