using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using MailKit.Net.Smtp;
using MailKit.Security;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimeKit;

// using System.Net.NetworkCredential;

namespace Jellyfin.Plugin.Newsletters.Clients.Emails;

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
    /// Sends a test email using the current SMTP configuration.
    /// </summary>
    [HttpPost("SendTestMail")]
    public void SendTestMail()
    {
        try
        {
            Logger.Debug("Sending out test mail!");
            string smtpAddress = Config.SMTPServer;
            int portNumber = Config.SMTPPort;
            bool enableSSL = true;
            string emailFromAddress = Config.FromAddr;
            string username = Config.SMTPUser;
            string password = Config.SMTPPass;
            string emailToAddress = Config.VisibleToAddr;
            string emailBccAddress = Config.ToAddr;
            string subject = Config.Subject;
            // string body;

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
                    Logger.Error($"[EMAIL CONFIG] Required field not set: {name}");
                    missingField = true;
                }
            }

            // Check if at least one recipient field is provided
            if (string.IsNullOrWhiteSpace(emailToAddress) && string.IsNullOrWhiteSpace(emailBccAddress))
            {
                Logger.Error("[EMAIL CONFIG] At least one recipient address (TO or BCC) must be provided.");
                missingField = true;
            }

            if (missingField)
            {
                Logger.Error("One or more required email configuration fields are missing. Aborting send.");
                return;
            }

            HtmlBuilder hb = new(Logger, Db);

            string body = hb.GetDefaultHTMLBody;
            string builtString = hb.BuildHtmlStringsForTest();
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
                var secureOptions = enableSSL ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
                client.Connect(smtpAddress, portNumber, secureOptions);
                client.Authenticate(username, password);
                client.Send(mail);
                client.Disconnect(true);
            }

            Logger.Debug($"Test email sent successfully sent.");
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }
    }

    /// <summary>
    /// Generates and sends the email newsletter.
    /// </summary>
    /// <returns>
    /// True if the email was sent successfully; otherwise, false.
    /// </returns>
    [HttpPost("SendSmtp")]
    // [ProducesResponseType(StatusCodes.Status201Created)]
    // [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public bool SendEmail()
    {
        bool result = false;
        try
        {
            if (NewsletterDbIsPopulated())
            {
                Logger.Debug("Sending out mail!");
                string smtpAddress = Config.SMTPServer;
                int portNumber = Config.SMTPPort;
                bool enableSSL = true;
                string emailFromAddress = Config.FromAddr;
                string username = Config.SMTPUser;
                string password = Config.SMTPPass;
                string emailToAddress = Config.VisibleToAddr;
                string emailBccAddress = Config.ToAddr;
                string subject = Config.Subject;
                int smtpTimeout = 100000;
                // string body;

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
                        Logger.Error($"[EMAIL CONFIG] Required field not set: {name}");
                        missingField = true;
                    }
                }

                // Check if at least one recipient field is provided
                if (string.IsNullOrWhiteSpace(emailToAddress) && string.IsNullOrWhiteSpace(emailBccAddress))
                {
                    Logger.Error("[EMAIL CONFIG] At least one recipient address (TO or BCC) must be provided.");
                    missingField = true;
                }

                if (missingField)
                {
                    Logger.Error("One or more required email configuration fields are missing. Aborting send.");
                    return false;
                }

                HtmlBuilder hb = new(Logger, Db);

                string body = hb.GetDefaultHTMLBody;
                ReadOnlyCollection<(string HtmlString, List<(MemoryStream? ImageStream, string ContentId)> InlineImages)> chunks = hb.BuildChunkedHtmlStringsFromNewsletterData(applicationHost.SystemId);
                // string finalBody = hb.ReplaceBodyWithBuiltString(body, builtString);
                // string finalBody = hb.TemplateReplace(hb.ReplaceBodyWithBuiltString(body, builtString), "{ServerURL}", Config.Hostname);

                string currDate = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

                int partNum = 1; // for multi-part email subjects if needed
                foreach (var (builtString, inlineImages) in chunks)
                {
                    Logger.Debug($"Email part {partNum} image count: {inlineImages.Count}");
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
                        // This is useful if the email contains large images or attachments
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
                        var secureOptions = enableSSL ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
                        
                        client.Connect(smtpAddress, portNumber, secureOptions);
                        client.Authenticate(username, password);
                        Logger.Debug($"Sending email part {partNum} with finalBody: {finalBody}");
                        client.Send(mail);
                        client.Disconnect(true);
                    }

                    Logger.Debug($"Email part {partNum} sent successfully.");
                    hb.CleanUp(finalBody); // or as appropriate for the chunk
                    partNum++;
                }

                result = true;
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

        return result;
    }

    /// <summary>
    /// Sends the email newsletter.
    /// </summary>
    /// <returns>
    /// True if the email was sent successfully; otherwise, false.
    /// </returns>
    public bool Send()
    {
        return SendEmail();
    }
}
