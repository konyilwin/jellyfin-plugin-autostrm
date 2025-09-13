using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AutoStrm.Services;

/// <summary>
/// Enumeration for media types.
/// </summary>
public enum MediaType
{
    /// <summary>
    /// Unknown media type.
    /// </summary>
    Unknown,

    /// <summary>
    /// Movie content.
    /// </summary>
    Movie,

    /// <summary>
    /// TV series content.
    /// </summary>
    TvSeries
}

/// <summary>
/// Service for detecting media type from filename using a weighted scoring system.
/// </summary>
public static class MediaTypeDetector
{
    /// <summary>
    /// Detects the media type from a filename using a weighted scoring system.
    /// </summary>
    /// <param name="filename">The filename to analyze.</param>
    /// <returns>The detected media type.</returns>
    public static MediaType DetectMediaType(string filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return MediaType.Unknown;
        }

        var movieScore = 0;
        var tvScore = 0;
        var lowerFilename = filename.ToLowerInvariant();

        // VERY STRONG TV SERIES INDICATORS (+20 points)
        if (Regex.IsMatch(filename, @"[Ss](\d+)[Ee](\d+)", RegexOptions.IgnoreCase)) // S01E01, s02e05
        {
            tvScore += 20;
        }

        if (Regex.IsMatch(filename, @"(\d+)x(\d+)")) // 1x01, 2x05
        {
            tvScore += 20;
        }

        // VERY STRONG MOVIE INDICATORS (+20 points)
        if (Regex.IsMatch(filename, @"\((19|20)\d{2}\)")) // (2003), (1985)
        {
            movieScore += 20;
        }

        // STRONG MOVIE INDICATORS (+15 points)
        if (Regex.IsMatch(filename, @"\b(19|20)\d{2}\b")) // Year without parentheses: 2003, 1985
        {
            movieScore += 15;
        }

        if (Regex.IsMatch(filename, @"\b(720p|1080p|4K|2160p|HDTV|BluRay|WEB-DL|WEBRip|DVDRip|BRRip)\b", RegexOptions.IgnoreCase))
        {
            movieScore += 15; // Quality indicators often mean movies
        }

        // STRONG TV SERIES INDICATORS (+15 points)
        if (Regex.IsMatch(filename, @"[Ss]eason\s*(\d+)", RegexOptions.IgnoreCase)) // Season 1, season 01
        {
            tvScore += 15;
        }

        if (Regex.IsMatch(filename, @"[Ee]pisode\s*(\d+)", RegexOptions.IgnoreCase)) // Episode 1, episode 01
        {
            tvScore += 15;
        }

        // MODERATE TV SERIES INDICATORS (+10 points)
        if (Regex.IsMatch(filename, @"\b(\d{1,2})(\d{2})\b")) // 101, 205 (season+episode combined)
        {
            // But only if no strong movie indicators
            if (movieScore < 15)
            {
                tvScore += 10;
            }
        }

        if (Regex.IsMatch(filename, @"[Pp]art\s*(\d+)", RegexOptions.IgnoreCase)) // Part 1, part 2
        {
            tvScore += 8; // Could be movie parts or TV episodes
        }

        // KEYWORD SCORING (+5 to +12 points)

        // Strong TV keywords
        string[] strongTvKeywords = { "series", "season", "episode", "pilot", "finale" };
        foreach (var keyword in strongTvKeywords)
        {
            if (lowerFilename.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                tvScore += 12;
            }
        }

        // Moderate TV keywords
        string[] moderateTvKeywords = { "show" };
        foreach (var keyword in moderateTvKeywords)
        {
            if (lowerFilename.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                tvScore += 8;
            }
        }

        // Strong movie keywords
        string[] strongMovieKeywords = { "movie", "film", "cinema", "theatrical" };
        foreach (var keyword in strongMovieKeywords)
        {
            if (lowerFilename.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                movieScore += 12;
            }
        }

        // PENALTY ADJUSTMENTS

        // If filename has both year and episode pattern, favor the stronger signal
        if (movieScore >= 15 && tvScore >= 15)
        {
            // Year in parentheses is usually stronger than episode patterns in titles
            if (Regex.IsMatch(filename, @"\((19|20)\d{2}\)"))
            {
                tvScore -= 10; // Reduce TV score
            }
        }

        // ADDITIONAL MOVIE INDICATORS (+5 points)

        // Common movie title patterns
        if (Regex.IsMatch(filename, @"\b(Director'?s?\s*Cut|Extended|Uncut|Remastered)\b", RegexOptions.IgnoreCase))
        {
            movieScore += 8;
        }

        // Franchise indicators (could be either, slight movie bias)
        if (Regex.IsMatch(filename, @"\b(II|III|IV|V|VI|VII|VIII|IX|X|\d+)\b"))
        {
            movieScore += 3;
        }

        // DECISION LOGIC

        // Clear winner (difference of 10+ points)
        if (Math.Abs(movieScore - tvScore) >= 10)
        {
            return tvScore > movieScore ? MediaType.TvSeries : MediaType.Movie;
        }

        // Close call - use additional tie-breakers
        if (tvScore == movieScore)
        {
            // Default to movie for tie-breaker
            return MediaType.Movie;
        }

        return tvScore > movieScore ? MediaType.TvSeries : MediaType.Movie;
    }

    /// <summary>
    /// Extracts series information from filename.
    /// </summary>
    /// <param name="filename">The filename to analyze.</param>
    /// <returns>Series information if detected.</returns>
    public static (string SeriesName, int? Season, int? Episode) ExtractSeriesInfo(string filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return (string.Empty, null, null);
        }

        // Try S01E01 pattern
        var match = Regex.Match(filename, @"^(.+?)\s*[Ss](\d+)[Ee](\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var seriesName = match.Groups[1].Value.Trim();
            var season = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var episode = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            return (seriesName, season, episode);
        }

        // Try 1x01 pattern
        match = Regex.Match(filename, @"^(.+?)\s*(\d+)x(\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var seriesName = match.Groups[1].Value.Trim();
            var season = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var episode = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            return (seriesName, season, episode);
        }

        // Just return the base name
        var baseName = System.IO.Path.GetFileNameWithoutExtension(filename);
        return (baseName, null, null);
    }

    /// <summary>
    /// Extracts movie information from filename.
    /// </summary>
    /// <param name="filename">The filename to analyze.</param>
    /// <returns>Movie information.</returns>
    public static (string MovieName, int? Year) ExtractMovieInfo(string filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return (string.Empty, null);
        }

        // Try to extract year in parentheses
        var match = Regex.Match(filename, @"^(.+?)\s*\((\d{4})\)");
        if (match.Success)
        {
            var movieName = match.Groups[1].Value.Trim();
            var year = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            return (movieName, year);
        }

        // Try to extract year without parentheses
        match = Regex.Match(filename, @"^(.+?)\s+(19|20)\d{2}\b");
        if (match.Success)
        {
            var movieName = match.Groups[1].Value.Trim();
            var yearMatch = Regex.Match(filename, @"\b(19|20)\d{2}\b");
            if (yearMatch.Success && int.TryParse(yearMatch.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
            {
                return (movieName, year);
            }
        }

        // No year found, return base name
        var baseName = System.IO.Path.GetFileNameWithoutExtension(filename);
        return (baseName, null);
    }
}
