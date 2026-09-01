namespace Liquida.Shared.Messaging;

public static class RabbitMqTopology
{
    public const string Queue = "liquidacoes";
    public const string DeadLetterQueue = "liquidacoes.dlq";

    public static Dictionary<string, object?> MainQueueArguments() => new()
    {
        ["x-dead-letter-exchange"] = string.Empty,
        ["x-dead-letter-routing-key"] = DeadLetterQueue
    };
}
