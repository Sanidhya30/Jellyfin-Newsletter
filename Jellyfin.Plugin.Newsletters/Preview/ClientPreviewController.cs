using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Clients.Discord;
using Jellyfin.Plugin.Newsletters.Clients.Email;
using Jellyfin.Plugin.Newsletters.Clients.Matrix;
using Jellyfin.Plugin.Newsletters.Clients.Telegram;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Integrations;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Newsletters.Preview;

/// <summary>
/// Renders what the next newsletter would look like for one client configuration, without sending anything.
/// The configuration is posted in the request body, so unsaved changes on the config page are previewed too.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Newsletters/ClientPreview")]
public class ClientPreviewController(
    IServerApplicationHost appHost,
    Logger loggerInstance,
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManager,
    UpcomingMediaService upcomingService) : ControllerBase
{
    private const string EmptyMessage = "<p>Nothing is queued for the next newsletter with this configuration.</p>";

    // Page styles. The preview runs in an isolated iframe, so these are the only styles applied.
    private const string BaseCss = "body{margin:0;padding:16px;font:14px/1.4 'Segoe UI',Arial,sans-serif}";
    private const string MatrixCss = BaseCss + "body{background:#fff;color:#2e2f32}img{max-width:100%}";
    private const string TelegramCss = BaseCss + "body{background:#dfe7ec;color:#000}"
        + ".msg{max-width:420px;margin:0 0 8px;background:#fff;border-radius:12px;overflow:hidden}"
        + ".msg img{display:block;width:100%}.txt{padding:8px 12px;white-space:pre-wrap}.link{color:#168acd}";

    private const string DiscordCss = BaseCss + "body{background:#313338;color:#dbdee1}"
        + ".embed{max-width:520px;margin:0 0 8px;padding:8px 16px 16px 12px;background:#2b2d31;border-left:4px solid;border-radius:4px}"
        + ".thumb{float:right;width:80px;margin:8px 0 0 16px;border-radius:4px}"
        + ".title{margin-top:8px;color:#00a8fc;font-weight:600}.desc,.val{margin-top:4px;white-space:pre-wrap}"
        + ".fields{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin-top:8px}"
        + ".name{font-size:12px;font-weight:600}.wide{grid-column:1/-1}";

    /// <summary>
    /// Previews the next email newsletter.
    /// </summary>
    /// <param name="config">The (possibly unsaved) email configuration.</param>
    /// <returns>An HTML page.</returns>
    [HttpPost("Email")]
    public Task<ActionResult> PreviewEmail([FromBody] EmailConfiguration config)
    {
        return RenderAsync(async () =>
        {
            var upcoming = await GetUpcomingAsync(config.NewsletterOnUpcomingItemEnabled).ConfigureAwait(false);
            var builder = new HtmlBuilder(loggerInstance, dbInstance, config, libraryManager, upcoming) { PreviewMode = true };

            string body = builder.GetDefaultHTMLBody(config);
            var chunks = builder.BuildChunkedHtmlStringsFromNewsletterData(appHost.SystemId, config);

            // Same assembly steps as SmtpMailer: fill the body template, then blank any unused {tags}.
            // A newsletter only splits into several emails when it is huge; if so, they are shown one after another.
            var emails = chunks.Select(chunk => Regex.Replace(
                builder.ReplaceBodyPlaceholders(HtmlBuilder.ReplaceBodyWithBuiltString(body, chunk.HtmlString), config),
                "{[A-za-z]*}",
                " "));

            return chunks.Count == 0 ? Page(BaseCss, EmptyMessage) : string.Join("<hr>", emails);
        });
    }

    /// <summary>
    /// Previews the next Matrix newsletter.
    /// </summary>
    /// <param name="config">The (possibly unsaved) Matrix configuration.</param>
    /// <returns>An HTML page.</returns>
    [HttpPost("Matrix")]
    public Task<ActionResult> PreviewMatrix([FromBody] MatrixConfiguration config)
    {
        return RenderAsync(async () =>
        {
            var upcoming = await GetUpcomingAsync(config.NewsletterOnUpcomingItemEnabled).ConfigureAwait(false);
            var builder = new MatrixMessageBuilder(loggerInstance, dbInstance, libraryManager, upcoming) { PreviewMode = true };

            string html = builder.BuildMessageFromNewsletterData(appHost.SystemId, config);
            html = builder.ReplaceBodyPlaceholders(html, config);

            // Matrix clients read data-mx-color; browsers need a CSS colour.
            html = Regex.Replace(html, @"data-mx-color=([""'])(#[0-9A-Fa-f]{3,8})\1", "style=\"color:$2\"");
            return Page(MatrixCss, html);
        });
    }

    /// <summary>
    /// Previews the next Telegram messages.
    /// </summary>
    /// <param name="config">The (possibly unsaved) Telegram configuration.</param>
    /// <returns>An HTML page.</returns>
    [HttpPost("Telegram")]
    public Task<ActionResult> PreviewTelegram([FromBody] TelegramConfiguration config)
    {
        return RenderAsync(async () =>
        {
            var upcoming = await GetUpcomingAsync(config.NewsletterOnUpcomingItemEnabled).ConfigureAwait(false);
            var builder = new TelegramMessageBuilder(loggerInstance, dbInstance, libraryManager, upcoming) { PreviewMode = true };

            var html = new StringBuilder();
            foreach (var (text, imageUrl, _, _) in builder.BuildMessagesFromNewsletterData(appHost.SystemId, config))
            {
                html.Append("<div class='msg'>");
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    html.Append("<img src='").Append(WebUtility.HtmlEncode(imageUrl)).Append("'>");
                }

                html.Append("<div class='txt'>").Append(TelegramToHtml(text)).Append("</div></div>");
            }

            return Page(TelegramCss, html.Length == 0 ? EmptyMessage : html.ToString());
        });
    }

    /// <summary>
    /// Previews the next Discord embeds.
    /// </summary>
    /// <param name="config">The (possibly unsaved) Discord configuration.</param>
    /// <returns>An HTML page.</returns>
    [HttpPost("Discord")]
    public Task<ActionResult> PreviewDiscord([FromBody] DiscordConfiguration config)
    {
        return RenderAsync(async () =>
        {
            var upcoming = await GetUpcomingAsync(config.NewsletterOnUpcomingItemEnabled).ConfigureAwait(false);
            var builder = new EmbedBuilder(loggerInstance, dbInstance, libraryManager, upcoming) { PreviewMode = true };

            var html = new StringBuilder();
            foreach (var (embed, _, _) in builder.BuildEmbedsFromNewsletterData(appHost.SystemId, config))
            {
                html.Append(EmbedToHtml(embed));
            }

            return Page(DiscordCss, html.Length == 0 ? EmptyMessage : html.ToString());
        });
    }

    /// <summary>
    /// Runs a preview builder and returns its HTML, or a 500 if it fails.
    /// </summary>
    private async Task<ActionResult> RenderAsync(Func<Task<string>> build)
    {
        try
        {
            return Content(await build().ConfigureAwait(false), MediaTypeNames.Text.Html);
        }
        catch (Exception e)
        {
            loggerInstance.Error("Could not build the client preview: " + e);
            return StatusCode(StatusCodes.Status500InternalServerError, "Could not build the preview.");
        }
    }

    /// <summary>
    /// Fetches upcoming items only when the client has them enabled, as the real send does.
    /// </summary>
    private async Task<IReadOnlyList<JsonFileObj>> GetUpcomingAsync(bool enabled)
    {
        if (!enabled)
        {
            return Array.Empty<JsonFileObj>();
        }

        return await upcomingService.GetAllUpcomingAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Wraps a fragment in a complete HTML page with the given styles.
    /// </summary>
    private static string Page(string css, string body)
    {
        return "<!DOCTYPE html><html><head><meta charset='utf-8'><style>" + css + "</style></head><body>" + body + "</body></html>";
    }

    /// <summary>
    /// Converts a Telegram MarkdownV2 message to simple HTML: *bold*, [link text](url) and \-escapes.
    /// </summary>
    private static string TelegramToHtml(string markdown)
    {
        bool bold = false;

        // Tokens, in order: \x escape, *, [, ](url), a run of plain text, any other single character.
        return Regex.Replace(
            markdown,
            @"\\(.)|\*|\[|\]\([^)]*\)|[^\\*\[\]]+|.",
            match =>
            {
                string token = match.Value;
                if (token[0] == '\\')
                {
                    return WebUtility.HtmlEncode(match.Groups[1].Value);
                }

                if (token == "*")
                {
                    bold = !bold;
                    return bold ? "<b>" : "</b>";
                }

                if (token == "[")
                {
                    return "<span class='link'>";
                }

                return token.StartsWith("](", StringComparison.Ordinal) ? "</span>" : WebUtility.HtmlEncode(token);
            },
            RegexOptions.Singleline);
    }

    /// <summary>
    /// Draws one Discord embed as an HTML card.
    /// </summary>
    private static string EmbedToHtml(Embed embed)
    {
        var html = new StringBuilder();
        html.Append("<div class='embed' style='border-left-color:#").Append(embed.Color.ToString("X6", CultureInfo.InvariantCulture)).Append("'>");

        if (!string.IsNullOrEmpty(embed.Thumbnail?.Url))
        {
            html.Append("<img class='thumb' src='").Append(WebUtility.HtmlEncode(embed.Thumbnail.Url)).Append("'>");
        }

        html.Append("<div class='title'>").Append(WebUtility.HtmlEncode(embed.Title ?? string.Empty)).Append("</div>");

        // Discord shows **text** as bold.
        string description = Regex.Replace(WebUtility.HtmlEncode(embed.Description ?? string.Empty), @"\*\*(.+?)\*\*", "<b>$1</b>");
        html.Append("<div class='desc'>").Append(description).Append("</div><div class='fields'>");

        foreach (var field in embed.Fields ?? Enumerable.Empty<EmbedField>())
        {
            html.Append(field.Inline ? "<div>" : "<div class='wide'>")
                .Append("<div class='name'>").Append(WebUtility.HtmlEncode(field.Name ?? string.Empty)).Append("</div>")
                .Append("<div class='val'>").Append(WebUtility.HtmlEncode(field.Value ?? string.Empty)).Append("</div></div>");
        }

        return html.Append("</div></div>").ToString();
    }
}
