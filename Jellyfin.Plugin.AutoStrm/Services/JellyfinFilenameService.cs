using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoStrm.Services;

/// <summary>
/// Service for creating Jellyfin-friendly filenames from original video names.
/// </summary>
public static class JellyfinFilenameService
{
    private static readonly Dictionary<string, string> InvalidChars = new()
    {
        { "<", string.Empty },
        { ">", string.Empty },
        { ":", " -" },
        { "\"", "'" },
        { "|", "-" },
        { "?", string.Empty },
        { "*", string.Empty },
        { "/", "-" },
        { "\\", "-" }
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm",
        ".m4v", ".3gp", ".mpg", ".mpeg", ".ts", ".m2ts"
    };

    private static readonly string[] AkaSeparators = { "(aka)", " aka ", " - aka ", "aka " };

    /// <summary>
    /// Creates a Jellyfin-friendly filename from the original video name.
    /// </summary>
    /// <param name="originalName">The original filename.</param>
    /// <param name="logger">Optional logger for debugging.</param>
    /// <returns>A cleaned, Jellyfin-friendly filename without extension.</returns>
    public static string CreateJellyfinFilename(string originalName, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(originalName))
        {
            logger?.LogWarning("Original name is null or empty, returning 'unknown'");
            return "unknown";
        }

        logger?.LogDebug("Processing filename: {OriginalName}", originalName);

        try
        {
            // Remove file extension
            var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(originalName);
            if (string.IsNullOrWhiteSpace(nameWithoutExt))
            {
                logger?.LogWarning("Name without extension is empty for: {OriginalName}", originalName);
                return "unknown";
            }

            // Extract year first
            var year = ExtractYear(nameWithoutExt);
            logger?.LogDebug("Extracted year: {Year}", year ?? "none");

            string cleanName;

            // Handle (aka) cases - find and keep only the English part
            if (ContainsAka(nameWithoutExt))
            {
                cleanName = HandleAkaCase(nameWithoutExt, year, logger);
            }
            else
            {
                // No (aka), use the original name
                cleanName = nameWithoutExt;
                if (!string.IsNullOrEmpty(year))
                {
                    cleanName = RemoveYearFromName(cleanName, year);
                }
            }

            // Clean problematic characters
            cleanName = CleanInvalidCharacters(cleanName);

            // Clean up extra spaces and trim
            cleanName = Regex.Replace(cleanName, @"\s+", " ").Trim();

            // Remove common release group tags and quality indicators
            cleanName = CleanReleaseInfo(cleanName);

            // Re-add year if we have one and it's not already there
            if (!string.IsNullOrEmpty(year) && !cleanName.Contains(year, StringComparison.OrdinalIgnoreCase))
            {
                cleanName = $"{cleanName} ({year})";
            }

            // Final validation
            if (string.IsNullOrWhiteSpace(cleanName) || cleanName == "()")
            {
                logger?.LogWarning("Clean name is empty after processing: {OriginalName}", originalName);
                return System.IO.Path.GetFileNameWithoutExtension(originalName) ?? "unknown";
            }

            logger?.LogDebug("Final filename: {CleanName}", cleanName);
            return cleanName;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error processing filename: {OriginalName}", originalName);
            return System.IO.Path.GetFileNameWithoutExtension(originalName) ?? "unknown";
        }
    }

    /// <summary>
    /// Checks if the file is a video file based on extension.
    /// </summary>
    /// <param name="filename">The filename to check.</param>
    /// <returns>True if it's a video file.</returns>
    public static bool IsVideoFile(string filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return false;
        }

        var ext = System.IO.Path.GetExtension(filename);
        return VideoExtensions.Contains(ext);
    }

    /// <summary>
    /// Checks if the filename contains any aka separators.
    /// </summary>
    /// <param name="filename">The filename to check.</param>
    /// <returns>True if aka separators are found.</returns>
    private static bool ContainsAka(string filename)
    {
        return AkaSeparators.Any(separator =>
            filename.Contains(separator, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extracts year from filename using regex pattern.
    /// </summary>
    /// <param name="filename">The filename to extract year from.</param>
    /// <returns>The extracted year or null if not found.</returns>
    private static string? ExtractYear(string filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return null;
        }

        // Look for year pattern (YYYY) in parentheses first
        var yearMatch = Regex.Match(filename, @"\((\d{4})\)");
        if (yearMatch.Success)
        {
            var year = yearMatch.Groups[1].Value;

            // Check if it's a valid year (1900-2099)
            if (int.TryParse(year, out var yearInt) && yearInt >= 1900 && yearInt <= 2099)
            {
                return year;
            }
        }

        // Also look for years without parentheses at the end or surrounded by spaces/dots
        var yearMatch2 = Regex.Match(filename, @"\b(19|20)\d{2}\b");
        if (yearMatch2.Success)
        {
            var year = yearMatch2.Value;
            if (int.TryParse(year, out var yearInt) && yearInt >= 1900 && yearInt <= 2099)
            {
                return year;
            }
        }

        return null;
    }

    /// <summary>
    /// Removes year from the name in various formats.
    /// </summary>
    /// <param name="name">The name to clean.</param>
    /// <param name="year">The year to remove.</param>
    /// <returns>Name with year removed.</returns>
    private static string RemoveYearFromName(string name, string year)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(year))
        {
            return name;
        }

        // Remove (YYYY)
        name = name.Replace($"({year})", string.Empty, StringComparison.OrdinalIgnoreCase);
        // Remove YYYY when surrounded by spaces or dots
        name = Regex.Replace(name, $@"\b{Regex.Escape(year)}\b", string.Empty, RegexOptions.IgnoreCase);
        return name.Trim();
    }

    /// <summary>
    /// Handles the (aka) case by finding the English part of the name.
    /// </summary>
    /// <param name="nameWithoutExt">The name without extension.</param>
    /// <param name="year">The extracted year.</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns>The cleaned name from the aka parts.</returns>
    private static string HandleAkaCase(string nameWithoutExt, string? year, ILogger? logger = null)
    {
        logger?.LogDebug("Handling aka case for: {Name}", nameWithoutExt);

        // Try each separator to split the name
        foreach (var separator in AkaSeparators)
        {
            if (nameWithoutExt.Contains(separator, StringComparison.OrdinalIgnoreCase))
            {
                var parts = nameWithoutExt.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
                logger?.LogDebug("Found {Count} parts using separator '{Separator}'", parts.Length, separator);

                foreach (var part in parts)
                {
                    var trimmedPart = part.Trim();

                    // Remove year from part to test if it's English
                    var partNoYear = trimmedPart;
                    if (!string.IsNullOrEmpty(year))
                    {
                        partNoYear = RemoveYearFromName(partNoYear, year);
                    }

                    // If this part is English and has reasonable length, use it
                    if (IsEnglish(partNoYear) && partNoYear.Trim().Length > 2)
                    {
                        logger?.LogDebug("Selected English part: {Part}", partNoYear);
                        return partNoYear.Trim();
                    }
                }

                // If no good English part found, use the first non-empty part
                var firstValidPart = parts.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
                if (!string.IsNullOrEmpty(firstValidPart))
                {
                    var cleaned = firstValidPart.Trim();
                    if (!string.IsNullOrEmpty(year))
                    {
                        cleaned = RemoveYearFromName(cleaned, year);
                    }

                    logger?.LogDebug("No ideal English part found, using first part: {Part}", cleaned);
                    return cleaned.Trim();
                }

                break;
            }
        }

        // No aka separator worked, return original with year removed
        var result = nameWithoutExt;
        if (!string.IsNullOrEmpty(year))
        {
            result = RemoveYearFromName(result, year);
        }

        logger?.LogDebug("Aka handling failed, returning original: {Result}", result);
        return result.Trim();
    }

    /// <summary>
    /// Checks if text is primarily English (contains only Latin characters, numbers, and common punctuation).
    /// </summary>
    /// <param name="text">The text to check.</param>
    /// <returns>True if the text appears to be English.</returns>
    private static bool IsEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var letterCount = 0;
        var nonLatinCount = 0;

        foreach (var c in text)
        {
            if (char.IsLetter(c))
            {
                letterCount++;
                // Allow Latin letters
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')))
                {
                    nonLatinCount++;
                }
            }

            // Allow numbers, spaces, and common punctuation
            else if (!(char.IsDigit(c) || c == ' ' || c == '(' || c == ')' || c == '-' || c == '.' ||
                      c == ',' || c == ':' || c == ';' || c == '!' || c == '?' ||
                      c == '\'' || c == '"' || c == '&' || c == '_'))
            {
                return false; // Contains unsupported characters
            }
        }

        // If more than 20% of letters are non-Latin, consider it non-English
        if (letterCount > 0 && (double)nonLatinCount / letterCount > 0.2)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Cleans invalid characters from the filename.
    /// </summary>
    /// <param name="filename">The filename to clean.</param>
    /// <returns>The cleaned filename.</returns>
    private static string CleanInvalidCharacters(string filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return filename;
        }

        var result = filename;

        foreach (var kvp in InvalidChars)
        {
            result = result.Replace(kvp.Key, kvp.Value, StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// Removes common release group tags and quality indicators.
    /// </summary>
    /// <param name="filename">The filename to clean.</param>
    /// <returns>The cleaned filename.</returns>
    private static string CleanReleaseInfo(string filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return filename;
        }

        // Remove quality indicators
        var patterns = new[]
        {
            @"\b(720p|1080p|2160p|4K|HD|CAM|TS|TC|DVDRip|BRRip|BluRay|WEB-DL|WEBRip|HDTV)\b",
            @"\b(x264|x265|H\.264|H\.265|AVC|HEVC)\b",
            @"\b(AAC|AC3|DTS|MP3|FLAC)\b",
            @"\[(.*?)\]", // Remove anything in square brackets
            @"\{(.*?)\}", // Remove anything in curly brackets
        };

        var result = filename;
        foreach (var pattern in patterns)
        {
            result = Regex.Replace(result, pattern, " ", RegexOptions.IgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// Creates a complete Jellyfin-friendly filename with proper movie formatting.
    /// This includes the movie name and year in the standard format: "Movie Name (YYYY)".
    /// </summary>
    /// <param name="originalName">The original filename.</param>
    /// <param name="logger">Optional logger for debugging.</param>
    /// <returns>A complete movie filename suitable for Jellyfin.</returns>
    public static string CreateMovieFilename(string originalName, ILogger? logger = null)
    {
        var baseName = CreateJellyfinFilename(originalName, logger);

        // Ensure we have a valid filename
        if (string.IsNullOrWhiteSpace(baseName) || baseName == "unknown")
        {
            logger?.LogWarning("Could not create valid filename from: {OriginalName}", originalName);
            var fallback = System.IO.Path.GetFileNameWithoutExtension(originalName);
            return !string.IsNullOrWhiteSpace(fallback) ? fallback : "unknown";
        }

        return baseName;
    }

    /// <summary>
    /// Validates and suggests improvements for a filename.
    /// </summary>
    /// <param name="filename">The filename to validate.</param>
    /// <returns>Validation result with suggestions.</returns>
    public static FilenameValidation ValidateFilename(string filename)
    {
        var validation = new FilenameValidation { OriginalFilename = filename ?? string.Empty };

        if (string.IsNullOrWhiteSpace(filename))
        {
            validation.IsValid = false;
            validation.Issues.Add("Filename is empty or whitespace");
            validation.SuggestedFilename = "unknown";
            return validation;
        }

        try
        {
            // Check for video extension
            if (!IsVideoFile(filename))
            {
                validation.Issues.Add("File does not appear to be a video file");
            }

            // Check for year
            var year = ExtractYear(filename);
            if (string.IsNullOrEmpty(year))
            {
                validation.Issues.Add("No year found in filename - consider adding (YYYY) for better organization");
            }

            // Check for non-English characters
            var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(filename);
            if (!string.IsNullOrEmpty(nameWithoutExt) && !IsEnglish(nameWithoutExt))
            {
                validation.Issues.Add("Filename contains non-English characters - may cause issues with some clients");
            }

            // Check for invalid characters
            var hasInvalidChars = InvalidChars.Keys.Any(invalidChar => filename.Contains(invalidChar, StringComparison.Ordinal));
            if (hasInvalidChars)
            {
                validation.Issues.Add("Filename contains characters that may cause issues on some filesystems");
            }

            validation.IsValid = validation.Issues.Count == 0;
            validation.SuggestedFilename = CreateJellyfinFilename(filename);
        }
        catch (Exception)
        {
            validation.IsValid = false;
            validation.Issues.Add("Error processing filename");
            validation.SuggestedFilename = "unknown";
        }

        return validation;
    }
}
