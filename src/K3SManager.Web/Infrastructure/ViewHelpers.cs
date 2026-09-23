using System.Globalization;

namespace K3SManager.Web.Infrastructure;

/// <summary>Formatting the views use so no view carries formatting logic of its own.</summary>
public static class ViewHelpers
{
    /// <summary>kubectl-style compact age: 12d, 4h, 37m, 9s.</summary>
    public static string Age(TimeSpan? span)
    {
        if (span is null || span.Value < TimeSpan.Zero)
        {
            return "-";
        }

        var value = span.Value;
        return value.TotalDays >= 1
            ? $"{(int)value.TotalDays}d"
            : value.TotalHours >= 1
                ? $"{(int)value.TotalHours}h"
                : value.TotalMinutes >= 1
                    ? $"{(int)value.TotalMinutes}m"
                    : $"{(int)value.TotalSeconds}s";
    }

    public static string Cores(double value) =>
        value >= 1
            ? value.ToString("0.##", CultureInfo.InvariantCulture)
            : $"{(int)Math.Round(value * 1000, MidpointRounding.AwayFromZero)}m";

    public static string GiB(double value) => $"{value.ToString("0.##", CultureInfo.InvariantCulture)} GiB";

    public static string Percent(double value) => $"{value.ToString("0.#", CultureInfo.InvariantCulture)}%";

    /// <summary>Maps a percentage to the bar colour class - green under 70, amber under 90, red above.</summary>
    public static string PressureClass(double percent) => percent switch
    {
        >= 90 => "is-danger",
        >= 70 => "is-warn",
        _ => "is-ok"
    };

    public static string PhaseClass(string? phase) => phase switch
    {
        "Running" or "Active" or "Succeeded" => "is-ok",
        "Pending" or "ContainerCreating" or "Terminating" => "is-warn",
        "Failed" or "CrashLoopBackOff" or "Unknown" => "is-danger",
        _ => "is-muted"
    };

    /// <summary>Truncates a long message so a table row stays one line.</summary>
    public static string Clip(string? value, int max = 120)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");
    }
}
