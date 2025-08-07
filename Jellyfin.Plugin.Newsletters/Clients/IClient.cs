#pragma warning disable 1591
using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Newsletters.Clients.Discord;
using Jellyfin.Plugin.Newsletters.Clients.Emails;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Mvc;

// using System.Net.NetworkCredential;

namespace Jellyfin.Plugin.Newsletters.Clients;

public interface IClient
{
    bool Send();

    void CopyNewsletterDataToArchive();
}