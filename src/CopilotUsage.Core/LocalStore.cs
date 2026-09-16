using System.Text.Json;

namespace CopilotUsage.Core;

public sealed class LocalStore(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public bool LegacySettingsDetected { get; private set; }

    public AppSettings LoadSettings()
    {
        using var document = Read<JsonDocument>("settings.json");
        if (document is null)
        {
            LegacySettingsDetected = false;
            return new AppSettings();
        }
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var value))
            throw new InvalidDataException("本地设置缺少有效版本，未加载。");
        if (value == 1)
        {
            LegacySettingsDetected = true;
            return new AppSettings { Account = null };
        }
        var settings = document.RootElement.Deserialize<AppSettings>(AppJson.Options)
            ?? throw new InvalidDataException("本地设置无效，未加载。");
        settings.Validate();
        LegacySettingsDetected = false;
        return settings;
    }

    public UsageSnapshot? LoadSnapshot()
    {
        var snapshot = Read<UsageSnapshot>("features-snapshot.json");
        if (snapshot is null) return null;
        snapshot.Validate();
        return snapshot;
    }

    public void SaveSettings(AppSettings settings)
    {
        settings.Validate();
        Write("settings.json", settings);
        LegacySettingsDetected = false;
    }

    public void SaveSnapshot(UsageSnapshot snapshot)
    {
        snapshot.Validate();
        Write("features-snapshot.json", snapshot);
    }
    public void DeleteSnapshot() => File.Delete(Path.Combine(DirectoryPath, "features-snapshot.json"));

    private T? Read<T>(string name)
    {
        var path = Path.Combine(DirectoryPath, name);
        FileStream stream;
        try { stream = File.OpenRead(path); }
        catch (FileNotFoundException) { return default; }
        catch (DirectoryNotFoundException) { return default; }
        using var ownedStream = stream;
        if (stream.Length > 16 * 1024 * 1024)
            throw new InvalidDataException("本地数据文件过大，未加载。");
        try
        {
            return JsonSerializer.Deserialize<T>(stream, AppJson.Options)
                   ?? throw new InvalidDataException("本地数据文件为空，未加载。");
        }
        catch (JsonException)
        {
            throw new JsonException("本地数据文件格式损坏或缺少必要字段，未加载。");
        }
    }

    private void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(DirectoryPath);
        var destination = Path.Combine(DirectoryPath, name);
        var temporary = Path.Combine(DirectoryPath, $".{name}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, AppJson.Options);
                stream.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
