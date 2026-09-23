using K3SManager.Core.Models;

namespace K3SManager.Web.Infrastructure;

/// <summary>Who is performing an action, for the audit trail.</summary>
public interface ICurrentActor
{
    string Name { get; }

    string? Address { get; }

    AuditEntry Describe(string action, string targetKind, string targetName, string? targetNamespace = null, string? detail = null);
}

internal sealed class HttpContextCurrentActor : ICurrentActor
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentActor(IHttpContextAccessor accessor) => _accessor = accessor;

    public string Name
    {
        get
        {
            var identity = _accessor.HttpContext?.User?.Identity;
            return identity?.IsAuthenticated == true && !string.IsNullOrEmpty(identity.Name)
                ? identity.Name
                : "anonymous";
        }
    }

    public string? Address => _accessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();

    public AuditEntry Describe(
        string action,
        string targetKind,
        string targetName,
        string? targetNamespace = null,
        string? detail = null) => new()
        {
            Action = action,
            TargetKind = targetKind,
            TargetName = targetName,
            TargetNamespace = targetNamespace,
            Detail = detail,
            ActorName = Name,
            ActorAddress = Address,
            CreatedUtc = DateTime.UtcNow
        };
}
