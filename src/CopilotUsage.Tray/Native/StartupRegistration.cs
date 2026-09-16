using Microsoft.Win32;

namespace CopilotUsage.Tray.Native;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CopilotUsage";

    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command
                && !string.IsNullOrWhiteSpace(command);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using RegistryKey? existingKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            existingKey?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) ||
            string.Equals(Path.GetFileName(executablePath), "dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Startup registration requires running the app's executable directly.");
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, $"\"{executablePath}\" --background", RegistryValueKind.String);
    }
}
