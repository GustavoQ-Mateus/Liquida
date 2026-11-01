using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Liquida.Api.Reading;

public static class MetricsStream
{
    private const string EventName = "metrics";

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    public static async Task WriteAsync(
        HttpContext context,
        MetricsService metrics,
        IOptions<JsonOptions> jsonOptions)
    {
        var cancellationToken = context.RequestAborted;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                var snapshot = await metrics.SnapshotAsync(cancellationToken);
                await WriteEventAsync(context.Response, snapshot, jsonOptions.Value.SerializerOptions, cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task WriteEventAsync(
        HttpResponse response,
        MetricsSnapshot snapshot,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(snapshot, serializerOptions);
        await response.WriteAsync($"event: {EventName}\ndata: {data}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
