using CopilotUsage.Core;

namespace CopilotUsage.Tray;

internal sealed class DetailsForm : Form
{
    private readonly Label title = new() { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 17, FontStyle.Bold) };
    private readonly Label summary = new() { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 12) };
    private readonly Label status = new() { AutoSize = true };
    private readonly Label timestamps = new() { AutoSize = true };
    private readonly Label explanation = new() { AutoSize = true, ForeColor = SystemColors.HotTrack };
    private readonly TextBox pageText = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
        ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window,
        AccessibleName = "Features Usage 卡片原文"
    };
    private readonly Button refreshButton;

    public DetailsForm(Action refresh, Action settings, Action openUsage)
    {
        SuspendLayout();
        Text = "Copilot Usage";
        Name = "UsageDetails";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(950, 610);
        MinimumSize = new Size(720, 510);
        StartPosition = FormStartPosition.Manual;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach (var label in new[] { title, summary, status, timestamps, explanation })
        {
            label.Margin = new Padding(0, 0, 0, 12);
            label.Dock = DockStyle.Fill;
            layout.Controls.Add(label);
        }
        layout.Controls.Add(new Label
        {
            AutoSize = true, Text = "来源：GitHub Settings → Copilot → Features → Usage（网页卡片原文）",
            Margin = new Padding(0, 4, 0, 8)
        });
        layout.Controls.Add(pageText);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
        refreshButton = Button("立即刷新", refresh);
        buttons.Controls.Add(refreshButton);
        buttons.Controls.Add(Button("登录与设置", settings));
        buttons.Controls.Add(Button("打开 GitHub 用量页", openUsage));
        buttons.Controls.Add(Button("隐藏到托盘", Hide));
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        layout.SizeChanged += (_, _) =>
        {
            foreach (var label in new[] { title, summary, status, timestamps, explanation })
                label.MaximumSize = new Size(Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal), 0);
        };
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
        ResumeLayout(true);
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(5), Margin = new Padding(0, 0, 10, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    public void UpdateView(AppSettings settings, UsageSnapshot? snapshot, string state, bool busy, bool stale, bool demo)
    {
        var now = DateTimeOffset.UtcNow;
        var result = UsageCalculator.Calculate(snapshot, settings, now);
        title.Text = demo ? "离线演示 — 不连接 GitHub" : $"Copilot Usage · {settings.Account ?? "尚未连接账号"}";
        summary.Text = $"本周期已用：{UsageCalculator.Number(result.Used)} AI credits    " +
                       $"页面额度：{UsageCalculator.Number(snapshot?.Limit)}\n" +
                       $"剩余（页面额度 − 已用）：{UsageCalculator.Number(result.Remaining)}";
        if (result.Percent is { } percent)
            summary.Text += $"    已用 {percent:0.####}%";
        status.Text = (stale ? "⚠ 数据可能过期 · " : "") + state;
        var fetched = snapshot?.FetchedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "尚未同步";
        var resetDate = snapshot?.ResetDate?.ToString("yyyy-MM-dd") ?? "未知";
        timestamps.Text = $"页面重置日期：{resetDate}（不推断时刻或时区）\n" +
                          $"页面说明：{snapshot?.ResetText ?? "尚未获取"}\n" +
                          $"最后成功获取：{fetched}    GitHub 数据延迟：未知";
        explanation.Text = result.Explanation ??
            "已用量、总额度和重置说明来自网页；剩余和百分比由页面数字计算。不读取 Billing，不设置手动额度。";
        refreshButton.Enabled = !busy && !demo;
        if (!ReferenceEquals(pageText.Tag, snapshot))
        {
            pageText.Tag = snapshot;
            pageText.Text = snapshot?.UsageText ?? "";
        }
    }

    public void BringUp()
    {
        if (!Visible)
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Max(area.Left, area.Right - Width - 16), Math.Max(area.Top, area.Bottom - Height - 16));
            Show();
        }
        WindowState = FormWindowState.Normal;
        Activate();
    }
}
