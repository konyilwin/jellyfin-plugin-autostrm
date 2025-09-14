using System.Text.Json.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AutoStrm.Configuration;

/// <summary>
/// Duplicate handling strategies.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DuplicateHandling
{
    /// <summary>
    /// Overwrite existing files.
    /// </summary>
    Overwrite = 0,

    /// <summary>
    /// Skip duplicate files.
    /// </summary>
    Skip = 1,

    /// <summary>
    /// Create numbered versions (file_1.strm, file_2.strm).
    /// </summary>
    CreateVersions = 2
}

/// <summary>
/// Auto-splitting strategies for folder organization.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AutoSplitMode
{
    /// <summary>
    /// Disabled - no automatic folder splitting.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Alphabetical splitting for movies (A-C, D-F, etc.).
    /// </summary>
    Alphabetical = 1,

    /// <summary>
    /// Smart splitting based on file count and content type.
    /// </summary>
    Smart = 2,

    /// <summary>
    /// Genre-based splitting (requires genre detection).
    /// </summary>
    Genre = 3
}

/// <summary>
/// Plugin configuration with enhanced folder management options.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // Set default options
        BaseStrmPath = "/media/strm";
        EnableLogging = true;
        ApiEndpoint = "/plugins/autostrm/webhook";
        EnableParentFolders = true;
        FileNamePattern = "{name}";
        EnableMediaTypeDetection = true;
        OrganizeByMediaType = true;
        DuplicateHandling = DuplicateHandling.Overwrite;

        // New auto-splitting options
        EnableAutoSplit = true;
        AutoSplitMode = AutoSplitMode.Smart;
        MaxFilesPerFolder = 400;
        EnablePerformanceOptimizations = true;
    }

    /// <summary>
    /// Gets or sets the base path where STRM files will be created.
    /// </summary>
    public string BaseStrmPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether logging is enabled.
    /// </summary>
    public bool EnableLogging { get; set; }

    /// <summary>
    /// Gets or sets the API endpoint path.
    /// </summary>
    public string ApiEndpoint { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to create parent folders based on parent ID.
    /// </summary>
    public bool EnableParentFolders { get; set; }

    /// <summary>
    /// Gets or sets the file name pattern for STRM files.
    /// </summary>
    public string FileNamePattern { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable automatic media type detection.
    /// </summary>
    public bool EnableMediaTypeDetection { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to organize files by media type (Movies/TV Shows).
    /// </summary>
    public bool OrganizeByMediaType { get; set; }

    /// <summary>
    /// Gets or sets the duplicate handling strategy.
    /// </summary>
    public DuplicateHandling DuplicateHandling { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether automatic folder splitting is enabled.
    /// </summary>
    public bool EnableAutoSplit { get; set; }

    /// <summary>
    /// Gets or sets the auto-splitting mode.
    /// </summary>
    public AutoSplitMode AutoSplitMode { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of files per folder before splitting occurs.
    /// </summary>
    public int MaxFilesPerFolder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether performance optimizations are enabled.
    /// </summary>
    public bool EnablePerformanceOptimizations { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to create folder statistics logs.
    /// </summary>
    public bool LogFolderStatistics { get; set; }
}
