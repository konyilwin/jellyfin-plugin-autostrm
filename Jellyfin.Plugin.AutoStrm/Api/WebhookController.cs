using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.AutoStrm.Models;
using Jellyfin.Plugin.AutoStrm.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoStrm.Api;

/// <summary>
/// Webhook API controller for receiving media data.
/// </summary>
[ApiController]
[Route("AutoStrm")]
[Produces("application/json")]
public class WebhookController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<WebhookController> _logger;
    private readonly StrmFileService _strmFileService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookController"/> class.
    /// </summary>
    public WebhookController()
    {
        _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<WebhookController>.Instance;
        _strmFileService = new StrmFileService(_logger);
    }

    /// <summary>
    /// Receives webhook data and creates STRM files.
    /// </summary>
    /// <returns>A response indicating success or failure.</returns>
    [HttpPost("webhook")]
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

            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogWarning("Empty request body received");
                return BadRequest(new { error = "Empty request body" });
            }

            _logger.LogDebug("Request body: {RequestBody}", requestBody);

            // Parse JSON
            WebhookData webhookData;
            try
            {
                webhookData = JsonSerializer.Deserialize<WebhookData>(requestBody, JsonOptions)!;
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
                _logger.LogWarning("No media items found in webhook data");
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
                processed_count = webhookData.Data.Count
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult HealthCheck()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
