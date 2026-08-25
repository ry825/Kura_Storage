using System.Text;
using KuraStorage.Application.Abstractions;

namespace KuraStorage.Application.Search;

public sealed class SearchService(ISearchRepository repository)
{
    public const int MaximumQueryCodePoints = 200;
    public const int MaximumPageSize = 100;

    private static readonly HashSet<string> EntryTypes = new(StringComparer.Ordinal)
    {
        "FILE",
        "FOLDER",
    };

    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
    {
        "ACTIVE",
        "MISSING_CANDIDATE",
        "MISSING",
    };

    public async Task<SearchResult<SearchPage>> SearchAsync(
        Guid actorUserId,
        SearchQuery query,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("The actor user ID is required.", nameof(actorUserId));
        }

        ArgumentNullException.ThrowIfNull(query);
        var validation = Validate(query);
        if (!validation.IsSuccess)
        {
            return SearchResult<SearchPage>.Fail(validation.Failure!.Code, validation.Failure.Kind);
        }

        return SearchResult<SearchPage>.Success(
            await repository.SearchAsync(actorUserId, validation.Value!, cancellationToken));
    }

    public static SearchResult<SearchFilter> Validate(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var text = query.Text?.Trim().Normalize(NormalizationForm.FormC);
        if (text is { Length: 0 })
        {
            return InvalidQuery();
        }

        var codePointCount = text?.EnumerateRunes().Count() ?? 0;
        if (codePointCount > MaximumQueryCodePoints)
        {
            return InvalidQuery();
        }

        var entryType = NormalizeEnum(query.EntryType);
        if (entryType is not null && !EntryTypes.Contains(entryType))
        {
            return InvalidFilter();
        }

        FileCategory? fileCategory = null;
        if (query.FileCategory is not null)
        {
            if (!Enum.TryParse<FileCategory>(query.FileCategory.Trim(), true, out var parsedCategory) ||
                !Enum.IsDefined(parsedCategory))
            {
                return InvalidFilter();
            }

            fileCategory = parsedCategory;
        }

        var status = NormalizeEnum(query.Status);
        if (status is not null && !Statuses.Contains(status))
        {
            return InvalidFilter();
        }

        if ((entryType == "FOLDER" && (fileCategory is not null || query.MinSize is not null || query.MaxSize is not null)) ||
            query.UpdatedFrom > query.UpdatedTo ||
            query.MinSize < 0 ||
            query.MaxSize < 0 ||
            query.MinSize > query.MaxSize ||
            query.OwnerUserId == Guid.Empty ||
            query.ShareTargetId == Guid.Empty ||
            query.Page < 1 ||
            query.PageSize is < 1 or > MaximumPageSize ||
            (long)(query.Page - 1) * query.PageSize > int.MaxValue)
        {
            return InvalidFilter();
        }

        var hasFilter = entryType is not null ||
            fileCategory is not null ||
            status is not null ||
            query.UpdatedFrom is not null ||
            query.UpdatedTo is not null ||
            query.MinSize is not null ||
            query.MaxSize is not null ||
            query.OwnerUserId is not null ||
            query.ShareTargetId is not null;
        if (text is null && !hasFilter)
        {
            return InvalidQuery();
        }

        var matchMode = codePointCount switch
        {
            0 => SearchMatchMode.None,
            <= 2 => SearchMatchMode.Prefix,
            _ => SearchMatchMode.Contains,
        };
        var escaped = text is null ? null : EscapeLike(text.ToLowerInvariant());
        var pattern = matchMode switch
        {
            SearchMatchMode.Prefix => $"{escaped}%",
            SearchMatchMode.Contains => $"%{escaped}%",
            _ => null,
        };

        return SearchResult<SearchFilter>.Success(
            new SearchFilter(
                text?.ToLowerInvariant(),
                pattern,
                matchMode,
                entryType,
                fileCategory,
                status,
                query.UpdatedFrom?.ToUniversalTime(),
                query.UpdatedTo?.ToUniversalTime(),
                query.MinSize,
                query.MaxSize,
                query.OwnerUserId,
                query.ShareTargetId,
                query.Page,
                query.PageSize));
    }

    public static FileCategory ClassifyMimeType(string? mimeType)
    {
        var normalized = mimeType?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return FileCategory.Other;
        }

        if (normalized.StartsWith("image/", StringComparison.Ordinal)) return FileCategory.Image;
        if (normalized.StartsWith("video/", StringComparison.Ordinal)) return FileCategory.Video;
        if (normalized.StartsWith("audio/", StringComparison.Ordinal)) return FileCategory.Audio;
        if (normalized.StartsWith("text/", StringComparison.Ordinal) || DocumentMimeTypes.Contains(normalized))
            return FileCategory.Document;
        if (ArchiveMimeTypes.Contains(normalized)) return FileCategory.Archive;
        return FileCategory.Other;
    }

    private static readonly HashSet<string> DocumentMimeTypes = new(StringComparer.Ordinal)
    {
        "application/pdf",
        "application/msword",
        "application/rtf",
        "application/vnd.ms-excel",
        "application/vnd.ms-powerpoint",
        "application/vnd.oasis.opendocument.presentation",
        "application/vnd.oasis.opendocument.spreadsheet",
        "application/vnd.oasis.opendocument.text",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    private static readonly HashSet<string> ArchiveMimeTypes = new(StringComparer.Ordinal)
    {
        "application/gzip",
        "application/vnd.rar",
        "application/x-7z-compressed",
        "application/x-bzip2",
        "application/x-tar",
        "application/zip",
    };

    private static string? NormalizeEnum(string? value) =>
        value is null ? null : value.Trim().ToUpperInvariant();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static SearchResult<SearchFilter> InvalidQuery() =>
        SearchResult<SearchFilter>.Fail(SearchErrorCodes.InvalidQuery, SearchFailureKind.InvalidQuery);

    private static SearchResult<SearchFilter> InvalidFilter() =>
        SearchResult<SearchFilter>.Fail(SearchErrorCodes.InvalidFilter, SearchFailureKind.InvalidFilter);
}
