using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Text.Json;
using CopilotUsage.Core;
using CopilotUsage.Tray.Native;
using Microsoft.Win32;

namespace CopilotUsage.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private readonly DetailsForm details;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly EventWaitHandle activation;
    private readonly LocalStore store;
    private readonly GitHubUsageBrowser browser;
    private readonly RefreshPolicy policy = new();
    private readonly bool smoke;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? currentRequest;
    private AppSettings settings = new();
    private UsageSnapshot? snapshot;
    private Icon? currentIcon;
    private (BatteryIndicator Indicator, int Size, IconPalette Palette)? iconState;
    private IconPalette iconPalette = new(SystemColors.WindowText, SystemColors.Window);
    private string state = "尚未连接账号";
    private bool busy;
    private bool cached;
    private bool failed;
    private bool exiting;
    private bool disposed;
    private bool settingsOpen;
    private int generation;
    private int smokeTicks;
    private SettingsForm? smokeSettings;
    private bool smokeRunning;
    private string? startupError;
    private bool settingsLoadFailed;
    public int ExitCode { get; private set; }

    public TrayApplicationContext(EventWaitHandle activationEvent, bool smokeTest)
    {
        activation = activationEvent;
        smoke = smokeTest;
        store = new LocalStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopilotUsage"));
        var profile = smoke
            ? Path.Combine(Path.GetTempPath(), $"CopilotUsage.Smoke.{Environment.ProcessId}")
            : Path.Combine(store.DirectoryPath, "GitHubWebView2");
        browser = new GitHubUsageBrowser(profile, smoke);
        details = new DetailsForm(() => _ = RefreshAsync(true), ShowSettings, OpenUsage);
        _ = details.Handle;
        menu = new ContextMenuStrip();
        menu.Items.Add("查看用量", null, (_, _) => ShowDetails());
        menu.Items.Add("立即刷新", null, async (_, _) => await RefreshAsync(true));
        menu.Items.Add("登录与设置", null, (_, _) => ShowSettings());
        menu.Items.Add("打开 GitHub 用量页", null, (_, _) => OpenUsage());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Text = "Copilot Usage", ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowDetails(); };
        timer.Tick += async (_, _) =>
        {
            if (smoke)
            {
                await RunSmokeStepAsync();
                return;
            }
            if (activation.WaitOne(0)) ShowDetails();
            Render();
            await RefreshAsync(false);
        };
        if (!smoke)
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            LoadSavedState();
            UpdateIconPalette();
        }
    }

    public void Start(bool background)
    {
        Render();
        tray.Visible = true;
        timer.Start();
        if (smoke || !background || startupError is not null) ShowDetails();
        if (startupError is not null)
            details.BeginInvoke(() => MessageBox.Show(details, startupError, "本地数据加载失败", MessageBoxButtons.OK, MessageBoxIcon.Warning));
    }

    private void LoadSavedState()
    {
        try { settings = store.LoadSettings(); }
        catch (Exception ex) when (IsStorageError(ex))
        {
            startupError = $"设置未加载：{ex.Message}\n文件目录：{store.DirectoryPath}\n未覆盖原文件。";
            state = "本地设置损坏或不可读取，请修复后重新启动。";
            settingsLoadFailed = true;
            failed = true;
            return;
        }
        try
        {
            snapshot = store.LoadSnapshot();
            if (snapshot is not null && !string.Equals(snapshot.Account, settings.Account, StringComparison.OrdinalIgnoreCase))
                snapshot = null;
            cached = snapshot is not null;
            state = settings.Account is null ? "尚未连接账号，请打开设置" : "正在等待同步；已有数据仅为本地缓存";
            if (store.LegacySettingsDetected)
                state = "已停用旧 Billing 数据，请在“登录与设置”中登录 GitHub；旧 PAT 和手动额度不会用于监控。";
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            startupError = $"缓存未加载：{ex.Message}\n将重新从 GitHub 获取。";
            state = "缓存不可读取，等待重新同步";
            failed = true;
        }
    }

    private async Task RefreshAsync(bool manual)
    {
        if (smoke || exiting || busy || settingsOpen || settings.Account is null) return;
        var now = DateTimeOffset.UtcNow;
        if (!policy.CanStart(now, manual))
        {
            if (manual)
            {
                state = policy.AuthenticationRequired
                    ? "请在“登录与设置”中重新登录或读取用量，当前错误不适合自动重试。"
                    : $"请等待至 {policy.NotBefore.ToLocalTime():HH:mm:ss} 后刷新（避免重复请求或限流）。";
                Render();
            }
            return;
        }
        busy = true;
        state = "正在读取 Copilot Features → Usage…";
        Render();
        var version = generation;
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        currentRequest = requestLifetime;
        try
        {
            var result = await browser.FetchAsync(settings.Account, requestLifetime.Token);
            if (exiting || version != generation) return;
            snapshot = result;
            cached = failed = false;
            policy.Success(DateTimeOffset.UtcNow);
            state = "网页用量同步成功 · 每 5 分钟更新";
            try { store.SaveSnapshot(result); }
            catch (Exception ex) when (IsStorageError(ex))
            {
                state = $"已获取新数据，但无法保存缓存：{ex.Message}";
                failed = true;
            }
        }
        catch (UsageException ex)
        {
            if (version != generation || exiting) return;
            failed = true;
            state = ex.Message;
            policy.Fail(ex, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (requestLifetime.IsCancellationRequested) { }
        finally
        {
            currentRequest = null;
            busy = false;
            if (!exiting) Render();
        }
    }

    private void Render()
    {
        if (exiting) return;
        var now = DateTimeOffset.UtcNow;
        var stale = failed || cached || snapshot is not null &&
            now - snapshot.FetchedAtUtc > TimeSpan.FromMinutes(10);
        var summary = UsageCalculator.Calculate(snapshot, settings, now);
        details.UpdateView(settings, snapshot, state, busy, stale, smoke);
        var nextIcon = (BatteryIndicator.FromUsage(summary, busy, stale), IconFactory.TrayPixelSize(), iconPalette);
        if (iconState != nextIcon)
        {
            var replacement = IconFactory.Create(nextIcon.Item1, nextIcon.Item2, nextIcon.Item3);
            tray.Icon = replacement;
            currentIcon?.Dispose();
            currentIcon = replacement;
            iconState = nextIcon;
        }
        var tooltip = $"Copilot · {(stale ? "过期/错误" : busy ? "同步中" : "用量")}\n" +
            $"已用 {UsageCalculator.Number(summary.Used)} · 剩余 {UsageCalculator.Number(summary.Remaining)}\n" +
            $"获取：{snapshot?.FetchedAtUtc.ToLocalTime().ToString("MM-dd HH:mm:ss") ?? "尚未同步"}";
        tray.Text = tooltip.Length <= 127 ? tooltip : tooltip[..124] + "...";
    }

    private void ShowDetails()
    {
        if (exiting) return;
        Render();
        details.BringUp();
    }

    private void ShowSettings()
    {
        if (smoke || settingsOpen || exiting) return;
        if (settingsLoadFailed)
        {
            MessageBox.Show(details, "请先处理本地设置文件错误，避免覆盖原有配置。", "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        settingsOpen = true;
        try
        {
            generation++;
            currentRequest?.Cancel();
            using var form = new SettingsForm(settings, snapshot, StartupRegistration.IsEnabled, browser);
            if (form.ShowDialog(details) != DialogResult.OK) return;
            generation++;
            currentRequest?.Cancel();
            store.SaveSettings(form.Result);
            var accountChanged = !string.Equals(settings.Account, form.Result.Account, StringComparison.OrdinalIgnoreCase);
            settings = form.Result;
            if (accountChanged || form.DisconnectRequested)
            {
                snapshot = null;
                store.DeleteSnapshot();
            }
            if (form.ConnectedSnapshot is { } connected)
            {
                snapshot = connected;
                store.SaveSnapshot(connected);
                cached = false;
            }
            if (form.ConnectedSnapshot is not null) policy.Success(DateTimeOffset.UtcNow);
            else policy.Reset();
            failed = false;
            state = settings.Account is null ? "已退出，请在设置中登录 GitHub" :
                form.ConnectedSnapshot is not null ? "网页用量已保存 · 每 5 分钟更新" : "设置已保存，等待同步";
            try { StartupRegistration.SetEnabled(form.StartWithWindows); }
            catch (InvalidOperationException ex)
            {
                state = "账号设置已保存，但无法注册开机启动。";
                MessageBox.Show(details, $"{state}\n{ex.Message}", "开机启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex) when (IsStorageError(ex) || ex is Win32Exception)
        {
            state = "设置或开机启动未全部保存，请检查错误后重试。";
            failed = true;
            MessageBox.Show(details, $"{state}\n{ex.Message}", "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            settingsOpen = false;
            Render();
        }
    }

    private void OpenUsage()
    {
        try { Process.Start(new ProcessStartInfo(CopilotPageParser.SourceUrl) { UseShellExecute = true }); }
        catch (Win32Exception ex)
        {
            MessageBox.Show(details, $"无法打开默认浏览器（错误 {ex.NativeErrorCode}）。\n{CopilotPageParser.SourceUrl}",
                "打开用量页", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && !exiting && details.IsHandleCreated)
        {
            try { details.BeginInvoke(async () => await RefreshAsync(true)); }
            catch (InvalidOperationException) when (exiting || details.IsDisposed || !details.IsHandleCreated) { }
        }
    }

    private void UpdateIconPalette()
    {
        try { iconPalette = IconPalette.Read(); }
        catch (Exception ex) when (IsStorageError(ex))
        {
            state = "无法读取任务栏主题，图标暂用系统配色。";
            failed = true;
            MessageBox.Show(details, $"{state}\n{ex.Message}", "托盘图标", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (exiting || !details.IsHandleCreated) return;
        try
        {
            details.BeginInvoke(() =>
            {
                if (exiting) return;
                UpdateIconPalette();
                Render();
            });
        }
        catch (InvalidOperationException) when (exiting || details.IsDisposed || !details.IsHandleCreated) { }
    }

    private async Task RunSmokeStepAsync()
    {
        if (smokeRunning) return;
        smokeTicks++;
        if (smokeTicks == 1)
        {
            settings = new AppSettings { Account = "offline-example" };
            snapshot = CopilotPageParser.Parse(new("offline-example",
                "Usage this cycle 123 / 1,000,000 AI credits Resets in 21 days on Sep 30, 2026",
                "Resets in 21 days on Sep 30, 2026"), DateTimeOffset.UtcNow);
            state = "离线 UI 演示；没有访问账号、凭据、网络或自启动设置";
            Render();
            ShowDetails();
        }
        if (smokeTicks == 2)
        {
            smokeRunning = true;
            try { await browser.RunOfflineSmokeAsync(lifetime.Token); }
            catch (UsageException ex)
            {
                ExitCode = 1;
                Console.Error.WriteLine($"Offline smoke failure: {ex.Message}");
                state = ex.Message;
                Render();
                ExitThread();
                return;
            }
            finally { smokeRunning = false; }
            smokeSettings = new SettingsForm(settings, snapshot, false, browser);
            smokeSettings.Show(details);
            failed = true;
            state = "离线模拟：断网，保留最后成功快照";
            Render();
        }
        if (smokeTicks == 5)
        {
            smokeSettings?.Close();
            smokeSettings?.Dispose();
            smokeSettings = null;
        }
        if (smokeTicks == 6)
        {
            if (!details.Visible || tray.Icon is null || !tray.Visible)
                ExitCode = 1;
            ExitThread();
        }
    }

    private static bool IsStorageError(Exception ex) =>
        ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or SecurityException;

    protected override void ExitThreadCore()
    {
        exiting = true;
        timer.Stop();
        lifetime.Cancel();
        tray.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            exiting = true;
            if (!smoke)
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            }
            lifetime.Cancel();
            timer.Dispose();
            tray.Visible = false;
            tray.Dispose();
            currentIcon?.Dispose();
            menu.Dispose();
            details.Dispose();
            smokeSettings?.Dispose();
            browser.Dispose();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
