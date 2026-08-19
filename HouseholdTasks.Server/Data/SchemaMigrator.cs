using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HouseholdTasks.Server.Data;

/// <summary>
/// A lightweight, additive substitute for real EF Core migrations. The app uses
/// EnsureCreated() rather than dotnet-ef, which is great for a fresh install (it always
/// builds the schema straight from the current C# model) but does nothing at all for an
/// existing database — a column added to a model class silently doesn't show up in a
/// database that already existed before that change, causing a SQL error the next time
/// something touches it.
///
/// Each method here is a small, idempotent, additive check: "does this column exist yet?
/// If not, add it and backfill existing rows." Safe to run on every startup — on a
/// database that's already up to date it's just a cheap PRAGMA query per check.
///
/// If the schema keeps evolving, this file is the place to add the next one-off migration
/// step (or, better, it's a good sign to switch to real `dotnet ef migrations` — this
/// approach doesn't scale well past a handful of additive column changes).
/// </summary>
public static class SchemaMigrator
{
    public static async Task RunAsync(AppDbContext db, ILogger logger)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync();

        try
        {
            await AddRecurrenceColumnIfMissing(db, connection, logger);
            await AddDueTimeColumnIfMissing(db, connection, logger);
            await AddAssignmentModeColumnIfMissing(connection, logger);
            await AddRosterOrderColumnIfMissing(connection, logger);
            await AddIsActiveTurnColumnIfMissing(connection, logger);
            await AddDeviceTokensTableIfMissing(connection, logger);
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await cmd.ExecuteReaderAsync();

        var nameOrdinal = -1;
        while (await reader.ReadAsync())
        {
            if (nameOrdinal < 0) nameOrdinal = reader.GetOrdinal("name");
            if (string.Equals(reader.GetString(nameOrdinal), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", table);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    // Added when push notifications were introduced. A whole new table, not just a column
    // — EnsureCreated() only ever builds missing tables into a brand-new database file, so
    // an existing database needs this created by hand too, same idea as the column checks.
    private static async Task AddDeviceTokensTableIfMissing(SqliteConnection connection, ILogger logger)
    {
        if (await TableExistsAsync(connection, "DeviceTokens"))
            return;

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE "DeviceTokens" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_DeviceTokens" PRIMARY KEY AUTOINCREMENT,
                    "FamilyMemberId" INTEGER NOT NULL,
                    "Token" TEXT NOT NULL,
                    "Platform" TEXT NOT NULL,
                    "CreatedAtUtc" TEXT NOT NULL,
                    "LastSeenUtc" TEXT NOT NULL,
                    CONSTRAINT "FK_DeviceTokens_FamilyMembers_FamilyMemberId" FOREIGN KEY ("FamilyMemberId")
                        REFERENCES "FamilyMembers" ("Id") ON DELETE CASCADE
                );
                """;
            await create.ExecuteNonQueryAsync();
        }

        await using (var index = connection.CreateCommand())
        {
            index.CommandText = """CREATE UNIQUE INDEX "IX_DeviceTokens_Token" ON "DeviceTokens" ("Token");""";
            await index.ExecuteNonQueryAsync();
        }

        logger.LogInformation("Created DeviceTokens table.");
    }

    // Added when tasks gained daily/weekly/monthly recurrence.
    private static async Task AddRecurrenceColumnIfMissing(AppDbContext db, SqliteConnection connection, ILogger logger)
    {
        if (await ColumnExistsAsync(connection, "Tasks", "Recurrence"))
            return;

        await using (var alter = connection.CreateCommand())
        {
            // Enums map to INTEGER by default in EF Core; RecurrenceType.None == 0, so a
            // literal 0 default is safe here (unlike DueTime, no EF converter guessing needed).
            alter.CommandText = "ALTER TABLE Tasks ADD COLUMN Recurrence INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync();
        }

        logger.LogInformation("Added Tasks.Recurrence column (existing tasks default to None).");
    }

    // Added when tasks gained a specific due time (default 23:59) alongside their due date.
    private static async Task AddDueTimeColumnIfMissing(AppDbContext db, SqliteConnection connection, ILogger logger)
    {
        if (await ColumnExistsAsync(connection, "Tasks", "DueTime"))
            return;

        await using (var alter = connection.CreateCommand())
        {
            // Nullable on the way in deliberately — SQLite can't backfill a non-constant
            // default via ALTER TABLE cleanly. We fill every existing row with the real
            // default in the very next step, through EF itself, so the stored text format
            // matches exactly what EF's own TimeOnly converter expects to read back later
            // (rather than guessing at SQLite's date/time string format by hand).
            alter.CommandText = "ALTER TABLE Tasks ADD COLUMN DueTime TEXT NULL;";
            await alter.ExecuteNonQueryAsync();
        }

        var endOfDay = new TimeOnly(23, 59);
        var updated = await db.Tasks.ExecuteUpdateAsync(setters =>
            setters.SetProperty(t => t.DueTime, endOfDay));

        logger.LogInformation(
            "Added Tasks.DueTime column and backfilled {Count} existing task(s) to 23:59.", updated);
    }

    // Added when tasks gained the choice between Shared (everyone can complete) and
    // Rotating (turn passes between assignees) assignment modes.
    private static async Task AddAssignmentModeColumnIfMissing(SqliteConnection connection, ILogger logger)
    {
        if (await ColumnExistsAsync(connection, "Tasks", "AssignmentMode"))
            return;

        await using (var alter = connection.CreateCommand())
        {
            // TaskAssignmentMode.Shared == 0, matching every task's behavior before this
            // feature existed — nothing changes for existing tasks.
            alter.CommandText = "ALTER TABLE Tasks ADD COLUMN AssignmentMode INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync();
        }

        logger.LogInformation("Added Tasks.AssignmentMode column (existing tasks default to Shared).");
    }

    // Added alongside AssignmentMode: tracks each assignee's position in the rotation
    // roster. Existing rows all default to 0 — harmless, since rotation only ever looks at
    // RosterOrder for tasks that are actually in Rotating mode, and none were before this.
    private static async Task AddRosterOrderColumnIfMissing(SqliteConnection connection, ILogger logger)
    {
        if (await ColumnExistsAsync(connection, "TaskAssignments", "RosterOrder"))
            return;

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE TaskAssignments ADD COLUMN RosterOrder INTEGER NOT NULL DEFAULT 0;";
        await alter.ExecuteNonQueryAsync();

        logger.LogInformation("Added TaskAssignments.RosterOrder column.");
    }

    // Added alongside AssignmentMode: whether this person is "on duty" for this occurrence.
    // Defaults to 1 (true) for every existing assignment — correct for Shared-mode tasks,
    // which is all that existed before this feature (everyone assigned is always "on duty").
    private static async Task AddIsActiveTurnColumnIfMissing(SqliteConnection connection, ILogger logger)
    {
        if (await ColumnExistsAsync(connection, "TaskAssignments", "IsActiveTurn"))
            return;

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE TaskAssignments ADD COLUMN IsActiveTurn INTEGER NOT NULL DEFAULT 1;";
        await alter.ExecuteNonQueryAsync();

        logger.LogInformation("Added TaskAssignments.IsActiveTurn column (existing assignments default to active).");
    }
}
