#pragma warning disable 1591
using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Mvc;

// using System.Net.NetworkCredential;

namespace Jellyfin.Plugin.Newsletters.Clients;

public class Client(Logger loggerInstance,
    SQLiteDatabase dbInstance) : ControllerBase
{
    protected PluginConfiguration Config { get; } = Plugin.Instance!.Configuration;

    protected SQLiteDatabase Db { get; set; } = dbInstance;

    protected Logger Logger { get; set; } = loggerInstance;

    public void CopyNewsletterDataToArchive()
    {
        Logger.Info("Appending NewsletterData for Current Newsletter Cycle to Archive Database..");

        try
        {
            Db.CreateConnection();

            // copy tables
            Db.ExecuteSQL("INSERT INTO ArchiveData SELECT * FROM CurrNewsletterData;");
            Db.ExecuteSQL("DELETE FROM CurrNewsletterData;");
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
}