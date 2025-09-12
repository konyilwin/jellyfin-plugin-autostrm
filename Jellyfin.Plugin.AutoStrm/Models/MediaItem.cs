using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AutoStrm.Models;

/// <summary>
/// Represents a single media item.
/// </summary>
public class MediaItem
{
    /// <summary>
    /// Gets or sets the URL of the media file.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the media file.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the parent ID.
    /// </summary>
    [JsonPropertyName("parent")]
    public int Parent { get; set; }
}
