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

- **Today's Tasks** (`/`) — public, no login, auto-refreshes every 60s. Includes a
  collapsible "show tomorrow's tasks" toggle (read-only preview, exact date match, no
  overdue-merging).
- **Kiosk** (`/kiosk`) — a compact, dark, no-chrome variant of Today's Tasks meant to be
  embedded in a Home Assistant dashboard on a small screen (e.g. a 7" Raspberry Pi
  panel). No menu, no login prompt, no interaction — just a dense auto-refreshing list.
- **My Tasks** (`/my-tasks`) — requires Google sign-in. Date picker (defaults to today),
  shows tasks assigned to the signed-in person, lets them mark tasks done, and includes a
  form to add new tasks and assign them to one or more family members.
- **Due date and time** — every task has a due date and a due time (defaulting to 23:59,
  i.e. end of day, unless changed). A task becomes "overdue" either the day after its due
  date, or the same day once the due time has passed — whichever comes first.
- **Family** (`/family`) — view family members; admins can register new members by name +
  email (the account links automatically the first time that email signs in with Google).
- **Admin** (`/admin`, admins only) — see all tasks for any date, edit a task's title,
  description, category, due date/time, recurrence, or assignees, reset a task that was
  marked done incorrectly, or delete a task. Also lists all family members with the same
  kind of inline edit for name and admin status.
- **Mobile-friendly** — responsive layout with a hamburger menu below ~768px width;
  no Bootstrap, just hand-rolled CSS to match the rest of the app.

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
email address(es) that should become admin automatically on first sign-in — this is the
**only** way in that gets a FamilyMember row created automatically.

**Sign-in is access-gated, not open.** Anyone who signs in with Google but whose email
isn't already a registered family member (and isn't in `InitialAdminEmails`) is rejected
during the Google callback itself — no cookie is ever issued, and they're bounced back to
the app with an "Access denied" banner. This means you need to register a family member's
email on the **Family** or **Admin** page *before* they can sign in for the first time —
there's no self-service signup. Anyone in `InitialAdminEmails` is the one exception: their
FamilyMember row (as an admin) is created automatically the first time they log in, since
that's the only way to get the very first admin into an otherwise-empty database.

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
install). **If you already had a database from before recurrence or due-time support was
added**, delete `household.db` (and its `-shm`/`-wal` files if present) so it gets
recreated with the newer `Recurrence`/`DueTime` columns — `EnsureCreated()` only creates
the schema once and won't alter an existing database, so an old file will throw a SQL
error on the missing columns.

## Notes & things you'll likely want to extend

- **Embedding the kiosk page in Home Assistant**: since `/kiosk` requires no login, it
  works fine through ingress or your direct hostname either way — add a "Webpage" card
  (or an iframe in a manual dashboard) pointing at `https://<your-domain>/kiosk`. On a
  7" panel you'll likely want to hide the HA header/sidebar around it too (kiosk-mode
  card or a dedicated dashboard) so it's the only thing on screen.

- **The living-room "Today" screen** is a public, read-only, unauthenticated page by
  design (per the requirements) — don't put anything on it you wouldn't want visible to
  anyone with a browser on that URL.
- Switch `EnsureCreated()` to proper EF Core migrations (`dotnet ef migrations add Initial`)
  once you start evolving the schema, so existing data survives model changes (and future
  additions like this one don't need a "delete the database" workaround).
- The cookie auth is `SameSite=Lax`, which works for same-origin hosting (this template)
  but would need adjusting if you ever split client and server onto different domains.
