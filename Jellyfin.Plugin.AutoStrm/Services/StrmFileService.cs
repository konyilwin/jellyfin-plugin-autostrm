using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.AutoStrm.Configuration;
using Jellyfin.Plugin.AutoStrm.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoStrm.Services;

/// <summary>
/// Service for creating STRM files.
/// </summary>
public class StrmFileService
{
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
        var fileName = SanitizeFileName(mediaItem.Name);

        // Remove the original extension and add .strm
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var strmFileName = $"{fileNameWithoutExtension}.strm";

        var targetDirectory = GetTargetDirectory(mediaItem, config);

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
        }

        // Write the URL to the STRM file
        // The path injection warning is suppressed because we validate the full path above
#pragma warning disable CA3003
        await File.WriteAllTextAsync(fullFilePath, mediaItem.Url).ConfigureAwait(false);
#pragma warning restore CA3003

        _logger.LogInformation("Successfully created STRM file: {FilePath}", fullFilePath);
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
    /// Gets the target directory for the STRM file.
    /// </summary>
    /// <param name="mediaItem">The media item.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>The target directory path.</returns>
    private string GetTargetDirectory(MediaItem mediaItem, PluginConfiguration config)
{
    var baseDirectory = config.BaseStrmPath;
    var targetPath = baseDirectory;

    if (config.EnableLogging)
    {
        _logger.LogInformation("GetTargetDirectory - EnableMediaTypeDetection: {Detection}, OrganizeByMediaType: {Organize}", config.EnableMediaTypeDetection, config.OrganizeByMediaType);
    }

    // Apply media type detection and organization if enabled
    if (config.EnableMediaTypeDetection && config.OrganizeByMediaType)
    {
        var mediaType = MediaTypeDetector.DetectMediaType(mediaItem.Name);
        if (config.EnableLogging)
        {
            _logger.LogInformation("Detected media type: {MediaType} for filename: {Name}", mediaType, mediaItem.Name);
        }

        string typeFolder;
        string contentFolder;

        if (mediaType == MediaType.TvSeries)
        {
            typeFolder = "TV Shows";
            var (seriesName, season, episode) = MediaTypeDetector.ExtractSeriesInfo(mediaItem.Name);
            if (config.EnableLogging)
            {
                _logger.LogInformation("TV Series info - Name: {SeriesName}, Season: {Season}, Episode: {Episode}", seriesName, season, episode);
            }

            if (!string.IsNullOrEmpty(seriesName))
            {
                contentFolder = season.HasValue ? Path.Combine(SanitizeFileName(seriesName), $"Season {season:D2}") : SanitizeFileName(seriesName);
            }
            else
            {
                contentFolder = "Unknown Series";
            }
        }
        else
        {
            typeFolder = "Movies";
            // For movies, don't create individual movie folders - place directly in Movies folder
            contentFolder = string.Empty;
        }

        targetPath = string.IsNullOrEmpty(contentFolder) ?
            Path.Combine(baseDirectory, typeFolder) : Path.Combine(baseDirectory, typeFolder, contentFolder);
        if (config.EnableLogging)
        {
            _logger.LogInformation("Target path after media type organization: {Path}", targetPath);
        }
    }

    // Optionally include parent folder if enabled
    if (config.EnableParentFolders && mediaItem.Parent > 0)
    {
        var parentFolder = $"parent_{mediaItem.Parent.ToString(CultureInfo.InvariantCulture)}";
        targetPath = config.OrganizeByMediaType ?
            Path.Combine(targetPath, parentFolder) : Path.Combine(baseDirectory, parentFolder);
        if (config.EnableLogging)
        {
            _logger.LogInformation("Target path after parent folder: {Path}", targetPath);
        }
    }

    return targetPath;
}

    /// <summary>
    /// Sanitizes a file name by removing invalid characters.
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
