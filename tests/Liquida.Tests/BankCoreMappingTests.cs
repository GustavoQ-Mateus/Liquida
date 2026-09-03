using System.Text.Json;
using Liquida.Producer.Http.BankCore;
using Liquida.Shared.Contracts;
using Xunit;

namespace Liquida.Tests;

public class BankCoreMappingTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AmountCents_ViraDecimalDividindoPor100()
    {
        var transfer = new TransferDto(
            Guid.NewGuid(), "a1", "b2", AmountCents: 15000, "PENDING", DateTimeOffset.UtcNow);

        var request = BankCoreClient.ParaRequest(transfer);

        Assert.Equal(150.00m, request.Valor);
    }

    [Fact]
    public void Mapeamento_PreencheContasMoedaEIdENaoDefineTipo()
    {
        var id = Guid.NewGuid();
        var transfer = new TransferDto(id, "acc-origem", "acc-destino", 12345, "PENDING", DateTimeOffset.UtcNow);

        var request = BankCoreClient.ParaRequest(transfer);

        Assert.Equal(id, request.Id);
        Assert.Equal(123.45m, request.Valor);
        Assert.Equal("BRL", request.Moeda);
        Assert.Equal("acc-origem", request.ContaOrigem);
        Assert.Equal("acc-destino", request.ContaDestino);
        Assert.Null(request.Tipo);
    }

    [Fact]
    public void EnvelopeReal_DesserializaTransfersEHasNext()
    {
        const string json = """
            {
              "page": 1,
              "page_size": 50,
              "has_next": true,
              "status": "PENDING",
              "transfers": [
                {
                  "id": "3f2b1c4d-0000-0000-0000-000000000001",
                  "from_account_id": "a1",
                  "to_account_id": "b2",
                  "amount_cents": 15000,
                  "status": "PENDING",
                  "created_at": "2026-09-03T10:00:00Z"
                }
              ]
            }
            """;

        var pagina = JsonSerializer.Deserialize<TransfersPage>(json, Web);

        Assert.NotNull(pagina);
        Assert.Equal(1, pagina!.Page);
        Assert.Equal(50, pagina.PageSize);
        Assert.True(pagina.HasNext);
        Assert.Single(pagina.Transfers);
        Assert.Equal(15000, pagina.Transfers[0].AmountCents);
    }

    [Fact]
    public void TokenResponse_DesserializaExpiresIn()
    {
        const string json = """{"token":"jwt-abc","token_type":"Bearer","expires_in":900}""";

        var token = JsonSerializer.Deserialize<TokenResponse>(json, Web);

        Assert.NotNull(token);
        Assert.Equal("jwt-abc", token!.Token);
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(900, token.ExpiresIn);
    }
}
