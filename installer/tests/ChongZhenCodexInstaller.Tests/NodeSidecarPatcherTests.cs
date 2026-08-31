using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class NodeSidecarPatcherTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-node-patcher-{Guid.NewGuid():N}");

    [Fact]
    public async Task PassesSecretThroughEnvironmentAndPathsThroughArgumentList()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "official game file.asar");
        var output = Path.Combine(root, "generated sidecar.asar");
        var script = Path.Combine(root, "fake patcher.mjs");
        var token = $"czb_{new string('c', 64)}";
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        await File.WriteAllTextAsync(script, $$"""
            import { copyFileSync, statSync } from 'node:fs';
            const args = Object.fromEntries(process.argv.slice(2).reduce((all, value, index, values) => {
              if (value.startsWith('--')) all.push([value.slice(2), values[index + 1]]);
              return all;
            }, []));
            const expected = '{{token}}';
            if (process.argv.includes(expected)) process.exit(23);
            if (process.env.CHONGZHEN_BRIDGE_TOKEN !== expected) process.exit(24);
            copyFileSync(args.input, args.output);
            console.log(JSON.stringify({
              sourceHeaderSha256: 'a'.repeat(64),
              sourceLength: statSync(args.input).size,
              sidecarHeaderSha256: 'b'.repeat(64),
              sidecarLength: statSync(args.output).size
            }));
            """);
        var patcher = new NodeSidecarPatcher("node", script);

        var result = await patcher.BuildAsync(source, output, token, CancellationToken.None);

        Assert.Equal(3, result.SourceLength);
        Assert.Equal(3, result.SidecarLength);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(output));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
