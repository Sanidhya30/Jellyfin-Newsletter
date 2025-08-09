using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.Newsletters.Configuration;
using SQLitePCL;
using SQLitePCL.pretty;

namespace Jellyfin.Plugin.Newsletters.Shared.Database;

/// <summary>
/// Provides methods for interacting with the SQLite database used by the plugin.
/// </summary>
public class SQLiteDatabase
{
    private readonly PluginConfiguration config;
    private readonly string dbFilePath;
    private readonly string dbLockPath;
    private readonly Logger logger;
    private SQLiteDatabaseConnection? _db;
    // private bool writeLock;

    /// <summary>
    /// Initializes a new instance of the <see cref="SQLiteDatabase"/> class.
    /// </summary>
    /// <param name="loggerInstance">The logger instance to use for logging.</param>
    public SQLiteDatabase(Logger loggerInstance)
    {
        logger = loggerInstance;
        config = Plugin.Instance!.Configuration;
        SQLite3.EnableSharedCache = false;

        _ = raw.sqlite3_config(raw.SQLITE_CONFIG_MEMSTATUS, 0);

        _ = raw.sqlite3_config(raw.SQLITE_CONFIG_MULTITHREAD, 1);

        _ = raw.sqlite3_enable_shared_cache(1);

        ThreadSafeMode = raw.sqlite3_threadsafe();
        dbFilePath = config.DataPath + "/newsletters.db"; // get directory from config
        dbLockPath = dbFilePath + ".lock";
    }

    internal static int ThreadSafeMode { get; set; }

    /// <summary>
    /// Creates a connection to the SQLite database and initializes tables if necessary.
    /// </summary>
    public void CreateConnection()
    {
        if (!File.Exists(dbLockPath)) // Database is not locked
        {
            logger.Debug("Opening Database: " + dbFilePath);
            _db = SQLite3.Open(dbFilePath);
            File.WriteAllText(dbLockPath, string.Empty);
            InitDatabaase();
            // writeLock = true;
        }
        else
        {
            logger.Debug("Database lock file shows database is in use: " + dbLockPath);
        }
    }

    private void InitDatabaase()
    {
        // Filename = string.Empty;
        // Title = string.Empty;
        // Season = 0;
        // Episode = 0;
        // SeriesOverview = string.Empty;
        // ImageURL = string.Empty;
        // ItemID = string.Empty;
        // PosterPath = string.Empty;

        logger.Debug("Creating Tables...");
        string[] tableNames = { "CurrRunData", "CurrNewsletterData", "ArchiveData" };
        CreateTables(tableNames);
        logger.Debug("Done Init of tables");
    }

    private void CreateTables(string[] tables)
    {
        foreach (string table in tables)
        {
            ExecuteSQL("create table if not exists " + table + " (" +
                            "Filename TEXT NOT NULL," +
                            "Title TEXT," +
                            "Season INT," +
                            "Episode INT," +
                            "SeriesOverview TEXT," +
                            "ImageURL TEXT," +
                            "ItemID TEXT," +
                            "PosterPath TEXT," +
                            "Type TEXT," +
                            // "PremiereYear TEXT" +
                            // "RunTime INT" +
                            // "OfficialRating TEXT" +
                            // "CommunityRating REAL" +
                            "PRIMARY KEY (Filename)" +
                        ");");

            // ExecuteSQL("ALTER TABLE " + table + " ADD COLUMN Type TEXT;");
            // logger.Debug("Altering Table not needed since V0.6.2.0");
            // continue;
            logger.Info($"Altering DB table: {table}");
            // <TABLE_NAME, DATA_TYPE>
            Dictionary<string, string> new_cols = new Dictionary<string, string>();
            new_cols.Add("PremiereYear", "TEXT");
            new_cols.Add("RunTime", "INT");
            new_cols.Add("OfficialRating", "TEXT");
            new_cols.Add("CommunityRating", "REAL");

            var existingColumns = GetTableColumns(table);

            foreach (KeyValuePair<string, string> col in new_cols)
            {
                if (!existingColumns.Contains(col.Key))
                {
                    try
                    {
                        logger.Debug($"Adding column {col.Key} to table {table}...");
                        ExecuteSQL($"ALTER TABLE {table} ADD COLUMN {col.Key} {col.Value};");
                    }
                    catch (SQLiteException sle)
                    {
                        logger.Warn(sle);
                    }
                }
                else
                {
                    logger.Debug($"Column {col.Key} already exists in table {table}, skipping.");
                }
            }
        }
    }

    /// <summary>
    /// Executes a SQL query and returns the result as an enumerable of read-only lists of <see cref="ResultSetValue"/>.
    /// </summary>
    /// <param name="query">The SQL query to execute.</param>
    /// <returns>An enumerable containing the query results.</returns>
    public IEnumerable<IReadOnlyList<ResultSetValue>> Query(string query)
    {
        logger.Debug("Running Query: " + query);
        return _db.Query(query);
    }

    // private IStatement PrepareStatement(string query)
    // {
    //     return _db.PrepareStatement(query);
    // }

    /// <summary>
    /// Executes a SQL statement on the database.
    /// </summary>
    /// <param name="query">The SQL query to execute.</param>
    public void ExecuteSQL(string query)
    {
        logger.Debug("Executing SQL Statement: " + query);
        _db.Execute(query);
    }

    /// <summary>
    /// Closes the database connection and removes the lock file if it exists.
    /// </summary>
    public void CloseConnection()
    {
        if (File.Exists(dbLockPath)) // Database is locked
        {
            logger.Debug("Closing Database: " + dbFilePath);
            // _db.Close();
            File.Delete(dbLockPath);
            // logger.Debug("TYPE: " + conn.GetType());
            // writeLock = true;
        }
        else
        {
            logger.Debug("Database lock file does not exist. Database is not use: " + dbLockPath);
        }
    }

    private List<string> GetTableColumns(string tableName)
    {
        var columns = new List<string>();
        var query = $"PRAGMA table_info({tableName});";
        
        foreach (var row in _db.Query(query))
        {
            // Column name is at index 1
            string colName = row[1].ToString();
            columns.Add(colName);
        }

        return columns;
    }
}