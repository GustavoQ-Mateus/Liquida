namespace Liquida.Shared.BankCore;

public sealed class BankCoreOptions
{
    public const string SectionName = "BankCore";

    public string BaseUrl { get; set; } = "http://bankcore-api:8080";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public int RefreshMarginSeconds { get; set; } = 30;
}
