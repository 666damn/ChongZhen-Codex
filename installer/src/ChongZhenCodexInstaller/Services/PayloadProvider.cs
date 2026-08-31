using System.Reflection;

namespace ChongZhenCodexInstaller.Services;

public interface IPayloadProvider
{
    Stream OpenRead();
}

public sealed class EmbeddedPayloadProvider(Assembly? assembly = null) : IPayloadProvider
{
    private readonly Assembly assembly = assembly ?? Assembly.GetExecutingAssembly();

    public Stream OpenRead()
    {
        var name = assembly.GetManifestResourceNames()
            .SingleOrDefault(candidate => candidate.EndsWith("ChongZhenCodexPayload.zip", StringComparison.Ordinal));
        return name is null
            ? throw new InvalidOperationException("Embedded installer payload is missing. Rebuild with the validated local payload.")
            : assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Embedded installer payload could not be opened.");
    }
}
