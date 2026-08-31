using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace ChongZhenCodexInstaller.Services;

public interface IGameDebugSession : IAsyncDisposable
{
    int Port { get; }
}

public interface IGameDebugSessionFactory
{
    Task<IGameDebugSession> StartAsync(string gamePath, CancellationToken cancellationToken);
}

public interface ICommandRunner
{
    Task RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken);
}

public sealed class GameByokService(
    BridgeRuntimePaths paths,
    IGameDebugSessionFactory sessions,
    ICommandRunner commands)
{
    public async Task ConfigureAsync(string gamePath, bool install, CancellationToken cancellationToken)
    {
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(paths.ConfigPath, cancellationToken));
        var token = config.RootElement.GetProperty("token").GetString();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidDataException("Bridge configuration has no local identifier.");
        await using var session = await sessions.StartAsync(gamePath, cancellationToken);
        var arguments = new[]
        {
            paths.ConfigureGameByokPath,
            "--port", session.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--action", install ? "install" : "remove",
        };
        await commands.RunAsync(paths.NodePath, arguments,
            new Dictionary<string, string> { ["CHONGZHEN_BRIDGE_TOKEN"] = token }, cancellationToken);
    }
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start the BYOK configurator.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var errorText = await stderr;
        _ = await stdout;
        if (process.ExitCode != 0) throw new InvalidOperationException($"Game BYOK configuration failed: {errorText.Trim()}");
    }
}

public sealed class GameDebugSessionFactory : IGameDebugSessionFactory
{
    public async Task<IGameDebugSession> StartAsync(string gamePath, CancellationToken cancellationToken)
    {
        var port = ReservePort();
        var executable = Path.Combine(Path.GetFullPath(gamePath), "ChongZhenSimulator.exe");
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = gamePath,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Minimized,
        };
        start.ArgumentList.Add($"--remote-debugging-port={port}");
        var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start a temporary game configuration session.");
        var session = new ProcessGameDebugSession(process, port);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var response = await client.GetAsync($"http://127.0.0.1:{port}/json/list", cancellationToken);
                    if (response.IsSuccessStatusCode) return session;
                }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
                {
                }
                await Task.Delay(250, cancellationToken);
            }
            throw new TimeoutException("Game renderer debugging endpoint did not become ready within 30 seconds.");
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private sealed class ProcessGameDebugSession(Process process, int port) : IGameDebugSession
    {
        public int Port => port;
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync();
            }
            catch (InvalidOperationException)
            {
            }
            finally { process.Dispose(); }
        }
    }
}
