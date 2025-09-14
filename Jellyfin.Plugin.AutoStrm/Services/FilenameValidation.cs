using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.AutoStrm.Services;

/// <summary>
/// Result of filename validation.
/// </summary>
public class FilenameValidation
{
    /// <summary>
    /// Gets or sets the original filename.
    /// </summary>
    public string OriginalFilename { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the filename is valid.
    /// </summary>
    public bool IsValid { get; set; } = true;

    /// <summary>
    /// Gets the list of validation issues.
    /// </summary>
    public Collection<string> Issues { get; } = new();

    /// <summary>
    /// Gets or sets the suggested improved filename.
    /// </summary>
    public string SuggestedFilename { get; set; } = string.Empty;
}
