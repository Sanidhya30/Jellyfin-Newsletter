#pragma warning disable 1591
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Clients;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// using System.Net.NetworkCredential;

namespace Jellyfin.Plugin.Newsletters.Clients.Emails;

/// <summary>
/// Interaction logic for SendMail.xaml.
/// </summary>
// [Route("newsletters/[controller]")]
[Authorize(Policy = Policies.RequiresElevation)]
[ApiController]
[Route("Smtp")]
public class Smtp : Client, IClient
{
    public Smtp(IServerApplicationHost applicationHost) : base(applicationHost) 
    {
    }

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

            HtmlBuilder hb = new HtmlBuilder();

            string body = hb.GetDefaultHTMLBody();
            string builtString = hb.BuildHtmlStringsForTest();
            builtString = hb.TemplateReplace(hb.ReplaceBodyWithBuiltString(body, builtString), "{ServerURL}", Config.Hostname);
            string currDate = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            builtString = builtString.Replace("{Date}", currDate, StringComparison.Ordinal);

            MailMessage mail = new MailMessage();

            mail.From = new MailAddress(emailFromAddress, emailFromAddress);
            mail.To.Clear();
            mail.Bcc.Clear();
            mail.Subject = subject;
            mail.Body = Regex.Replace(builtString, "{[A-za-z]*}", " ");
            mail.IsBodyHtml = true;

            if (!string.IsNullOrWhiteSpace(emailToAddress))
            {
                foreach (string email in emailToAddress.Split(','))
                {
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        mail.To.Add(email.Trim());
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(emailBccAddress))
            {
                foreach (string email in emailBccAddress.Split(','))
                {
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        mail.Bcc.Add(email.Trim());
                    }
                }
            }

            SmtpClient smtp = new SmtpClient(smtpAddress, portNumber);
            smtp.Credentials = new NetworkCredential(username, password);
            smtp.EnableSsl = enableSSL;
            smtp.Send(mail);

            Logger.Debug($"Test email sent successfully sent.");
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }
    }

    [HttpPost("SendSmtp")]
    // [ProducesResponseType(StatusCodes.Status201Created)]
    // [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public bool SendEmail()
    {
        bool result = false;
        try
        {
            Db.CreateConnection();

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

                HtmlBuilder hb = new HtmlBuilder();

                string body = hb.GetDefaultHTMLBody();
                List<(string HtmlString, List<(MemoryStream? ImageStream, string ContentId)> InlineImages)> chunks = hb.BuildChunkedHtmlStringsFromNewsletterData();
                // string finalBody = hb.ReplaceBodyWithBuiltString(body, builtString);
                // string finalBody = hb.TemplateReplace(hb.ReplaceBodyWithBuiltString(body, builtString), "{ServerURL}", Config.Hostname);

                string currDate = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

                int partNum = 1; // for multi-part email subjects if needed
                foreach (var (builtString, inlineImages) in chunks)
                {
                    Logger.Debug($"Email part {partNum} image count: {inlineImages.Count}");
                    // Add template substitutions
                    string finalBody = hb.TemplateReplace(hb.ReplaceBodyWithBuiltString(body, builtString), "{ServerURL}", Config.Hostname)
                                        .Replace("{Date}", currDate, StringComparison.Ordinal);

                    using (MailMessage mail = new MailMessage())
                    {
                        mail.From = new MailAddress(emailFromAddress, emailFromAddress);
                        mail.IsBodyHtml = true;

                        if (!string.IsNullOrWhiteSpace(emailToAddress))
                        {
                            foreach (string email in emailToAddress.Split(','))
                            {
                                if (!string.IsNullOrWhiteSpace(email))
                                {
                                    mail.To.Add(email.Trim());
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(emailBccAddress))
                        {
                            foreach (string email in emailBccAddress.Split(','))
                            {
                                if (!string.IsNullOrWhiteSpace(email))
                                {
                                    mail.Bcc.Add(email.Trim());
                                }
                            }
                        }

                        // Multi-part subject (optional, for clarity)
                        mail.Subject = (chunks.Count > 1)
                            ? $"{subject} (Part {partNum} of {chunks.Count})"
                            : subject;

                        if (Config.PosterType == "attachment")
                        {
                            AlternateView htmlView = AlternateView.CreateAlternateViewFromString(finalBody, null, MediaTypeNames.Text.Html);

                            foreach (var (stream, cid) in inlineImages)
                            {
                                if (stream == null)
                                {
                                    Logger.Warn($"Skipped LinkedResource creation for cid {cid}: stream is null.");
                                    continue;
                                }

                                var imgRes = new LinkedResource(stream, MediaTypeNames.Image.Jpeg)
                                {
                                    ContentId = cid,
                                    TransferEncoding = TransferEncoding.Base64,
                                    ContentType = new ContentType("image/jpeg"),
                                    ContentLink = new Uri("cid:" + cid)
                                };
                                imgRes.ContentType.Name = cid;
                                htmlView.LinkedResources.Add(imgRes);
                            }

                            mail.AlternateViews.Add(htmlView);

                            // Increase timeout for larger attachments
                            // This is useful if the email contains large images or attachments
                            smtpTimeout = 300000; // 5 minutes timeout
                        }
                        else
                        {
                            mail.Body = Regex.Replace(finalBody, "{[A-za-z]*}", " ");
                        }

                        using (SmtpClient smtp = new SmtpClient(smtpAddress, portNumber))
                        {   
                            smtp.Credentials = new NetworkCredential(username, password);
                            smtp.EnableSsl = enableSSL;
                            smtp.Timeout = smtpTimeout;
                            Logger.Debug($"Sending email part {partNum} with finalBody: {finalBody}");
                            smtp.Send(mail);
                        }
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
        finally
        {
            Db.CloseConnection();
        }

        return result;
    }

    public bool Send()
    {
        return SendEmail();
    }
}
