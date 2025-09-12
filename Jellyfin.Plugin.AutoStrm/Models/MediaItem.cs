using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AutoStrm.Models;

/// <summary>
/// Represents a single media item.
/// </summary>
public class MediaItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MediaItem"/> class.
    /// </summary>
    public MediaItem()
    {
        Url = string.Empty;
        Name = string.Empty;
        Parent = 0;
    }

    /// <summary>
    /// Gets or sets the URL of the media file.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; }

    /// <summary>
    /// Gets or sets the name of the media file.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the parent ID.
    /// </summary>
    [JsonPropertyName("parent")]
    public int Parent { get; set; }
}
