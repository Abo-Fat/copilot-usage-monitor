using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CopilotUsage.Core;

internal static class Program
{
    private const string Account = "fixture-user";
    private const string Token = "ghp_ARTIFICIAL_TEST_ONLY";
    private const string PrivateBody = "ARTIFICIAL_PAGE_BODY_MUST_NOT_APPEAR";
    private const string Reset = "Resets in 21 days on Sep 30, 2026";
    private const string Screenshot = "Usage this cycle\n123 / 1,000,000 AI credits\n" + Reset;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static int Main()
    {
        var tests = new List<(string Name, Action Run)>();
        AddParserTests(tests);
        AddCalculatorTests(tests);
        AddStorageTests(tests);
        AddRefreshTests(tests);
        AddBatteryTests(tests);
        var failures = 0;
        foreach (var (name, run) in tests)
        {
            try { run(); }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {error.GetType().Name}: {error.Message}");
            }
        }
        Console.WriteLine($"Regression suite: {tests.Count - failures}/{tests.Count} passed; {failures} failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void AddBatteryTests(List<(string Name, Action Run)> tests)
    {
        foreach (var (percent, bars) in new (decimal, int)[]
        {
            (0, 4), (0.0123m, 4), (24.9999m, 4), (25, 3), (49.9999m, 3),
            (50, 2), (74.9999m, 2), (75, 1), (99.9999m, 1), (100, 0), (125, 0)
        })
            Add(tests, $"battery used {percent} percent maps to {bars} remaining bars", () =>
                Equal(new BatteryIndicator(bars, BatteryBadge.None),
                    BatteryIndicator.FromUsage(new(percent, Math.Max(0, 100 - percent), percent, null), false, false)));
        Add(tests, "battery zero used is full not empty", () =>
            Equal(new BatteryIndicator(4, BatteryBadge.None),
                BatteryIndicator.FromUsage(new(0, 100, 0, null), false, false)));
        Add(tests, "battery missing allowance is unknown not empty or full", () =>
            Equal(new BatteryIndicator(null, BatteryBadge.Unknown),
                BatteryIndicator.FromUsage(new(456, null, null, null), false, false)));
        Add(tests, "battery missing usage is unknown", () =>
            Equal(new BatteryIndicator(null, BatteryBadge.Unknown),
                BatteryIndicator.FromUsage(new(null, null, null, null), false, false)));
        Add(tests, "battery explicit zero capacity is empty", () =>
            Equal(new BatteryIndicator(0, BatteryBadge.None),
                BatteryIndicator.FromUsage(new(0, 0, null, null), false, false)));
        Add(tests, "battery overflow percentage with zero remaining is empty", () =>
            Equal(new BatteryIndicator(0, BatteryBadge.None),
                BatteryIndicator.FromUsage(new(decimal.MaxValue, 0, null, null), false, false)));
        Add(tests, "battery stale data retains level and shows warning", () =>
            Equal(new BatteryIndicator(2, BatteryBadge.Warning),
                BatteryIndicator.FromUsage(new(60, 40, 60, null), false, true)));
        Add(tests, "battery warning remains visible while retrying", () =>
            Equal(new BatteryIndicator(2, BatteryBadge.Warning),
                BatteryIndicator.FromUsage(new(60, 40, 60, null), true, true)));
        Add(tests, "battery syncing retains last known level", () =>
            Equal(new BatteryIndicator(3, BatteryBadge.Syncing),
                BatteryIndicator.FromUsage(new(30, 70, 30, null), true, false)));
        Add(tests, "battery syncing before first snapshot does not invent level", () =>
            Equal(new BatteryIndicator(null, BatteryBadge.Syncing),
                BatteryIndicator.FromUsage(new(null, null, null, null), true, false)));
        Add(tests, "battery error without snapshot does not invent level", () =>
            Equal(new BatteryIndicator(null, BatteryBadge.Warning),
                BatteryIndicator.FromUsage(new(null, null, null, null), false, true)));
    }

    private static void AddParserTests(List<(string Name, Action Run)> tests)
    {
        Add(tests, "features source is exact official page", () =>
            Equal("https://github.com/settings/copilot/features", CopilotPageParser.SourceUrl));
        Add(tests, "artificial card values are used without billing reinterpretation", () =>
        {
            var snapshot = Parse(Screenshot, Reset);
            Equal(Account, snapshot.Account);
            Equal(Now, snapshot.FetchedAtUtc);
            Equal(123m, snapshot.Used);
            Equal<decimal?>(1_000_000m, snapshot.Limit);
            Equal<decimal?>(999_877m, snapshot.Remaining);
            Equal<decimal?>(0.0123m, snapshot.Percent);
            Equal<DateOnly?>(new DateOnly(2026, 9, 30), snapshot.ResetDate);
            Equal(Reset, snapshot.ResetText);
            Equal(Screenshot, snapshot.UsageText);
            snapshot.Validate();
        });
        Add(tests, "two-column card puts reset before the usage value in DOM order", () =>
        {
            var text = $"Usage this cycle {Reset} 123 / 1,000,000 AI credits";
            var snapshot = Parse(text, Reset);
            Equal(123m, snapshot.Used);
            Equal<decimal?>(1_000_000m, snapshot.Limit);
            Equal<DateOnly?>(new DateOnly(2026, 9, 30), snapshot.ResetDate);
            Equal(text, snapshot.UsageText);
        });
        foreach (var (name, text, used, limit) in new (string, string, decimal, decimal?)[]
        {
            ("explicit zero", "Usage this cycle 0 / 1,000,000 AI credits", 0m, 1_000_000m),
            ("zero capacity", "Usage this cycle 0 / 0 AI credits", 0m, 0m),
            ("nonzero usage with zero capacity", "Usage this cycle 42 / 0 AI credits", 42m, 0m),
            ("unbounded page", "Usage this cycle 456 AI credits used", 456m, null),
            ("unbounded zero", "Usage this cycle 0 AI credits used", 0m, null),
            ("decimal counts", "Usage this cycle 1,234.5678 / 10,000.125 AI credits", 1234.5678m, 10000.125m),
            ("small fractional usage", "Usage this cycle 0.0001 / 1 AI credits", 0.0001m, 1m),
            ("high precision", "Usage this cycle 0.1234567890123456789012345678 AI credits used",
                0.1234567890123456789012345678m, null),
            ("minimum nonzero decimal", "Usage this cycle 0.0000000000000000000000000001 AI credits used",
                0.0000000000000000000000000001m, null),
            ("maximum decimal", $"Usage this cycle {decimal.MaxValue.ToString(CultureInfo.InvariantCulture)} AI credits used",
                decimal.MaxValue, null),
            ("representable trailing decimal zero", "Usage this cycle 0.12345678901234567890123456780 AI credits used",
                0.1234567890123456789012345678m, null),
            ("bounded used suffix", "Usage this cycle 123 / 1,000,000 AI credits used", 123m, 1_000_000m),
            ("whitespace normalization", "\tUsage\u00a0this\u202fcycle\r\n123 \n/\t1,000,000 \r\nAI\tcredits ", 123m, 1_000_000m)
        })
        {
            Add(tests, $"parser {name}", () =>
            {
                var snapshot = Parse(text);
                Equal(used, snapshot.Used);
                Equal(limit, snapshot.Limit);
                Equal<DateOnly?>(null, snapshot.ResetDate);
                if (limit is null)
                {
                    Equal<decimal?>(null, snapshot.Remaining);
                    Equal<decimal?>(null, snapshot.Percent);
                }
                if (limit == 0)
                {
                    Equal<decimal?>(0m, snapshot.Remaining);
                    Equal<decimal?>(null, snapshot.Percent);
                }
            });
        }

        foreach (var (name, text) in new (string, string?)[]
        {
            ("missing usage", null),
            ("empty usage", ""),
            ("only heading", "Usage this cycle"),
            ("loading placeholder", "Usage this cycle Loading..."),
            ("no heading", "123 / 1,000,000 AI credits"),
            ("missing used", "Usage this cycle / 1,000,000 AI credits"),
            ("missing limit", "Usage this cycle 123 / AI credits"),
            ("missing credits unit", "Usage this cycle 123 / 1,000,000"),
            ("missing unit separator", "Usage this cycle 456AI credits used"),
            ("unsupported percent-only display", "Usage this cycle 12.3% used"),
            ("unqualified bare count", "Usage this cycle 456 AI credits"),
            ("negative used", "Usage this cycle -1 / 100 AI credits"),
            ("negative limit", "Usage this cycle 1 / -100 AI credits"),
            ("Unicode minus", "Usage this cycle \u22121 / 100 AI credits"),
            ("positive sign", "Usage this cycle +1 / 100 AI credits"),
            ("bad first comma group", "Usage this cycle 1234,567 / 1,000,000 AI credits"),
            ("bad final comma group", "Usage this cycle 1,00 / 1,000,000 AI credits"),
            ("bad middle comma group", "Usage this cycle 1,00,000 / 1,000,000 AI credits"),
            ("bad limit grouping", "Usage this cycle 1 / 1,00,000 AI credits"),
            ("double comma", "Usage this cycle 1,,000 / 1,000,000 AI credits"),
            ("decimal comma", "Usage this cycle 1,5 / 100 AI credits"),
            ("spaced comma grouping", "Usage this cycle 1, 000 / 2,000 AI credits"),
            ("space grouped English number", "Usage this cycle 1 000 / 2,000 AI credits"),
            ("missing integer", "Usage this cycle .5 / 100 AI credits"),
            ("missing fraction", "Usage this cycle 5. / 100 AI credits"),
            ("multiple decimal marks", "Usage this cycle 5.5.5 / 100 AI credits"),
            ("scientific notation", "Usage this cycle 1e3 / 1,000,000 AI credits"),
            ("infinite", "Usage this cycle Infinity AI credits used"),
            ("not a number", "Usage this cycle NaN AI credits used"),
            ("overflow used", "Usage this cycle 79228162514264337593543950336 AI credits used"),
            ("overflow limit", "Usage this cycle 1 / 79228162514264337593543950336 AI credits"),
            ("silently rounded fraction", "Usage this cycle 0.12345678901234567890123456789 AI credits used"),
            ("silently rounded large decimal", "Usage this cycle 79228162514264337593543950335.1 AI credits used"),
            ("decimal underflow", "Usage this cycle 0.00000000000000000000000000001 AI credits used"),
            ("two usage cards", "Usage this cycle 1 / 100 AI credits Usage this cycle 2 / 100 AI credits"),
            ("duplicate identical cards", "Usage this cycle 1 / 100 AI credits Usage this cycle 1 / 100 AI credits"),
            ("two values in a card", "Usage this cycle 1 / 100 AI credits 2 / 100 AI credits"),
            ("conflicting unlimited value", "Usage this cycle 1 / 100 AI credits 2 AI credits used"),
            ("extra usage in reset copy", "Usage this cycle 1 / 100 AI credits Resets tomorrow 2 AI credits used"),
            ("unrelated page prefix", "Account settings billing " + Screenshot),
            ("unrelated suffix", "Usage this cycle 1 / 100 AI credits " + PrivateBody),
            ("secret suffix", "Usage this cycle 1 / 100 AI credits " + Token),
            ("embedded NUL", "Usage this cycle 1\0 / 100 AI credits"),
            ("invisible digit separator", "Usage this cycle 1\u200b0 / 100 AI credits"),
            ("oversized card", "Usage this cycle 1 / 100 AI credits " + new string('x', 600)),
            ("oversized decimal input", "Usage this cycle " + new string('1', 10000) + " AI credits used")
        })
            Add(tests, $"parser rejects {name} safely", () => Safe(Throws<UsageException>(() => Parse(text))));

        var invalidAccounts = new string?[] { null, "", " ", "with space", "a/b", "@fixture", "-fixture",
            "fixture-", "double--hyphen", "user@example.test", new('a', 40), Token };
        foreach (var (account, index) in invalidAccounts.Select((account, index) => (account, index)))
            Add(tests, $"parser rejects invalid account fixture {index}",
                () => Safe(Throws<UsageException>(() => CopilotPageParser.Parse(new(account, Screenshot, Reset), Now))));
        Add(tests, "parser accepts legitimate mixed-case login", () =>
            Equal("Fixture-User123", CopilotPageParser.Parse(new("Fixture-User123", Screenshot, Reset), Now).Account));
        Add(tests, "parser missing input is sanitized", () =>
            Safe(Throws<UsageException>(() => CopilotPageParser.Parse(null!, Now))));
        Add(tests, "parser normalizes source timestamp to UTC without altering instant", () =>
        {
            var snapshot = CopilotPageParser.Parse(new(Account, Screenshot, Reset), Now.ToOffset(TimeSpan.FromHours(8)));
            Equal(Now, snapshot.FetchedAtUtc);
            Equal(TimeSpan.Zero, snapshot.FetchedAtUtc.Offset);
        });
        foreach (var time in new[] { default(DateTimeOffset), DateTimeOffset.UnixEpoch.AddTicks(-1),
                     DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.MaxValue })
            Add(tests, "parser rejects invalid source timestamp", () =>
                Safe(Throws<UsageException>(() => CopilotPageParser.Parse(new(Account, Screenshot, Reset), time))));

        foreach (var (reset, expected) in new (string, DateOnly?)[]
        {
            (Reset, new DateOnly(2026, 9, 30)),
            ("Resets on September 30, 2026", new DateOnly(2026, 9, 30)),
            ("Resets on Sep 30 2026", new DateOnly(2026, 9, 30)),
            ("Resets on Sep 30,2026", new DateOnly(2026, 9, 30)),
            ("Resets on Feb 29, 2028", new DateOnly(2028, 2, 29)),
            ("Resets on Feb 29, 2027", null),
            ("Resets on Sep 31, 2026", null),
            ("Resets in 21 days", null),
            ("Resets tomorrow", null),
            ("Reset date unavailable", null),
            ("Resets on 2026-09-30", null),
            ("Resets on Sep 30", null),
            ("Resets on Sep 30, 2026 or Oct 1, 2026", null),
            ("Schedule unavailable", null)
        })
            Add(tests, $"reset preserves date-only source {reset}", () =>
            {
                var snapshot = Parse($"Usage this cycle 456 AI credits used {reset}", reset);
                Equal(expected, snapshot.ResetDate);
                Equal(reset, snapshot.ResetText);
            });
        Add(tests, "missing scoped reset stays unknown even when card mentions a date", () =>
        {
            var snapshot = Parse(Screenshot);
            Equal("", snapshot.ResetText);
            Equal<DateOnly?>(null, snapshot.ResetDate);
        });
        Add(tests, "null scoped reset is explicitly unknown", () =>
        {
            var snapshot = Parse(Screenshot, null);
            Equal("", snapshot.ResetText);
            Equal<DateOnly?>(null, snapshot.ResetDate);
        });
        Add(tests, "normalization does not discard original safe display strings", () =>
        {
            const string reset = "Resets\u00a0in\t21 days\non Sep 30, 2026";
            const string usage = "Usage\tthis cycle\n123 / 1,000,000 AI credits\n" + reset;
            var snapshot = Parse(usage, reset);
            Equal(usage, snapshot.UsageText);
            Equal(reset, snapshot.ResetText);
            Equal<DateOnly?>(new DateOnly(2026, 9, 30), snapshot.ResetDate);
            using var area = new StorageArea();
            area.Store.SaveSnapshot(snapshot);
            Equal(snapshot, area.Store.LoadSnapshot());
        });
        Add(tests, "reset from a different card is rejected", () =>
            Safe(Throws<UsageException>(() => Parse(Screenshot, "Resets on Oct 1, 2026"))));
        Add(tests, "reset not present in full card is rejected", () =>
            Safe(Throws<UsageException>(() => Parse("Usage this cycle 456 AI credits used", Reset))));
        Add(tests, "secret in scoped reset is never retained", () =>
        {
            var reset = $"Resets tomorrow {Token} {PrivateBody}";
            Safe(Throws<UsageException>(() => Parse($"Usage this cycle 456 AI credits used {reset}", reset)));
        });
        Add(tests, "oversized scoped reset is rejected", () =>
        {
            var reset = "Resets " + new string('x', 260);
            Safe(Throws<UsageException>(() => Parse($"Usage this cycle 456 AI credits used {reset}", reset)));
        });
        Add(tests, "English number and reset parsing is culture invariant", () => WithCulture("fr-FR", () =>
        {
            var snapshot = Parse(Screenshot, Reset);
            Equal(123m, snapshot.Used);
            Equal<decimal?>(1_000_000m, snapshot.Limit);
            Equal<DateOnly?>(new DateOnly(2026, 9, 30), snapshot.ResetDate);
        }));
    }

    private static void AddCalculatorTests(List<(string Name, Action Run)> tests)
    {
        Add(tests, "calculator uses exact page totals", () =>
        {
            var result = Calculate(Parse(Screenshot, Reset));
            Equal<decimal?>(123m, result.Used);
            Equal<decimal?>(999_877m, result.Remaining);
            Equal<decimal?>(0.0123m, result.Percent);
            Equal<string?>(null, result.Explanation);
        });
        Add(tests, "over-limit remaining is zero without capping percent", () =>
        {
            var result = Calculate(Parse("Usage this cycle 125 / 100 AI credits"));
            Equal<decimal?>(125m, result.Used);
            Equal<decimal?>(0m, result.Remaining);
            Equal<decimal?>(125m, result.Percent);
        });
        Add(tests, "absent capacity is unknown not inferred", () =>
        {
            var result = Calculate(Parse("Usage this cycle 456 AI credits used"));
            Equal<decimal?>(456m, result.Used);
            Equal<decimal?>(null, result.Remaining);
            Equal<decimal?>(null, result.Percent);
            Explained(result);
        });
        Add(tests, "zero capacity percent is explicitly unknown", () =>
        {
            var result = Calculate(Parse("Usage this cycle 9 / 0 AI credits"));
            Equal<decimal?>(9m, result.Used);
            Equal<decimal?>(0m, result.Remaining);
            Equal<decimal?>(null, result.Percent);
            Explained(result);
        });
        Add(tests, "page-reported zero is valid zero", () =>
        {
            var result = Calculate(Parse("Usage this cycle 0 / 100 AI credits"));
            Equal<decimal?>(0m, result.Used);
            Equal<decimal?>(100m, result.Remaining);
            Equal<decimal?>(0m, result.Percent);
        });
        Add(tests, "missing snapshot is unknown", () => Unknown(Calculate(null)));
        Add(tests, "account mismatch is unknown", () =>
            Unknown(UsageCalculator.Calculate(Parse(Screenshot, Reset), new() { Account = "other-user" }, Now)));
        Add(tests, "disconnected settings are unknown", () =>
            Unknown(UsageCalculator.Calculate(Parse(Screenshot, Reset), new(), Now)));
        Add(tests, "account match is case insensitive", () =>
            Equal<decimal?>(123m, UsageCalculator.Calculate(Parse(Screenshot, Reset),
                new() { Account = Account.ToUpperInvariant() }, Now).Used));
        Add(tests, "new calendar month does not erase page snapshot", () =>
            Equal<decimal?>(123m, UsageCalculator.Calculate(Parse(Screenshot, Reset), Settings(),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)).Used));
        Add(tests, "date-only reset is not treated as a UTC expiry", () =>
            Equal<decimal?>(123m, UsageCalculator.Calculate(Parse(Screenshot, Reset), Settings(),
                new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.FromHours(-10))).Used));
        Add(tests, "snapshot staleness is not a calculator expiry", () =>
        {
            var snapshot = Parse(Screenshot, Reset) with { FetchedAtUtc = Now.AddYears(-1) };
            Equal<decimal?>(123m, Calculate(snapshot).Used);
        });
        Add(tests, "decimal percentage and remaining are exact", () =>
        {
            var result = Calculate(Parse("Usage this cycle 0.3001 / 1 AI credits"));
            Equal<decimal?>(0.6999m, result.Remaining);
            Equal<decimal?>(30.01m, result.Percent);
        });
        Add(tests, "tiny percent does not underflow before multiplication", () =>
            Equal<decimal?>(0.0000000000000000000000000001m,
                Calculate(Parse("Usage this cycle 0.0000000000000000000000000001 / 100 AI credits")).Percent));
        Add(tests, "large valid percent avoids intermediate overflow", () =>
            Equal<decimal?>(100m, Calculate(Parse(
                "Usage this cycle 79228162514264337593543950335 / 79228162514264337593543950335 AI credits")).Percent));
        Add(tests, "percentage overflow leaves actual use and remaining available", () =>
        {
            var snapshot = Parse("Usage this cycle 79228162514264337593543950335 / 0.1 AI credits");
            Equal<decimal?>(null, snapshot.Percent);
            var result = Calculate(snapshot);
            Equal<decimal?>(decimal.MaxValue, result.Used);
            Equal<decimal?>(0m, result.Remaining);
            Equal<decimal?>(null, result.Percent);
            Explained(result);
        });
        Add(tests, "invalid snapshot is unknown rather than throwing", () =>
            Unknown(Calculate(Parse(Screenshot, Reset) with { Used = -1m })));
        Add(tests, "unsupported settings version is unknown", () =>
            Unknown(UsageCalculator.Calculate(Parse(Screenshot, Reset), Settings() with { Version = 1 }, Now)));
        Add(tests, "display formatting has no cents assumption", () => WithCulture("en-US", () =>
        {
            Equal("123", UsageCalculator.Number(123m));
            Equal("0.0001", UsageCalculator.Number(0.0001m));
            Equal("0.1234567890123456789012345678",
                UsageCalculator.Number(0.1234567890123456789012345678m));
            Equal("未知", UsageCalculator.Number(null));
        }));
    }

    private static void AddStorageTests(List<(string Name, Action Run)> tests)
    {
        Add(tests, "absent files yield disconnected v2 defaults", () =>
        {
            using var area = new StorageArea();
            Equal(new AppSettings(), area.Store.LoadSettings());
            Equal(2, area.Store.LoadSettings().Version);
            Check(!area.Store.LegacySettingsDetected, "Missing settings are not legacy.");
            Equal<UsageSnapshot?>(null, area.Store.LoadSnapshot());
            area.AssertOnly();
        });
        Add(tests, "legacy migration is disconnected and nondestructive until explicit save", () =>
        {
            using var area = new StorageArea();
            const string legacy = """{"version":1,"account":"billing-only-user","monthlyAllowance":1000,"mapping":{"knownBuckets":[],"includedBuckets":[]}}""";
            File.WriteAllText(area.SettingsPath, legacy);
            File.WriteAllText(area.LegacySnapshotPath, PrivateBody);
            File.WriteAllText(area.SentinelPath, "keep");
            Equal(new AppSettings(), area.Store.LoadSettings());
            Check(area.Store.LegacySettingsDetected, "Migration notice flag was not set.");
            Equal(legacy, File.ReadAllText(area.SettingsPath));
            Equal<UsageSnapshot?>(null, area.Store.LoadSnapshot());
            Equal(new AppSettings(), area.Store.LoadSettings());
            Check(area.Store.LegacySettingsDetected, "Repeated reads must preserve migration notice.");
            area.Store.SaveSettings(Settings());
            Check(!area.Store.LegacySettingsDetected, "Successful explicit save must clear notice.");
            Equal(Settings(), area.Store.LoadSettings());
            var saved = File.ReadAllText(area.SettingsPath);
            Check(!saved.Contains("monthlyAllowance", StringComparison.Ordinal), "Legacy total was retained.");
            Check(!saved.Contains("mapping", StringComparison.Ordinal), "Legacy mapping was retained.");
            Equal(PrivateBody, File.ReadAllText(area.LegacySnapshotPath));
            Equal("keep", File.ReadAllText(area.SentinelPath));
            area.AssertOnly("settings.json", "snapshot.json", "sentinel.txt");
        });
        Add(tests, "legacy account and allowance are not interpreted as a page connection", () =>
        {
            using var area = new StorageArea();
            File.WriteAllText(area.SettingsPath, """{"version":1,"account":"bad account","monthlyAllowance":-100}""");
            Equal(new AppSettings(), area.Store.LoadSettings());
            Check(area.Store.LegacySettingsDetected, "Legacy settings were not detected.");
        });
        Add(tests, "invalid migration save preserves original settings and notice", () =>
        {
            using var area = new StorageArea();
            const string legacy = """{"version":1,"account":"billing-user"}""";
            File.WriteAllText(area.SettingsPath, legacy);
            area.Store.LoadSettings();
            Throws<InvalidDataException>(() => area.Store.SaveSettings(new() { Account = "" }));
            Equal(legacy, File.ReadAllText(area.SettingsPath));
            Check(area.Store.LegacySettingsDetected, "Failed migration cleared notice.");
            area.AssertOnly("settings.json");
        });
        Add(tests, "settings replacement roundtrips only v2 account and version", () =>
        {
            using var area = new StorageArea();
            File.WriteAllText(area.SentinelPath, "owned sentinel");
            area.Store.SaveSettings(new());
            area.Store.SaveSettings(Settings());
            Equal(Settings(), area.Store.LoadSettings());
            var json = JsonNode.Parse(File.ReadAllText(area.SettingsPath))!.AsObject();
            SequenceEqual(new[] { "account", "version" }, json.Select(p => p.Key).Order());
            Equal("owned sentinel", File.ReadAllText(area.SentinelPath));
            area.AssertOnly("settings.json", "sentinel.txt");
        });
        Add(tests, "feature snapshot atomic replacement roundtrips all fields", () =>
        {
            using var area = new StorageArea();
            area.Store.SaveSnapshot(Parse("Usage this cycle 0 / 100 AI credits"));
            var snapshot = Parse(Screenshot, Reset);
            area.Store.SaveSnapshot(snapshot);
            Equal(snapshot, area.Store.LoadSnapshot());
            var json = File.ReadAllText(area.SnapshotPath);
            Check(json.Contains("\"fetchedAtUtc\"", StringComparison.Ordinal), "JSON is not camelCase.");
            Check(json.Contains("\"2026-09-30\"", StringComparison.Ordinal), "Reset was not stored as a date-only.");
            Check(!json.Contains("\"percent\"", StringComparison.Ordinal), "Derived percentage should not be stored.");
            Check(!json.Contains("\"remaining\"", StringComparison.Ordinal), "Derived remaining should not be stored.");
            area.AssertOnly("features-snapshot.json");
        });
        Add(tests, "unbounded snapshot roundtrips explicit null capacity and reset", () =>
        {
            using var area = new StorageArea();
            var snapshot = Parse("Usage this cycle 0.1234567890123456789012345678 AI credits used");
            area.Store.SaveSnapshot(snapshot);
            Equal(snapshot, area.Store.LoadSnapshot());
        });
        Add(tests, "legacy snapshot alone is never loaded or deleted", () =>
        {
            using var area = new StorageArea();
            File.WriteAllText(area.LegacySnapshotPath, PrivateBody);
            Equal<UsageSnapshot?>(null, area.Store.LoadSnapshot());
            area.Store.DeleteSnapshot();
            Equal(PrivateBody, File.ReadAllText(area.LegacySnapshotPath));
            area.AssertOnly("snapshot.json");
        });
        Add(tests, "delete feature cache preserves settings, legacy cache and sentinel", () =>
        {
            using var area = new StorageArea();
            area.Store.SaveSettings(Settings());
            area.Store.SaveSnapshot(Parse(Screenshot, Reset));
            File.WriteAllText(area.LegacySnapshotPath, PrivateBody);
            File.WriteAllText(area.SentinelPath, "keep");
            area.Store.DeleteSnapshot();
            area.Store.DeleteSnapshot();
            Equal<UsageSnapshot?>(null, area.Store.LoadSnapshot());
            Equal(Settings(), area.Store.LoadSettings());
            Equal(PrivateBody, File.ReadAllText(area.LegacySnapshotPath));
            Equal("keep", File.ReadAllText(area.SentinelPath));
            area.AssertOnly("settings.json", "snapshot.json", "sentinel.txt");
        });
        foreach (var (name, json) in new[]
        {
            ("null", "null"),
            ("missing version", "{}"),
            ("string version", """{"version":"2"}"""),
            ("null version", """{"version":null}"""),
            ("fractional version", """{"version":2.5}"""),
            ("unknown version", """{"version":3}"""),
            ("invalid account", """{"version":2,"account":" "}"""),
            ("malformed JSON", "{")
        })
            Add(tests, $"settings rejects {name} without replacing file", () =>
            {
                using var area = new StorageArea();
                File.WriteAllText(area.SettingsPath, json);
                ThrowsData(() => area.Store.LoadSettings());
                Equal(json, File.ReadAllText(area.SettingsPath));
                area.AssertOnly("settings.json");
            });
        foreach (var field in new[] { "account", "fetchedAtUtc", "used", "limit", "resetDate", "resetText", "usageText" })
            Add(tests, $"cache requires field {field} instead of assuming a default", () =>
            {
                using var area = new StorageArea();
                var json = JsonSerializer.SerializeToNode(Parse(Screenshot, Reset), AppJson.Options)!.AsObject();
                json.Remove(field);
                File.WriteAllText(area.SnapshotPath, json.ToJsonString());
                ThrowsData(() => area.Store.LoadSnapshot());
            });
        foreach (var (name, mutate) in new (string, Func<UsageSnapshot, UsageSnapshot>)[]
        {
            ("null account", s => s with { Account = null! }),
            ("empty account", s => s with { Account = "" }),
            ("wrong login format", s => s with { Account = "bad/account" }),
            ("missing timestamp", s => s with { FetchedAtUtc = default }),
            ("pre-epoch timestamp", s => s with { FetchedAtUtc = DateTimeOffset.UnixEpoch.AddTicks(-1) }),
            ("non-UTC timestamp", s => s with { FetchedAtUtc = Now.ToOffset(TimeSpan.FromHours(8)) }),
            ("future timestamp", s => s with { FetchedAtUtc = Now.AddDays(1) }),
            ("negative usage", s => s with { Used = -1m }),
            ("negative limit", s => s with { Limit = -1m }),
            ("inconsistent usage", s => s with { Used = 0m }),
            ("invented total", s => s with { Limit = 100m }),
            ("invented month reset", s => s with { ResetDate = new DateOnly(2026, 10, 1) }),
            ("null usage display", s => s with { UsageText = null! }),
            ("empty usage display", s => s with { UsageText = "" }),
            ("null reset display", s => s with { ResetText = null! }),
            ("oversized usage", s => s with { UsageText = new string('x', 513) }),
            ("oversized reset", s => s with { ResetText = new string('x', 257) }),
            ("secret display", s => s with { UsageText = $"{s.UsageText} {Token}" })
        })
        {
            Add(tests, $"cache validates {name} on read and before replacing valid data", () =>
            {
                using var area = new StorageArea();
                var good = Parse(Screenshot, Reset);
                area.Store.SaveSnapshot(good);
                var before = File.ReadAllText(area.SnapshotPath);
                var invalid = mutate(good);
                Throws<InvalidDataException>(() => area.Store.SaveSnapshot(invalid));
                Equal(before, File.ReadAllText(area.SnapshotPath));
                File.WriteAllText(area.SnapshotPath, JsonSerializer.Serialize(invalid, AppJson.Options));
                ThrowsData(() => area.Store.LoadSnapshot());
                area.AssertOnly("features-snapshot.json");
            });
        }
        foreach (var json in new[] { "{", "null", """{"items":[],"period":{"year":2026,"month":9}}""" })
            Add(tests, "cache rejects malformed, null or old-schema contents", () =>
            {
                using var area = new StorageArea();
                File.WriteAllText(area.SnapshotPath, json);
                ThrowsData(() => area.Store.LoadSnapshot());
                Equal(json, File.ReadAllText(area.SnapshotPath));
            });
        Add(tests, "invalid settings cannot replace existing data", () =>
        {
            using var area = new StorageArea();
            area.Store.SaveSettings(Settings());
            var before = File.ReadAllText(area.SettingsPath);
            Throws<InvalidDataException>(() => area.Store.SaveSettings(Settings() with { Version = 1 }));
            Equal(before, File.ReadAllText(area.SettingsPath));
            area.AssertOnly("settings.json");
        });
        Add(tests, "failed atomic snapshot move leaves no staging file", () =>
        {
            using var area = new StorageArea();
            Directory.CreateDirectory(area.SnapshotPath);
            try
            {
                ThrowsIo(() => area.Store.SaveSnapshot(Parse(Screenshot, Reset)));
                area.AssertOnly("features-snapshot.json");
            }
            finally { Directory.Delete(area.SnapshotPath, recursive: false); }
        });
        Add(tests, "failed atomic settings move leaves no staging file", () =>
        {
            using var area = new StorageArea();
            Directory.CreateDirectory(area.SettingsPath);
            try
            {
                ThrowsIo(() => area.Store.SaveSettings(Settings()));
                area.AssertOnly("settings.json");
            }
            finally { Directory.Delete(area.SettingsPath, recursive: false); }
        });
        Add(tests, "write failure preserves previous snapshot and sentinel", () =>
        {
            using var area = new StorageArea();
            area.Store.SaveSnapshot(Parse(Screenshot, Reset));
            File.WriteAllText(area.SentinelPath, "keep");
            var before = File.ReadAllText(area.SnapshotPath);
            using (var locked = new FileStream(area.SnapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                ThrowsIo(() => area.Store.SaveSnapshot(Parse("Usage this cycle 1 / 100 AI credits")));
            Equal(before, File.ReadAllText(area.SnapshotPath));
            Equal("keep", File.ReadAllText(area.SentinelPath));
            area.AssertOnly("features-snapshot.json", "sentinel.txt");
        });
        Add(tests, "oversized local file is rejected without reading its contents", () =>
        {
            using var area = new StorageArea();
            using (var stream = new FileStream(area.SnapshotPath, FileMode.CreateNew))
                stream.SetLength(16 * 1024 * 1024 + 1);
            Throws<InvalidDataException>(() => area.Store.LoadSnapshot());
            Equal(16 * 1024 * 1024 + 1L, new FileInfo(area.SnapshotPath).Length);
        });
        Add(tests, "malformed cache error never includes artificial secret or page body", () =>
        {
            using var area = new StorageArea();
            File.WriteAllText(area.SnapshotPath, $"{{\"{Token}\":\"{PrivateBody}\"");
            var error = Throws<JsonException>(() => area.Store.LoadSnapshot());
            Check(!error.ToString().Contains(Token, StringComparison.Ordinal), "Secret leaked in cache error.");
            Check(!error.ToString().Contains(PrivateBody, StringComparison.Ordinal), "Page body leaked in cache error.");
        });
    }

    private static void AddRefreshTests(List<(string Name, Action Run)> tests)
    {
        Add(tests, "refresh permits initial automatic and manual attempt", () =>
        {
            var policy = new RefreshPolicy();
            Check(policy.CanStart(Now, false), "Initial automatic attempt blocked.");
            Check(policy.CanStart(Now, true), "Initial manual attempt blocked.");
            Equal(TimeSpan.FromMinutes(5), RefreshPolicy.Interval);
        });
        Add(tests, "refresh success uses five minutes and manual cooldown", () =>
        {
            var policy = new RefreshPolicy();
            policy.Success(Now);
            Equal(Now.AddSeconds(15), policy.NotBefore);
            Equal(Now.AddMinutes(5), policy.NextAttempt);
            Check(!policy.CanStart(Now.AddSeconds(15).AddTicks(-1), true), "Manual cooldown bypassed.");
            Check(policy.CanStart(Now.AddSeconds(15), true), "Manual attempt blocked after cooldown.");
            Check(!policy.CanStart(Now.AddMinutes(5).AddTicks(-1), false), "Automatic interval bypassed.");
            Check(policy.CanStart(Now.AddMinutes(5), false), "Automatic attempt blocked after interval.");
        });
        Add(tests, "refresh backs off exponentially and caps at thirty minutes", () =>
        {
            var policy = new RefreshPolicy();
            var delays = new[] { 30, 60, 120, 240, 480, 960, 1800, 1800, 1800, 1800, 1800, 1800 };
            for (var index = 0; index < delays.Length; index++)
            {
                policy.Fail(new UsageException("synthetic network failure", true), Now);
                Equal(Math.Min(index + 1, 10), policy.Failures);
                Equal(Now.AddSeconds(delays[index]), policy.NotBefore);
                Equal(policy.NotBefore, policy.NextAttempt);
                Check(!policy.AuthenticationRequired, "Retryable error required authentication.");
                Check(!policy.CanStart(policy.NotBefore.AddTicks(-1), true), "Manual backoff bypassed.");
                Check(policy.CanStart(policy.NotBefore, false), "Automatic attempt blocked after backoff.");
            }
        });
        Add(tests, "refresh respects RetryAt beyond exponential cap", () =>
        {
            var policy = new RefreshPolicy();
            policy.Fail(new UsageException("synthetic rate limit", true, Now.AddHours(3)), Now);
            Equal(Now.AddHours(3), policy.NotBefore);
            Check(!policy.CanStart(Now.AddHours(3).AddTicks(-1), true), "RetryAt bypassed.");
            Check(policy.CanStart(Now.AddHours(3), false), "Attempt blocked after RetryAt.");
        });
        Add(tests, "refresh short RetryAt cannot reduce exponential delay", () =>
        {
            var policy = new RefreshPolicy();
            policy.Fail(new UsageException("synthetic rate limit", true, Now.AddSeconds(1)), Now);
            Equal(Now.AddSeconds(30), policy.NotBefore);
        });
        Add(tests, "refresh nonretryable failure blocks until explicit reset", () =>
        {
            var policy = new RefreshPolicy();
            policy.Fail(new UsageException("synthetic sign-in failure"), Now);
            Check(policy.AuthenticationRequired, "Missing authentication block.");
            Check(!policy.CanStart(Now.AddYears(1), true), "Manual refresh escaped authentication block.");
            Check(!policy.CanStart(Now.AddYears(1), false), "Automatic refresh escaped authentication block.");
            policy.Reset();
            Equal(0, policy.Failures);
            Check(!policy.AuthenticationRequired, "Reset retained authentication block.");
            Equal(DateTimeOffset.MinValue, policy.NotBefore);
            Equal(DateTimeOffset.MinValue, policy.NextAttempt);
            Check(policy.CanStart(Now, false), "Reset blocked immediate attempt.");
        });
        Add(tests, "refresh success clears failures and restores initial backoff", () =>
        {
            var policy = new RefreshPolicy();
            policy.Fail(new UsageException("synthetic failure", true), Now);
            policy.Fail(new UsageException("synthetic failure", true), Now);
            var recovered = Now.AddMinutes(2);
            policy.Success(recovered);
            Equal(0, policy.Failures);
            Check(!policy.AuthenticationRequired, "Success retained authentication block.");
            Equal(recovered.AddSeconds(15), policy.NotBefore);
            Equal(recovered.AddMinutes(5), policy.NextAttempt);
            policy.Fail(new UsageException("synthetic failure", true), recovered);
            Equal(recovered.AddSeconds(30), policy.NotBefore);
        });
        Add(tests, "usage exception retains exact retry contract", () =>
        {
            var error = new UsageException("synthetic", true, Now.AddHours(1));
            Equal("synthetic", error.Message);
            Check(error.CanRetry, "CanRetry lost.");
            Equal<DateTimeOffset?>(Now.AddHours(1), error.RetryAt);
            Check(!new UsageException("synthetic").CanRetry, "Default failure must not auto-retry.");
        });
    }

    private static UsageSnapshot Parse(string? usage, string? reset = "") =>
        CopilotPageParser.Parse(new(Account, usage, reset), Now);
    private static AppSettings Settings() => new() { Account = Account };
    private static UsageSummary Calculate(UsageSnapshot? snapshot) => UsageCalculator.Calculate(snapshot, Settings(), Now);
    private static void Add(List<(string Name, Action Run)> tests, string name, Action run) => tests.Add((name, run));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected <{expected}>; actual <{actual}>.");
    }
    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        Check(expected.SequenceEqual(actual), "Sequences did not match.");
    private static TException Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
    private static void ThrowsData(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or JsonException) { return; }
        throw new InvalidOperationException("Expected a data validation failure.");
    }
    private static void ThrowsIo(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
        throw new InvalidOperationException("Expected a file write failure.");
    }
    private static void Safe(UsageException error)
    {
        Check(!string.IsNullOrWhiteSpace(error.Message), "Failure needs an explanation.");
        Check(!error.CanRetry, "Invalid page format must not auto-retry.");
        Check(error.InnerException is null, "Parser must not retain the raw input through inner exceptions.");
        Check(!error.ToString().Contains(Token, StringComparison.Ordinal), "Secret leaked into error.");
        Check(!error.ToString().Contains(PrivateBody, StringComparison.Ordinal), "Page body leaked into error.");
    }
    private static void Explained(UsageSummary summary) =>
        Check(!string.IsNullOrWhiteSpace(summary.Explanation), "Unknown capacity needs an explanation.");
    private static void Unknown(UsageSummary summary)
    {
        Equal<decimal?>(null, summary.Used);
        Equal<decimal?>(null, summary.Remaining);
        Equal<decimal?>(null, summary.Percent);
        Explained(summary);
    }
    private static void WithCulture(string name, Action action)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            action();
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    private sealed class StorageArea : IDisposable
    {
        private readonly string directory = Path.Combine(Environment.CurrentDirectory,
            $".copilot-usage-regression-{Guid.NewGuid():N}");
        public LocalStore Store { get; }
        public string SettingsPath => Path.Combine(directory, "settings.json");
        public string SnapshotPath => Path.Combine(directory, "features-snapshot.json");
        public string LegacySnapshotPath => Path.Combine(directory, "snapshot.json");
        public string SentinelPath => Path.Combine(directory, "sentinel.txt");
        public StorageArea()
        {
            Directory.CreateDirectory(directory);
            Store = new LocalStore(directory);
        }
        public void AssertOnly(params string[] expected)
        {
            var actual = Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal);
            SequenceEqual(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
        }
        public void Dispose()
        {
            // Delete only fixture-owned names; unexpected staging files deliberately fail cleanup.
            File.Delete(SettingsPath);
            File.Delete(SnapshotPath);
            File.Delete(LegacySnapshotPath);
            File.Delete(SentinelPath);
            Directory.Delete(directory, recursive: false);
        }
    }
}
