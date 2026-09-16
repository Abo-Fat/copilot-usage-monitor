namespace CopilotUsage.Tray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length > 0 && args[0] == "--icon-smoke-test")
            return Native.IconSmokeTest.Run(args.Length > 1 ? args[1] : null);
        var smoke = args.Contains("--smoke-test", StringComparer.Ordinal);
        var suffix = smoke ? $"Smoke.{Environment.ProcessId}" : Environment.UserName;
        using var mutex = new Mutex(true, $@"Local\CopilotUsage.Tray.{suffix}", out var first);
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\CopilotUsage.Activate.{suffix}");
        if (!first)
        {
            activation.Set();
            return 0;
        }
        try
        {
            using var context = new TrayApplicationContext(activation, smoke);
            context.Start(args.Contains("--background", StringComparer.Ordinal));
            Application.Run(context);
            return context.ExitCode;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
