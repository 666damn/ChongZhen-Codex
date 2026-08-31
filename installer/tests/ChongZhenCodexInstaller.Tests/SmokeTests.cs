using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller.Tests;

public sealed class SmokeTests
{
    [Theory]
    [InlineData("--watch", AppCommand.Watch)]
    [InlineData("--status", AppCommand.Status)]
    [InlineData("--install-bridge", AppCommand.InstallBridge)]
    [InlineData("--install-global", AppCommand.InstallGlobal)]
    [InlineData("--uninstall", AppCommand.Uninstall)]
    [InlineData("--audit-payload", AppCommand.AuditPayload)]
    public void ParsesCommand(string argument, AppCommand expected)
    {
        Assert.Equal(expected, CommandLine.Parse([argument]).Command);
    }

    [Fact]
    public void DefaultsToUserInterfaceAndRejectsUnknownArguments()
    {
        Assert.Equal(AppCommand.UserInterface, CommandLine.Parse([]).Command);
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(["--unknown"]));
    }

    [Fact]
    public void InstallStateCarriesOnlyPortableOperationalMetadata()
    {
        var state = new InstallState(
            @"D:\Steam\game",
            InstallMode.BridgeAndGlobal,
            "1.0.0",
            ["bridge", "app.asar"],
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(InstallMode.BridgeAndGlobal, state.Mode);
        Assert.DoesNotContain(state.Files, item => item.Contains("token", StringComparison.OrdinalIgnoreCase));
    }
}
