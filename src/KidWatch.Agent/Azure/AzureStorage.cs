using Azure.Storage.Blobs;

namespace KidWatch.Agent.Azure;

/// <summary>
/// Lazily builds a <see cref="BlobServiceClient"/> from the on-device credential
/// (connection string or account-SAS) and hands out container clients. The
/// credential is read from the bootstrap secret file.
/// </summary>
public sealed class AzureStorage(AgentOptions options)
{
    private readonly Lock _gate = new();
    private BlobServiceClient? _service;

    public BlobContainerClient Container(string device) =>
        Service().GetBlobContainerClient(device);

    private BlobServiceClient Service()
    {
        lock (_gate)
        {
            return _service ??= new BlobServiceClient(ReadCredential());
        }
    }

    /// <summary>
    /// Reads the storage credential. v0 accepts a plaintext connection string or
    /// SAS. Hardening TODO: DPAPI-decrypt (ProtectedData.Unprotect) when the file
    /// is machine-encrypted at install time.
    /// </summary>
    private string ReadCredential()
    {
        var path = options.ResolveSecretFile();
        if (!File.Exists(path))
            throw new FileNotFoundException($"storage credential not found: {path}");
        return File.ReadAllText(path).Trim();
    }
}
