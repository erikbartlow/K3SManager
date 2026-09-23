namespace K3SManager.Core.Abstractions;

/// <summary>
/// Raised when the API server rejects an operation. Carries a message that is safe to show an
/// operator, so controllers can surface it without leaking a stack trace into the view.
/// </summary>
public sealed class KubernetesOperationException : Exception
{
    public KubernetesOperationException(string message)
        : base(message)
    {
    }

    public KubernetesOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int? StatusCode { get; init; }
}
