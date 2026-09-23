using CodedThought.Core.Security;
using K3SManager.Core.Options;
using K3SManager.Data;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace K3SManager.Web.Tests;

public sealed class CoreConnectionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("TEST")]
    public void Framework_encrypted_connections_are_decrypted_without_changing_configuration(string name)
    {
        const string plain = "Server=unused.invalid;Database=test;User=test;Password=test-only;";
        var encrypted = Encryption.EncryptString(plain, "test-only encryption key");
        var config = Config(encrypted, "test-only encryption key");
        var resolved = CoreConnectionResolver.Resolve(config, new DataOptions { ConnectionName = name });
        Assert.Equal(plain, resolved!.ConnectionString);
        Assert.Equal(encrypted, config["CoreSettings:Connections:MYSQL:ConnectionString"]);
        Assert.Equal(plain, CoreConnectionResolver.Resolve(config, new DataOptions { ConnectionName = name })!.ConnectionString);
    }

    [Fact]
    public void Plain_connection_is_preserved_even_when_encryption_key_exists()
    {
        const string plain = "Server=unused.invalid;Database=test;";
        Assert.Equal(plain, CoreConnectionResolver.Resolve(Config(plain, "unused"), new DataOptions())!.ConnectionString);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("incorrect key")]
    public void Missing_or_wrong_key_fails_without_leaking_secrets(string? key)
    {
        var encrypted = Encryption.EncryptString("Server=unused.invalid;Password=sensitive-test-only;", "test-only key");
        var error = Assert.Throws<InvalidOperationException>(() => CoreConnectionResolver.Resolve(Config(encrypted, key), new DataOptions()));
        Assert.DoesNotContain(encrypted, error.ToString());
        Assert.DoesNotContain("sensitive-test-only", error.ToString());
        Assert.Null(error.InnerException);
    }

    private static IConfiguration Config(string connection, string? key) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["CoreSettings:Connections:MYSQL:Name"] = "TEST",
        ["CoreSettings:Connections:MYSQL:Primary"] = "true",
        ["CoreSettings:Connections:MYSQL:ProviderType"] = "MySql",
        ["CoreSettings:Connections:MYSQL:ConnectionString"] = connection,
        ["AppSettings:EncryptionKey"] = key
    }).Build();
}
