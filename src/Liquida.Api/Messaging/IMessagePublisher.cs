using Liquida.Shared.Contracts;

namespace Liquida.Api.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync(LiquidacaoMessage message, CancellationToken cancellationToken);
}
