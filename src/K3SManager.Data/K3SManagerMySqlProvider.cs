using System.Data;
using CodedThought.Core.Data;
using CodedThought.Core.Data.Interfaces;
using CodedThought.Core.Data.MySql;

namespace K3SManager.Data;

/// <summary>
/// Compatibility for the object-insert path of CodedThought.Core.Data.MySql 1.0.1.3.
/// Keep DbType.String on entities and let the framework create MySQL string parameters.
/// </summary>
public class K3SManagerMySqlProvider : MySqlDatabaseObject
{
    public override IDataParameter CreateParameter(object obj, TableColumn col, IDBStore store)
    {
        if (col.Type != DbTypeSupported.dbNVarChar)
            return base.CreateParameter(obj, col, store);

        var value = store.Extract(obj, col.Name);
        var parameter = base.CreateStringParameter(col.Name, value as string ?? string.Empty);
        parameter.Value = value is null or DBNull || value is string { Length: 0 } ? DBNull.Value : value;
        ((IDbDataParameter)parameter).Size = col.Size;
        return parameter;
    }
}
