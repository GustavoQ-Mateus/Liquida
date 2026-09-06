using System.Text.Json.Serialization;

namespace Liquida.Shared.BankCore;

public sealed record TokenRequest(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_secret")] string ClientSecret);

public sealed record TokenResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public sealed record SettleRequest(
    [property: JsonPropertyName("settlement_ref")] string SettlementRef);

public sealed record BankCoreErrorEnvelope(
    [property: JsonPropertyName("error")] BankCoreError? Error);

public sealed record BankCoreError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
