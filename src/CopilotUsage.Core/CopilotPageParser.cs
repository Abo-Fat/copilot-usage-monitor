using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace CopilotUsage.Core;

public sealed class UsageException(string message, bool canRetry = false, DateTimeOffset? retryAt = null)
    : Exception(message)
{
    public bool CanRetry { get; } = canRetry;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}

public sealed record PageUsageData(string? Account, string? UsageText, string? ResetText);

public static class CopilotPageParser
{
    public const string SourceUrl = "https://github.com/settings/copilot/features";
    private const int MaximumUsageLength = 512;
    private const int MaximumResetLength = 256;
    private const string NumberPattern = @"(?:[1-9][0-9]{0,2}(?:,[0-9]{3})+|[0-9]+)(?:\.[0-9]+)?";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex AccountPattern = new(
        @"\A[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*\z", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex CardPattern = new(
        @"\AUsage this cycle\s+(?<used>" + NumberPattern + @")(?:" +
        @"\s*/\s*(?<limit>" + NumberPattern + @")\s+AI credits(?: used)?|" +
        @"\s+AI credits used)(?:\s+(?<reset>.+))?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex DatePattern = new(
        @"\b(?<month>[A-Za-z]{3,9}) (?<day>[0-9]{1,2}),? *(?<year>[0-9]{4})\b",
        RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex SensitivePattern = new(
        @"gh[pousr]_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|\b(?:bearer\s+\S+|(?:authorization|cookie|password|secret|token)\s*[:=])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex ExtraUsagePattern = new(
        @"Usage this cycle|AI credits|[0-9]\s*/\s*[0-9]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

    public static UsageSnapshot Parse(PageUsageData data, DateTimeOffset fetchedAtUtc)
    {
        if (data is null || !IsValidAccount(data.Account))
            throw new UsageException("官方页面未提供有效的已登录账号，请重新登录。");
        try
        {
            var card = ReadCard(data.UsageText, data.ResetText);
            var snapshot = new UsageSnapshot(data.Account!, fetchedAtUtc.ToUniversalTime(),
                card.Used, card.Limit, card.ResetDate, card.ResetText, card.UsageText);
            snapshot.Validate();
            return snapshot;
        }
        catch (Exception error) when (error is InvalidDataException or RegexMatchTimeoutException)
        {
            throw new UsageException("无法确认官方页面的本周期用量或采集时间；未将缺失数据当作零，请打开官方页面查看。");
        }
    }

    internal static bool IsValidAccount(string? account) =>
        account is { Length: > 0 and <= 39 } && AccountPattern.IsMatch(account);

    internal sealed record CardValues(decimal Used, decimal? Limit, DateOnly? ResetDate, string ResetText, string UsageText);

    internal static CardValues ReadCard(string? usageText, string? resetText)
    {
        var usage = Normalize(usageText, MaximumUsageLength);
        var reset = Normalize(resetText ?? "", MaximumResetLength);
        var match = CardPattern.Match(usage);
        var resetFirstPrefix = $"Usage this cycle {reset} ";
        if (!match.Success && reset.Length != 0 && usage.StartsWith(resetFirstPrefix, StringComparison.OrdinalIgnoreCase))
            match = CardPattern.Match($"Usage this cycle {usage[resetFirstPrefix.Length..]} {reset}");
        if (!match.Success) throw InvalidCard();

        var tail = match.Groups["reset"].Value;
        if (tail.Length > MaximumResetLength || ExtraUsagePattern.IsMatch(tail) ||
            (tail.Length != 0 && !tail.StartsWith("Reset", StringComparison.OrdinalIgnoreCase) && tail != reset) ||
            (reset.Length != 0 && reset != tail))
            throw InvalidCard();

        var used = ReadNumber(match.Groups["used"].Value);
        decimal? limit = match.Groups["limit"].Success ? ReadNumber(match.Groups["limit"].Value) : null;
        return new(used, limit, ReadResetDate(reset), resetText ?? "", usageText!);
    }

    private static string Normalize(string? text, int maximumLength)
    {
        if (text is null || text.Length > maximumLength ||
            text.Any(c => (char.IsControl(c) && !char.IsWhiteSpace(c)) ||
                          char.GetUnicodeCategory(c) == UnicodeCategory.Format) ||
            SensitivePattern.IsMatch(text))
            throw InvalidCard();
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static decimal ReadNumber(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var number))
            throw InvalidCard();

        // Decimal.TryParse can silently round excessive precision or underflow to zero.
        var digits = text.Replace(",", "", StringComparison.Ordinal);
        var dot = digits.IndexOf('.');
        var sourceScale = dot < 0 ? 0 : digits.Length - dot - 1;
        var sourceInteger = BigInteger.Parse(digits.Replace(".", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        var bits = decimal.GetBits(number);
        var storedInteger = ((BigInteger)(uint)bits[2] << 64) |
                            ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        var storedScale = (bits[3] >> 16) & 0xff;
        if (sourceInteger * BigInteger.Pow(10, storedScale) != storedInteger * BigInteger.Pow(10, sourceScale))
            throw InvalidCard();
        return number;
    }

    private static DateOnly? ReadResetDate(string text)
    {
        var dates = DatePattern.Matches(text);
        if (dates.Count != 1) return null;
        var date = dates[0];
        var display = $"{date.Groups["month"].Value} {date.Groups["day"].Value} {date.Groups["year"].Value}";
        return DateOnly.TryParseExact(display, ["MMM d yyyy", "MMMM d yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    private static InvalidDataException InvalidCard() =>
        new("官方用量卡片缺失、格式不受支持或包含相互冲突的数值。");
}
