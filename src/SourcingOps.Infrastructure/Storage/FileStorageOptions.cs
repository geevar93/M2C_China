namespace SourcingOps.Infrastructure.Storage;

/// <summary>Bound from configuration section "Storage" (TECH_SPEC §4.6).</summary>
public sealed class FileStorageOptions
{
    public string RootPath { get; set; } = "/data/uploads";
    public long MaxUploadSizeBytes { get; set; } = 20 * 1024 * 1024; // 20 MB default
}
