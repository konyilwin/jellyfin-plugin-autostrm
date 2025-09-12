using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AutoStrm.Configuration;

/// <summary>
/// Plugin configuration.
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
        ApiEndpoint = "/AutoStrm/webhook";
        EnableParentFolders = true;
        FileNamePattern = "{name}";
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
}
