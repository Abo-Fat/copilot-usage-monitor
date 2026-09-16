using System.Runtime.InteropServices;
using System.Text.Json;
using CopilotUsage.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CopilotUsage.Tray;

internal sealed class GitHubUsageBrowser : Form
{
    private readonly WebView2 web = new() { Dock = DockStyle.Fill, AllowExternalDrop = false };
    private readonly Label message = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Button capture = new() { Text = "读取此账号用量", AutoSize = true };
    private readonly Button features = new() { Text = "返回 Features 用量页", AutoSize = true };
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly string profilePath;
    private readonly bool offline;
    private readonly string extractor;
    private Task? initialization;
    private TaskCompletionSource<UsageSnapshot?>? connection;
    private CancellationTokenSource? connectionLifetime;
    private Task? captureTask;
    private readonly CancellationTokenSource lifetime = new();
    private bool disposing;
    private bool reading;

    public GitHubUsageBrowser(string profilePath, bool offline = false)
    {
        this.profilePath = profilePath;
        this.offline = offline;
        using var stream = typeof(GitHubUsageBrowser).Assembly.GetManifestResourceStream("CopilotUsage.UsageCard.js")
            ?? throw new InvalidOperationException("Missing usage card extractor.");
        using var reader = new StreamReader(stream);
        extractor = reader.ReadToEnd();
        Text = "登录 GitHub · Copilot Features Usage";
        ClientSize = new Size(1100, 780);
        MinimumSize = new Size(780, 580);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            AutoSize = true, Padding = new Padding(10),
            Text = "独立登录会话 · 仅允许 https://github.com\n请直接在 GitHub 网页完成登录；到达 Features 后点击“读取此账号用量”。"
        });
        layout.Controls.Add(web);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10) };
        actions.Controls.Add(features);
        actions.Controls.Add(capture);
        actions.Controls.Add(message);
        layout.Controls.Add(actions);
        Controls.Add(layout);
        features.Click += (_, _) => web.CoreWebView2?.Navigate(CopilotPageParser.SourceUrl);
        capture.Click += async (_, _) =>
        {
            if (reading) return;
            captureTask = CaptureConnectionAsync();
            await captureTask;
        };
        FormClosing += (_, e) =>
        {
            if (!disposing && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                connection?.TrySetResult(null);
                Hide();
            }
        };
    }

    private async Task InitializeAsync()
    {
        if (initialization is null || initialization.IsFaulted)
            initialization = InitializeCoreAsync();
        await initialization;
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            _ = Handle;
            _ = web.Handle;
            var environment = await CoreWebView2Environment.CreateAsync(null, profilePath,
                new CoreWebView2EnvironmentOptions { Language = "en-US" });
            await web.EnsureCoreWebView2Async(environment);
            var core = web.CoreWebView2;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.NavigationStarting += (_, e) =>
            {
                var allowed = e.Uri == "about:blank" || (offline
                    ? e.Uri.StartsWith("data:text/html;", StringComparison.OrdinalIgnoreCase)
                    : IsGitHub(e.Uri));
                if (!allowed)
                {
                    e.Cancel = true;
                    message.Text = "已阻止离开 github.com 的跳转；此窗口不支持外部 SSO 登录。";
                }
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                message.Text = "已阻止弹出新窗口，请在当前 GitHub 页面完成登录。";
            };
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            throw new UsageException("未找到 Microsoft Edge WebView2 Runtime，请安装后重启应用：https://developer.microsoft.com/microsoft-edge/webview2/");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            throw new UsageException("无法初始化独立 GitHub 浏览器。请检查 WebView2 Runtime 及本地数据目录权限。");
        }
    }

    public async Task<UsageSnapshot?> ConnectAsync(IWin32Window owner, CancellationToken cancellationToken)
    {
        await operation.WaitAsync(cancellationToken);
        try
        {
            await InitializeAsync();
            cancellationToken.ThrowIfCancellationRequested();
            connectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            connection = new(TaskCreationOptions.RunContinuationsAsynchronously);
            message.Text = "登录信息仅保存在本应用的独立浏览器配置中。";
            Show(owner);
            Activate();
            web.CoreWebView2.Navigate(CopilotPageParser.SourceUrl);
            return await connection.Task.WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            throw new UsageException("GitHub 登录窗口不可用，请重启应用后重试。");
        }
        finally
        {
            connectionLifetime?.Cancel();
            if (captureTask is not null) await captureTask;
            captureTask = null;
            connectionLifetime?.Dispose();
            connectionLifetime = null;
            connection = null;
            web.CoreWebView2?.Stop();
            Hide();
            Owner = null;
            operation.Release();
        }
    }

    private async Task CaptureConnectionAsync()
    {
        if (reading || connection is null || connectionLifetime is null) return;
        var pending = connection;
        var cancellationToken = connectionLifetime.Token;
        reading = true;
        capture.Enabled = features.Enabled = false;
        message.Text = "正在读取 Usage this cycle…";
        try
        {
            var snapshot = await ReadPageAsync(null, cancellationToken);
            pending.TrySetResult(snapshot);
        }
        catch (UsageException ex) { message.Text = ex.Message; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (InvalidOperationException) { message.Text = "登录页面暂时不可用，请关闭此窗口后重试。"; }
        finally
        {
            reading = false;
            if (!IsDisposed) capture.Enabled = features.Enabled = true;
        }
    }

    public async Task<UsageSnapshot> FetchAsync(string expectedAccount, CancellationToken cancellationToken)
    {
        await operation.WaitAsync(cancellationToken);
        try
        {
            await InitializeAsync();
            await NavigateAsync(() => web.CoreWebView2.Navigate(CopilotPageParser.SourceUrl), cancellationToken);
            return await ReadPageAsync(expectedAccount, cancellationToken);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            throw new UsageException("GitHub 浏览器读取失败，请重新打开登录窗口或重启程序。", true);
        }
        finally
        {
            web.CoreWebView2?.Stop();
            operation.Release();
        }
    }

    public async Task ClearSessionAsync(CancellationToken cancellationToken)
    {
        await operation.WaitAsync(cancellationToken);
        try
        {
            await InitializeAsync();
            await NavigateAsync(() => web.CoreWebView2.Navigate("about:blank"), cancellationToken);
            await web.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            throw new UsageException("无法清除本应用的 GitHub 登录会话，请重启应用后重试。");
        }
        finally { operation.Release(); }
    }

    private async Task NavigateAsync(Action navigate, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ulong? navigationId = null;
        void Starting(object? sender, CoreWebView2NavigationStartingEventArgs args) => navigationId ??= args.NavigationId;
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.NavigationId == navigationId) completion.TrySetResult(args);
        }
        web.CoreWebView2.NavigationStarting += Starting;
        web.CoreWebView2.NavigationCompleted += Completed;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            navigate();
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(45), cancellationToken);
            if (!result.IsSuccess)
                throw new UsageException($"GitHub 网页加载失败（{result.WebErrorStatus}）。", true);
            if (result.HttpStatusCode >= 400)
                throw new UsageException($"GitHub 网页返回 HTTP {result.HttpStatusCode}。",
                    result.HttpStatusCode == 429 || result.HttpStatusCode >= 500);
        }
        catch (TimeoutException) { throw new UsageException("GitHub 网页加载超时。", true); }
        finally
        {
            web.CoreWebView2.NavigationStarting -= Starting;
            web.CoreWebView2.NavigationCompleted -= Completed;
        }
    }

    private async Task<UsageSnapshot> ReadPageAsync(string? expectedAccount, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsFeatures(web.CoreWebView2.Source))
                throw new UsageException("请先在此窗口登录 GitHub，并打开 Copilot → Features 用量页。");
            var result = await ExecuteExtractorAsync(false);
            if (!IsFeatures(web.CoreWebView2.Source))
                throw new UsageException("页面在读取过程中发生跳转，请重新读取。");
            switch (result.Status)
            {
                case "signed-out":
                case "wrong-page":
                    throw new UsageException("GitHub 登录已失效或无法确认账号，请重新登录。");
                case "ambiguous":
                    throw new UsageException("页面存在多个 Usage this cycle 区域，未合并或猜测用量。");
                case "ready":
                    var snapshot = CopilotPageParser.Parse(new(result.Account, result.UsageText, result.ResetText),
                        DateTimeOffset.UtcNow);
                    if (expectedAccount is not null &&
                        !string.Equals(snapshot.Account, expectedAccount, StringComparison.OrdinalIgnoreCase))
                        throw new UsageException("网页账号与已连接账号不同，请在设置中确认切换账号，避免混用缓存。");
                    return snapshot;
                case "loading":
                    break;
                default:
                    throw new UsageException("无法识别 GitHub 用量页面结构，未将缺失数据当作零用量。");
            }
            await Task.Delay(500, cancellationToken);
        } while (DateTimeOffset.UtcNow < deadline);
        throw new UsageException("未找到已加载的 Usage this cycle 卡片。请打开登录窗口查看页面或稍后重新连接。");
    }

    private async Task<PageResult> ExecuteExtractorAsync(bool fixture)
    {
        var guard = fixture ? "" : """
            if (location.origin !== "https://github.com" ||
                location.pathname.replace(/\/$/, "") !== "/settings/copilot/features")
                return {status: "wrong-page"};
            """;
        try
        {
            var json = await web.CoreWebView2.ExecuteScriptAsync($"(() => {{ {guard}\n{extractor}\nreturn readUsageCard(document); }})()");
            return JsonSerializer.Deserialize<PageResult>(json, AppJson.Options)
                ?? throw new UsageException("GitHub 网页未返回可识别的用量数据。");
        }
        catch (JsonException) { throw new UsageException("GitHub 网页用量结构不受支持。"); }
        catch (COMException) { throw new UsageException("浏览器页面暂时无法读取，请重试。", true); }
    }

    private static bool IsGitHub(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host == "github.com" && uri.Port == 443 && uri.UserInfo.Length == 0;

    private static bool IsFeatures(string url) =>
        IsGitHub(url) && new Uri(url).AbsolutePath.TrimEnd('/') == "/settings/copilot/features";

    private sealed record PageResult(string Status, string? Account = null, string? UsageText = null, string? ResetText = null);

    internal async Task RunOfflineSmokeAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync();
        if (!offline) throw new InvalidOperationException("Offline smoke requires an isolated browser.");
        // A separate smoke-test profile and artificial document: never loads GitHub or real credentials.
        await NavigateAsync(() => web.NavigateToString("""
            <!doctype html><html><head><meta name="user-login" content="offline-example"></head>
            <body><h2>Usage</h2><section><div><h3>Usage this cycle</h3>
            <p>Resets in 21 days on Sep 30, 2026</p></div>
            <div><span>123 / 1,000,000 AI credits</span></div></section>
            <aside>Unrelated billing: 999 AI credits used</aside></body></html>
            """), cancellationToken);
        var extracted = await ExecuteExtractorAsync(true);
        if (extracted.Status != "ready")
            throw new UsageException("Offline card extraction failed.");
        var snapshot = CopilotPageParser.Parse(new(extracted.Account, extracted.UsageText, extracted.ResetText), DateTimeOffset.UtcNow);
        if (snapshot.Used != 123 || snapshot.Limit != 1_000_000 || snapshot.ResetDate != new DateOnly(2026, 9, 30))
            throw new UsageException("Offline card values did not match the artificial fixture.");
        if ((await ExecuteExtractorAsync(false)).Status != "wrong-page")
            throw new UsageException("Non-GitHub page was accepted.");

        async Task<PageResult> ReadFixture(string content)
        {
            await NavigateAsync(() => web.NavigateToString("<!doctype html><html><head>" +
                "<meta name=\"user-login\" content=\"offline-example\"></head><body>" +
                content + "</body></html>"), cancellationToken);
            return await ExecuteExtractorAsync(true);
        }

        var unlimited = await ReadFixture("""
            <section><h3>Usage this cycle</h3><span>456 AI credits used</span>
            <p><span>Resets in 21 days</span> on <span>Sep 30, 2026</span></p></section>
            """);
        var unlimitedSnapshot = CopilotPageParser.Parse(
            new(unlimited.Account, unlimited.UsageText, unlimited.ResetText), DateTimeOffset.UtcNow);
        if (unlimitedSnapshot.Used != 456 || unlimitedSnapshot.Limit is not null ||
            unlimitedSnapshot.ResetDate != new DateOnly(2026, 9, 30))
            throw new UsageException("Unbounded quota or nested reset extraction failed.");

        const string card = "<section><h3>Usage this cycle</h3><span>0 / 1,000 AI credits</span></section>";
        var zero = await ReadFixture(card + "<section hidden><h3>Usage this cycle</h3><span>999 AI credits used</span></section>");
        if (zero.Status != "ready" || CopilotPageParser.Parse(
                new(zero.Account, zero.UsageText, zero.ResetText), DateTimeOffset.UtcNow).Used != 0)
            throw new UsageException("Zero usage or hidden card handling failed.");
        if ((await ReadFixture(card + card)).Status != "ambiguous")
            throw new UsageException("Multiple visible quota cards were accepted.");
        if ((await ReadFixture("<section><h3>Billing</h3><span>123 / 1,000,000 AI credits</span></section>")).Status != "loading")
            throw new UsageException("A billing card was accepted as Features Usage.");
        await NavigateAsync(() => web.NavigateToString("<html><body>Sign in to GitHub</body></html>"), cancellationToken);
        if ((await ExecuteExtractorAsync(true)).Status != "signed-out")
            throw new UsageException("Signed-out fixture was accepted.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.disposing)
        {
            this.disposing = true;
            lifetime.Cancel();
            connection?.TrySetResult(null);
            web.Dispose();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
