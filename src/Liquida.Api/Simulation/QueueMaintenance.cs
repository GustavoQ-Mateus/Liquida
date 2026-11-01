using Liquida.Shared.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Liquida.Api.Simulation;

public interface IQueueMaintenance
{
    Task<IQueueSession> ConectarAsync(CancellationToken cancellationToken);
}

public interface IQueueSession : IAsyncDisposable
{
    Task<uint> PurgarAsync(string fila, CancellationToken cancellationToken);
    Task<uint> ContarAsync(string fila, CancellationToken cancellationToken);
}

public sealed class RabbitMqQueueMaintenance : IQueueMaintenance
{
    private readonly RabbitMqOptions _options;

    public RabbitMqQueueMaintenance(IOptions<RabbitMqOptions> options) => _options = options.Value;

    public async Task<IQueueSession> ConectarAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.User,
            Password = _options.Password
        };
        var connection = await factory.CreateConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        return new RabbitMqQueueSession(connection, channel);
    }

    private sealed class RabbitMqQueueSession(IConnection connection, IChannel channel) : IQueueSession
    {
        public Task<uint> PurgarAsync(string fila, CancellationToken cancellationToken)
            => channel.QueuePurgeAsync(fila, cancellationToken);

        public async Task<uint> ContarAsync(string fila, CancellationToken cancellationToken)
            => (await channel.QueueDeclarePassiveAsync(fila, cancellationToken)).MessageCount;

        public async ValueTask DisposeAsync()
        {
            await channel.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
