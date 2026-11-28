namespace Contract.Api.Options;
public class ContractPolicyOptions
{
    public const string Section = "ContractPolicy";
    public decimal FeeRate { get; set; } = 0.05m;
    public decimal MinFeeRate { get; set; } = 0.05m;
    public decimal MaxFeeRate { get; set; } = 0.10m;
    public decimal MaxDepositRate { get; set; } = 0.20m;      // tỷ lệ cọc phải < 20%
    public decimal MaxDepositAmount { get; set; } = 2_000_000m;
    public int SignExpiryDays { get; set; } = 7;
    public int AutoApproveDays { get; set; } = 7;
    public int DefaultRevisionLimit { get; set; } = 2;
    public int ScanIntervalSeconds { get; set; } = 60;
}
