using System.Text.Json;
using Liquida.Shared.Contracts;
using Liquida.Shared.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Liquida.Api.Messaging;

public sealed class RabbitMqPublisher : IMessagePublisher, IHostedService, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly SemaphoreSlim _publishLock = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.User,
            Password = _options.Password
        };

        _connection = await ConnectWithRetryAsync(factory, cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await _channel.QueueDeclareAsync(
            queue: RabbitMqTopology.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: RabbitMqTopology.MainQueueArguments(),
            cancellationToken: cancellationToken);

        await _channel.QueueDeclareAsync(
            queue: RabbitMqTopology.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "RabbitMQ conectado em {Host}:{Port}; filas '{Queue}' e '{Dlq}' declaradas.",
            _options.Host, _options.Port, RabbitMqTopology.Queue, RabbitMqTopology.DeadLetterQueue);
    }

    public async Task PublishAsync(LiquidacaoMessage message, CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Publisher não inicializado.");
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.Id.ToString()
        };

        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            await _channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: RabbitMqTopology.Queue,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private async Task<IConnection> ConnectWithRetryAsync(ConnectionFactory factory, CancellationToken cancellationToken)
    {
        const int maxTentativas = 10;
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                return await factory.CreateConnectionAsync(cancellationToken);
            }
            catch (Exception ex) when (tentativa < maxTentativas && !cancellationToken.IsCancellationRequested)
            {
                var espera = TimeSpan.FromSeconds(Math.Min(tentativa * 2, 10));
                _logger.LogWarning(
                    "RabbitMQ indisponível em {Host}:{Port} (tentativa {Tentativa}/{Max}): {Motivo}. Nova tentativa em {Espera}s.",
                    _options.Host, _options.Port, tentativa, maxTentativas, ex.Message, espera.TotalSeconds);
                await Task.Delay(espera, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _publishLock.Dispose();
    }
}
