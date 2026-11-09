# Buildings, floors and rooms — design

Date: 2026-11-10 · Status: draft for review · Phase 1 of 2 (phase 2 = network map)

## Goal

Let any school using SNM describe its premises as **buildings → floors → rooms**, see planned vs actual
equipment at every level ("Ισόγειο · 4 χώροι · PCs 45/48 · Πίνακες 4/4", "Εργαστήριο 1 · PCs 11/12 · Πίνακες 1/1"),
and manage that structure (add, edit, delete, reorder, change a room's use and planned counts).
Each floor can carry a floor-plan image with the rooms placed on it; phase 2 draws the network map on that plan.

Must be generic: SNM is one installation per school, used by more than one school. Nothing is specific to one EPAL.

### Decisions taken with the user

| Topic | Decision |
|---|---|
| Hierarchy | Buildings → floors → rooms. No "school/site" level (one SNM = one school). |
| Room use | Editable list of room types in Settings, pre-filled with defaults. |
| Planned counts | Per device type (any of `MachineTypes.All`), per room. |
| Floor view | Room cards + optional floor-plan image with rooms placed on it (plan is step 5). |
| Device ↔ room link | By `RoomId`, with automatic one-time conversion of existing data. |

### Out of scope (phase 1)

Network map / topology (phase 2). Switch-port records. Multi-school in one installation.

## Data model

New tables:

- **Buildings**: `Id`, `Name` (required), `Description?`, `SortOrder`.
- **Floors**: `Id`, `BuildingId`, `Name` (required, e.g. "Ισόγειο"), `Level` (int, −1 basement, 0 ground, 1, 2…),
  `SortOrder`, `PlanImagePath?`, `AspectRatio` (default 1.6).
- **RoomTypes**: `Id`, `Name` (required, unique), `Icon?` (emoji), `Color?` (CSS colour), `SortOrder`.
  Seeded once with: Εργαστήριο Πληροφορικής 💻, Αίθουσα διδασκαλίας 🏫, Εργαστήριο ειδικότητας 🛠️, Γραφείο 🗂️,
  Αίθουσα καθηγητών ☕, Βιβλιοθήκη 📚, Αποθήκη 📦, Server / Rack 🗄️, Άλλο ▫️.
- **RoomCapacities**: `Id`, `RoomId`, `MachineType` (string from `MachineTypes.All`), `PlannedCount` (int ≥ 0).
  Unique on (`RoomId`, `MachineType`).

Changed tables:

- **Rooms** gains `FloorId?` (null = not yet placed), `RoomTypeId?`, `SortOrder`, `PlanX/PlanY/PlanW/PlanH?`
  (percent coordinates on the floor plan; step 5). `Name` becomes unique (case-insensitive, trimmed).
- **Computers** gains `RoomId?` (null = no room). `Location` is kept as a **synced copy of the room name**.

All schema changes go through `DbSchema` (`CREATE TABLE IF NOT EXISTS` / `AddColumnIfMissing`) and the matching
`DbSet`s and indexes in `AppDbContext`, following the existing pattern. Indexes: `Floors(BuildingId)`,
`Rooms(FloorId)`, `Computers(RoomId)`, unique `RoomCapacities(RoomId, MachineType)`.

### Why keep `Location`

The room name is read in ~145 places across 36 files (lists, filters, report, print, Veyon export, auto-shutdown,
public pages). Rewriting all reads is high-risk for no user benefit. Instead:

- `RoomId` is the source of truth.
- `Location` is written **only** by `RoomService` and always equals the room's `Name` (or null).
- Every write path that changes a device's room goes through `RoomService`. Read paths stay unchanged and can move
  to `RoomId` gradually later.

## RoomService (new, `Services/RoomService.cs`)

Single owner of room identity. Operations:

- `AssignAsync(computer, roomId?)` — sets `RoomId` and `Location` together.
- `AssignByNameAsync(computer, name?)` — for Import/Scan/Agent/LabLock and other text inputs: finds the room by
  normalised name (trim, case-insensitive) or creates it as an unplaced room; returns whether it was created.
- `RenameAsync(roomId, newName)` — rejects a name already used by another room; updates `Rooms.Name`,
  `Computers.Location`, `PlanItems.Location`, `RoomPlans.Location`, `SchoolClasses.Location` in one transaction
  (moves the existing rename logic out of `Locations/Index.cshtml.cs`).
- `DeleteAsync(roomId)` — devices get `RoomId = null`, `Location = null`; the room's plan items, room plan,
  classes and seats are removed (same as today's Locations delete); capacities removed.
- `MoveDevicesAsync(ids, roomId?)` — the Locations page "move devices" action.
- `NormaliseName(string?)` — shared trim/compare helper.

## Counting rules (`Services/RoomStats.cs`, pure functions)

- **Actual** = devices with that `RoomId`, excluding `IsDynamic` and `IsEmptySlot`, grouped by `DisplayMachineType`.
- Per room and device type: planned vs actual. Status ✅ when equal, ⚠️ when actual < planned or > planned.
  A type present but not planned shows as "+N μη προγραμματισμένα".
- Floor totals = sum of its rooms; building totals = sum of its floors. Unplaced rooms count only toward the
  building summary line "Αταξινόμητοι".
- Online count per room comes from the existing status data (same source as the Locations page).

## One-time conversion (`Services/RoomMigration.cs`, runs at startup)

`DbSchema.Upgrade` (synchronous, in `Program.cs`) only adds the tables and columns. The data conversion is a separate
`RoomMigration.RunAsync(db, backupService)` called from `Program.cs` right after `DbSchema.Upgrade`, inside the same
startup scope, because it needs the async `BackupService.CreateAsync`.

Guarded by a new `AppSettings.RoomsMigratedAt` (null = not yet run):

1. Back up the database with `BackupService.CreateAsync("pre-rooms")` (same folder/format as daily backups). If the backup fails, skip
   the conversion this start and log a warning (retry next start).
2. If there are no buildings: create "Κτίριο 1" with one floor "Χωρίς όροφο" (Level 0).
3. Seed `RoomTypes` if empty.
4. Collect distinct names from `Rooms.Name` and non-empty `Computers.Location`, merged by normalised name.
   Create a `Room` for each name not already present (unplaced: `FloorId = null`).
   If two existing `Rooms` rows collide after normalisation, keep the oldest and repoint the other's references.
5. Set every computer's `RoomId` from its `Location` and rewrite `Location` to the room's exact `Name`.
6. Set `RoomsMigratedAt = now`. Steps 4–6 run in one transaction.

Rooms are created unplaced on purpose: the user then drags them to the right floors on the Κτίρια page.

## Pages and UI

### Navigation

"📍 Χώροι" becomes "🏢 Κτίρια" → `/Buildings`. `/Locations` stays, reached from room cards (device management for one
room, plan, seats).

### `/Buildings` (new)

- Building tabs (hidden when there is one building). Header: floors · rooms · planned/actual totals.
- "Αταξινόμητοι χώροι" box first when any room has no floor, with "Μετακίνηση σε όροφο…".
- One section per floor (ordered by `Level`, then `SortOrder`): header with totals, room cards, "＋ Χώρος".
- Room card: type icon/colour, name, type, planned/actual per type with ✅/⚠️, online count; click → room view.
- Modal dialogs (Bootstrap, like the rest of SNM):
  - Building: name, description; add / edit / delete / reorder.
  - Floor: name, level; add / edit / delete / move ↑↓.
  - Room: name, type, floor, description, auto-shutdown time, planned counts (rows of device type + number).
- Deletion rules: a building or floor with rooms cannot be deleted (message tells how many rooms to move first).
  A room can be deleted after confirmation; its devices become "no room".
- Access follows the existing auth model: like `/Locations`, `/Buildings` is open to any signed-in user
  (covered by `AuthorizeFolder("/")`); no new admin-only policy. Changes record `UpdatedBy` where the table has it.

### Settings → "Τύποι χώρων" tab

List with name, emoji, colour, order; add / rename / reorder. Delete blocked while any room uses the type.

### Device forms

- Create / Edit: "Χώρος" becomes a `<select>` grouped by building › floor (unplaced rooms in their own group,
  plus "— χωρίς χώρο —").
- Import: text column still accepted → `RoomService.AssignByNameAsync`; newly created rooms listed in the summary.
- Scan, Agent ingest, LabLock ingest: unchanged behaviour, routed through `RoomService`.

### Floor plan (step 5)

Per floor "🗺️ Κάτοψη" tab: upload plan image (stored like room plan backgrounds), drag/resize room boxes using
the same percent-coordinate approach as `wwwroot/js/plan.js`. Boxes show name + ✅/⚠️ and link to the room.
Rooms without a box are listed beside the plan to drag on. This is the canvas for the phase 2 network map.

### Optional summaries (step 6)

Dashboard "per floor" card; Annual report and print lists grouped by building › floor › room.

## Error handling

- Duplicate room name (create/rename) → validation message, nothing saved.
- Delete of non-empty floor/building → refused with explanation.
- Conversion: backup failure → conversion skipped and retried next start; any exception inside the conversion
  transaction → rolled back, logged, app still starts (rooms behave as before since `Location` is untouched).
- Planned count < 0 or unknown device type → validation message.

## Testing

- Unit tests in a new `SchoolNetworkManager.Tests` project (the repo has no test project yet; xUnit + SQLite
  in-memory), added to the `.slnx` but not to the Docker image build:
  conversion (case/space variants, devices without room, empty `Rooms`, colliding room rows, runs once);
  `RoomStats` counting (dynamic/empty excluded, over/under/unplanned); `RoomService` rename/delete/assign;
  delete blocks.
- End-to-end on a scratch database (as done for the LabLock ingest): create building/floor/room, set planned counts,
  move devices, rename a room (devices, plan items, seats follow), delete floor (blocked) and room (devices unassigned).
- Manual check in the browser of `/Buildings`, dialogs, and the device form dropdown.

## Order of work

1. Schema, conversion, `RoomService`, `RoomStats` (+ tests).
2. `/Buildings` page and dialogs.
3. Room types in Settings.
4. Device form dropdowns; route Import/Scan/Agent/LabLock/Locations writes through `RoomService`.
5. Floor plan.
6. Optional summaries.

Each step leaves the app working, so it can be released step by step.
