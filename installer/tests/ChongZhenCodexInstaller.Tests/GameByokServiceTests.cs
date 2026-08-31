using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class GameByokServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-byok-service-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(true, "install")]
    [InlineData(false, "remove")]
    public async Task RunsMemoryOnlyConfiguratorAgainstTemporaryGameDebugSession(bool install, string expectedAction)
    {
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, "config.json");
        await File.WriteAllTextAsync(config, $"{{\"port\":43129,\"token\":\"czb_{new string('c', 64)}\"}}");
        var paths = new BridgeRuntimePaths(root, "node.exe", "main.js", "configure.mjs", config, "installer.exe");
        var sessions = new FakeDebugSessionFactory();
        var commands = new FakeCommandRunner();
        var service = new GameByokService(paths, sessions, commands);

        await service.ConfigureAsync(@"D:\Game", install, CancellationToken.None);

        Assert.Equal(@"D:\Game", sessions.GamePath);
        Assert.Contains("--action", commands.Arguments);
        Assert.Contains(expectedAction, commands.Arguments);
        Assert.DoesNotContain(commands.Arguments, argument => argument.Contains("czb_", StringComparison.Ordinal));
        Assert.Equal($"czb_{new string('c', 64)}", commands.Environment["CHONGZHEN_BRIDGE_TOKEN"]);
        Assert.True(sessions.Disposed);
    }

    private sealed class FakeDebugSessionFactory : IGameDebugSessionFactory
    {
        public string? GamePath { get; private set; }
        public bool Disposed { get; private set; }
        public Task<IGameDebugSession> StartAsync(string gamePath, CancellationToken cancellationToken)
        {
            GamePath = gamePath;
            return Task.FromResult<IGameDebugSession>(new FakeSession(9222, () => Disposed = true));
        }
    }

    private sealed class FakeSession(int port, Action dispose) : IGameDebugSession
    {
        public int Port => port;
        public ValueTask DisposeAsync() { dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class FakeCommandRunner : ICommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public IReadOnlyDictionary<string, string> Environment { get; private set; } = new Dictionary<string, string>();
        public Task RunAsync(string executable, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
        {
            Arguments = arguments;
            Environment = environment;
            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
