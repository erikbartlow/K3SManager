using K3SManager.Core.Options;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace K3SManager.Data.Internal;

/// <summary>
/// Hands out open MySQL connections. The connection string is resolved once at construction so a
/// missing configuration entry fails at startup rather than on the first page load.
/// </summary>
internal sealed class MySqlConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(string connectionString, IOptions<DataOptions> options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(options);

        _connectionString = connectionString;
        CommandTimeoutSeconds = options.Value.CommandTimeoutSeconds;
    }

    public int CommandTimeoutSeconds { get; }

    public async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public MySqlCommand CreateCommand(MySqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        return command;
    }
}
