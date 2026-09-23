using System.Text.Json;
using K3SManager.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace K3SManager.Web.Controllers;

/// <summary>Shared flash-message plumbing so no controller hand-rolls it.</summary>
public abstract class K3SControllerBase : Controller
{
    protected void Flash(Alert alert) =>
        TempData[Alert.TempDataKey] = JsonSerializer.Serialize(alert);

    /// <summary>Parses "key=value" lines or comma-separated pairs from a textarea.</summary>
    protected static Dictionary<string, string> ParsePairs(string? raw)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }

        var tokens = raw.Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var separator = token.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = token[..separator].Trim();
            var value = token[(separator + 1)..].Trim();
            if (key.Length > 0)
            {
                result[key] = value;
            }
        }

        return result;
    }

    protected static List<string> ParseList(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : [.. raw.Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
