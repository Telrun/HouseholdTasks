# Household Tasks

A Blazor WebAssembly app (hosted by an ASP.NET Core API) for assigning and tracking
household chores, backed by SQLite.

## Solution layout

Blazor WebAssembly compiles to a separate client-side assembly, so a true WASM app with
server-side API endpoints needs (at minimum) a client project and a server project. This
is the standard "hosted Blazor WebAssembly" layout — one solution, one thing to run (F5),
three projects:

- **HouseholdTasks.Server** — ASP.NET Core app. Hosts the compiled Blazor client as static
  files, exposes all `/api/...` endpoints (minimal APIs), owns the SQLite database via EF
  Core, and handles Google sign-in.
- **HouseholdTasks.Client** — the Blazor WebAssembly app itself: pages, layout, and the
  auth-state provider that talks to the server.
- **HouseholdTasks.Shared** — DTOs shared by both (so client and server agree on shapes).

## Features implemented

- **Today's Tasks** (`/`) — public, no login, auto-refreshes every 60s. Meant for a
  living-room screen/tablet in kiosk mode.
- **My Tasks** (`/my-tasks`) — requires Google sign-in. Date picker (defaults to today),
  shows tasks assigned to the signed-in person, lets them mark tasks done, and includes a
  form to add new tasks and assign them to one or more family members.
- **Family** (`/family`) — view family members; admins can register new members by name +
  email (the account links automatically the first time that email signs in with Google).
- **Admin** (`/admin`, admins only) — see all tasks for any date, reset a task that was
  marked done incorrectly, or delete a task.
- **Recurring tasks** — when adding a task, set it to repeat Daily / Weekly / Monthly.
  Completing a recurring task automatically creates the next occurrence, due on the next
  cadence date calculated from the *original* due date (so a late completion doesn't
  permanently shift the schedule).
- **Rotating responsibility** — if a recurring task is assigned to 2+ people, only one
  person is "on duty" per occurrence, rotating through the roster in the order they were
  assigned (A → B → C → A → …) each time it's completed. Only the person currently on
  duty (or an admin) can mark it done, and it only shows up in that person's "My Tasks"
  for that occurrence — everyone else still sees the full rotation listed on the task so
  they know who's up next.

## Before you run it

### 1. Update package versions to match your installed SDK

The project targets **.NET 10** (`net10.0`) and pins package versions to `10.0.10` as a
reasonable default. If your installed .NET 10 SDK / runtime is a different patch version,
update the `Microsoft.AspNetCore.*` package versions in the three `.csproj` files to match
(Visual Studio's NuGet "Manage Packages" will offer to align these for you). Note that
`Microsoft.EntityFrameworkCore.Sqlite`/`.Design` version numbers don't always land on the
exact same patch digit as the ASP.NET Core packages — pick the latest EF Core 10.x release
available in NuGet if `10.0.10` isn't found. Requires the **.NET 10 SDK** installed. Or run:

### 1b. SQLitePCLRaw native SQLite vulnerability

`Microsoft.EntityFrameworkCore.Sqlite` currently pulls in `SQLitePCLRaw.lib.e_sqlite3`
2.1.11 as a transitive dependency, which bundles a native SQLite build affected by a known
high-severity vulnerability (`CVE-2025-6965` / `GHSA-2m69-gcr7-jv3q`) — not yet fixed
upstream (tracked at `dotnet/efcore#38257`). The Server `.csproj` works around it with a
direct reference to the current `SQLitePCLRaw.bundle_e_sqlite3` package, which wins
NuGet's version resolution and pulls in a patched native SQLite build instead. If NuGet
later offers a newer `SQLitePCLRaw.bundle_e_sqlite3` version, bump the pin — and once EF
Core ships a release with the dependency fixed, this override can be removed.

```
dotnet restore
```

and let it fail with a clear version mismatch message if there is one.

### 2. Create a Google OAuth Client ID

1. Go to the [Google Cloud Console](https://console.cloud.google.com/apis/credentials).
2. Create an **OAuth client ID** of type **Web application**.
3. Add an authorized redirect URI matching your dev URL, e.g.
   `https://localhost:7100/signin-google` (check `Properties/launchSettings.json` in the
   Server project for your actual local port).
4. Copy the **Client ID** and **Client Secret**.

### 3. Configure the secrets

Don't put real secrets in `appsettings.json` if this repo will be shared/committed. In the
**Server** project, use user-secrets instead:

```
cd HouseholdTasks.Server
dotnet user-secrets init
dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_CLIENT_SECRET"
```

### 4. Set the initial administrator(s)

Edit `HouseholdTasks.Server/appsettings.json` → `InitialAdminEmails` and list the Google
email address(es) that should become admin automatically on first sign-in. Everyone else
who signs in with a Google account is auto-registered as a regular (non-admin) family
member the first time they log in — an admin can promote them later, or you can register
people ahead of time on the **Family** page so their name is already set before they log
in for the first time.

## Running

The solution uses the `.slnx` XML solution format (the .NET 10 default). Requires Visual
Studio 2022 17.13+ or Visual Studio 2026, or any recent JetBrains Rider / VS Code with
C# Dev Kit — all have first-class `.slnx` support at this point.

Open `HouseholdTasks.slnx` in Visual Studio and press **F5** with `HouseholdTasks.Server`
as the startup project (this is the default for the one `.csproj` marked `Sdk.Web` with a
project reference to the client — Visual Studio should pick it automatically since it's
the only runnable web project). Or from the command line:

```
cd HouseholdTasks.Server
dotnet run
```

The SQLite database file (`household.db`) is created automatically next to the Server
project on first run (via `EnsureCreated()` — no manual migration step needed for a fresh
install). **If you already had a database from before recurrence/rotation support was
added**, delete `household.db` (and its `-shm`/`-wal` files if present) so it gets
recreated with the new `Recurrence`, `RosterOrder`, and `IsActiveTurn` columns —
`EnsureCreated()` only creates the schema once and won't alter an existing database, so
an old file will throw a SQL error on the missing columns.

## Notes & things you'll likely want to extend

- **The living-room "Today" screen** is a public, read-only, unauthenticated page by
  design (per the requirements) — don't put anything on it you wouldn't want visible to
  anyone with a browser on that URL.
- Switch `EnsureCreated()` to proper EF Core migrations (`dotnet ef migrations add Initial`)
  once you start evolving the schema, so existing data survives model changes (and future
  additions like this one don't need a "delete the database" workaround).
- The cookie auth is `SameSite=Lax`, which works for same-origin hosting (this template)
  but would need adjusting if you ever split client and server onto different domains.
