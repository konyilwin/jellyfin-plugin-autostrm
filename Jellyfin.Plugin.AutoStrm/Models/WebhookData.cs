using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AutoStrm.Models;

/// <summary>
/// Represents the incoming webhook data structure.
/// </summary>
public class WebhookData
{
    /// <summary>
    /// Gets or sets the response code.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Gets the data array containing media items.
    /// </summary>
    [JsonPropertyName("data")]
    public Collection<MediaItem> Data { get; } = new();

    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;
}
