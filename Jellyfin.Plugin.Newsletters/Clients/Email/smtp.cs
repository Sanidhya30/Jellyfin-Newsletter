using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using MailKit.Net.Smtp;
using MailKit.Security;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
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
    SQLiteDatabase dbInstance) : Client(loggerInstance, dbInstance), IClient
{
    private readonly IServerApplicationHost applicationHost = appHost;

    /// <summary>
    /// Sends a test email using a specific email configuration.
    /// </summary>
    /// <param name="configurationId">The ID of the Email configuration to test.</param>
    [HttpPost("SendTestMail")]
    public void SendTestMail([FromQuery] string configurationId)
    {
        try
        {
            if (string.IsNullOrEmpty(configurationId))
            {
                Logger.Error("Configuration ID is required for testing.");
                return;
            }

            var emailConfig = Config.EmailConfigurations
                .FirstOrDefault(c => c.Id == configurationId);

            if (emailConfig == null)
            {
                Logger.Error($"Email configuration with ID '{configurationId}' not found.");
                return;
            }

            Logger.Debug($"Sending out test mail for '{emailConfig.Name}'!");
            string smtpAddress = emailConfig.SMTPServer;
            int portNumber = emailConfig.SMTPPort;
            bool enableSSL = true;
            string emailFromAddress = emailConfig.FromAddr;
            string username = emailConfig.SMTPUser;
            string password = emailConfig.SMTPPass;
            string emailToAddress = emailConfig.VisibleToAddr;
            string emailBccAddress = emailConfig.ToAddr;
            string subject = emailConfig.Subject;

            // Tuple format: (display-name, value)
            var requiredFields = new[]
            {
                (name: "SMTP Server Address", value: smtpAddress),
                (name: "From Address", value: emailFromAddress),
                (name: "SMTP Username", value: username),
                (name: "SMTP Password", value: password),
            };

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
                return;
            }

            HtmlBuilder hb = new(Logger, Db, emailConfig);

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
                var secureOptions = enableSSL ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
                client.Connect(smtpAddress, portNumber, secureOptions);
                client.Authenticate(username, password);
                client.Send(mail);
                client.Disconnect(true);
            }

            Logger.Debug($"Test email sent successfully for '{emailConfig.Name}'.");
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
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
            if (NewsletterDbIsPopulated())
            {
                // Iterate over all Email configurations
                foreach (var emailConfig in Config.EmailConfigurations)
                {
                    if (string.IsNullOrEmpty(emailConfig.SMTPServer) || string.IsNullOrEmpty(emailConfig.SMTPUser))
                    {
                        Logger.Info($"Email configuration '{emailConfig.Name}' has no SMTP server or user. Skipping.");
                        continue;
                    }

                    Logger.Debug($"Sending email to '{emailConfig.Name}'!");

                    bool result = SendToSmtp(emailConfig);
                    anySuccess |= result;
                }
            }
            else
            {
                Logger.Info("There is no Newsletter data.. Have I scanned or sent out an email newsletter recently?");
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
    /// <returns>True if the email was sent successfully; otherwise, false.</returns>
    private bool SendToSmtp(EmailConfiguration emailConfig)
    {
        bool result = false;
        try
        {
            Logger.Debug($"Sending out mail for '{emailConfig.Name}'!");
            string smtpAddress = emailConfig.SMTPServer;
            int portNumber = emailConfig.SMTPPort;
            bool enableSSL = true;
            string emailFromAddress = emailConfig.FromAddr;
            string username = emailConfig.SMTPUser;
            string password = emailConfig.SMTPPass;
            string emailToAddress = emailConfig.VisibleToAddr;
            string emailBccAddress = emailConfig.ToAddr;
            string subject = emailConfig.Subject;
            int smtpTimeout = 100000;

            // Tuple format: (display-name, value)
            var requiredFields = new[]
            {
                (name: "SMTP Server Address", value: smtpAddress),
                (name: "From Address", value: emailFromAddress),
                (name: "SMTP Username", value: username),
                (name: "SMTP Password", value: password),
            };

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

            HtmlBuilder hb = new(Logger, Db, emailConfig);

            string body = hb.GetDefaultHTMLBody(emailConfig);
            ReadOnlyCollection<(string HtmlString, List<(MemoryStream? ImageStream, string ContentId)> InlineImages)> chunks = hb.BuildChunkedHtmlStringsFromNewsletterData(applicationHost.SystemId, emailConfig);

            string currDate = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            int partNum = 1; // for multi-part email subjects if needed
            foreach (var (builtString, inlineImages) in chunks)
            {
                result = false;
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
                    var secureOptions = enableSSL ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
                    client.Connect(smtpAddress, portNumber, secureOptions);
                    client.Authenticate(username, password);
                    Logger.Debug($"Sending email part {partNum} for '{emailConfig.Name}' with finalBody: {finalBody}");
                    client.Send(mail);
                    client.Disconnect(true);
                }

                Logger.Debug($"Email part {partNum} for '{emailConfig.Name}' sent successfully.");
                hb.CleanUp(finalBody); // or as appropriate for the chunk
                result = true;
                partNum++;
            }
        }
        catch (Exception e)
        {
            Logger.Error($"An error has occured while sending to '{emailConfig.Name}': " + e);
        }

        return result;
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
