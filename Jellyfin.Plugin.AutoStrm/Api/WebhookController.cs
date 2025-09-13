using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.AutoStrm.Models;
using Jellyfin.Plugin.AutoStrm.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoStrm.Api;

/// <summary>
/// Webhook API controller for receiving media data.
/// </summary>
[ApiController]
[Route("plugins/autostrm")]
[Produces("application/json")]
public class WebhookController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly ILogger<WebhookController> _logger;
    private readonly StrmFileService _strmFileService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookController"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public WebhookController(ILogger<WebhookController> logger)
    {
        _logger = logger;
        _strmFileService = new StrmFileService(logger);
    }

    /// <summary>
    /// Receives webhook data and creates STRM files.
    /// </summary>
    /// <returns>A response indicating success or failure.</returns>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ReceiveWebhook()
    {
        try
        {
            _logger.LogInformation("Received webhook request");

            // Read the request body
            using var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);

            _logger.LogInformation("Request body received: {Length} characters", requestBody?.Length ?? 0);

            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogWarning("Empty request body received");
                return BadRequest(new { error = "Empty request body" });
            }

            _logger.LogDebug("Request body: {RequestBody}", requestBody);

            // Parse JSON
            WebhookData? webhookData;
            try
            {
                _logger.LogDebug("About to deserialize JSON: {RequestBody}", requestBody);
                webhookData = JsonSerializer.Deserialize<WebhookData>(requestBody, JsonOptions);
                _logger.LogInformation(
                    "JSON parsed successfully. Code: {Code}, Data count: {Count}",
                    webhookData?.Code,
                    webhookData?.Data?.Count ?? 0);

                // If Data is empty but we know there's data in the JSON, try manual approach
                if (webhookData != null && (webhookData.Data == null || webhookData.Data.Count == 0))
                {
                    _logger.LogWarning("MANUAL FALLBACK TRIGGERED - Data array is empty, attempting manual deserialization");

                    using var document = JsonDocument.Parse(requestBody);
                    var root = document.RootElement;
                    if (root.TryGetProperty("data", out var dataElement) && dataElement.GetArrayLength() > 0)
                    {
                        var manualItems = JsonSerializer.Deserialize<List<MediaItem>>(dataElement.GetRawText(), JsonOptions);
                        if (manualItems != null && manualItems.Count > 0)
                        {
                            webhookData.Data = manualItems;
                            _logger.LogInformation("SUCCESS: Populated Data array manually with {Count} items", manualItems.Count);
                        }
                        else
                        {
                            _logger.LogError("FAILED: Manual deserialization returned null or empty list");
                        }
                    }
                    else
                    {
                        _logger.LogError("FAILED: Could not find data element in JSON or it was empty");
                    }
                }

                // Log first item for debugging
                if (webhookData?.Data?.Count > 0)
                {
                    var firstItem = webhookData.Data[0];
                    _logger.LogDebug(
                        "FINAL RESULT - First item - URL: {Url}, Name: {Name}, Parent: {Parent}",
                        firstItem.Url,
                        firstItem.Name,
                        firstItem.Parent);
                }
                else
                {
                    _logger.LogError("FINAL RESULT - Data array is still empty after all attempts");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse JSON from request body");
                return BadRequest(new { error = "Invalid JSON format", details = ex.Message });
            }

            if (webhookData == null)
            {
                _logger.LogWarning("Null webhook data after deserialization");
                return BadRequest(new { error = "Invalid webhook data" });
            }

            // Validate the webhook data
            if (webhookData.Code != 0)
            {
                _logger.LogWarning(
                    "Webhook data contains error code: {Code}, message: {Message}",
                    webhookData.Code,
                    webhookData.Msg);
                return BadRequest(new
                {
                    error = "Webhook data contains error",
                    code = webhookData.Code,
                    message = webhookData.Msg
                });
            }

            if (webhookData.Data == null || webhookData.Data.Count == 0)
            {
                _logger.LogWarning(
                    "No media items found in webhook data. Data is null: {IsNull}, Count: {Count}",
                    webhookData.Data == null,
                    webhookData.Data?.Count ?? 0);
                return BadRequest(new { error = "No media items found" });
            }

            // Get plugin configuration
            var config = Plugin.Instance?.Configuration;
            if (config == null)
            {
                _logger.LogError("Plugin configuration not found");
                return StatusCode(500, new { error = "Plugin configuration not available" });
            }

            // Create STRM files
            await _strmFileService.CreateStrmFilesAsync(webhookData, config).ConfigureAwait(false);

            _logger.LogInformation("Successfully processed {Count} media items", webhookData.Data.Count);

            return Ok(new
            {
                success = true,
                message = "STRM files created successfully",
                processed_count = webhookData.Data.Count,
                note = "Library scan may be needed to see new files in Jellyfin"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing webhook");
            return StatusCode(500, new { error = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Health check endpoint.
    /// </summary>
    /// <returns>A response indicating the service is running.</returns>
    [HttpGet("health")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult HealthCheck()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
