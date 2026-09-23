using System.Reflection;
using System.Data;
using Microsoft.Extensions.Caching.Memory;
using CodedThought.Core.Data;
using CodedThought.Core.Data.Interfaces;
using CodedThought.Core.Data.MySql;
using K3SManager.Data.Entities;
using K3SManager.Data;
using Xunit;

namespace K3SManager.Web.Tests;

public sealed class LocalUserMappingTests
{
    [Fact]
    public void SaveNew_includes_the_username_and_preserves_String_mappings()
    {
        var provider = new RecordingProvider();
        // Exercise framework insert generation without opening a database connection.
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = (GenericDataStore)typeof(GenericDataStore).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(IMemoryCache)], null)!.Invoke([cache]);
        store.LoadAssemblyAndORM(typeof(LocalUserEntity).Assembly);
        typeof(GenericDataStore).GetProperty("DatabaseObjectInstance", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(store, provider);
        store.SaveNew(new NewLocalUserPersistenceRecord(new K3SManager.Core.Models.LocalUser { UserName = "TEST.USER", PasswordHash = "hash", SecurityStamp = "stamp", Enabled = false }));
        Assert.Contains("UserName", provider.Command);
        Assert.Contains("PasswordHash", provider.Command);
        Assert.Equal(DbType.String, typeof(LocalUserEntity).GetProperty(nameof(LocalUserEntity.UserName))!.GetCustomAttribute<DataColumnAttribute>()!.ColumnType);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_user_column_creates_a_parameter_with_the_installed_MySql_provider(bool enabled)
    {
        var user = new LocalUserEntity
        {
            UserName = "TEST.USER", PasswordHash = "test-password-hash",
            SecurityStamp = "test-security-stamp", Enabled = enabled
        };
        var provider = new K3SManagerMySqlProvider();
        var store = DispatchProxy.Create<IDBStore, EntityExtractor>();
        foreach (var property in typeof(LocalUserEntity).GetProperties())
        {
            var attribute = property.GetCustomAttribute<DataColumnAttribute>()!;
            var column = new TableColumn(attribute.ColumnName, attribute.ConvertTypeToDbTypeSupported(), attribute.Size, property.Name == nameof(LocalUserEntity.UserName));
            var parameter = provider.CreateParameter(user, column, store);
            Assert.Equal(property.GetValue(user), parameter.Value);
        }
    }
}

public sealed class RecordingProvider : K3SManagerMySqlProvider
{
    public string Command { get; private set; } = string.Empty;
    public override int ExecuteNonQuery(string commandText, CommandType type, ParameterCollection parameters)
    {
        Command = commandText;
        return 1;
    }
}

public class EntityExtractor : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IDBStore.Extract))
            return args![0]!.GetType().GetProperty((string)args[1]!)!.GetValue(args[0]);
        throw new NotSupportedException(targetMethod?.Name);
    }
}
