namespace CopilotUsage.Core;

public enum BatteryBadge
{
    None,
    Syncing,
    Warning,
    Unknown
}

public readonly record struct BatteryIndicator(int? Bars, BatteryBadge Badge)
{
    public static BatteryIndicator FromUsage(UsageSummary summary, bool busy, bool stale)
    {
        int? bars = summary.Used is null ? null : summary.Percent switch
        {
            >= 100 => 0,
            >= 75 => 1,
            >= 50 => 2,
            >= 25 => 3,
            >= 0 => 4,
            _ => summary.Remaining == 0 ? 0 : null
        };
        var badge = stale ? BatteryBadge.Warning : busy ? BatteryBadge.Syncing :
            bars is null ? BatteryBadge.Unknown : BatteryBadge.None;
        return new(bars, badge);
    }
}
