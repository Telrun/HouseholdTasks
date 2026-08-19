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
- **Shared or rotating assignment** — a recurring task with 2+ assignees can either stay
  Shared (anyone assigned can complete it, as before) or switch to Rotating: one person is
  "on duty" per occurrence, and the turn passes to the next person in the roster each time
  it's completed, wrapping back to the first once everyone's had a turn. Only whoever's
  currently on duty (or an admin) can mark a rotating task done.
- **Push notifications (PWA + Firebase Cloud Messaging)** — the app is installable as a
  Progressive Web App and can send a push notification when someone gets a new task, or
  when a rotating task's turn passes to them. Requires your own Firebase project — see
  "Setting up push notifications" below. **iOS note:** Apple only allows web push for a
  PWA that's been added to the Home Screen; it will never work in a plain Safari tab, on
  any iOS browser, full stop.
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

### 5. Setting up push notifications (optional)

Skip this section entirely and the app works fine without it — `PushNotificationSender`
silently no-ops if Firebase isn't configured, so nothing breaks. If you want the "new
task" / "it's your turn" notifications, though, here's what's needed. **Every step below
happens in your own Google/Firebase account — none of it can be done from this repo.**

1. Go to the [Firebase Console](https://console.firebase.google.com/) and create a
   project (or use an existing Google Cloud project).
2. Add a **Web app** to the project (Project Settings → General → Your apps → Add app →
   Web). Firebase will show you a `firebaseConfig` object with `apiKey`, `authDomain`,
   `projectId`, `storageBucket`, `messagingSenderId`, and `appId`.
3. Copy those six values into **both**
   `HouseholdTasks.Client/wwwroot/js/notifications.js` and
   `HouseholdTasks.Client/wwwroot/firebase-messaging-sw.js` — search for `REPLACE_ME` in
   each file. They have to match exactly in both places (the service worker runs in its
   own context and can't share the page's config). These values aren't secret — they
   identify your project, not a credential — so it's fine for them to sit in plain
   client-side files.
4. In the same project: **Project Settings → Cloud Messaging → Web configuration → Web
   Push certificates → Generate key pair**. Copy the resulting key into the `vapidKey`
   constant near the top of `notifications.js` (also `REPLACE_ME`).
5. For server-side sending: **Project Settings → Service accounts → Generate new private
   key**. This downloads a JSON file — **never commit it to source control.** Paste its
   full contents into either:
   - `dotnet user-secrets set "Firebase:ServiceAccountJson" "<paste the whole JSON here>"`
     in `HouseholdTasks.Server`, for local dev, or
   - the `firebase_service_account_json` option on the Home Assistant add-on config page,
     for the deployed version (already wired up in `config.yaml` — the option is optional
     and left blank by default).
6. Rebuild and redeploy. Sign in on **My Tasks**, tap "Aktiver varsler", and you should get
   a real browser permission prompt. Create a task assigned to yourself from another
   account (or ask a family member to) and confirm a notification arrives.

**About the icons**: `HouseholdTasks.Client/wwwroot/icons/icon-192.png` and `icon-512.png`
are simple placeholders (a plain house shape on the app's brand green) generated just so
the PWA is installable out of the box — swap them for a real logo whenever you like, same
filenames and sizes (192×192, 512×512).

**iOS is a hard platform limitation, not a bug**: web push only works for a PWA that's
been added to the Home Screen via Safari's Share → "Add to Home Screen" — never in a
regular browser tab, on any iOS browser (they're all WebKit under the hood, Apple's
choice, not Google's or ours). The "Aktiver varsler" banner detects this and shows install
instructions instead of a broken permission prompt when it can tell it's needed. Android
and desktop have no such restriction — notifications work directly in the browser there,
installed or not.

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
install). **Existing databases upgrade themselves automatically on startup** — see
`HouseholdTasks.Server/Data/SchemaMigrator.cs`, a small self-healing step that runs right
after `EnsureCreated()`: it checks (via `PRAGMA table_info` / `sqlite_master`) whether each
column or table added since the initial schema already exists, and if not, adds it and
backfills existing rows with a sensible default, all without touching your existing tasks
or family members. Currently covers `Tasks.Recurrence`, `Tasks.DueTime`,
`Tasks.AssignmentMode`, `TaskAssignments.RosterOrder`, `TaskAssignments.IsActiveTurn`, and
the whole `DeviceTokens` table. You'll see log lines like `Added Tasks.DueTime column and
backfilled 12 existing task(s) to 23:59.` the first time it runs after an update; on every
run after that it's a no-op. You no longer need to delete `household.db` when pulling in
schema changes from this repo.

## Notes & things you'll likely want to extend

- **Embedding the kiosk page in Home Assistant**: since `/kiosk` requires no login, it
  works fine through ingress or your direct hostname either way — add a "Webpage" card
  (or an iframe in a manual dashboard) pointing at `https://<your-domain>/kiosk`. On a
  7" panel you'll likely want to hide the HA header/sidebar around it too (kiosk-mode
  card or a dedicated dashboard) so it's the only thing on screen.
- **The living-room "Today" screen** is a public, read-only, unauthenticated page by
  design (per the requirements) — don't put anything on it you wouldn't want visible to
  anyone with a browser on that URL.
- **Notification triggers are deliberately minimal right now**: a push fires when a task
  is created (to whoever's assigned — just the first person, for a rotating task) and when
  a rotating task's turn passes to someone new on completion. There's no overdue reminder
  job yet — a natural next step would be a small hosted background service that
  periodically scans for overdue tasks and notifies whoever's on duty, but that's not
  built. There's also no per-person notification preferences (mute a category, quiet
  hours, etc.) — everyone who enables notifications gets both trigger types.
- **Device tokens aren't cleaned up when someone signs out** — `DELETE
  /api/notifications/register-token` exists for that but nothing calls it yet. In
  practice this self-corrects: `PushNotificationSender` prunes any token FCM reports as
  no-longer-valid the next time it tries to send to it.
- `SchemaMigrator.cs` is a stopgap, not a real migrations system — it only knows how to
  additively check-and-add specific named columns/tables, one at a time, by hand. It's
  fine for a personal project with occasional small changes, but doesn't handle renames,
  drops, or anything structural. If the schema keeps evolving, switching to real EF Core
  migrations (`dotnet ef migrations add ...`) is the more durable long-term move —
  `SchemaMigrator` was written as a bridge to avoid that setup for now, not a permanent
  replacement for it.
- The cookie auth is `SameSite=Lax`, which works for same-origin hosting (this template)
  but would need adjusting if you ever split client and server onto different domains.
