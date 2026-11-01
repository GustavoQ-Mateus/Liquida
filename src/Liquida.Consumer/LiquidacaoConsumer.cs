using System.Text.Json;
using System.Text.Json.Serialization;
using Liquida.Consumer.Data;
using Liquida.Shared.Contracts;
using Liquida.Shared.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Liquida.Consumer;

public sealed class LiquidacaoConsumer : BackgroundService
{
    public const int ProcessamentoParalelo = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly RabbitMqOptions _rabbit;
    private readonly IServiceProvider _services;
    private readonly LiquidacaoProcessor _processor;
    private readonly ILogger<LiquidacaoConsumer> _logger;
    private readonly SemaphoreSlim _emProcessamento = new(ProcessamentoParalelo, ProcessamentoParalelo);

    private IConnection? _connection;
    private IChannel? _channel;

    private long _paraDlq;

    public LiquidacaoConsumer(
        IOptions<RabbitMqOptions> rabbit,
        IServiceProvider services,
        LiquidacaoProcessor processor,
        ILogger<LiquidacaoConsumer> logger)
    {
        _rabbit = rabbit.Value;
        _services = services;
        _processor = processor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = _services.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<DatabaseInitializer>()
                .EnsureSchemaAsync(stoppingToken);
        }

        var factory = new ConnectionFactory
        {
            HostName = _rabbit.Host,
            Port = _rabbit.Port,
            UserName = _rabbit.User,
            Password = _rabbit.Password,
            ConsumerDispatchConcurrency = ProcessamentoParalelo
        };

        _connection = await ConnectWithRetryAsync(factory, stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: RabbitMqTopology.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: RabbitMqTopology.MainQueueArguments(),
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: RabbitMqTopology.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: ProcessamentoParalelo, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue: RabbitMqTopology.Queue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Consumer aguardando mensagens em '{Queue}'.", RabbitMqTopology.Queue);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }

        for (var slot = 0; slot < ProcessamentoParalelo; slot++)
        {
            await _emProcessamento.WaitAsync(CancellationToken.None);
        }

        _logger.LogInformation(
            "Consumer encerrando. liquidadas={Liquidadas} duplicadas={Duplicadas} dlq={Dlq}",
            _processor.Liquidadas,
            _processor.Duplicadas,
            Interlocked.Read(ref _paraDlq));
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
                    _rabbit.Host, _rabbit.Port, tentativa, maxTentativas, ex.Message, espera.TotalSeconds);
                await Task.Delay(espera, cancellationToken);
            }
        }
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        if (_channel is null)
        {
            return;
        }

        await _emProcessamento.WaitAsync(CancellationToken.None);
        try
        {
            LiquidacaoMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<LiquidacaoMessage>(ea.Body.Span, JsonOptions);
            }
            catch (JsonException ex)
            {
                Interlocked.Increment(ref _paraDlq);
                _logger.LogWarning(ex, "Mensagem inválida (JSON) enviada para a DLQ.");
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            if (message is null)
            {
                Interlocked.Increment(ref _paraDlq);
                _logger.LogWarning("Mensagem nula enviada para a DLQ.");
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            var sucesso = await _processor.ProcessarAsync(message);
            if (sucesso)
            {
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            else
            {
                Interlocked.Increment(ref _paraDlq);
                _logger.LogError("Transação {TransacaoId} não pôde ser processada; enviada para a DLQ.",
                    message.Id);
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        }
        finally
        {
            _emProcessamento.Release();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _emProcessamento.Dispose();
    }
}
