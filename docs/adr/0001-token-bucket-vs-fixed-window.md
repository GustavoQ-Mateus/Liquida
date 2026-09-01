# ADR 0001 — Token Bucket em vez de Fixed Window para o rate limit de 25 rps

- **Status:** Aceita
- **Data:** 2026-08-31
- **Contexto:** A API `POST /liquidacoes` deve respeitar 25 requisições por segundo (requisito central do desafio). O .NET 8 oferece via `AddRateLimiter` os algoritmos Fixed Window, Sliding Window, Token Bucket e Concurrency.

## Decisão
Usar **Token Bucket** com `TokenLimit = 25`, `TokensPerPeriod = 25`, `ReplenishmentPeriod = 1s` e `QueueLimit = 0`.

## Justificativa
- **Fixed Window** de 25/s sofre do efeito de borda: podem passar até 50 requisições na virada da janela (25 no fim de uma janela e 25 no início da seguinte, em milissegundos), violando o "25 por segundo" real.
- **Token Bucket** entrega 25 rps sustentado e mais suave, repondo tokens continuamente, que é a semântica correta de "25 req/s".
- `QueueLimit = 0` aplica backpressure imediato: o excedente recebe `429` na hora em vez de acumular em memória.

## Consequências
- Excedente recebe `429` + header `Retry-After: 1`; o Producer reage com Polly (backoff honrando `Retry-After`).
- Rajadas curtas até 25 são absorvidas; acima disso, rejeição imediata.
- Rate limit é **global** na v1 (ver suposição 3 da spec); particionar por cliente/API key seria uma mudança futura.
