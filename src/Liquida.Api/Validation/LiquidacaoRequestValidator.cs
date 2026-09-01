using Liquida.Shared.Contracts;

namespace Liquida.Api.Validation;

public static class LiquidacaoRequestValidator
{
    public static bool TryValidate(LiquidacaoRequest? request, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>();

        if (request is null)
        {
            errors["body"] = ["Corpo da requisição ausente ou inválido."];
            return false;
        }

        if (request.Id == Guid.Empty)
        {
            errors[nameof(request.Id)] = ["id é obrigatório."];
        }

        if (request.Valor <= 0)
        {
            errors[nameof(request.Valor)] = ["valor deve ser maior que zero."];
        }

        if (string.IsNullOrWhiteSpace(request.Moeda) || request.Moeda.Length != 3)
        {
            errors[nameof(request.Moeda)] = ["moeda deve ter 3 caracteres (ex: BRL)."];
        }

        if (string.IsNullOrWhiteSpace(request.ContaOrigem))
        {
            errors[nameof(request.ContaOrigem)] = ["contaOrigem é obrigatória."];
        }

        if (string.IsNullOrWhiteSpace(request.ContaDestino))
        {
            errors[nameof(request.ContaDestino)] = ["contaDestino é obrigatória."];
        }

        if (!Enum.IsDefined(request.Tipo))
        {
            errors[nameof(request.Tipo)] = ["tipo inválido (use PIX, TED ou BOLETO)."];
        }

        return errors.Count == 0;
    }
}
