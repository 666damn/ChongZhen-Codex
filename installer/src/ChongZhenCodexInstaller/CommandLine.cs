using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller;

public sealed record CommandLineOptions(AppCommand Command);

public static class CommandLine
{
    private static readonly IReadOnlyDictionary<string, AppCommand> Commands =
        new Dictionary<string, AppCommand>(StringComparer.OrdinalIgnoreCase)
        {
            ["--watch"] = AppCommand.Watch,
            ["--status"] = AppCommand.Status,
            ["--install-bridge"] = AppCommand.InstallBridge,
            ["--install-global"] = AppCommand.InstallGlobal,
            ["--uninstall"] = AppCommand.Uninstall,
        };

    public static CommandLineOptions Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0) return new(AppCommand.UserInterface);
        if (arguments.Count == 1 && Commands.TryGetValue(arguments[0], out var command)) return new(command);
        throw new ArgumentException($"Unsupported command line: {string.Join(' ', arguments)}");
    }
}
