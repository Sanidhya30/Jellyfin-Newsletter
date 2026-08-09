using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Newsletters.Preview;

/// <summary>
/// API controller for previewing and editing the contents of the next newsletter.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Newsletters/Preview")]
[Produces(MediaTypeNames.Application.Json)]
public class NewsletterPreviewController : ControllerBase
{
    private readonly NewsletterPreviewService previewService;
    private readonly Logger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NewsletterPreviewController"/> class.
    /// </summary>
    /// <param name="previewServiceInstance">The preview service instance.</param>
    /// <param name="loggerInstance">The logger instance.</param>
    public NewsletterPreviewController(NewsletterPreviewService previewServiceInstance, Logger loggerInstance)
    {
        previewService = previewServiceInstance;
        logger = loggerInstance;
    }

    /// <summary>
    /// Gets everything queued for the next newsletter.
    /// </summary>
    /// <returns>The grouped preview.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PreviewResponse> GetPreview()
    {
        logger.Debug("Building newsletter preview");
        return Ok(previewService.GetPending());
    }

    /// <summary>
    /// Excludes the given queued files from the next newsletter.
    /// </summary>
    /// <param name="selection">The filenames to exclude.</param>
    /// <returns>An <see cref="IActionResult"/> indicating success.</returns>
    [HttpPost("Exclude")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Exclude([FromBody] PreviewSelection selection)
    {
        if (selection?.Filenames is null || selection.Filenames.Count == 0)
        {
            return BadRequest(new { Message = "No items specified." });
        }

        int count = previewService.SetExcluded(selection.Filenames, true);
        return Ok(new { Message = $"Excluded {count} item(s) from the next newsletter.", Count = count });
    }

    /// <summary>
    /// Puts previously excluded files back into the next newsletter.
    /// </summary>
    /// <param name="selection">The filenames to re-include.</param>
    /// <returns>An <see cref="IActionResult"/> indicating success.</returns>
    [HttpPost("Include")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Include([FromBody] PreviewSelection selection)
    {
        if (selection?.Filenames is null || selection.Filenames.Count == 0)
        {
            return BadRequest(new { Message = "No items specified." });
        }

        int count = previewService.SetExcluded(selection.Filenames, false);
        return Ok(new { Message = $"Restored {count} item(s) to the next newsletter.", Count = count });
    }
}
