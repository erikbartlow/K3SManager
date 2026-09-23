using CodedThought.Core.Configuration;
using CodedThought.Core.Security;
using K3SManager.Core.Options;
using Microsoft.Extensions.Configuration;

namespace K3SManager.Data;

public static class CoreConnectionResolver
{
    public static ConnectionSetting? Resolve(IConfiguration configuration, DataOptions options)
    {
        var connection = string.IsNullOrWhiteSpace(options.ConnectionName)
            ? configuration.GetCorePrimaryConnection()
            : configuration.GetDatabaseConnection(options.ConnectionName);
        if (connection is null || string.IsNullOrWhiteSpace(connection.ConnectionString)) return connection;

        // Chorli stores the framework's Base64 ciphertext in ConnectionString. Ordinary
        // key=value connection strings are not Base64 and pass through unchanged.
        var encoded = connection.ConnectionString;
        var buffer = new byte[encoded.Length];
        if (!Convert.TryFromBase64String(encoded, buffer, out _)) return connection;

        var key = configuration["AppSettings:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("The encrypted database connection requires AppSettings:EncryptionKey.");
        try
        {
            connection.ConnectionString = Encryption.DecryptString(encoded, key);
            if (string.IsNullOrWhiteSpace(connection.ConnectionString) || !connection.ConnectionString.Contains('='))
                throw new InvalidOperationException();
        }
        catch (Exception)
        {
            // Do not attach framework exceptions that may contain credentials or ciphertext.
            throw new InvalidOperationException("Unable to decrypt the database connection with CodedThought.Core. Check CoreSettings:Connections and AppSettings:EncryptionKey.");
        }
        return connection;
    }
}
