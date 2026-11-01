# ADR 0006: Push das métricas por Server-Sent Events

- **Status:** Aceita (spec-v2.1.0)
- **Data:** 2026-10-31
- **Substitui:** o item "Push por WebSocket/SSE" das alternativas da ADR 0004, que deixava o push para depois. O restante da ADR 0004 (cada métrica na sua fonte, leitura fora do rate limiter, degradação graciosa) continua valendo.
- **Contexto:** A 2.1.0 transforma o dashboard na única interface do conjunto BankCore + Liquida: uma tela de tempo real que acompanha uma simulação inteira sendo liquidada. A regra do projeto é push quando o backend oferecer, polling só como fallback. Na 1.1.0 o dashboard fazia polling de `GET /metrics` a cada 1s.

## Decisão
A `Liquida.Api` expõe `GET /metrics/stream` em **Server-Sent Events**: um evento `metrics` por segundo, com o mesmo `MetricsSnapshot` do `GET /metrics`, montado pelo mesmo `MetricsService`. O endpoint fica fora do rate limiter, envia o primeiro evento ao conectar e termina quando a conexão é cancelada. O `GET /metrics` é mantido como fallback.

A escrita é manual no `HttpResponse` (`text/event-stream`, `no-cache`, buffering desligado, `PeriodicTimer` de 1s), porque o projeto está em net8.0 e o `TypedResults.ServerSentEvents` só existe no .NET 10. Nenhum pacote novo.

## Justificativa
- **Fluxo é de uma via:** servidor para navegador. SSE é HTTP comum, atravessa o CORS atual (o `EventSource` sem credenciais faz um `GET` simples, sem preflight) e reconecta sozinho no navegador.
- **Uma conexão em vez de 60 requisições por minuto** por aba aberta, e o dashboard pinta no instante em que conecta.
- **Sem estado novo no servidor:** cada conexão só repete o snapshot que o `GET /metrics` já monta. O custo por conexão é o mesmo de um polling de 1s, sem o overhead de requisição.
- **Cadência fixa de 1s** casa com a janela de `rpsLiquidacao` (1s no banco, ADR 0004).

## Alternativas consideradas
- **Manter polling:** funciona, mas contraria a regra de push quando disponível e duplica o custo de requisição. Fica como fallback.
- **WebSocket / SignalR:** bidirecional sem necessidade (o dashboard não envia nada ao Liquida); SignalR seria dependência e protocolo próprio no cliente. Rejeitada.
- **Push por evento de domínio (emitir a cada liquidação):** acoplaria o stream ao Consumer, que é outro processo; exigiria barramento entre Consumer e API. A cadência de 1s basta para o que a tela mostra. Rejeitada.
- **Subir o projeto para .NET 10 para usar `ServerSentEvents`:** mudança de runtime fora do escopo da 2.1.0.

## Consequências
- Cada aba aberta mantém uma conexão HTTP longa na API e dispara as três leituras (Postgres, Management API, memória) por segundo, como o polling já fazia.
- O cancelamento da conexão é o fim normal do endpoint: `OperationCanceledException` do `RequestAborted` não é erro.
- Proxies com buffer quebrariam o stream; em dev não há proxy. Se houver no futuro, o cabeçalho `X-Accel-Buffering: no` entra na versão que introduzir o proxy.
- Em `Origem=BankCore`, `pendentes`/`enviadas` do snapshot ficam em 0 (tabela local não é usada); o dashboard tira o backlog do BankCore (spec-v2.1.0 D10).
