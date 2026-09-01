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
    private const int MaxTentativas = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly RabbitMqOptions _rabbit;
    private readonly IServiceProvider _services;
    private readonly ILogger<LiquidacaoConsumer> _logger;
    private readonly SemaphoreSlim _emProcessamento = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    private long _liquidadas;
    private long _duplicadas;
    private long _paraDlq;

    public LiquidacaoConsumer(
        IOptions<RabbitMqOptions> rabbit,
        IServiceProvider services,
        ILogger<LiquidacaoConsumer> logger)
    {
        _rabbit = rabbit.Value;
        _services = services;
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
            Password = _rabbit.Password
        };

        _connection = await factory.CreateConnectionAsync(stoppingToken);
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

        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

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

        await _emProcessamento.WaitAsync(CancellationToken.None);
        _emProcessamento.Release();

        _logger.LogInformation(
            "Consumer encerrando. liquidadas={Liquidadas} duplicadas={Duplicadas} dlq={Dlq}",
            Interlocked.Read(ref _liquidadas),
            Interlocked.Read(ref _duplicadas),
            Interlocked.Read(ref _paraDlq));
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

            var sucesso = await ProcessarComRetryAsync(message);
            if (sucesso)
            {
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            else
            {
                Interlocked.Increment(ref _paraDlq);
                _logger.LogError("Transação {TransacaoId} falhou {Tentativas}x; enviada para a DLQ.",
                    message.Id, MaxTentativas);
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        }
        finally
        {
            _emProcessamento.Release();
        }
    }

    private async Task<bool> ProcessarComRetryAsync(LiquidacaoMessage message)
    {
        for (var tentativa = 1; tentativa <= MaxTentativas; tentativa++)
        {
            try
            {
                using var scope = _services.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ILiquidacaoRepository>();
                var inserida = await repo.LiquidarAsync(message, CancellationToken.None);

                if (inserida)
                {
                    Interlocked.Increment(ref _liquidadas);
                    _logger.LogInformation("Transação {TransacaoId} liquidada (valor={Valor} {Moeda}).",
                        message.Id, message.Valor, message.Moeda);
                }
                else
                {
                    Interlocked.Increment(ref _duplicadas);
                    _logger.LogInformation("Transação {TransacaoId} já liquidada; ignorada (idempotência).",
                        message.Id);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao liquidar {TransacaoId} (tentativa {Tentativa}/{Max}).",
                    message.Id, tentativa, MaxTentativas);

                if (tentativa < MaxTentativas)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * tentativa));
                }
            }
        }

        return false;
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
