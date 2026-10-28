using System.Data;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Προσθέτει στη βάση τις νέες στήλες που λείπουν από παλιότερες εκδόσεις,
/// χωρίς να πειράζει τα υπάρχοντα δεδομένα.
/// </summary>
public static class DbSchema
{
    public static void Upgrade(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;

        if (wasClosed)
        {
            connection.Open();
        }

        try
        {
            Execute(connection, """
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Id INTEGER NOT NULL CONSTRAINT PK_AppSettings PRIMARY KEY,
                    SchoolTitle TEXT NOT NULL,
                    SchoolSubtitle TEXT NOT NULL,
                    LogoPath TEXT NULL
                );
                """);

            AddColumnIfMissing(connection, "Computers", "MachineType", "TEXT NOT NULL DEFAULT 'Υπολογιστής'");
            AddColumnIfMissing(connection, "Computers", "LastSeenOnline", "TEXT NULL");
            AddColumnIfMissing(connection, "Computers", "Vendor", "TEXT NULL");
            AddColumnIfMissing(connection, "Computers", "OpenPorts", "TEXT NULL");
            AddColumnIfMissing(connection, "Computers", "DetectedMac", "TEXT NULL");
            AddColumnIfMissing(connection, "Computers", "Specs", "TEXT NULL");

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS StatusSnapshots (
                    Id INTEGER NOT NULL CONSTRAINT PK_StatusSnapshots PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Online INTEGER NOT NULL,
                    Total INTEGER NOT NULL
                );
                """);

            Execute(connection, "CREATE INDEX IF NOT EXISTS IX_StatusSnapshots_Timestamp ON StatusSnapshots (Timestamp);");

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS DeviceHours (
                    Id INTEGER NOT NULL CONSTRAINT PK_DeviceHours PRIMARY KEY AUTOINCREMENT,
                    ComputerId INTEGER NOT NULL,
                    Hour TEXT NOT NULL,
                    Checks INTEGER NOT NULL,
                    OnlineChecks INTEGER NOT NULL,
                    Drops INTEGER NOT NULL
                );
                """);

            Execute(connection, "CREATE UNIQUE INDEX IF NOT EXISTS IX_DeviceHours_ComputerId_Hour ON DeviceHours (ComputerId, Hour);");

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS Faults (
                    Id INTEGER NOT NULL CONSTRAINT PK_Faults PRIMARY KEY AUTOINCREMENT,
                    ComputerId INTEGER NOT NULL,
                    Description TEXT NOT NULL,
                    Severity TEXT NOT NULL,
                    ReportedBy TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    ResolvedAt TEXT NULL,
                    Resolution TEXT NULL
                );
                """);

            Execute(connection, "CREATE INDEX IF NOT EXISTS IX_Faults_ComputerId_ResolvedAt ON Faults (ComputerId, ResolvedAt);");

            AddColumnIfMissing(connection, "Computers", "VeyonDown", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "Computers", "VeyonDownSince", "TEXT NULL");
            AddColumnIfMissing(connection, "Computers", "IsDynamic", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "Computers", "UpdatedBy", "TEXT NULL");
            AddColumnIfMissing(connection, "Computers", "NoAutoShutdown", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "Faults", "ResolvedBy", "TEXT NULL");
            AddColumnIfMissing(connection, "Faults", "CreatedBy", "TEXT NULL");

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS Consumables (
                    Id INTEGER NOT NULL CONSTRAINT PK_Consumables PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    CompatibleWith TEXT NULL,
                    Quantity INTEGER NOT NULL,
                    MinQuantity INTEGER NOT NULL,
                    StoredAt TEXT NULL,
                    Notes TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NULL
                );
                """);

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS ConsumableLogs (
                    Id INTEGER NOT NULL CONSTRAINT PK_ConsumableLogs PRIMARY KEY AUTOINCREMENT,
                    ConsumableId INTEGER NOT NULL,
                    Change INTEGER NOT NULL,
                    QuantityAfter INTEGER NOT NULL,
                    Note TEXT NULL,
                    "By" TEXT NULL,
                    "At" TEXT NOT NULL
                );
                """);

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS PlanItems (
                    Id INTEGER NOT NULL CONSTRAINT PK_PlanItems PRIMARY KEY AUTOINCREMENT,
                    Location TEXT NOT NULL,
                    ComputerId INTEGER NULL,
                    Label TEXT NULL,
                    Kind TEXT NOT NULL,
                    X REAL NOT NULL,
                    Y REAL NOT NULL,
                    W REAL NOT NULL,
                    H REAL NOT NULL
                );
                """);

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS RoomPlans (
                    Id INTEGER NOT NULL CONSTRAINT PK_RoomPlans PRIMARY KEY AUTOINCREMENT,
                    Location TEXT NOT NULL,
                    AspectRatio REAL NOT NULL,
                    BackgroundPath TEXT NULL,
                    UpdatedAt TEXT NULL
                );
                """);

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS SchoolClasses (
                    Id INTEGER NOT NULL CONSTRAINT PK_SchoolClasses PRIMARY KEY AUTOINCREMENT,
                    Location TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    Teacher TEXT NULL,
                    Schedule TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """);

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS Seats (
                    Id INTEGER NOT NULL CONSTRAINT PK_Seats PRIMARY KEY AUTOINCREMENT,
                    SchoolClassId INTEGER NOT NULL,
                    ComputerId INTEGER NOT NULL,
                    StudentName TEXT NOT NULL
                );
                """);

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS PasswordRecords (
                    Id INTEGER NOT NULL CONSTRAINT PK_PasswordRecords PRIMARY KEY AUTOINCREMENT,
                    Account TEXT NOT NULL,
                    ComputerId INTEGER NULL,
                    Target TEXT NULL,
                    Secret TEXT NULL,
                    SetBy TEXT NULL,
                    SetAt TEXT NOT NULL,
                    Note TEXT NULL,
                    BatchId TEXT NULL
                );
                """);

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS Users (
                    Id INTEGER NOT NULL CONSTRAINT PK_Users PRIMARY KEY AUTOINCREMENT,
                    UserName TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    PasswordHash TEXT NOT NULL,
                    IsAdmin INTEGER NOT NULL DEFAULT 0,
                    IsDisabled INTEGER NOT NULL DEFAULT 0,
                    SecurityStamp TEXT NOT NULL,
                    MustChangePassword INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    LastLoginAt TEXT NULL
                );
                """);
            Execute(connection, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_UserName ON Users (UserName);");

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS Documents (
                    Id INTEGER NOT NULL CONSTRAINT PK_Documents PRIMARY KEY AUTOINCREMENT,
                    "Key" TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Body TEXT NOT NULL,
                    IsPublic INTEGER NOT NULL DEFAULT 1,
                    FilePath TEXT NULL,
                    FileName TEXT NULL,
                    UpdatedAt TEXT NOT NULL,
                    UpdatedBy TEXT NULL
                );
                """);
            Execute(connection, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Documents_Key ON Documents (\"Key\");");

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS FaultReports (
                    Id INTEGER NOT NULL CONSTRAINT PK_FaultReports PRIMARY KEY AUTOINCREMENT,
                    Location TEXT NULL,
                    ComputerId INTEGER NULL,
                    DeviceText TEXT NULL,
                    Description TEXT NOT NULL,
                    Severity TEXT NOT NULL,
                    ReporterName TEXT NULL,
                    ReporterRole TEXT NULL,
                    ClassName TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    IpAddress TEXT NULL,
                    Status TEXT NOT NULL,
                    HandledBy TEXT NULL,
                    HandledAt TEXT NULL,
                    FaultId INTEGER NULL
                );
                """);
            Execute(connection, "CREATE INDEX IF NOT EXISTS IX_FaultReports_Status ON FaultReports (Status);");

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS AgentReports (
                    Id INTEGER NOT NULL CONSTRAINT PK_AgentReports PRIMARY KEY AUTOINCREMENT,
                    ComputerId INTEGER NOT NULL,
                    FirstReportAt TEXT NOT NULL,
                    ReceivedAt TEXT NOT NULL,
                    AgentVersion TEXT NULL,
                    Hostname TEXT NULL,
                    LoggedUser TEXT NULL,
                    OsName TEXT NULL,
                    OsVersion TEXT NULL,
                    LastBoot TEXT NULL,
                    CpuName TEXT NULL,
                    RamGb REAL NULL,
                    DisksJson TEXT NULL,
                    MinFreePercent REAL NULL,
                    Manufacturer TEXT NULL,
                    Model TEXT NULL,
                    Serial TEXT NULL,
                    VeyonInstalled INTEGER NOT NULL DEFAULT 0,
                    VeyonRunning INTEGER NOT NULL DEFAULT 0,
                    VeyonVersion TEXT NULL,
                    IpList TEXT NULL,
                    MacList TEXT NULL,
                    SoftwareCount INTEGER NOT NULL DEFAULT 0,
                    Warning TEXT NULL
                );
                """);
            Execute(connection, "CREATE UNIQUE INDEX IF NOT EXISTS IX_AgentReports_ComputerId ON AgentReports (ComputerId);");

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS InstalledSoftware (
                    Id INTEGER NOT NULL CONSTRAINT PK_InstalledSoftware PRIMARY KEY AUTOINCREMENT,
                    ComputerId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Version TEXT NULL,
                    Publisher TEXT NULL,
                    InstallDate TEXT NULL,
                    Scope TEXT NULL,
                    FirstSeen TEXT NOT NULL
                );
                """);
            Execute(connection, "CREATE INDEX IF NOT EXISTS IX_InstalledSoftware_ComputerId ON InstalledSoftware (ComputerId);");

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS ToolFiles (
                    Id INTEGER NOT NULL CONSTRAINT PK_ToolFiles PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Description TEXT NULL,
                    Category TEXT NOT NULL,
                    Version TEXT NULL,
                    Url TEXT NULL,
                    FileName TEXT NULL,
                    StoredName TEXT NULL,
                    Size INTEGER NOT NULL DEFAULT 0,
                    Sha256 TEXT NULL,
                    UploadedBy TEXT NULL,
                    UploadedAt TEXT NOT NULL,
                    UpdatedAt TEXT NULL,
                    Downloads INTEGER NOT NULL DEFAULT 0,
                    IsPublic INTEGER NOT NULL DEFAULT 0
                );
                """);

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS Rooms (
                    Id INTEGER NOT NULL CONSTRAINT PK_Rooms PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Description TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """);

            AddColumnIfMissing(connection, "Rooms", "AutoShutdown", "TEXT NULL");
            AddColumnIfMissing(connection, "Rooms", "AutoShutdownLast", "TEXT NULL");
            AddColumnIfMissing(connection, "AgentReports", "RustDeskId", "TEXT NULL");

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS AgentCommands (
                    Id INTEGER NOT NULL CONSTRAINT PK_AgentCommands PRIMARY KEY AUTOINCREMENT,
                    ComputerId INTEGER NOT NULL,
                    Kind TEXT NOT NULL,
                    Text TEXT NULL,
                    DelaySeconds INTEGER NOT NULL DEFAULT 0,
                    Status INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    CreatedBy TEXT NULL,
                    SentAt TEXT NULL,
                    DoneAt TEXT NULL,
                    Result TEXT NULL,
                    IsAutomatic INTEGER NOT NULL DEFAULT 0
                );
                """);
            Execute(connection, "CREATE INDEX IF NOT EXISTS IX_AgentCommands_ComputerId_Status ON AgentCommands (ComputerId, Status);");

            Execute(connection, "CREATE INDEX IF NOT EXISTS IX_PlanItems_Location ON PlanItems (Location);");
            Execute(connection, "CREATE INDEX IF NOT EXISTS IX_Seats_SchoolClassId ON Seats (SchoolClassId);");
            Execute(connection, "CREATE INDEX IF NOT EXISTS IX_PasswordRecords_ComputerId ON PasswordRecords (ComputerId);");

            // Ένωση του παλιού δεύτερου πεδίου «Σημειώσεις» (Comments) με το «NOTES»
            Execute(connection, """
                UPDATE Computers
                SET Notes = CASE
                        WHEN Notes IS NULL OR TRIM(Notes) = '' THEN TRIM(Comments)
                        WHEN TRIM(Notes) = TRIM(Comments) OR INSTR(Notes, TRIM(Comments)) > 0 THEN Notes
                        ELSE Notes || char(10) || TRIM(Comments)
                    END,
                    Comments = NULL
                WHERE Comments IS NOT NULL AND TRIM(Comments) <> '';
                """);

            AddColumnIfMissing(connection, "AppSettings", "PageSize", "INTEGER NOT NULL DEFAULT 25");
            AddColumnIfMissing(connection, "AppSettings", "DefaultTheme", "TEXT NULL DEFAULT 'light-blue'");
            AddColumnIfMissing(connection, "AppSettings", "EnableGlow", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfMissing(connection, "AppSettings", "ShowWeather", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfMissing(connection, "AppSettings", "WeatherPlace", "TEXT NULL DEFAULT 'Σάμος'");
            AddColumnIfMissing(connection, "AppSettings", "WeatherLatitude", "REAL NOT NULL DEFAULT 37.69");
            AddColumnIfMissing(connection, "AppSettings", "WeatherLongitude", "REAL NOT NULL DEFAULT 26.94");
            AddColumnIfMissing(connection, "AppSettings", "ScanRange", "TEXT NULL DEFAULT '192.168.2.1-254'");
            AddColumnIfMissing(connection, "AppSettings", "MonitorIntervalMinutes", "INTEGER NOT NULL DEFAULT 10");
            AddColumnIfMissing(connection, "AppSettings", "PortsToScan", $"TEXT NULL DEFAULT '{AppSettings.DefaultPorts}'");
            AddColumnIfMissing(connection, "AppSettings", "WolBroadcast", "TEXT NULL DEFAULT '192.168.2.255'");
            AddColumnIfMissing(connection, "AppSettings", "MySpeedUrl", "TEXT NULL DEFAULT 'http://127.0.0.1:5216'");
            AddColumnIfMissing(connection, "AppSettings", "MySpeedPassword", "TEXT NULL");
            AddColumnIfMissing(connection, "AppSettings", "NewBadgeDays", "INTEGER NOT NULL DEFAULT 7");
            AddColumnIfMissing(connection, "AppSettings", "PiholeUrl", "TEXT NULL");
            AddColumnIfMissing(connection, "AppSettings", "PiholePassword", "TEXT NULL");
            AddColumnIfMissing(connection, "AppSettings", "PublicFaultsPage", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "AppSettings", "PublicPortal", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfMissing(connection, "AppSettings", "AutoBackup", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfMissing(connection, "AppSettings", "AutoBackupHour", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "AppSettings", "BackupKeep", "INTEGER NOT NULL DEFAULT 3");
            AddColumnIfMissing(connection, "AppSettings", "AgentKey", "TEXT NULL");
            AddColumnIfMissing(connection, "AppSettings", "CustomLinks", "TEXT NULL");
            AddColumnIfMissing(connection, "AppSettings", "ScreenWall", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfMissing(connection, "AppSettings", "RustDeskHost", "TEXT NULL");
            AddColumnIfMissing(connection, "AppSettings", "RustDeskKey", "TEXT NULL");
            AddColumnIfMissing(connection, "AppSettings", "RustDeskPassword", "TEXT NULL");
            AddColumnIfMissing(connection, "AppSettings", "RustDeskApply", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "AppSettings", "ScreenKey", "TEXT NULL");
            if (AddColumnIfMissing(connection, "AppSettings", "BadgesClearedAt", "TEXT NULL"))
            {
                // Πρώτη φορά: ό,τι υπάρχει ήδη δεν μετράει ως «ΝΕΟ» — μόνο όσα μπαίνουν από εδώ και πέρα.
                var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                Execute(connection, $"UPDATE AppSettings SET BadgesClearedAt = '{now}';");
            }
        }
        finally
        {
            if (wasClosed)
            {
                connection.Close();
            }
        }
    }

    private static bool AddColumnIfMissing(IDbConnection connection, string table, string column, string definition)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info(\"{table}\");";

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (columns.Count == 0 || columns.Contains(column))
        {
            return false;
        }

        Execute(connection, $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};");

        return true;
    }

    private static void Execute(IDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
