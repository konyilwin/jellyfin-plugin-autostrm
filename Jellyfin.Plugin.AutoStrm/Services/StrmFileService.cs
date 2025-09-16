using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.AutoStrm.Configuration;
using Jellyfin.Plugin.AutoStrm.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoStrm.Services;

/// <summary>
/// Service for creating STRM files with automatic folder splitting for performance.
/// </summary>
public class StrmFileService
{
    private const int MaxFilesPerFolder = 400; // Safe limit below Jellyfin's ~500 file issue

    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrmFileService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public StrmFileService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates STRM files from webhook data.
    /// </summary>
    /// <param name="webhookData">The webhook data containing media items.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CreateStrmFilesAsync(WebhookData webhookData, PluginConfiguration config)
    {
        if (webhookData.Data == null || webhookData.Data.Count == 0)
        {
            _logger.LogWarning("No media items found in webhook data");
            return;
        }

        foreach (var mediaItem in webhookData.Data)
        {
            try
            {
                await CreateStrmFileAsync(mediaItem, config).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create STRM file for {MediaName}", mediaItem.Name);
            }
        }
    }

    /// <summary>
    /// Creates a single STRM file for a media item.
    /// </summary>
    /// <param name="mediaItem">The media item.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CreateStrmFileAsync(MediaItem mediaItem, PluginConfiguration config)
    {
        // Use JellyfinFilenameService to create a proper filename instead of just sanitizing
        var jellyfinFriendlyName = JellyfinFilenameService.CreateJellyfinFilename(mediaItem.Name, _logger);
        // Apply the configured filename pattern
        var fileName = ApplyFilenamePattern(jellyfinFriendlyName, config.FileNamePattern);
        // Ensure we have a valid filename
        if (string.IsNullOrWhiteSpace(fileName))
        {
            _logger.LogWarning("Generated filename is empty for media item: {MediaName}", mediaItem.Name);
            fileName = "unknown_file";
        }

        // Add .strm extension
        var strmFileName = $"{fileName}.strm";

        var targetDirectory = GetTargetDirectoryWithAutoSplit(mediaItem, config);

        // Validate the target directory to prevent path injection
        var fullTargetPath = Path.GetFullPath(targetDirectory);
        var configBasePath = Path.GetFullPath(config.BaseStrmPath);
        if (!fullTargetPath.StartsWith(configBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Target directory is outside of configured base path");
        }

        // Ensure directory exists
        Directory.CreateDirectory(targetDirectory);

        // Handle duplicates based on configuration
        var finalFileName = HandleDuplicateFile(targetDirectory, strmFileName, config.DuplicateHandling);
        var strmFilePath = Path.Combine(targetDirectory, finalFileName);

        // Additional validation for the file path
        var fullFilePath = Path.GetFullPath(strmFilePath);
        if (!fullFilePath.StartsWith(configBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("File path is outside of configured base path");
        }

        // Check if we should skip this file
        if (string.IsNullOrEmpty(finalFileName))
        {
            _logger.LogInformation("Skipping duplicate file: {OriginalPath}", Path.Combine(targetDirectory, strmFileName));
            return;
        }

        if (config.EnableLogging)
        {
            _logger.LogInformation("Creating STRM file: {FilePath} -> {Url}", fullFilePath, mediaItem.Url);
            _logger.LogInformation("Original filename: {Original} -> Jellyfin filename: {JellyfinName}", mediaItem.Name, jellyfinFriendlyName);
        }

        // Write the URL to the STRM file
        // The path injection warning is suppressed because we validate the full path above
#pragma warning disable CA3003
        await File.WriteAllTextAsync(fullFilePath, mediaItem.Url).ConfigureAwait(false);
#pragma warning restore CA3003

        _logger.LogInformation("Successfully created STRM file: {FilePath}", fullFilePath);
    }

    /// <summary>
    /// Applies the filename pattern to the jellyfin-friendly name.
    /// </summary>
    /// <param name="jellyfinName">The jellyfin-friendly filename.</param>
    /// <param name="pattern">The filename pattern.</param>
    /// <returns>The final filename with pattern applied.</returns>
    private static string ApplyFilenamePattern(string jellyfinName, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return jellyfinName;
        }

        // Replace {name} placeholder with the jellyfin-friendly name
        var result = pattern.Replace("{name}", jellyfinName, StringComparison.OrdinalIgnoreCase);
        // You can add more placeholders here in the future, like:
        // result = result.Replace("{year}", extractedYear, StringComparison.OrdinalIgnoreCase);
        // result = result.Replace("{quality}", extractedQuality, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>
    /// Gets the target directory with automatic folder splitting for optimal performance.
    /// </summary>
    /// <param name="mediaItem">The media item.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>The target directory path with auto-splitting applied.</returns>
    private string GetTargetDirectoryWithAutoSplit(MediaItem mediaItem, PluginConfiguration config)
    {
        var baseDirectory = config.BaseStrmPath;
        var targetPath = baseDirectory;

        if (config.EnableLogging)
        {
            _logger.LogInformation(
                "GetTargetDirectory - EnableMediaTypeDetection: {Detection}, OrganizeByMediaType: {Organize}",
                config.EnableMediaTypeDetection,
                config.OrganizeByMediaType);
        }

        // Apply media type detection and organization if enabled
        if (config.EnableMediaTypeDetection && config.OrganizeByMediaType)
        {
            var mediaType = MediaTypeDetector.DetectMediaType(mediaItem.Name);
            if (config.EnableLogging)
            {
                _logger.LogInformation("Detected media type: {MediaType} for filename: {Name}", mediaType, mediaItem.Name);
            }

            if (mediaType == MediaType.TvSeries)
            {
                targetPath = GetTvSeriesPath(mediaItem, baseDirectory, config);
            }
            else
            {
                targetPath = GetMoviePath(mediaItem, baseDirectory, config);
            }
        }

        // Optionally include parent folder if enabled
        if (config.EnableParentFolders && mediaItem.Parent > 0)
        {
            var parentFolder = $"parent_{mediaItem.Parent.ToString(CultureInfo.InvariantCulture)}";
            targetPath = Path.Combine(targetPath, parentFolder);
            if (config.EnableLogging)
            {
                _logger.LogInformation("Target path after parent folder: {Path}", targetPath);
            }
        }

        return targetPath;
    }

    /// <summary>
    /// Gets the movie path with automatic alphabetical splitting - UPDATED VERSION.
    /// </summary>
    /// <param name="mediaItem">The media item.</param>
    /// <param name="baseDirectory">The base directory.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>The movie directory path with auto-splitting.</returns>
    private string GetMoviePath(MediaItem mediaItem, string baseDirectory, PluginConfiguration config)
    {
        var moviesFolder = Path.Combine(baseDirectory, "Movies");

        // Use Jellyfin-friendly name for grouping - this ensures consistency
        var jellyfinFriendlyName = JellyfinFilenameService.CreateJellyfinFilename(mediaItem.Name, _logger);
        // Extract movie info from the cleaned name
        var (movieName, _) = MediaTypeDetector.ExtractMovieInfo(jellyfinFriendlyName);
        if (string.IsNullOrEmpty(movieName))
        {
            movieName = jellyfinFriendlyName;
        }

        if (config.EnableLogging)
        {
            _logger.LogDebug("Movie path generation - Original: {Original}, Jellyfin: {Jellyfin}, Extracted: {Extracted}", mediaItem.Name, jellyfinFriendlyName, movieName);
        }

        // Create alphabetical grouping (A-C, D-F, etc.)
        var alphabetGroup = GetAlphabeticalGroup(movieName);
        var groupFolder = Path.Combine(moviesFolder, alphabetGroup);

        // Check if we need to split further due to file count
        if (Directory.Exists(groupFolder))
        {
            var currentFileCount = Directory.GetFiles(groupFolder, "*.strm").Length;
            if (currentFileCount >= MaxFilesPerFolder)
            {
                // Create sub-groups within the alphabet group
                var subGroup = GetSubGroup(groupFolder, movieName, "movie");
                groupFolder = Path.Combine(groupFolder, subGroup);

                if (config.EnableLogging)
                {
                    _logger.LogInformation("Movie folder split: {GroupFolder} (files: {FileCount})", groupFolder, currentFileCount);
                }
            }
        }

        return groupFolder;
    }

    /// <summary>
    /// Gets the TV series path with automatic season splitting - UPDATED VERSION with duplicate prevention.
    /// </summary>
    /// <param name="mediaItem">The media item.</param>
    /// <param name="baseDirectory">The base directory.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>The TV series directory path with auto-splitting.</returns>
    private string GetTvSeriesPath(MediaItem mediaItem, string baseDirectory, PluginConfiguration config)
    {
        var tvShowsFolder = Path.Combine(baseDirectory, "TV Shows");
        // Use Jellyfin-friendly name for consistency
        var jellyfinFriendlyName = JellyfinFilenameService.CreateJellyfinFilename(mediaItem.Name, _logger);
        var (seriesName, season, episode) = MediaTypeDetector.ExtractSeriesInfo(jellyfinFriendlyName);

        // Normalize the series name to prevent duplicates
        seriesName = NormalizeSeriesName(seriesName);

        if (config.EnableLogging)
        {
            _logger.LogDebug("TV path generation - Original: {Original}, Jellyfin: {Jellyfin}, Normalized Series: {Series}, S{Season}E{Episode}", mediaItem.Name, jellyfinFriendlyName, seriesName, season, episode);
        }

        // Check for existing similar folder (case-insensitive)
        var existingFolder = FindExistingSeriesFolder(tvShowsFolder, seriesName);
        var seriesFolder = existingFolder ?? Path.Combine(tvShowsFolder, SanitizeFileName(seriesName));

        if (config.EnableLogging && existingFolder != null)
        {
            _logger.LogDebug("Found existing series folder: {ExistingFolder} for series: {SeriesName}", existingFolder, seriesName);
        }

        if (season.HasValue)
        {
            var seasonFolder = Path.Combine(seriesFolder, $"Season {season:D2}");

            // Check if season folder needs splitting due to too many episodes
            if (Directory.Exists(seasonFolder))
            {
                var currentFileCount = Directory.GetFiles(seasonFolder, "*.strm").Length;
                if (currentFileCount >= MaxFilesPerFolder)
                {
                    // Split large seasons into episode ranges (1-100, 101-200, etc.)
                    var episodeRange = GetEpisodeRangeFolder(episode ?? 1);
                    seasonFolder = Path.Combine(seasonFolder, episodeRange);

                    if (config.EnableLogging)
                    {
                        _logger.LogInformation("Season folder split: {SeasonFolder} (files: {FileCount})", seasonFolder, currentFileCount);
                    }
                }
            }

            return seasonFolder;
        }
        else
        {
            // No season info, place in main series folder with potential splitting
            if (Directory.Exists(seriesFolder))
            {
                var currentFileCount = Directory.GetFiles(seriesFolder, "*.strm").Length;
                if (currentFileCount >= MaxFilesPerFolder)
                {
                    var subGroup = GetSubGroup(seriesFolder, jellyfinFriendlyName, "episode");
                    seriesFolder = Path.Combine(seriesFolder, subGroup);
                }
            }

            return seriesFolder;
        }
    }

    /// <summary>
    /// Normalizes series names to prevent duplicate folders.
    /// </summary>
    /// <param name="seriesName">The original series name.</param>
    /// <returns>A normalized series name.</returns>
    private static string NormalizeSeriesName(string seriesName)
    {
        if (string.IsNullOrEmpty(seriesName))
        {
            return "Unknown Series";
        }

        // Remove common variations and normalize
        var normalized = seriesName.Trim()
            .Replace("\u2019", "'", StringComparison.Ordinal) // Right single quotation mark to apostrophe
            .Replace("\u2018", "'", StringComparison.Ordinal) // Left single quotation mark to apostrophe
            .Replace("\u201C", "\"", StringComparison.Ordinal) // Left double quotation mark to quote
            .Replace("\u201D", "\"", StringComparison.Ordinal) // Right double quotation mark to quote
            .Replace(".", " ", StringComparison.Ordinal) // Replace dots with spaces
            .Replace("_", " ", StringComparison.Ordinal) // Replace underscores with spaces
            .Replace("  ", " ", StringComparison.Ordinal) // Replace double spaces with single spaces
            .Trim();

        // Normalize accented characters to their base form
        normalized = RemoveAccents(normalized);

        // Convert to title case for consistency
        try
        {
            var textInfo = CultureInfo.CurrentCulture.TextInfo;
            normalized = textInfo.ToTitleCase(normalized.ToLowerInvariant());
        }
        catch
        {
            // Fallback if title case conversion fails - keep original normalized value
        }

        return normalized;
    }

    /// <summary>
    /// Removes accents and diacritical marks from text.
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>Text with accents removed.</returns>
    private static string RemoveAccents(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        try
        {
            // Normalize to decomposed form (NFD) where accents are separate characters
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new System.Text.StringBuilder();

            foreach (var c in normalizedString)
            {
                // Keep only characters that are not diacritical marks
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            // Normalize back to composed form (NFC)
            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }
        catch
        {
            // If normalization fails, return original text
            return text;
        }
    }

    /// <summary>
    /// Finds an existing series folder with case-insensitive matching.
    /// </summary>
    /// <param name="tvShowsFolder">The TV shows base folder.</param>
    /// <param name="seriesName">The series name to search for.</param>
    /// <returns>The path to an existing folder or null if not found.</returns>
    private static string? FindExistingSeriesFolder(string tvShowsFolder, string seriesName)
    {
        if (!Directory.Exists(tvShowsFolder))
        {
            return null;
        }

        var sanitizedTarget = SanitizeFileName(seriesName);

        try
        {
            // Look for existing folders with similar names (case-insensitive)
            var existingFolders = Directory.GetDirectories(tvShowsFolder)
                .Where(dir => string.Equals(
                    Path.GetFileName(dir),
                    sanitizedTarget,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            return existingFolders.FirstOrDefault();
        }
        catch
        {
            // If we can't read the directory, return null
            return null;
        }
    }

    /// <summary>
    /// Gets the alphabetical group for a movie name (A-C, D-F, etc.).
    /// </summary>
    /// <param name="name">The movie name.</param>
    /// <returns>The alphabetical group folder name.</returns>
    private static string GetAlphabeticalGroup(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "0-9";
        }

        var firstChar = char.ToUpperInvariant(name[0]);

        // Handle numbers and special characters
        if (!char.IsLetter(firstChar))
        {
            return "0-9";
        }

        // Create groups: A-C, D-F, G-I, J-L, M-O, P-R, S-U, V-X, Y-Z
        var groups = new[]
        {
            ("A-C", 'A', 'C'),
            ("D-F", 'D', 'F'),
            ("G-I", 'G', 'I'),
            ("J-L", 'J', 'L'),
            ("M-O", 'M', 'O'),
            ("P-R", 'P', 'R'),
            ("S-U", 'S', 'U'),
            ("V-X", 'V', 'X'),
            ("Y-Z", 'Y', 'Z')
        };

        foreach (var (groupName, startChar, endChar) in groups)
        {
            if (firstChar >= startChar && firstChar <= endChar)
            {
                return groupName;
            }
        }

        return "Other";
    }

    /// <summary>
    /// Gets an episode range folder for large seasons.
    /// </summary>
    /// <param name="episodeNumber">The episode number.</param>
    /// <returns>The episode range folder name.</returns>
    private static string GetEpisodeRangeFolder(int episodeNumber)
    {
        // Create ranges: 001-100, 101-200, 201-300, etc.
        var rangeStart = (((episodeNumber - 1) / 100) * 100) + 1;
        var rangeEnd = rangeStart + 99;
        return $"Episodes {rangeStart:D3}-{rangeEnd:D3}";
    }

    /// <summary>
    /// Gets a sub-group folder when the main group is too large.
    /// </summary>
    /// <param name="parentFolder">The parent folder path.</param>
    /// <param name="itemName">The item name.</param>
    /// <param name="prefix">The prefix for the sub-group.</param>
    /// <returns>The sub-group folder name.</returns>
    private static string GetSubGroup(string parentFolder, string itemName, string prefix)
    {
        // Create hash-based sub-groups for even distribution
        var hash = Math.Abs(itemName.GetHashCode(StringComparison.Ordinal)) % 10;
        return $"{prefix}_{hash:D2}";
    }

    /// <summary>
    /// Handles duplicate file scenarios based on configuration.
    /// </summary>
    /// <param name="directory">The target directory.</param>
    /// <param name="fileName">The original filename.</param>
    /// <param name="duplicateHandling">The duplicate handling strategy.</param>
    /// <returns>The final filename to use, or null/empty to skip.</returns>
    private static string HandleDuplicateFile(string directory, string fileName, DuplicateHandling duplicateHandling)
    {
        var filePath = Path.Combine(directory, fileName);

        // Path injection warning suppressed because we validate all paths before calling this method
#pragma warning disable CA3003
        if (!File.Exists(filePath))
#pragma warning restore CA3003
        {
            // File doesn't exist, use original name
            return fileName;
        }

        return duplicateHandling switch
        {
            DuplicateHandling.Skip => string.Empty, // Return empty to indicate skip
            DuplicateHandling.Overwrite => fileName, // Use original name (will overwrite)
            DuplicateHandling.CreateVersions => CreateVersionedFileName(directory, fileName),
            _ => fileName
        };
    }

    /// <summary>
    /// Creates a versioned filename for duplicates.
    /// </summary>
    /// <param name="directory">The target directory.</param>
    /// <param name="originalFileName">The original filename.</param>
    /// <returns>A versioned filename.</returns>
    private static string CreateVersionedFileName(string directory, string originalFileName)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var counter = 1;

        string versionedFileName;
        string versionedFilePath;

        do
        {
            versionedFileName = $"{fileNameWithoutExtension}_{counter}{extension}";
            versionedFilePath = Path.Combine(directory, versionedFileName);
            counter++;

            // Path injection warning suppressed because we validate all paths before calling this method
#pragma warning disable CA3003
        }
        while (File.Exists(versionedFilePath));
#pragma warning restore CA3003

        return versionedFileName;
    }

    /// <summary>
    /// Sanitizes a file name by removing invalid characters.
    /// This method is kept for backward compatibility and edge cases.
    /// </summary>
    /// <param name="fileName">The original file name.</param>
    /// <returns>The sanitized file name.</returns>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return "unknown";
        }

        var result = fileName;
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var invalidChar in invalidChars)
        {
            result = result.Replace(invalidChar.ToString(), "_", StringComparison.Ordinal);
        }

        // Also replace some additional problematic characters
        result = result.Replace(":", "_", StringComparison.Ordinal)
                      .Replace("?", "_", StringComparison.Ordinal)
                      .Replace("*", "_", StringComparison.Ordinal)
                      .Replace("\"", "_", StringComparison.Ordinal)
                      .Replace("<", "_", StringComparison.Ordinal)
                      .Replace(">", "_", StringComparison.Ordinal)
                      .Replace("|", "_", StringComparison.Ordinal);

        return result;
    }
}
