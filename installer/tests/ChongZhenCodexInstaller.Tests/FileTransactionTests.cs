using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class FileTransactionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-transaction-{Guid.NewGuid():N}");

    [Fact]
    public async Task RollsBackExactOriginalBytesWhenNotCommitted()
    {
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "target.bin");
        var backup = Path.Combine(root, "backup.bin");
        var replacement = Path.Combine(root, "replacement.bin");
        await File.WriteAllBytesAsync(target, [1, 2, 3]);
        await File.WriteAllBytesAsync(replacement, [9, 8]);

        await using (var transaction = new FileTransaction())
        {
            await transaction.BackupAsync(target, backup, CancellationToken.None);
            await transaction.ReplaceAsync(replacement, target, CancellationToken.None);
        }

        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(target));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
