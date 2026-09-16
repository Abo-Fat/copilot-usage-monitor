using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotUsage.Core;

public sealed record AppSettings
{
    public int Version { get; init; } = 2;
    public string? Account { get; init; }

    public void Validate()
    {
        if (Version != 2)
            throw new InvalidDataException("不支持此设置文件版本。");
        if (Account is not null && !CopilotPageParser.IsValidAccount(Account))
            throw new InvalidDataException("账号设置无效，请重新连接。");
    }
}

public sealed record UsageSnapshot(
    string Account,
    DateTimeOffset FetchedAtUtc,
    decimal Used,
    decimal? Limit,
    DateOnly? ResetDate,
    string ResetText,
    string UsageText)
{
    [JsonIgnore]
    public decimal? Remaining => Limit is { } limit ? Math.Max(0, limit - Used) : null;

    [JsonIgnore]
    public decimal? Percent
    {
        get
        {
            if (Limit is not > 0) return null;
            try { return Used * 100 / Limit.Value; }
            catch (OverflowException)
            {
                try { return Used / Limit.Value * 100; }
                catch (OverflowException) { return null; }
            }
        }
    }

    public void Validate()
    {
        if (!CopilotPageParser.IsValidAccount(Account) ||
            FetchedAtUtc.Offset != TimeSpan.Zero ||
            FetchedAtUtc < DateTimeOffset.UnixEpoch ||
            FetchedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5) ||
            Used < 0 || Limit < 0 || ResetText is null || UsageText is null)
            throw new InvalidDataException("本地页面用量缓存的账号、时间或数值无效。");

        var card = CopilotPageParser.ReadCard(UsageText, ResetText);
        if (card.Used != Used || card.Limit != Limit || card.ResetDate != ResetDate ||
            card.UsageText != UsageText || card.ResetText != ResetText)
            throw new InvalidDataException("本地页面用量缓存与页面显示值不一致。");
    }
}

public sealed record UsageSummary(decimal? Used, decimal? Remaining, decimal? Percent, string? Explanation);

public static class UsageCalculator
{
    public static UsageSummary Calculate(UsageSnapshot? snapshot, AppSettings settings, DateTimeOffset now)
    {
        if (snapshot is null)
            return Unknown("尚未取得页面用量数据。");
        if (settings.Account is null ||
            !string.Equals(snapshot.Account, settings.Account, StringComparison.OrdinalIgnoreCase))
            return Unknown("缓存账号与当前账号不一致。");
        try
        {
            settings.Validate();
            snapshot.Validate();
            var explanation = snapshot.Limit switch
            {
                null => "官方页面未显示总额度；剩余量与百分比未知。",
                0 => "官方页面显示总额度为 0；百分比未知。",
                _ when snapshot.Percent is null => "百分比超出可计算范围；保留页面原始用量。",
                _ => null
            };
            return new(snapshot.Used, snapshot.Remaining, snapshot.Percent, explanation);
        }
        catch (InvalidDataException)
        {
            return Unknown("页面用量缓存无效，请重新读取官方页面。");
        }
    }

    private static UsageSummary Unknown(string message) => new(null, null, null, message);
    public static string Number(decimal? value) => value?.ToString("0.############################", CultureInfo.CurrentCulture) ?? "未知";
}

public static class AppJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectRequiredConstructorParameters = true
    };
}
