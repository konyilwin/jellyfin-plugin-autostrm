using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    /// Gets or sets the data array containing media items.
    /// </summary>
    [JsonPropertyName("data")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Required for JSON deserialization and manual assignment")]
    [SuppressMessage("Microsoft.Usage", "CA1002:DoNotExposeGenericLists", Justification = "Required for JSON deserialization")]
    public List<MediaItem> Data { get; set; } = new();

    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;
}
