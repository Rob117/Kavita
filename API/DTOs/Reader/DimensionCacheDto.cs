using System;
using System.Collections.Generic;

namespace API.DTOs.Reader;

/// <summary>
/// Represents the cached dimension data stored in .kavita-dimensions.json files
/// </summary>
public class DimensionCacheDto
{
    /// <summary>
    /// Cache format version for future compatibility
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Dictionary of archive filename to its dimension cache entry
    /// </summary>
    public Dictionary<string, FileDimensionCacheEntry> Files { get; set; } = new();
}

/// <summary>
/// Cache entry for a single archive file's dimensions
/// </summary>
public class FileDimensionCacheEntry
{
    /// <summary>
    /// The UTC timestamp when the archive was last modified (used for cache invalidation)
    /// </summary>
    public DateTime LastModifiedUtc { get; set; }

    /// <summary>
    /// List of dimensions for each page in the archive
    /// </summary>
    public List<FileDimensionDto> Dimensions { get; set; } = new();
}
