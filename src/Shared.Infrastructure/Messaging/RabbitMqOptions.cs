namespace Shared.Infrastructure.Messaging;

public class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";
}

public static class Topology
{
    public const string Exchange = "jobplatform.events";
    public const string DeadLetterExchange = "jobplatform.dlx";
}
