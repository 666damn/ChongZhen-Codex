using Microsoft.Win32;

namespace ChongZhenCodexInstaller.Services;

public interface IStartupRegistry
{
    void Set(string name, string value);
    void Delete(string name);
}

public sealed class WindowsStartupRegistry : IStartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue(name, false);
    }
}

public sealed class StartupService(IStartupRegistry registry)
{
    public const string ValueName = "ChongZhenCodexBridge";

    public void Enable(string installedExecutable) =>
        registry.Set(ValueName, $"\"{Path.GetFullPath(installedExecutable)}\" --watch");

    public void Disable() => registry.Delete(ValueName);
}
