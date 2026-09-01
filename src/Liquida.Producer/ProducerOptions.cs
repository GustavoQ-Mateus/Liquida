namespace Liquida.Producer;

public sealed class ProducerOptions
{
    public const string SectionName = "Producer";

    public string ApiBaseUrl { get; set; } = "http://localhost:5058";
    public int PageSize { get; set; } = 200;
    public int RequestsPerSecond { get; set; } = 25;
    public int SeedCount { get; set; } = 100;
}
