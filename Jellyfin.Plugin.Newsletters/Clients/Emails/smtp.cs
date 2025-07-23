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
using Jellyfin.Plugin.Newsletters.Clients.CLIENT;
using Jellyfin.Plugin.Newsletters.Clients.Emails.HTMLBuilder;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.LOGGER;
using Jellyfin.Plugin.Newsletters.Shared.DATA;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// using System.Net.NetworkCredential;

namespace Jellyfin.Plugin.Newsletters.Clients.Emails.EMAIL;

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
        MailMessage mail;
        SmtpClient smtp;

        try
        {
            Logger.Debug("Sending out test mail!");
            mail = new MailMessage();

            mail.From = new MailAddress(Config.FromAddr);
            mail.To.Clear();
            mail.Subject = "Jellyfin Newsletters - Test";
            mail.Body = "Success! You have properly configured your email notification settings";
            mail.IsBodyHtml = false;

            foreach (string email in Config.ToAddr.Split(','))
            {
                mail.Bcc.Add(email.Trim());
            }

            smtp = new SmtpClient(Config.SMTPServer, Config.SMTPPort);
            smtp.Credentials = new NetworkCredential(Config.SMTPUser, Config.SMTPPass);
            smtp.EnableSsl = true;
            smtp.Send(mail);
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
                string emailToAddress = Config.ToAddr;
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
                    (name: "To Address", value: emailToAddress),
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

                        // Add all Bccs every time (to keep code structure the same)
                        foreach (string email in emailToAddress.Split(','))
                        {
                            mail.Bcc.Add(email.Trim());
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
