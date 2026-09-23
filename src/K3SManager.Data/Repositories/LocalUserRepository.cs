using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Data.Entities;
using K3SManager.Data.Internal;

namespace K3SManager.Data.Repositories;

internal sealed class LocalUserRepository(CoreDataStore data) : ILocalUserRepository
{
    public Task<IReadOnlyList<LocalUser>> GetAllAsync(CancellationToken cancellationToken) =>
        data.ReadAsync<IReadOnlyList<LocalUser>>(store => store.GetMultiple<LocalUserEntity>(null)
            .Select(row => new LocalUser { UserName = row.UserName, PasswordHash = row.PasswordHash,
                SecurityStamp = row.SecurityStamp, Enabled = row.Enabled, Role = row.Role })
            .OrderBy(user => user.UserName).ToList(), cancellationToken);

    public Task<LocalUser?> FindAsync(string userName, CancellationToken cancellationToken) =>
        data.ReadAsync(store =>
        {
            var parameters = store.CreateParameterCollection();
            parameters.AddStringParameter("UserName", userName);
            var row = store.GetMultiple<LocalUserEntity>(parameters).FirstOrDefault();
            return row is null ? null : new LocalUser
            {
                UserName = row.UserName, PasswordHash = row.PasswordHash,
                SecurityStamp = row.SecurityStamp, Enabled = row.Enabled, Role = row.Role
            };
        }, cancellationToken);

    public Task CreateAsync(LocalUser user, CancellationToken cancellationToken) =>
        data.WriteAsync(store => store.SaveNew(new NewLocalUserPersistenceRecord(user)), cancellationToken);

    public Task UpdateAsync(LocalUser user, CancellationToken cancellationToken) =>
        data.WriteAsync(store => store.SaveExisting(Map(user)), cancellationToken);

    private static LocalUserEntity Map(LocalUser user) => new()
    {
        UserName = user.UserName, PasswordHash = user.PasswordHash,
        SecurityStamp = user.SecurityStamp, Enabled = user.Enabled, Role = user.Role
    };
}
