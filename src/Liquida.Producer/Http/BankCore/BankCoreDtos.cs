using System.Text.Json.Serialization;

namespace Liquida.Producer.Http.BankCore;

public sealed record TransfersPage(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("has_next")] bool HasNext,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("transfers")] IReadOnlyList<TransferDto> Transfers);

public sealed record TransferDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("from_account_id")] string FromAccountId,
    [property: JsonPropertyName("to_account_id")] string ToAccountId,
    [property: JsonPropertyName("amount_cents")] long AmountCents,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
