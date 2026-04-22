using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Integrations;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MailKit.Net.Smtp;
using MailKit.Security;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimeKit;

// using System.Net.NetworkCredential;

namespace Jellyfin.Plugin.Newsletters.Clients.Email;

/// <summary>
/// Interaction logic for SendMail.xaml.
/// </summary>
// [Route("newsletters/[controller]")]
[Authorize(Policy = Policies.RequiresElevation)]
[ApiController]
[Route("SmtpMailer")]
public class SmtpMailer(IServerApplicationHost appHost,
    Logger loggerInstance,
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManager,
    UpcomingMediaService upcomingMediaService) : Client(loggerInstance, dbInstance, libraryManager, upcomingMediaService), IClient
{
    private readonly IServerApplicationHost applicationHost = appHost;

    /// <summary>
    /// Sends a test email using a specific email configuration.
    /// </summary>
    /// <param name="configurationId">The ID of the Email configuration to test.</param>
    /// <returns>An <see cref="ActionResult"/> indicating success or failure.</returns>
    [HttpPost("SendTestMail")]
    public ActionResult SendTestMail([FromQuery] string configurationId)
    {
        try
        {
            if (string.IsNullOrEmpty(configurationId))
            {
                Logger.Error("Configuration ID is required for testing.");
                return BadRequest("Configuration ID is required for testing.");
            }

            var emailConfig = Config.EmailConfigurations
                .FirstOrDefault(c => c.Id == configurationId);

            if (emailConfig == null)
            {
                Logger.Error($"Email configuration with ID '{configurationId}' not found.");
                return BadRequest($"Email configuration with ID '{configurationId}' not found.");
            }

            if (!emailConfig.IsEnabled)
            {
                Logger.Info($"Email configuration '{emailConfig.Name}' is disabled. Aborting test message.");
                return BadRequest($"Email configuration '{emailConfig.Name}' is disabled. Aborting test message.");
            }

            Logger.Debug($"Sending out test mail for '{emailConfig.Name}'!");
            string smtpAddress = emailConfig.SMTPServer;
            int portNumber = emailConfig.SMTPPort;
            bool enableSSL = emailConfig.EnableSsl;
            string emailFromAddress = emailConfig.FromAddr;
            string username = emailConfig.SMTPUser;
            string password = emailConfig.SMTPPass;
            string emailToAddress = emailConfig.VisibleToAddr;
            string emailBccAddress = emailConfig.ToAddr;
            string subject = emailConfig.Subject;

            // Tuple format: (display-name, value)
            var requiredFields = new List<(string Name, string Value)>
            {
                (Name: "SMTP Server Address", Value: smtpAddress),
                (Name: "From Address", Value: emailFromAddress)
            };

            if (emailConfig.UseAuthentication)
            {
                requiredFields.Add((Name: "SMTP Username", Value: username));
                requiredFields.Add((Name: "SMTP Password", Value: password));
            }

            bool missingField = false;
            foreach (var (name, value) in requiredFields)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Logger.Error($"[EMAIL CONFIG] Required field not set for '{emailConfig.Name}': {name}");
                    missingField = true;
                }
            }

            // Check if at least one recipient field is provided
            if (string.IsNullOrWhiteSpace(emailToAddress) && string.IsNullOrWhiteSpace(emailBccAddress))
            {
                Logger.Error($"[EMAIL CONFIG] At least one recipient address (TO or BCC) must be provided for '{emailConfig.Name}'.");
                missingField = true;
            }

            if (missingField)
            {
                Logger.Error($"One or more required email configuration fields are missing for '{emailConfig.Name}'. Aborting send.");
                return BadRequest($"One or more required email configuration fields are missing for '{emailConfig.Name}'. Aborting send.");
            }

            HtmlBuilder hb = new(Logger, Db, emailConfig, LibraryManager, new List<JsonFileObj>());

            string body = hb.GetDefaultHTMLBody(emailConfig);
            string builtString = hb.BuildHtmlStringsForTest(emailConfig);
            builtString = hb.TemplateReplace(HtmlBuilder.ReplaceBodyWithBuiltString(body, builtString), "{ServerURL}", Config.Hostname);
            string currDate = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            builtString = builtString.Replace("{Date}", currDate, StringComparison.Ordinal);

            var mail = new MimeMessage();
            mail.From.Add(new MailboxAddress(emailFromAddress, emailFromAddress));
            mail.Subject = subject;

            if (!string.IsNullOrWhiteSpace(emailToAddress))
            {
                foreach (string email in emailToAddress.Split(','))
                {
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        mail.To.Add(MailboxAddress.Parse(email.Trim()));
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(emailBccAddress))
            {
                foreach (string email in emailBccAddress.Split(','))
                {
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        mail.Bcc.Add(MailboxAddress.Parse(email.Trim()));
                    }
                }
            }

            var bodyBuilder = new BodyBuilder();
            bodyBuilder.HtmlBody = Regex.Replace(builtString, "{[A-za-z]*}", " ");
            mail.Body = bodyBuilder.ToMessageBody();

            using (var client = new SmtpClient())
            {
                client.CheckCertificateRevocation = false;
                var secureOptions = enableSSL ? SecureSocketOptions.Auto : SecureSocketOptions.None;
                client.Connect(smtpAddress, portNumber, secureOptions);
                if (emailConfig.UseAuthentication)
                {
                    client.Authenticate(username, password);
                }

                client.Send(mail);
                client.Disconnect(true);
            }

            Logger.Debug($"Test email sent successfully for '{emailConfig.Name}'.");
            return Ok($"Test email sent successfully for '{emailConfig.Name}'.");
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
            return BadRequest("An error occurred while sending the test email.");
        }
    }

    /// <summary>
    /// Generates and sends the email newsletter to all configured recipients.
    /// </summary>
    /// <returns>
    /// True if at least one email was sent successfully; otherwise, false.
    /// </returns>
    [HttpPost("SendSmtp")]
    public bool SendEmail()
    {
        bool anySuccess = false;

        if (Config.EmailConfigurations.Count == 0)
        {
            Logger.Info("No Email configurations found. Aborting sending messages.");
            return false;
        }

        try
        {
            var (hasData, upcomingItems) = HasDataToSendAsync().GetAwaiter().GetResult();
            if (hasData)
            {
                // Iterate over all Email configurations
                foreach (var emailConfig in Config.EmailConfigurations)
                {
                    if (!emailConfig.IsEnabled)
                    {
                        Logger.Info($"Email configuration '{emailConfig.Name}' is disabled. Skipping.");
                        continue;
                    }
                    
                    Logger.Debug($"Sending email to '{emailConfig.Name}'!");

                    bool anyResult = SendToSmtp(emailConfig, emailConfig.NewsletterOnUpcomingItemEnabled ? upcomingItems : Array.Empty<JsonFileObj>());
                    anySuccess |= anyResult;
                }
            }
            else
            {
                Logger.Info("There is no Newsletter data, nor any upcoming media. Have I scanned or sent out an email newsletter recently?");
            }
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }

        return anySuccess;
    }

    /// <summary>
    /// Sends newsletter data to a specific SMTP configuration.
    /// </summary>
    /// <param name="emailConfig">The Email configuration to use.</param>
    /// <param name="upcomingItems">The prefetched list of upcoming media items.</param>
    /// <returns>True if the email was sent successfully; otherwise, false.</returns>
    private bool SendToSmtp(EmailConfiguration emailConfig, IReadOnlyList<JsonFileObj> upcomingItems)
    {
        bool anyResult = false;
        try
        {
            Logger.Debug($"Sending out mail for '{emailConfig.Name}'!");
            string smtpAddress = emailConfig.SMTPServer;
            int portNumber = emailConfig.SMTPPort;
            bool enableSSL = emailConfig.EnableSsl;
            string emailFromAddress = emailConfig.FromAddr;
            string username = emailConfig.SMTPUser;
            string password = emailConfig.SMTPPass;
            string emailToAddress = emailConfig.VisibleToAddr;
            string emailBccAddress = emailConfig.ToAddr;
            string subject = emailConfig.Subject;
            int smtpTimeout = 100000;

            // Tuple format: (display-name, value)
            var requiredFields = new List<(string Name, string Value)>
            {
                (Name: "SMTP Server Address", Value: smtpAddress),
                (Name: "From Address", Value: emailFromAddress)
            };

            if (emailConfig.UseAuthentication)
            {
                requiredFields.Add((Name: "SMTP Username", Value: username));
                requiredFields.Add((Name: "SMTP Password", Value: password));
            }

            bool missingField = false;
            foreach (var (name, value) in requiredFields)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Logger.Error($"[EMAIL CONFIG] Required field not set for '{emailConfig.Name}': {name}");
                    missingField = true;
                }
            }

            // Check if at least one recipient field is provided
            if (string.IsNullOrWhiteSpace(emailToAddress) && string.IsNullOrWhiteSpace(emailBccAddress))
            {
                Logger.Error($"[EMAIL CONFIG] At least one recipient address (TO or BCC) must be provided for '{emailConfig.Name}'.");
                missingField = true;
            }

            if (missingField)
            {
                Logger.Error($"One or more required email configuration fields are missing for '{emailConfig.Name}'. Aborting send.");
                return false;
            }

            HtmlBuilder hb = new(Logger, Db, emailConfig, LibraryManager, upcomingItems);

            string body = hb.GetDefaultHTMLBody(emailConfig);
            ReadOnlyCollection<(string HtmlString, List<(MemoryStream? ImageStream, string ContentId)> InlineImages)> chunks = hb.BuildChunkedHtmlStringsFromNewsletterData(applicationHost.SystemId, emailConfig);

            string currDate = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            int partNum = 1; // for multi-part email subjects if needed
            foreach (var (builtString, inlineImages) in chunks)
            {
                try
                {
                    Logger.Debug($"Email part {partNum} for '{emailConfig.Name}' image count: {inlineImages.Count}");
                    // Add template substitutions
                    string finalBody = hb.TemplateReplace(HtmlBuilder.ReplaceBodyWithBuiltString(body, builtString), "{ServerURL}", Config.Hostname)
                                        .Replace("{Date}", currDate, StringComparison.Ordinal);

                    var mail = new MimeMessage();
                    mail.From.Add(new MailboxAddress(emailFromAddress, emailFromAddress));

                    if (!string.IsNullOrWhiteSpace(emailToAddress))
                    {
                        foreach (string email in emailToAddress.Split(','))
                        {
                            if (!string.IsNullOrWhiteSpace(email))
                            {
                                mail.To.Add(MailboxAddress.Parse(email.Trim()));
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(emailBccAddress))
                    {
                        foreach (string email in emailBccAddress.Split(','))
                        {
                            if (!string.IsNullOrWhiteSpace(email))
                            {
                                mail.Bcc.Add(MailboxAddress.Parse(email.Trim()));
                            }
                        }
                    }

                    // Multi-part subject (optional, for clarity)
                    mail.Subject = (chunks.Count > 1)
                        ? $"{subject} (Part {partNum} of {chunks.Count})"
                        : subject;

                    var bodyBuilder = new BodyBuilder();

                    if (Config.PosterType == "attachment")
                    {
                        bodyBuilder.HtmlBody = finalBody;

                        foreach (var (stream, cid) in inlineImages)
                        {
                            if (stream == null)
                            {
                                Logger.Warn($"Skipped LinkedResource creation for cid {cid}: stream is null.");
                                continue;
                            }

                            stream.Position = 0;
                            var image = bodyBuilder.LinkedResources.Add(cid, stream.ToArray(), new ContentType("image", "jpeg"));
                            image.ContentId = cid;
                        }

                        // Increase timeout for larger attachments
                        smtpTimeout = 300000; // 5 minutes timeout
                    }
                    else
                    {
                        bodyBuilder.HtmlBody = Regex.Replace(finalBody, "{[A-za-z]*}", " ");
                    }

                    mail.Body = bodyBuilder.ToMessageBody();

                    using (var client = new SmtpClient())
                    {
                        client.Timeout = smtpTimeout;
                        client.CheckCertificateRevocation = false;
                        var secureOptions = enableSSL ? SecureSocketOptions.Auto : SecureSocketOptions.None;
                        client.Connect(smtpAddress, portNumber, secureOptions);
                        if (emailConfig.UseAuthentication)
                        {
                            client.Authenticate(username, password);
                        }
                        
                        Logger.Debug($"Sending email part {partNum} for '{emailConfig.Name}' with finalBody: {finalBody}");
                        client.Send(mail);
                        client.Disconnect(true);
                    }

                    Logger.Debug($"Email part {partNum} for '{emailConfig.Name}' sent successfully.");
                    hb.CleanUp(finalBody); // or as appropriate for the chunk
                    
                    // Track any successful send
                    anyResult |= true;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to send email part {partNum} for '{emailConfig.Name}': {ex.Message} - Continuing to next part.");
                }
                
                partNum++;
            }
        }
        catch (Exception e)
        {
            Logger.Error($"An error has occured while sending to '{emailConfig.Name}': " + e);
        }

        return anyResult;
    }

    /// <summary>
    /// Sends the email newsletter.
    /// </summary>
    /// <returns>
    /// True if at least one email was sent successfully; otherwise, false.
    /// </returns>
    public bool Send()
    {
        return SendEmail();
    }
}
