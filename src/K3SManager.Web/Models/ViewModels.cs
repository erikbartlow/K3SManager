using System.ComponentModel.DataAnnotations;
using K3SManager.Core.Models;

namespace K3SManager.Web.Models;

public sealed class DashboardViewModel
{
    public required ClusterSummary Cluster { get; init; }
    public IReadOnlyList<NodeSummary> Nodes { get; init; } = [];
    public IReadOnlyList<NamespaceSummary> Namespaces { get; init; } = [];
    public IReadOnlyList<AuditEntry> RecentActions { get; init; } = [];
    public int RefreshSeconds { get; init; }
    public bool PersistenceEnabled { get; init; }
}

public sealed class NamespaceListViewModel
{
    public IReadOnlyList<NamespaceSummary> Namespaces { get; init; } = [];
    public bool IncludeSystem { get; init; }
    public string? Filter { get; init; }
    public bool CanDelete { get; init; }

    public int TotalPods => Namespaces.Sum(n => n.PodCount);
    public int TroubledCount => Namespaces.Count(n => n.HasProblems);
}

public sealed class NamespaceDetailViewModel
{
    public required NamespaceDetail Detail { get; init; }
    public bool CanDelete { get; init; }
    public bool PersistenceEnabled { get; init; }
    public NamespaceProfileEditModel Profile { get; init; } = new();
    public QuotaEditModel Quota { get; init; } = new();
}

public sealed class NamespaceCreateModel
{
    [Required(ErrorMessage = "A namespace needs a name.")]
    [RegularExpression(
        "^[a-z0-9]([-a-z0-9]*[a-z0-9])?$",
        ErrorMessage = "Use lower-case letters, digits and dashes only, starting and ending with a letter or digit.")]
    [StringLength(63, MinimumLength = 2)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Labels")]
    [StringLength(1024)]
    public string? Labels { get; set; }

    [Display(Name = "Owner")]
    [StringLength(128)]
    public string? Owner { get; set; }

    [Display(Name = "Environment")]
    [StringLength(32)]
    public string? Environment { get; set; }

    [Display(Name = "Description")]
    [StringLength(512)]
    public string? Description { get; set; }
}

public sealed class NamespaceProfileEditModel
{
    public string NamespaceName { get; set; } = string.Empty;

    [StringLength(128)]
    [Display(Name = "Owner")]
    public string? Owner { get; set; }

    [StringLength(32)]
    [Display(Name = "Environment")]
    public string? Environment { get; set; }

    [StringLength(512)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Pin to the top of the list")]
    public bool IsPinned { get; set; }
}

public sealed class QuotaEditModel
{
    public string NamespaceName { get; set; } = string.Empty;

    [Display(Name = "CPU requests")]
    [StringLength(16)]
    public string? CpuRequests { get; set; }

    [Display(Name = "CPU limits")]
    [StringLength(16)]
    public string? CpuLimits { get; set; }

    [Display(Name = "Memory requests")]
    [StringLength(16)]
    public string? MemoryRequests { get; set; }

    [Display(Name = "Memory limits")]
    [StringLength(16)]
    public string? MemoryLimits { get; set; }

    [Range(0, 10000)]
    [Display(Name = "Max pods")]
    public int? PodLimit { get; set; }
}

public sealed class NodeListViewModel
{
    public IReadOnlyList<NodeSummary> Nodes { get; init; } = [];
    public bool CanDrain { get; init; }

    public int ReadyCount => Nodes.Count(n => n.IsReady);
    public int CordonedCount => Nodes.Count(n => n.IsUnschedulable);
    public double TotalCpuCores => Math.Round(Nodes.Sum(n => n.CpuAllocatableCores), 2);
    public double TotalMemoryGiB => Math.Round(Nodes.Sum(n => n.MemoryAllocatableGiB), 2);
}

public sealed class NodeDetailViewModel
{
    public required NodeSummary Node { get; init; }
    public IReadOnlyList<PodSummary> Pods { get; init; } = [];
    public bool CanDrain { get; init; }
    public TaintInputModel Taint { get; init; } = new();
    public LabelInputModel Label { get; init; } = new();
}

public sealed class TaintInputModel
{
    public string NodeName { get; set; } = string.Empty;

    [Required]
    [StringLength(253)]
    [Display(Name = "Key")]
    public string Key { get; set; } = string.Empty;

    [StringLength(63)]
    [Display(Name = "Value")]
    public string? Value { get; set; }

    [Required]
    [Display(Name = "Effect")]
    public string Effect { get; set; } = "NoSchedule";

    public static IReadOnlyList<string> Effects { get; } = ["NoSchedule", "PreferNoSchedule", "NoExecute"];
}

public sealed class LabelInputModel
{
    public string NodeName { get; set; } = string.Empty;

    [Required]
    [StringLength(253)]
    [Display(Name = "Key")]
    public string Key { get; set; } = string.Empty;

    [StringLength(63)]
    [Display(Name = "Value")]
    public string? Value { get; set; }
}

public sealed class NodeJoinViewModel
{
    [Display(Name = "Node name")]
    [StringLength(63)]
    [RegularExpression("^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$", ErrorMessage = "Use lower-case letters, digits, dashes and dots.")]
    public string? NodeName { get; set; }

    [Display(Name = "Node labels")]
    [StringLength(512)]
    public string? Labels { get; set; }

    [Display(Name = "Node taints")]
    [StringLength(512)]
    public string? Taints { get; set; }

    public JoinInstruction? Instruction { get; set; }
}

public sealed class SettingsViewModel
{
    public IReadOnlyList<SystemSetting> Settings { get; init; } = [];
    public IReadOnlyList<AuditEntry> RecentActions { get; init; } = [];
    public bool PersistenceEnabled { get; init; }
}

public sealed class SettingUpdateModel
{
    [Required]
    [StringLength(128)]
    public string SettingKey { get; set; } = string.Empty;

    [StringLength(4096)]
    public string? SettingValue { get; set; }
}

public sealed class ErrorViewModel
{
    public string? RequestId { get; init; }
    public string? Message { get; init; }
    public int StatusCode { get; init; } = 500;

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}

/// <summary>A one-shot banner carried across a redirect in TempData.</summary>
public sealed record Alert(string Level, string Text)
{
    public const string TempDataKey = "K3SManager.Alert";

    public static Alert Success(string text) => new("success", text);

    public static Alert Warning(string text) => new("warning", text);

    public static Alert Danger(string text) => new("danger", text);
}
