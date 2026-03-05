using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Newsletters.Integrations;

/// <summary>
/// API controller for testing Radarr/Sonarr connections.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("UpcomingMedia")]
[Produces(MediaTypeNames.Application.Json)]
public class UpcomingMediaController : ControllerBase
{
    private readonly UpcomingMediaService upcomingMediaService;
    private readonly Logger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpcomingMediaController"/> class.
    /// </summary>
    /// <param name="upcomingMediaServiceInstance">The upcoming media service instance.</param>
    /// <param name="loggerInstance">The logger instance.</param>
    public UpcomingMediaController(UpcomingMediaService upcomingMediaServiceInstance, Logger loggerInstance)
    {
        upcomingMediaService = upcomingMediaServiceInstance;
        logger = loggerInstance;
    }

    private static PluginConfiguration Config => Plugin.Instance!.Configuration;

    /// <summary>
    /// Tests the connection to a specific Radarr instance by configuration ID.
    /// </summary>
    /// <param name="configurationId">The ID of the Radarr configuration to test.</param>
    /// <returns>An <see cref="IActionResult"/> indicating success or failure.</returns>
    [HttpPost("TestRadarr")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TestRadarrConnection([FromQuery] string configurationId)
    {
        var radarrConfig = Config.RadarrConfigurations.FirstOrDefault(c => c.Id == configurationId);
        if (radarrConfig == null)
        {
            return NotFound(new { Message = $"Radarr configuration '{configurationId}' not found." });
        }

        logger.Debug($"Testing connection to Radarr instance: '{radarrConfig.Name}' with URL: {radarrConfig.Url}");
        var success = await upcomingMediaService.TestConnectionAsync(radarrConfig.Url, radarrConfig.ApiKey).ConfigureAwait(false);
        if (success)
        {
            return Ok(new { Message = $"Radarr '{radarrConfig.Name}' connection successful!" });
        }

        return StatusCode(StatusCodes.Status500InternalServerError, new { Message = $"Failed to connect to Radarr '{radarrConfig.Name}'. Check URL and API key." });
    }

    /// <summary>
    /// Tests the connection to a specific Sonarr instance by configuration ID.
    /// </summary>
    /// <param name="configurationId">The ID of the Sonarr configuration to test.</param>
    /// <returns>An <see cref="IActionResult"/> indicating success or failure.</returns>
    [HttpPost("TestSonarr")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TestSonarrConnection([FromQuery] string configurationId)
    {
        var sonarrConfig = Config.SonarrConfigurations.FirstOrDefault(c => c.Id == configurationId);
        if (sonarrConfig == null)
        {
            return NotFound(new { Message = $"Sonarr configuration '{configurationId}' not found." });
        }

        logger.Debug($"Testing connection to Sonarr instance: '{sonarrConfig.Name}' with URL: {sonarrConfig.Url}");
        var success = await upcomingMediaService.TestConnectionAsync(sonarrConfig.Url, sonarrConfig.ApiKey).ConfigureAwait(false);
        if (success)
        {
            return Ok(new { Message = $"Sonarr '{sonarrConfig.Name}' connection successful!" });
        }

        return StatusCode(StatusCodes.Status500InternalServerError, new { Message = $"Failed to connect to Sonarr '{sonarrConfig.Name}'. Check URL and API key." });
    }
}
