using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using API.Archive;
using API.DTOs.Reader;
using API.Entities;
using API.Entities.Enums;
using API.Extensions;
using Microsoft.Extensions.Logging;
using NetVips;
using SharpCompress.Archives;

namespace API.Services;

#nullable enable

public interface IDimensionCacheService
{
    /// <summary>
    /// Gets cached dimensions for a manga file. Returns null if cache is invalid/missing.
    /// </summary>
    IEnumerable<FileDimensionDto>? GetCachedDimensions(MangaFile mangaFile);

    /// <summary>
    /// Generates and caches dimensions for a manga file.
    /// </summary>
    void GenerateAndCacheDimensions(MangaFile mangaFile);

    /// <summary>
    /// Gets the cache file path for an archive file.
    /// </summary>
    string GetCacheFilePath(string archiveDirectory);
}

public class DimensionCacheService : IDimensionCacheService
{
    private readonly ILogger<DimensionCacheService> _logger;
    private readonly IArchiveService _archiveService;
    private readonly IDirectoryService _directoryService;

    private const string CacheFileName = ".kavita-dimensions.json";
    private const int CurrentCacheVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public DimensionCacheService(
        ILogger<DimensionCacheService> logger,
        IArchiveService archiveService,
        IDirectoryService directoryService)
    {
        _logger = logger;
        _archiveService = archiveService;
        _directoryService = directoryService;
    }

    /// <inheritdoc />
    public string GetCacheFilePath(string archiveDirectory)
    {
        return Path.Combine(archiveDirectory, CacheFileName);
    }

    /// <inheritdoc />
    public IEnumerable<FileDimensionDto>? GetCachedDimensions(MangaFile mangaFile)
    {
        _logger.LogDebug("[DimensionCache] GetCachedDimensions called for {FilePath}, Format: {Format}", mangaFile.FilePath, mangaFile.Format);

        if (mangaFile.Format != MangaFormat.Archive)
        {
            _logger.LogDebug("[DimensionCache] Skipping non-archive format: {Format}", mangaFile.Format);
            return null;
        }

        var archiveDirectory = Path.GetDirectoryName(mangaFile.FilePath);
        if (string.IsNullOrEmpty(archiveDirectory))
        {
            _logger.LogWarning("[DimensionCache] Could not get directory from {FilePath}", mangaFile.FilePath);
            return null;
        }

        var cacheFilePath = GetCacheFilePath(archiveDirectory);
        _logger.LogDebug("[DimensionCache] Looking for cache file at: {CacheFilePath}", cacheFilePath);

        if (!_directoryService.FileSystem.File.Exists(cacheFilePath))
        {
            _logger.LogInformation("[DimensionCache] Cache file does not exist: {CacheFilePath}", cacheFilePath);
            return null;
        }

        try
        {
            var json = _directoryService.FileSystem.File.ReadAllText(cacheFilePath);
            var cache = JsonSerializer.Deserialize<DimensionCacheDto>(json, JsonOptions);

            if (cache == null || cache.Version != CurrentCacheVersion)
            {
                _logger.LogWarning("[DimensionCache] Cache file invalid or wrong version at {CacheFilePath}", cacheFilePath);
                return null;
            }

            var archiveFileName = Path.GetFileName(mangaFile.FilePath);
            if (!cache.Files.TryGetValue(archiveFileName, out var entry))
            {
                _logger.LogInformation("[DimensionCache] Archive {ArchiveFileName} not found in cache file {CacheFilePath}. Available entries: {Entries}",
                    archiveFileName, cacheFilePath, string.Join(", ", cache.Files.Keys));
                return null;
            }

            // Validate cache - check if file has been modified since cache was created
            if (entry.LastModifiedUtc != mangaFile.LastModifiedUtc)
            {
                _logger.LogInformation("[DimensionCache] Dimension cache is stale for {FilePath}. Cache LastModifiedUtc: {CacheTime}, File LastModifiedUtc: {FileTime}",
                    mangaFile.FilePath, entry.LastModifiedUtc, mangaFile.LastModifiedUtc);
                return null;
            }

            _logger.LogDebug("[DimensionCache] Successfully retrieved {Count} cached dimensions for {FilePath}",
                entry.Dimensions.Count, mangaFile.FilePath);
            return entry.Dimensions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DimensionCache] Failed to read dimension cache from {CacheFilePath}", cacheFilePath);
            return null;
        }
    }

    /// <inheritdoc />
    public void GenerateAndCacheDimensions(MangaFile mangaFile)
    {
        _logger.LogInformation("[DimensionCache] GenerateAndCacheDimensions called for file: {FilePath}, Format: {Format}, LastModifiedUtc: {LastModifiedUtc}",
            mangaFile.FilePath, mangaFile.Format, mangaFile.LastModifiedUtc);

        if (mangaFile.Format != MangaFormat.Archive)
        {
            _logger.LogInformation("[DimensionCache] Skipping non-archive format: {Format} for {FilePath}", mangaFile.Format, mangaFile.FilePath);
            return;
        }

        var archiveDirectory = Path.GetDirectoryName(mangaFile.FilePath);
        if (string.IsNullOrEmpty(archiveDirectory))
        {
            _logger.LogWarning("[DimensionCache] Could not get directory name from {FilePath}", mangaFile.FilePath);
            return;
        }

        var cacheFilePath = GetCacheFilePath(archiveDirectory);
        _logger.LogInformation("[DimensionCache] Cache file path will be: {CacheFilePath}", cacheFilePath);

        try
        {
            _logger.LogInformation("[DimensionCache] Starting dimension generation for archive: {FilePath}", mangaFile.FilePath);
            var dimensions = GenerateDimensionsFromArchive(mangaFile.FilePath);
            if (dimensions.Count == 0)
            {
                _logger.LogWarning("[DimensionCache] No images found in archive {FilePath}", mangaFile.FilePath);
                return;
            }

            _logger.LogInformation("[DimensionCache] Generated {Count} dimensions, now reading/creating cache at {CacheFilePath}",
                dimensions.Count, cacheFilePath);
            var cache = ReadOrCreateCache(cacheFilePath);

            var archiveFileName = Path.GetFileName(mangaFile.FilePath);
            cache.Files[archiveFileName] = new FileDimensionCacheEntry
            {
                LastModifiedUtc = mangaFile.LastModifiedUtc,
                Dimensions = dimensions
            };

            _logger.LogInformation("[DimensionCache] Writing cache to {CacheFilePath} with {Count} entries for file {ArchiveFileName}",
                cacheFilePath, dimensions.Count, archiveFileName);
            WriteCache(cacheFilePath, cache);
            _logger.LogInformation("[DimensionCache] Successfully cached dimensions for {FilePath}: {Count} pages at {CacheFilePath}",
                mangaFile.FilePath, dimensions.Count, cacheFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DimensionCache] Failed to generate/cache dimensions for {FilePath}", mangaFile.FilePath);
        }
    }

    private DimensionCacheDto ReadOrCreateCache(string cacheFilePath)
    {
        if (_directoryService.FileSystem.File.Exists(cacheFilePath))
        {
            try
            {
                var json = _directoryService.FileSystem.File.ReadAllText(cacheFilePath);
                var cache = JsonSerializer.Deserialize<DimensionCacheDto>(json, JsonOptions);
                if (cache != null && cache.Version == CurrentCacheVersion)
                {
                    return cache;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read existing cache, creating new one");
            }
        }

        return new DimensionCacheDto { Version = CurrentCacheVersion };
    }

    private void WriteCache(string cacheFilePath, DimensionCacheDto cache)
    {
        var json = JsonSerializer.Serialize(cache, JsonOptions);
        _directoryService.FileSystem.File.WriteAllText(cacheFilePath, json);
    }

    private List<FileDimensionDto> GenerateDimensionsFromArchive(string archivePath)
    {
        var dimensions = new ConcurrentBag<(int Index, FileDimensionDto Dto)>();
        var libraryHandler = _archiveService.CanOpen(archivePath);

        switch (libraryHandler)
        {
            case ArchiveLibrary.Default:
                GenerateDimensionsFromZipArchive(archivePath, dimensions);
                break;
            case ArchiveLibrary.SharpCompress:
                GenerateDimensionsFromSharpCompressArchive(archivePath, dimensions);
                break;
            case ArchiveLibrary.NotSupported:
                _logger.LogWarning("Archive cannot be opened for dimension scanning: {ArchivePath}", archivePath);
                return new List<FileDimensionDto>();
        }

        return dimensions.OrderBy(x => x.Index).Select(x => x.Dto).ToList();
    }

    private void GenerateDimensionsFromZipArchive(string archivePath, ConcurrentBag<(int Index, FileDimensionDto Dto)> dimensions)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var imageEntries = archive.Entries
            .Where(e => !Tasks.Scanner.Parser.Parser.HasBlacklistedFolderInPath(e.FullName)
                        && Tasks.Scanner.Parser.Parser.IsImage(e.FullName))
            .OrderByNatural(e => e.FullName)
            .ToList();

        if (imageEntries.Count == 0) return;

        var originalCacheSize = Cache.MaxFiles;
        try
        {
            Cache.MaxFiles = 0;

            // Note: ZipArchiveEntry.Open() is not thread-safe, so we process sequentially
            for (var i = 0; i < imageEntries.Count; i++)
            {
                var entry = imageEntries[i];
                try
                {
                    using var stream = entry.Open();
                    using var memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    memoryStream.Position = 0;

                    using var image = Image.NewFromStream(memoryStream, access: Enums.Access.SequentialUnbuffered);
                    dimensions.Add((i, new FileDimensionDto
                    {
                        PageNumber = i,
                        Height = image.Height,
                        Width = image.Width,
                        IsWide = image.Width > image.Height,
                        FileName = "/" + entry.FullName
                    }));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to read dimensions for entry {EntryName} in {ArchivePath}", entry.FullName, archivePath);
                }
            }
        }
        finally
        {
            Cache.MaxFiles = originalCacheSize;
        }
    }

    private void GenerateDimensionsFromSharpCompressArchive(string archivePath, ConcurrentBag<(int Index, FileDimensionDto Dto)> dimensions)
    {
        using var archive = ArchiveFactory.Open(archivePath);
        var imageEntries = archive.Entries
            .Where(e => !e.IsDirectory
                        && !Tasks.Scanner.Parser.Parser.HasBlacklistedFolderInPath(Path.GetDirectoryName(e.Key) ?? string.Empty)
                        && Tasks.Scanner.Parser.Parser.IsImage(e.Key))
            .OrderByNatural(e => e.Key)
            .ToList();

        if (imageEntries.Count == 0) return;

        var originalCacheSize = Cache.MaxFiles;
        try
        {
            Cache.MaxFiles = 0;

            // Note: SharpCompress entry streams are not thread-safe, so we process sequentially
            for (var i = 0; i < imageEntries.Count; i++)
            {
                var entry = imageEntries[i];
                try
                {
                    using var stream = entry.OpenEntryStream();
                    using var memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    memoryStream.Position = 0;

                    using var image = Image.NewFromStream(memoryStream, access: Enums.Access.SequentialUnbuffered);
                    dimensions.Add((i, new FileDimensionDto
                    {
                        PageNumber = i,
                        Height = image.Height,
                        Width = image.Width,
                        IsWide = image.Width > image.Height,
                        FileName = "/" + entry.Key
                    }));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to read dimensions for entry {EntryKey} in {ArchivePath}", entry.Key, archivePath);
                }
            }
        }
        finally
        {
            Cache.MaxFiles = originalCacheSize;
        }
    }
}
