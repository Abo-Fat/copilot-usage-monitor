using CopilotUsage.Core;

namespace CopilotUsage.Tray;

internal sealed class SettingsForm : Form
{
    private readonly Label account = new() { AutoSize = true };
    private readonly Label message = new() { AutoSize = true, MaximumSize = new Size(630, 0) };
    private readonly CheckBox startup = new() { AutoSize = true, Text = "登录 Windows 后启动（仅当前用户，可随时关闭）" };
    private readonly Button connect = new() { AutoSize = true, Text = "登录 GitHub / 重新读取" };
    private readonly Button disconnect = new() { AutoSize = true, Text = "退出此应用的 GitHub 登录" };
    private readonly Button save = new() { AutoSize = true, Text = "保存设置" };
    private readonly CancellationTokenSource lifetime = new();
    private readonly GitHubUsageBrowser browser;
    private bool busy;
    private bool disposed;

    public AppSettings Result { get; private set; }
    public UsageSnapshot? ConnectedSnapshot { get; private set; }
    public bool DisconnectRequested { get; private set; }
    public bool StartWithWindows => startup.Checked;

    public SettingsForm(AppSettings settings, UsageSnapshot? snapshot, bool autoStart, GitHubUsageBrowser browser)
    {
        this.browser = browser;
        Result = settings;
        Text = "Copilot Usage · 登录与设置";
        Name = "UsageSettings";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(690, 440);
        MinimumSize = new Size(660, 420);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(20)
        };
        layout.Controls.Add(Label("读取 GitHub Settings → Copilot → Features → Usage"));
        layout.Controls.Add(Label("不再使用 Billing PAT、产品/SKU 汇总或手动总额度。请在独立的 GitHub 网页中登录。"));
        layout.Controls.Add(account);
        layout.Controls.Add(connect);
        layout.Controls.Add(disconnect);
        layout.Controls.Add(message);
        layout.Controls.Add(Label("网页登录会话仅保存在此应用的 WebView2 配置中，不导入其他浏览器的 Cookie。\n每 5 分钟刷新；登录过期时保留旧快照并提示重新登录。"));
        layout.Controls.Add(startup);
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        actions.Controls.Add(save);
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions);
        Controls.Add(layout);
        CancelButton = cancel;
        startup.Checked = autoStart;
        account.Text = $"当前账号：{settings.Account ?? "未连接"}";
        message.Text = snapshot is null ? "点击登录，在 Features 页面读取用量后保存。" :
            $"最近读取：{UsageCalculator.Number(snapshot.Used)} / {UsageCalculator.Number(snapshot.Limit)} AI credits";
        connect.Click += async (_, _) => await ConnectAsync();
        disconnect.Click += async (_, _) => await DisconnectAsync();
        save.Click += (_, _) =>
        {
            if (busy) return;
            DialogResult = DialogResult.OK;
            Close();
        };
        FormClosing += (_, e) =>
        {
            if (busy)
            {
                e.Cancel = true;
                message.Text = "请先关闭登录窗口或等待当前操作结束。";
            }
            else lifetime.Cancel();
        };
    }

    private static Label Label(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(630, 0), Margin = new Padding(0, 8, 0, 10)
    };

    private void SetBusy(bool value)
    {
        busy = value;
        connect.Enabled = disconnect.Enabled = save.Enabled = !value;
    }

    private async Task ConnectAsync()
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            var snapshot = await browser.ConnectAsync(this, lifetime.Token);
            if (snapshot is null)
            {
                message.Text = "未读取用量；原账号设置未更改。";
                return;
            }
            ConnectedSnapshot = snapshot;
            Result = new AppSettings { Account = snapshot.Account };
            account.Text = $"已读取账号：{snapshot.Account}";
            message.Text = $"{UsageCalculator.Number(snapshot.Used)} / {UsageCalculator.Number(snapshot.Limit)} AI credits\n" +
                $"重置说明：{snapshot.ResetText}\n点击“保存设置”后开始监控。";
        }
        catch (UsageException ex) { message.Text = ex.Message; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    private async Task DisconnectAsync()
    {
        if (busy) return;
        if (MessageBox.Show(this, "清除此应用独立浏览器的登录信息并移除用量缓存？不会退出你的其他浏览器。",
            "退出 GitHub 登录", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        SetBusy(true);
        try
        {
            await browser.ClearSessionAsync(lifetime.Token);
            Result = new AppSettings();
            ConnectedSnapshot = null;
            DisconnectRequested = true;
            SetBusy(false);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (UsageException ex) { message.Text = ex.Message; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
