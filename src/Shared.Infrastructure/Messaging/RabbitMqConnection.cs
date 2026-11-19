using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Shared.Infrastructure.Messaging;

/// <summary>Quản lý kết nối RabbitMQ và gửi thông điệp bền vững (persistent + publisher confirm).</summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options) : IDisposable
{
    private readonly RabbitMqOptions _o = options.Value;
    private readonly object _lock = new();
    private IConnection? _connection;
    private IModel? _publishChannel;

    public IConnection Connection
    {
        get
        {
            lock (_lock)
            {
                if (_connection is { IsOpen: true }) return _connection;
                _publishChannel = null;
                _connection = new ConnectionFactory
                {
                    HostName = _o.Host, Port = _o.Port, UserName = _o.User, Password = _o.Password,
                    DispatchConsumersAsync = true
                }.CreateConnection();
                return _connection;
            }
        }
    }

    public IModel CreateChannel() => Connection.CreateModel();

    public void Publish(string routingKey, byte[] body, Guid messageId)
    {
        lock (_lock)
        {
            var connection = Connection;
            if (_publishChannel is not { IsOpen: true })
            {
                _publishChannel = connection.CreateModel();
                _publishChannel.ExchangeDeclare(Topology.Exchange, ExchangeType.Topic, durable: true);
                _publishChannel.ConfirmSelect();
            }
            var props = _publishChannel.CreateBasicProperties();
            props.Persistent = true;
            props.MessageId = messageId.ToString();
            props.ContentType = "application/json";
            _publishChannel.BasicPublish(Topology.Exchange, routingKey, props, body);
            _publishChannel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
        }
    }

    public void Dispose()
    {
        _publishChannel?.Dispose();
        _connection?.Dispose();
    }
}
