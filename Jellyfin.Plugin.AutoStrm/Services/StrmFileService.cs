using System;
using System.Collections.ObjectModel;
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

        // Ensure directory exists
        Directory.CreateDirectory(targetDirectory);

        var strmFilePath = Path.Combine(targetDirectory, strmFileName);

        if (config.EnableLogging)
        {
            _logger.LogInformation("Creating STRM file: {FilePath} -> {Url}", strmFilePath, mediaItem.Url);
        }

        // Write the URL to the STRM file
        await File.WriteAllTextAsync(strmFilePath, mediaItem.Url).ConfigureAwait(false);

        _logger.LogInformation("Successfully created STRM file: {FilePath}", strmFilePath);
    }

    /// <summary>
    /// Gets the target directory for the STRM file.
    /// </summary>
    /// <param name="mediaItem">The media item.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>The target directory path.</returns>
    private static string GetTargetDirectory(MediaItem mediaItem, PluginConfiguration config)
    {
        var baseDirectory = config.BaseStrmPath;

        if (config.EnableParentFolders && mediaItem.Parent > 0)
        {
            // Create a subfolder based on parent ID
            var parentFolder = $"parent_{mediaItem.Parent.ToString(CultureInfo.InvariantCulture)}";
            return Path.Combine(baseDirectory, parentFolder);
        }

        return baseDirectory;
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

        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var invalidChar in invalidChars)
        {
            fileName = fileName.Replace(invalidChar, '_', StringComparison.Ordinal);
        }

        // Also replace some additional problematic characters
        fileName = fileName.Replace(":", "_", StringComparison.Ordinal)
                          .Replace("?", "_", StringComparison.Ordinal)
                          .Replace("*", "_", StringComparison.Ordinal)
                          .Replace("\"", "_", StringComparison.Ordinal)
                          .Replace("<", "_", StringComparison.Ordinal)
                          .Replace(">", "_", StringComparison.Ordinal)
                          .Replace("|", "_", StringComparison.Ordinal);

        return fileName;
    }
}
