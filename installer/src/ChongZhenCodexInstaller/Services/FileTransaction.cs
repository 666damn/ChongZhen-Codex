using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller.Services;

public sealed class FileTransaction : IAsyncDisposable
{
    private readonly List<Func<Task>> rollback = [];
    private bool committed;

    public async Task<OriginalFileState> BackupAsync(string target, string backup, CancellationToken cancellationToken)
    {
        if (!File.Exists(target))
        {
            rollback.Add(() =>
            {
                if (File.Exists(target)) File.Delete(target);
                return Task.CompletedTask;
            });
            return new(false, null, null);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        await CopyAndFlushAsync(target, backup, cancellationToken);
        var sourceHash = Hashing.Sha256(target);
        if (!string.Equals(sourceHash, Hashing.Sha256(backup), StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Backup verification failed: {target}");
        rollback.Add(async () => await ReplaceAsync(backup, target, CancellationToken.None));
        return new(true, sourceHash, Path.GetFullPath(backup));
    }

    public async Task ReplaceAsync(string source, string target, CancellationToken cancellationToken)
    {
        var targetDirectory = Path.GetDirectoryName(target) ?? throw new ArgumentException("Target has no directory.", nameof(target));
        Directory.CreateDirectory(targetDirectory);
        var temporary = Path.Combine(targetDirectory, $".{Path.GetFileName(target)}.codex-installing-{Guid.NewGuid():N}");
        try
        {
            await CopyAndFlushAsync(source, temporary, cancellationToken);
            if (!string.Equals(Hashing.Sha256(source), Hashing.Sha256(temporary), StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Temporary file verification failed: {target}");
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Commit() => committed = true;

    public async ValueTask DisposeAsync()
    {
        if (committed) return;
        List<Exception>? errors = null;
        for (var index = rollback.Count - 1; index >= 0; index--)
        {
            try { await rollback[index](); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (errors is { Count: > 0 }) throw new AggregateException("File transaction rollback failed.", errors);
    }

    private static async Task CopyAndFlushAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(true);
    }
}
