namespace InstituteHub.Application.Abstractions;

/// <summary>
/// Uploaded files such as institute logos (design doc 7: object storage). The local implementation writes under
/// Storage:LocalRoot (a Docker volume in production); an Azure Blob / S3 implementation can replace it later.
/// Paths are storage keys like "logos/{tenantId}/logo-….png", never user-supplied file names.
/// </summary>
public interface IFileStorage
{
    Task<string> SaveAsync(string key, Stream content, CancellationToken ct = default);

    /// <summary>The file's bytes, or null when it does not exist.</summary>
    Task<byte[]?> ReadAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}
