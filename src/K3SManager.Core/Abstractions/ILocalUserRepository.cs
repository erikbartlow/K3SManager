using K3SManager.Core.Models;

namespace K3SManager.Core.Abstractions;

public interface ILocalUserRepository
{
    Task<IReadOnlyList<LocalUser>> GetAllAsync(CancellationToken cancellationToken);
    Task<LocalUser?> FindAsync(string userName, CancellationToken cancellationToken);
    Task CreateAsync(LocalUser user, CancellationToken cancellationToken);
    Task UpdateAsync(LocalUser user, CancellationToken cancellationToken);
}
