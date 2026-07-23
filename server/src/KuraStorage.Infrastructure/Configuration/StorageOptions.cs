using System.ComponentModel.DataAnnotations;

namespace KuraStorage.Infrastructure.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string RootPath { get; init; } = string.Empty;

    [Required]
    public string StorageId { get; init; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long MinimumFreeBytes { get; init; } = 1_073_741_824;
}
