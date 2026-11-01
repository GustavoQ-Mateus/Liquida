using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Liquida.Api.Messaging;
using Liquida.Api.Reading;
using Liquida.Api.Simulation;
using Liquida.Shared.Contracts;
using Liquida.Shared.Messaging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liquida.Tests;

public class SimulationResetTests
{
    private const string DashboardOrigin = "http://localhost:4200";

    [Fact]
    public async Task Reset_Nao_Existe_Com_Flag_Desligada()
    {
        await using var factory = new ReadEndpointsTests.ReadApiFactory();

        var resposta = await factory.CreateClient().PostAsync("/simulacao/reset", null);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Reset_Zera_Contadores_Filas_E_Tabelas()
    {
        await using var factory = new ResetApiFactory();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/liquidacoes", NovaLiquidacao());

        var resposta = await client.PostAsync("/simulacao/reset", null);
        var snapshot = await resposta.Content.ReadFromJsonAsync<MetricsSnapshot>();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(0, snapshot!.Recebidas);
        Assert.Equal(0, snapshot.RateLimited);
        Assert.Equal(0, snapshot.Fila);
        Assert.Equal(0, snapshot.Dlq);
        Assert.True(factory.Repositorio.Limpo);
        Assert.Contains(RabbitMqTopology.DeadLetterQueue, factory.Filas.Purgadas);
    }

    [Fact]
    public async Task Liquidacoes_Durante_Reset_Responde_409_Sem_Publicar_Nem_Contar()
    {
        await using var factory = new ResetApiFactory();
        var client = factory.CreateClient();
        var gate = factory.Services.GetRequiredService<ResetGate>();

        var reset = client.PostAsync("/simulacao/reset", null);
        await EsperarAsync(() => gate.EstaFechado);
        var durante = await client.PostAsJsonAsync("/liquidacoes", NovaLiquidacao());
        var corpo = await durante.Content.ReadFromJsonAsync<JsonElement>();
        await reset;
        var snapshot = await client.GetFromJsonAsync<MetricsSnapshot>("/metrics");

        Assert.Equal(HttpStatusCode.Conflict, durante.StatusCode);
        Assert.Equal("simulacao_reiniciando", corpo.GetProperty("error").GetString());
        Assert.Equal(0, factory.Publicador.Publicadas);
        Assert.Equal(0, snapshot!.Recebidas);
    }

    [Fact]
    public async Task Reset_Concorrente_Responde_409()
    {
        await using var factory = new ResetApiFactory();
        var client = factory.CreateClient();

        var respostas = await Task.WhenAll(
            client.PostAsync("/simulacao/reset", null),
            client.PostAsync("/simulacao/reset", null));

        Assert.Contains(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Contains(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reset_Aceita_Post_Do_Dashboard_Com_Cors()
    {
        await using var factory = new ResetApiFactory();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/simulacao/reset");
        request.Headers.Add("Origin", DashboardOrigin);

        var resposta = await factory.CreateClient().SendAsync(request);

        Assert.Equal(DashboardOrigin, resposta.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Liquidacoes_Continua_Sem_Cors_Para_Post()
    {
        await using var factory = new ResetApiFactory();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/liquidacoes");
        preflight.Headers.Add("Origin", DashboardOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type");

        var resposta = await factory.CreateClient().SendAsync(preflight);

        Assert.False(resposta.Headers.Contains("Access-Control-Allow-Methods")
            && resposta.Headers.GetValues("Access-Control-Allow-Methods").Any(m => m.Contains("POST")));
    }

    [Fact]
    public async Task Coordenador_Estabiliza_Apos_Janela_De_Silencio()
    {
        var relogio = new RelogioQueAvanca();
        var repositorio = new RepositorioFalso();
        var servico = NovoServico(relogio, repositorio, new FilasFalsas());

        var resultado = await servico.ResetarAsync(CancellationToken.None);

        Assert.Equal(ResultadoReset.Concluido, resultado);
        Assert.InRange(relogio.Decorrido, SimulationResetService.JanelaDeSilencio, SimulationResetService.TempoMaximoDeReset);
        Assert.True(repositorio.Limpo);
    }

    [Fact]
    public async Task Coordenador_Nao_Estabiliza_Com_Chegadas_Continuas()
    {
        var relogio = new RelogioQueAvanca();
        var repositorio = new RepositorioFalso { CresceACadaLeitura = true };
        var servico = NovoServico(relogio, repositorio, new FilasFalsas());

        var resultado = await servico.ResetarAsync(CancellationToken.None);

        Assert.Equal(ResultadoReset.NaoEstabilizou, resultado);
        Assert.True(relogio.Decorrido >= SimulationResetService.TempoMaximoDeReset);
        Assert.True(repositorio.Limpo);
    }

    private static SimulationResetService NovoServico(TimeProvider relogio, IResetRepository repositorio, IQueueMaintenance filas)
        => new(
            new ResetGate(),
            filas,
            repositorio,
            new ReceivedCounter(),
            new RateLimitCounter(),
            relogio,
            NullLogger<SimulationResetService>.Instance);

    private static LiquidacaoRequest NovaLiquidacao()
        => new(Guid.NewGuid(), 10m, "BRL", "a", "b", TipoTransacao.PIX);

    private static async Task EsperarAsync(Func<bool> condicao)
    {
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (!condicao() && DateTime.UtcNow < limite)
        {
            await Task.Delay(10);
        }
    }

    public sealed class ResetApiFactory : WebApplicationFactory<Program>
    {
        public RepositorioFalso Repositorio { get; } = new();
        public FilasFalsas Filas { get; } = new();
        public PublicadorContador Publicador { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Simulacao:ResetHabilitado", "true");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<RabbitMqPublisher>();
                services.RemoveAll<IMessagePublisher>();
                services.AddSingleton<IMessagePublisher>(Publicador);
                services.RemoveAll<IMetricsRepository>();
                services.AddSingleton<IMetricsRepository, MetricsRepositoryVazio>();
                services.RemoveAll<IResetRepository>();
                services.AddSingleton<IResetRepository>(Repositorio);
                services.RemoveAll<IQueueMaintenance>();
                services.AddSingleton<IQueueMaintenance>(Filas);
            });
        }
    }

    public sealed class PublicadorContador : IMessagePublisher
    {
        private int _publicadas;

        public int Publicadas => _publicadas;

        public Task PublishAsync(LiquidacaoMessage message, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _publicadas);
            return Task.CompletedTask;
        }
    }

    public sealed class RepositorioFalso : IResetRepository
    {
        private long _liquidacoes;

        public bool CresceACadaLeitura { get; init; }
        public bool Limpo { get; private set; }

        public Task<long> ContarLiquidacoesAsync(CancellationToken cancellationToken)
            => Task.FromResult(CresceACadaLeitura ? Interlocked.Increment(ref _liquidacoes) : _liquidacoes);

        public Task LimparAsync(CancellationToken cancellationToken)
        {
            Limpo = true;
            return Task.CompletedTask;
        }
    }

    public sealed class FilasFalsas : IQueueMaintenance, IQueueSession
    {
        public List<string> Purgadas { get; } = [];

        public Task<IQueueSession> ConectarAsync(CancellationToken cancellationToken)
            => Task.FromResult<IQueueSession>(this);

        public Task<uint> PurgarAsync(string fila, CancellationToken cancellationToken)
        {
            lock (Purgadas)
            {
                Purgadas.Add(fila);
            }

            return Task.FromResult(0u);
        }

        public Task<uint> ContarAsync(string fila, CancellationToken cancellationToken) => Task.FromResult(0u);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MetricsRepositoryVazio : IMetricsRepository
    {
        public Task<ContadoresPostgres> LerContadoresAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ContadoresPostgres(0, 0, 0, 0));

        public Task<IReadOnlyList<LiquidacaoRecente>> LiquidacoesRecentesAsync(int limite, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LiquidacaoRecente>>([]);

        public Task<IReadOnlyList<TransacaoRecente>> TransacoesRecentesAsync(int limite, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TransacaoRecente>>([]);
    }

    private sealed class RelogioQueAvanca : TimeProvider
    {
        private readonly DateTimeOffset _inicio = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        private DateTimeOffset _agora = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        public TimeSpan Decorrido => _agora - _inicio;

        public override DateTimeOffset GetUtcNow() => _agora;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _agora += dueTime;
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new TimerParado();
        }

        private sealed class TimerParado : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
