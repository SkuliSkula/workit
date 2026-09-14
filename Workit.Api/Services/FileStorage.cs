using Amazon.S3;
using Amazon.S3.Model;

namespace Workit.Api.Services;

/// <summary>A stored file's bytes and content type, as read back from storage.</summary>
public sealed record StoredFile(Stream Content, string ContentType, long Length);

/// <summary>
/// Abstraction over binary file storage (job attachments). Keys are opaque,
/// server-generated paths; callers never construct them from user input.
/// </summary>
public interface IFileStorageService
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task<StoredFile?> GetAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>R2/S3 endpoint, e.g. https://&lt;accountid&gt;.r2.cloudflarestorage.com</summary>
    public string? ServiceUrl { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? BucketName { get; set; }

    /// <summary>Local directory used by the dev fallback when R2 is not configured.</summary>
    public string LocalPath { get; set; } = "AttachmentStore";

    public bool IsR2Configured => MissingR2Settings.Count == 0;

    /// <summary>
    /// Names of the R2 settings that are absent. Used at startup to explain why
    /// the API fell back to local disk — a single missing value is otherwise silent.
    /// Never includes secret values, only setting names.
    /// </summary>
    public IReadOnlyList<string> MissingR2Settings
    {
        get
        {
            var missing = new List<string>(4);
            if (string.IsNullOrWhiteSpace(ServiceUrl)) missing.Add($"{SectionName}:ServiceUrl");
            if (string.IsNullOrWhiteSpace(AccessKeyId)) missing.Add($"{SectionName}:AccessKeyId");
            if (string.IsNullOrWhiteSpace(SecretAccessKey)) missing.Add($"{SectionName}:SecretAccessKey");
            if (string.IsNullOrWhiteSpace(BucketName)) missing.Add($"{SectionName}:BucketName");
            return missing;
        }
    }
}

/// <summary>
/// Cloudflare R2 (S3-compatible) storage. Files never pass through any client —
/// the API reads and writes objects on the caller's behalf.
/// </summary>
public sealed class R2FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;

    public R2FileStorageService(StorageOptions options)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            ForcePathStyle = true,        // required for R2
            // R2 ignores the region but the SDK requires one to be set.
            AuthenticationRegion = "auto",
        };
        _client = new AmazonS3Client(options.AccessKeyId, options.SecretAccessKey, config);
        _bucket = options.BucketName!;
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            DisablePayloadSigning = true, // R2 does not support streaming SigV4 payload signing
        };
        await _client.PutObjectAsync(request, ct);
    }

    public async Task<StoredFile?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(_bucket, key, ct);
            return new StoredFile(response.ResponseStream, response.Headers.ContentType, response.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default) =>
        await _client.DeleteObjectAsync(_bucket, key, ct);
}

/// <summary>
/// Dev/test fallback that stores files on local disk. Used automatically when
/// R2 is not configured, so the feature works locally without cloud credentials.
/// Keys map to a path under <see cref="StorageOptions.LocalPath"/>; traversal is
/// blocked by resolving and confirming the final path stays inside the root.
/// </summary>
public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string _root;

    public LocalFileStorageService(StorageOptions options, IWebHostEnvironment env)
    {
        _root = Path.IsPathRooted(options.LocalPath)
            ? options.LocalPath
            : Path.Combine(env.ContentRootPath, options.LocalPath);
        Directory.CreateDirectory(_root);
    }

    private string ResolvePath(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key));
        var root = Path.GetFullPath(_root + Path.DirectorySeparatorChar);
        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid storage key.");
        return full;
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
        await File.WriteAllTextAsync(path + ".type", contentType, ct);
    }

    public async Task<StoredFile?> GetAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        if (!File.Exists(path)) return null;
        var contentType = File.Exists(path + ".type")
            ? await File.ReadAllTextAsync(path + ".type", ct)
            : "application/octet-stream";
        var bytes = await File.ReadAllBytesAsync(path, ct);
        return new StoredFile(new MemoryStream(bytes), contentType, bytes.Length);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".type")) File.Delete(path + ".type");
        return Task.CompletedTask;
    }
}
