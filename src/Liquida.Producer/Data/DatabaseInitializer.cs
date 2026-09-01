using Npgsql;

namespace Liquida.Producer.Data;

public sealed class DatabaseInitializer
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IConfiguration configuration, ILogger<DatabaseInitializer> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
        _logger = logger;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");
        var ddl = await File.ReadAllTextAsync(schemaPath, cancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(ddl, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Schema do banco garantido.");
    }
}
