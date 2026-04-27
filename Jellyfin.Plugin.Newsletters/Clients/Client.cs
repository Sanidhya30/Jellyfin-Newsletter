using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Integrations;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;

// using System.Net.NetworkCredential;

namespace Jellyfin.Plugin.Newsletters.Clients;

/// <summary>
/// Represents a base client for handling newsletter-related operations.
/// </summary>
public class Client(Logger loggerInstance,
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManagerInstance,
    UpcomingMediaService upcomingMediaServiceInstance) : ControllerBase
{
    /// <summary>
    /// Gets the upcoming media service instance.
    /// </summary>
    protected UpcomingMediaService UpcomingService { get; } = upcomingMediaServiceInstance;
    
    /// <summary>
    /// Gets the current plugin configuration.
    /// </summary>
    protected PluginConfiguration Config => Plugin.Instance!.Configuration;

    /// <summary>
    /// Gets the database instance.
    /// </summary>
    protected SQLiteDatabase Db { get; } = dbInstance;

    /// <summary>
    /// Gets the logger instance.
    /// </summary>
    protected Logger Logger { get; } = loggerInstance;

    /// <summary>
    /// Gets the library manager instance.
    /// </summary>
    protected ILibraryManager LibraryManager { get; } = libraryManagerInstance;

    /// <summary>
    /// Copies the current newsletter data to the archive and clears the current data.
    /// </summary>
    public void CopyNewsletterDataToArchive()
    {
        Logger.Info("Appending NewsletterData for Current Newsletter Cycle to Archive Database..");

        try
        {
            Db.CreateConnection();

            // copy tables - use INSERT OR REPLACE to handle potential conflicts
            Db.ExecuteSQL("INSERT OR REPLACE INTO ArchiveData SELECT * FROM CurrNewsletterData;");
            Db.ExecuteSQL("DELETE FROM CurrNewsletterData;");

            // Update and save the last published date
            Config.LastPublishedDate = DateTime.Now;
            Plugin.Instance!.SaveConfiguration();
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }
        finally
        {
            Db.CloseConnection();
        }
    }

    /// <summary>
    /// Checks if the current newsletter database is populated with any data.
    /// </summary>
    /// <returns>True if the database contains at least one entry; otherwise, false.</returns>
    protected bool NewsletterDbIsPopulated()
    {
        try
        {
            Db.CreateConnection();

            foreach (var row in Db.Query("SELECT COUNT(*) FROM CurrNewsletterData;"))
            {
                if (row is not null)
                {
                    if (int.Parse(row[0].ToString(), CultureInfo.CurrentCulture) > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
            return false;
        }
        finally
        {
            Db.CloseConnection();
        }
    }

    /// <summary>
    /// Checks if there is any data to send (either current DB or upcoming media).
    /// </summary>
    /// <returns>The task result contains a boolean indicating if there is data to send, and a list of prefetched upcoming items.</returns>
    protected async Task<(bool HasData, List<JsonFileObj> UpcomingItems)> HasDataToSendAsync()
    {
        bool dbPopulated = NewsletterDbIsPopulated();
        var upcomingItems = await UpcomingService.GetAllUpcomingAsync().ConfigureAwait(false);
        
        return (dbPopulated || upcomingItems.Count > 0, upcomingItems);
    }
}
