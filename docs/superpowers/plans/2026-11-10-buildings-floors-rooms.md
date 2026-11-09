# Buildings, Floors and Rooms Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Buildings → floors → rooms in SNM, with room types, planned-vs-actual equipment per room, a `/Buildings` page, and a per-floor plan, with existing rooms converted automatically.

**Architecture:** New EF entities (`Building`, `Floor`, `RoomType`, `RoomCapacity`) plus `Room.FloorId/RoomTypeId` and `Computer.RoomId`. `RoomService` is the only writer of a device's room and keeps `Computer.Location` as a synced copy of the room name, so the ~145 existing read sites stay unchanged. `RoomStats` (pure) computes planned/actual; `RoomMigration` converts existing data once at startup.

**Tech Stack:** ASP.NET Core Razor Pages (.NET 10), EF Core 10 + SQLite, Bootstrap, vanilla JS (`wwwroot/js/plan.js` pattern); tests: xUnit + `Microsoft.Data.Sqlite` in-memory.

**Spec:** `docs/superpowers/specs/2026-11-10-buildings-floors-rooms-design.md`

## Global Constraints

- All paths below are relative to the repo root `SchoolNetworkManager/` (the folder holding `SchoolNetworkManager.slnx`); the app project is `SchoolNetworkManager/SchoolNetworkManager.csproj`.
- Schema changes only via `Services/DbSchema.cs` (`CREATE TABLE IF NOT EXISTS`, `AddColumnIfMissing`) **and** matching `DbSet`/indexes in `Data/AppDbContext.cs`. No EF migrations.
- `Computer.Location` is written **only** by `RoomService`; it always equals the room's `Name` or is null.
- Actual counts exclude `Computer.IsDynamic` and `Computer.IsEmptySlot`; group by `Computer.DisplayMachineType`.
- Planned counts use device types from `MachineTypes.All`; `PlannedCount >= 0`.
- Room names unique after normalisation: trimmed, compared case-insensitively (`StringComparison.OrdinalIgnoreCase`), max 80 chars (existing limit). Enforced in `RoomService` and by the conversion's merge step, not by a DB index (SQLite's default collation is case-sensitive).
- Default room types, in this order: Εργαστήριο Πληροφορικής 💻, Αίθουσα διδασκαλίας 🏫, Εργαστήριο ειδικότητας 🛠️, Γραφείο 🗂️, Αίθουσα καθηγητών ☕, Βιβλιοθήκη 📚, Αποθήκη 📦, Server / Rack 🗄️, Άλλο ▫️.
- Conversion defaults: building "Κτίριο 1", floor "Χωρίς όροφο" (Level 0); backup kind `"pre-rooms"`; guard `AppSettings.RoomsMigratedAt`.
- UI text in Greek, matching existing pages; modals and badges use the existing Bootstrap patterns.
- Access: `/Buildings` is covered by the existing `AuthorizeFolder("/")`; no new policy.
- Test project at repo root `SchoolNetworkManager.Tests/` (NOT inside `SchoolNetworkManager/`, which the Dockerfile copies and the app csproj globs).
- Work on branch `feature/buildings`. Commit after each task (the user approves committing before execution starts).

## Review Focus

1. Import or Scan typing an existing room name in different case / extra spaces → must reuse the existing room, never create "Εργαστήριο 1" twice. (Task 3 test `AssignByName_ReusesRoomIgnoringCaseAndSpaces`.)
2. App restarts after conversion, or conversion interrupted → no duplicate rooms, no second building. (Task 4 tests `Run_IsIdempotent`, `Run_RollsBackOnFailure`.)
3. Deleting a room that still has devices → devices kept with no room, not deleted. (Task 3 test `Delete_UnassignsDevicesKeepsThem`.)
4. Renaming a room to a name another room already has (different case) → refused, nothing changed. (Task 3 test `Rename_RejectsExistingNameIgnoringCase`.)
5. Device `Location` that is only spaces, or rooms table rows that collide after trimming → treated as no room / merged into the oldest room. (Task 4 tests `Run_BlankLocationMeansNoRoom`, `Run_MergesCollidingRooms`.)

---

### Task 1: Test project, entities and schema

**Files:**
- Create: `SchoolNetworkManager.Tests/SchoolNetworkManager.Tests.csproj`, `SchoolNetworkManager.Tests/TestDb.cs`, `SchoolNetworkManager.Tests/SchemaTests.cs`
- Create: `SchoolNetworkManager/Models/Building.cs` (holds `Building`, `Floor`), `SchoolNetworkManager/Models/RoomType.cs` (holds `RoomType`, `RoomCapacity`, `RoomTypeDefaults`)
- Modify: `SchoolNetworkManager/Models/Room.cs`, `SchoolNetworkManager/Models/Computer.cs`, `SchoolNetworkManager/Models/AppSettings.cs`, `SchoolNetworkManager/Data/AppDbContext.cs`, `SchoolNetworkManager/Services/DbSchema.cs`, `SchoolNetworkManager.slnx`

**Interfaces:**
- Produces:
  - `Building { int Id; string Name; string? Description; int SortOrder; }`
  - `Floor { int Id; int BuildingId; string Name; int Level; int SortOrder; string? PlanImagePath; double AspectRatio = 1.6; }`
  - `RoomType { int Id; string Name; string? Icon; string? Color; int SortOrder; }`
  - `RoomCapacity { int Id; int RoomId; string MachineType; int PlannedCount; }`
  - `RoomTypeDefaults.All : IReadOnlyList<(string Name, string Icon)>` (the 9 defaults, in order)
  - `Room` gains `int? FloorId; int? RoomTypeId; int SortOrder; double? PlanX, PlanY, PlanW, PlanH;`
  - `Computer` gains `int? RoomId;`
  - `AppSettings` gains `DateTime? RoomsMigratedAt;`
  - `AppDbContext` gains `DbSet<Building> Buildings`, `DbSet<Floor> Floors`, `DbSet<RoomType> RoomTypes`, `DbSet<RoomCapacity> RoomCapacities`
  - `TestDb.Create() : (AppDbContext Db, SqliteConnection Conn)` — open in-memory SQLite, `EnsureCreated()`, then `DbSchema.Upgrade(db)`; calls `PasswordProtector.Initialize(new EphemeralDataProtectionProvider())` once.

- [ ] **Step 1: Create branch and test project**

`git checkout -b feature/buildings`. Test csproj: `net10.0`, packages `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.DataProtection` (for `EphemeralDataProtectionProvider`), `ProjectReference` to `../SchoolNetworkManager/SchoolNetworkManager.csproj`. Add `<Project Path="SchoolNetworkManager.Tests/SchoolNetworkManager.Tests.csproj" />` to the `.slnx`.

- [ ] **Step 2: Write failing schema test**

```csharp
[Fact] public void Upgrade_CreatesNewTablesAndColumns_AndIsRepeatable()
{
    var (db, conn) = TestDb.Create();
    DbSchema.Upgrade(db);                       // second run must not throw
    Assert.True(TableExists(conn, "Buildings"));
    Assert.True(TableExists(conn, "Floors"));
    Assert.True(TableExists(conn, "RoomTypes"));
    Assert.True(TableExists(conn, "RoomCapacities"));
    Assert.True(ColumnExists(conn, "Rooms", "FloorId"));
    Assert.True(ColumnExists(conn, "Computers", "RoomId"));
    Assert.True(ColumnExists(conn, "AppSettings", "RoomsMigratedAt"));
}
```
(`TableExists`/`ColumnExists` via `sqlite_master` / `pragma table_info`, in `TestDb`.)

- [ ] **Step 3: Run — expect FAIL**

Run: `dotnet test SchoolNetworkManager.Tests --filter Upgrade_CreatesNewTablesAndColumns_AndIsRepeatable`
Expected: compile errors (entities missing).

- [ ] **Step 4: Add entities, DbSets, indexes, DbSchema statements**

Indexes in `OnModelCreating`: `Floor(BuildingId)`, `Room(FloorId)`, `Computer(RoomId)`, unique `RoomCapacity(RoomId, MachineType)`. `DbSchema`: `CREATE TABLE IF NOT EXISTS` for the 4 tables with the same index names EF generates (`IX_Floors_BuildingId`, `IX_Rooms_FloorId`, `IX_Computers_RoomId`, `IX_RoomCapacities_RoomId_MachineType`), `AddColumnIfMissing` for `Rooms.FloorId/RoomTypeId/SortOrder/PlanX/PlanY/PlanW/PlanH`, `Computers.RoomId`, `AppSettings.RoomsMigratedAt`. Place after the existing LabLock tables block.

- [ ] **Step 5: Run — expect PASS**, plus `dotnet build SchoolNetworkManager/SchoolNetworkManager.csproj` → 0 errors.

- [ ] **Step 6: Commit** — `feat(rooms): buildings, floors, room types schema + test project`

---

### Task 2: RoomStats (planned vs actual)

**Files:**
- Create: `SchoolNetworkManager/Services/RoomStats.cs`
- Test: `SchoolNetworkManager.Tests/RoomStatsTests.cs`

**Interfaces:**
- Consumes: `Computer`, `RoomCapacity` (Task 1)
- Produces:
  - `record TypeCount(string MachineType, int Planned, int Actual) { bool Ok => Planned == Actual; bool Unplanned => Planned == 0 && Actual > 0; }`
  - `static IReadOnlyList<TypeCount> RoomStats.ForRoom(IEnumerable<Computer> devices, IEnumerable<RoomCapacity> plan)` — one entry per type in plan ∪ actual; ordered by `MachineTypes.All` index, unknown types last.
  - `static IReadOnlyList<TypeCount> RoomStats.Sum(IEnumerable<IReadOnlyList<TypeCount>> rooms)` — floor/building totals.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void ForRoom_ExcludesDynamicAndEmptySlots()      // 3 PCs + 1 dynamic + 1 "EMPTY 4" → Actual 3
[Fact] public void ForRoom_ReportsShortOverAndUnplanned()      // plan PC 12, Board 1; actual PC 11, Board 1, Printer 1
    // → (Υπολογιστής,12,11,Ok=false), (Διαδραστικός Πίνακας,1,1,Ok=true), (Εκτυπωτής,0,1,Unplanned=true)
[Fact] public void Sum_AddsPlannedAndActualPerType()           // two rooms PC 12/11 and 10/10 → PC 22/21
```

- [ ] **Step 2: Run — expect FAIL** (`dotnet test SchoolNetworkManager.Tests --filter RoomStats`)
- [ ] **Step 3: Implement `RoomStats`** (pure, no DbContext)
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit** — `feat(rooms): planned vs actual counting`

---

### Task 3: RoomService

**Files:**
- Create: `SchoolNetworkManager/Services/RoomService.cs`
- Modify: `SchoolNetworkManager/Program.cs` (`builder.Services.AddScoped<RoomService>();`)
- Test: `SchoolNetworkManager.Tests/RoomServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 entities, `TestDb`
- Produces (all on `RoomService(AppDbContext db)`):
  - `static string? NormaliseName(string? name)` — trim, null if empty, cut to 80 chars
  - `Task<Room?> FindByNameAsync(string? name)` — normalised, case-insensitive
  - `Task<Room> CreateAsync(string name, int? floorId = null, int? roomTypeId = null, string? description = null)` — throws `RoomNameTakenException` if name exists
  - `Task AssignAsync(Computer c, int? roomId)` — sets `RoomId` + `Location` (room name or null); does not SaveChanges
  - `Task<bool> AssignByNameAsync(Computer c, string? name)` — find-or-create (unplaced); returns true when created; does not SaveChanges
  - `Task<int> MoveDevicesAsync(IEnumerable<int> computerIds, int? roomId, string? by)` — saves; returns count
  - `Task RenameAsync(int roomId, string newName)` — throws `RoomNameTakenException`; one transaction updating `Rooms.Name`, `Computers.Location`, `PlanItems.Location`, `RoomPlans.Location`, `SchoolClasses.Location`
  - `Task DeleteAsync(int roomId)` — devices → `RoomId=null, Location=null`; removes `PlanItems`, `RoomPlans`, `SchoolClasses` + their `Seats`, `RoomCapacities` of that room; saves
  - `Task SetCapacitiesAsync(int roomId, IEnumerable<(string MachineType, int Planned)> rows)` — replaces rows; ignores `Planned == 0`; throws `ArgumentException` for type not in `MachineTypes.All` or negative count
  - `class RoomNameTakenException(string name) : Exception`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public async Task Assign_SetsRoomIdAndSyncedLocation()
[Fact] public async Task AssignByName_ReusesRoomIgnoringCaseAndSpaces()   // existing "Εργαστήριο 1"; input "  εργαστήριο 1 " → same Id, created=false, Location=="Εργαστήριο 1"
[Fact] public async Task AssignByName_CreatesUnplacedRoom()               // FloorId == null, created=true
[Fact] public async Task Rename_UpdatesDevicesPlansClasses()             // PlanItem/RoomPlan/SchoolClass Location follow
[Fact] public async Task Rename_RejectsExistingNameIgnoringCase()        // throws RoomNameTakenException; nothing changed
[Fact] public async Task Delete_UnassignsDevicesKeepsThem()              // device still exists, RoomId null, Location null; Seats of room's classes gone
[Fact] public async Task SetCapacities_RejectsUnknownTypeAndNegative()
```

- [ ] **Step 2: Run — expect FAIL** (`dotnet test SchoolNetworkManager.Tests --filter RoomService`)
- [ ] **Step 3: Implement `RoomService`**; move the rename logic now in `Pages/Locations/Index.cshtml.cs:OnPostRenameAsync` here (that page switches over in Task 5).
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit** — `feat(rooms): RoomService as single writer of device rooms`

---

### Task 4: One-time conversion

**Files:**
- Create: `SchoolNetworkManager/Services/RoomMigration.cs`
- Modify: `SchoolNetworkManager/Program.cs` (startup scope, right after `DbSchema.Upgrade(db);` at line ~158)
- Test: `SchoolNetworkManager.Tests/RoomMigrationTests.cs`

**Interfaces:**
- Consumes: `RoomService.NormaliseName`, `RoomTypeDefaults.All`, Task 1 entities
- Produces: `static Task<bool> RoomMigration.RunAsync(AppDbContext db, Func<Task> backup, ILogger log, Func<Task>? afterRoomsCreated = null)` — returns true if it converted this run. `afterRoomsCreated` is a test seam invoked inside the transaction after rooms are created and before devices are linked; production passes null.
  `Program.cs` calls it with `() => app.Services.GetRequiredService<BackupService>().CreateAsync("pre-rooms")` and blocks with `.GetAwaiter().GetResult()` (startup is synchronous there).

Steps, in this order (spec "One-time conversion"): return false if `RoomsMigratedAt != null`; call `backup()` — on exception log warning and return false; then in **one transaction**: create "Κτίριο 1"/"Χωρίς όροφο" if no buildings; seed `RoomTypes` if empty; merge `Rooms` rows that collide after normalisation (keep lowest `Id`, repoint `Computers.Location`/plans/classes to its name, delete the others); create unplaced rooms for every distinct normalised non-empty `Computers.Location` not yet a room; set each computer's `RoomId` and rewrite `Location` to the room's exact `Name` (blank → both null); set `RoomsMigratedAt = DateTime.Now`; commit. Any exception → rollback, log error, return false.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public async Task Run_CreatesDefaultBuildingFloorAndTypes()     // 1 building "Κτίριο 1", 1 floor "Χωρίς όροφο" Level 0, 9 RoomTypes
[Fact] public async Task Run_LinksDevicesByNormalisedName()             // Locations "Lab 1", " lab 1", "LAB 1 " → one room, all RoomId equal, Location=="Lab 1"
[Fact] public async Task Run_KeepsExistingEmptyRoomsUnplaced()          // Rooms row "Αποθήκη" with no devices → still there, FloorId null
[Fact] public async Task Run_BlankLocationMeansNoRoom()                 // Location "   " → RoomId null, Location null
[Fact] public async Task Run_MergesCollidingRooms()                     // Rooms "Lab 1"(Id 1), "lab 1 "(Id 2) → only Id 1 left
[Fact] public async Task Run_IsIdempotent()                             // second RunAsync returns false; counts unchanged
[Fact] public async Task Run_SkipsWhenBackupFails()                     // backup throws → returns false, RoomsMigratedAt null, no buildings
[Fact] public async Task Run_RollsBackOnFailure()                       // afterRoomsCreated throws → returns false; 0 buildings, 0 new rooms, all RoomId null, RoomsMigratedAt null
```

- [ ] **Step 2: Run — expect FAIL** (`dotnet test SchoolNetworkManager.Tests --filter RoomMigration`)
- [ ] **Step 3: Implement `RoomMigration`; hook into `Program.cs`**
- [ ] **Step 4: Run — expect PASS**; full suite `dotnet test SchoolNetworkManager.Tests` green.
- [ ] **Step 5: Smoke test on a scratch DB** — `APP_DATA_DIR=<scratch> ASPNETCORE_URLS=http://localhost:5099 dotnet run --project SchoolNetworkManager --no-launch-profile`; check log has no error and `data/backups` contains a `pre-rooms` backup; stop.
- [ ] **Step 6: Commit** — `feat(rooms): one-time conversion of existing rooms`

---

### Task 5: Route every device-room write through RoomService; room dropdown in forms

**Files:**
- Modify: `SchoolNetworkManager/Pages/Computers/Create.cshtml(.cs)` (line ~33–35), `Edit.cshtml(.cs)` (line ~80, form line ~73), `Import.cshtml.cs` (line ~123), `Pages/Network/Scan.cshtml.cs` (line ~171–193), `Pages/Locations/Index.cshtml.cs` (`OnPostAutoShutdownAsync`, `OnPostCreateRoomAsync`, `OnPostAddToRoomAsync`, `OnPostDeleteRoomAsync`, `OnPostAssignAsync`, `OnPostRenameAsync`)
- Create: `SchoolNetworkManager/Pages/Shared/_RoomSelect.cshtml` (partial: `<select>` grouped by building › floor, "Αταξινόμητοι" group, "— χωρίς χώρο —")
- Create: `SchoolNetworkManager/Services/RoomOptions.cs`
- Test: `SchoolNetworkManager.Tests/RoomOptionsTests.cs`

**Interfaces:**
- Consumes: `RoomService` (Task 3)
- Produces: `record RoomOption(int Id, string Name, string Group)`; `static Task<List<RoomOption>> RoomOptions.LoadAsync(AppDbContext db)` — groups "Κτίριο › Όροφος" ordered by building `SortOrder`, floor `Level` then `SortOrder`, room `SortOrder` then `Name`; unplaced rooms group "Αταξινόμητοι χώροι" last. Partial model: `(List<RoomOption> Options, int? Selected, string FieldName)`.

Form field becomes `Computer.RoomId` (bound int?); page handlers call `RoomService.AssignAsync`. Import/Scan keep their text inputs and call `AssignByNameAsync`; Import summary lists rooms created ("Νέοι χώροι: …"). Locations page: rename → `RenameAsync` (catch `RoomNameTakenException` → warning message); delete → keeps today's rule "room must be empty" there, then `DeleteAsync`; create → `CreateAsync`; add/assign → `MoveDevicesAsync`; auto-shutdown → find room via `FindByNameAsync` (create via `CreateAsync` if missing). Agent and LabLock ingest do not write `Location` today — no change.

- [ ] **Step 1: Write failing test** `LoadAsync_GroupsByBuildingAndFloorWithUnplacedLast` (2 floors, 1 unplaced room → groups in order "Κτίριο 1 › Ισόγειο", "Κτίριο 1 › 1ος", "Αταξινόμητοι χώροι").
- [ ] **Step 2: Run — expect FAIL**
- [ ] **Step 3: Implement `RoomOptions`, `_RoomSelect`, switch the handlers listed above**
- [ ] **Step 4: Verify no other writer remains** — `grep -rn "\.Location = \|Location = " SchoolNetworkManager --include=*.cs` shows only `RoomService.cs`, `RoomMigration.cs` and non-Computer types (`PlanItem`, `RoomPlan`, `SchoolClass` constructors in `Plan.cshtml.cs`).
- [ ] **Step 5: Run full tests + build — expect PASS**
- [ ] **Step 6: Manual check** on scratch DB: create a device with a room from the dropdown; edit it to another room; import a 2-row xlsx with room "  lab 1"; rename room on Locations page; all show the right room.
- [ ] **Step 7: Commit** — `feat(rooms): devices get their room through RoomService; room dropdown`

---

### Task 6: `/Buildings` page

**Files:**
- Create: `SchoolNetworkManager/Pages/Buildings/Index.cshtml`, `Index.cshtml.cs`, `SchoolNetworkManager/wwwroot/js/buildings.js` (modal fill + capacity rows add/remove only)
- Create: `SchoolNetworkManager/Services/BuildingQueries.cs`
- Modify: `SchoolNetworkManager/Pages/Shared/_Layout.cshtml:98` (nav "🏢 Κτίρια" → `/Buildings/Index`, active for `/Buildings` and `/Locations`)
- Test: `SchoolNetworkManager.Tests/BuildingQueriesTests.cs`

**Interfaces:**
- Consumes: `RoomStats`, `RoomService`
- Produces:
  - `record RoomCard(Room Room, RoomType? Type, IReadOnlyList<TypeCount> Counts, int Online, int Devices)`
  - `record FloorView(Floor Floor, List<RoomCard> Rooms, IReadOnlyList<TypeCount> Totals)`
  - `record BuildingView(Building Building, List<FloorView> Floors, IReadOnlyList<TypeCount> Totals)`
  - `static Task<(List<BuildingView> Buildings, List<RoomCard> Unplaced)> BuildingQueries.LoadAsync(AppDbContext db)` — online = devices whose latest status is online, same rule the Locations page uses for its online badge.
  - `static Task DeleteFloorAsync(AppDbContext db, int floorId)` / `DeleteBuildingAsync(...)` — throw `InvalidOperationException("…{n} χώροι…")` when not empty.

Page handlers (POST, antiforgery as elsewhere): `CreateBuilding`, `EditBuilding`, `DeleteBuilding`, `MoveBuilding(id, dir)`, `CreateFloor`, `EditFloor`, `DeleteFloor`, `MoveFloor(id, dir)`, `CreateRoom`, `EditRoom` (name via `RenameAsync`, floor, type, description, auto-shutdown `HH:mm`, capacities via `SetCapacitiesAsync`), `DeleteRoom` (confirm text says devices will be kept without room), `PlaceRooms(int[] ids, int floorId)`. Status messages use the existing `StatusMessage`/`StatusKind` TempData pattern. Layout per spec "/Buildings": building tabs (hidden if one), unplaced box first, floor sections, room cards with ✅/⚠️ per type and "+N μη προγραμματισμένα", card opens the existing room view `/Locations/Plan?Location=<name>` (devices, plan, seats) and has a "Συσκευές" link to `/Computers/Index?LocationFilter=<name>` (the filter `Scan.cshtml.cs` already redirects to). The Locations index page has no single-room filter, so it is linked only from the nav/“Όλοι οι χώροι” link.

- [ ] **Step 1: Write failing tests** `LoadAsync_TotalsRollUpFloorToBuilding`, `LoadAsync_UnplacedRoomsSeparate`, `DeleteFloor_RefusedWhenRoomsPresent`, `DeleteBuilding_RefusedWhenRoomsPresent`.
- [ ] **Step 2: Run — expect FAIL**
- [ ] **Step 3: Implement `BuildingQueries`, page, JS, nav link**
- [ ] **Step 4: Run tests + build — expect PASS**
- [ ] **Step 5: Browser check** on scratch DB (after Task 4 conversion): rename "Κτίριο 1", add floors Ισόγειο/1ος/2ος, place rooms, set capacities PC 12 + board 1, see ✅/⚠️ and floor totals; try deleting a non-empty floor → refused message; delete an empty floor → gone. Screenshot at desktop and 400 px width.
- [ ] **Step 6: Commit** — `feat(rooms): Buildings page with floors, room cards and dialogs`

---

### Task 7: Room types in Settings

**Files:**
- Modify: `SchoolNetworkManager/Pages/Settings/Index.cshtml`, `Index.cshtml.cs` (new tab key `"roomtypes"` alongside existing `ActiveTab` values)
- Test: `SchoolNetworkManager.Tests/RoomTypeTests.cs`

**Interfaces:**
- Consumes: `RoomType`
- Produces: `static Task RoomTypeOps.DeleteAsync(AppDbContext db, int id)` in `SchoolNetworkManager/Services/RoomTypeOps.cs` — throws `InvalidOperationException` when any room uses it; `static Task RoomTypeOps.SaveAsync(AppDbContext db, int? id, string name, string? icon, string? color)` — rejects duplicate name (case-insensitive).

- [ ] **Step 1: Write failing tests** `Delete_RefusedWhenInUse`, `Save_RejectsDuplicateNameIgnoringCase`, `Rename_KeepsRoomsLinked`.
- [ ] **Step 2: Run — expect FAIL**
- [ ] **Step 3: Implement `RoomTypeOps` + Settings tab** (list with name/emoji/colour, add, edit, ↑↓ order, delete)
- [ ] **Step 4: Run — expect PASS**; browser check of the tab.
- [ ] **Step 5: Commit** — `feat(rooms): editable room types in Settings`

---

### Task 8: Floor plan

**Files:**
- Create: `SchoolNetworkManager/Pages/Buildings/FloorPlan.cshtml`, `FloorPlan.cshtml.cs`, `SchoolNetworkManager/wwwroot/js/floorplan.js`
- Modify: `SchoolNetworkManager/Pages/Buildings/Index.cshtml` (per-floor "🗺️ Κάτοψη" link)

**Interfaces:**
- Consumes: `Floor.PlanImagePath/AspectRatio`, `Room.PlanX/PlanY/PlanW/PlanH`, `BuildingQueries` (for card status)
- Produces: handlers `OnGetAsync(int floorId)`, `OnPostLayoutAsync(int floorId, string layoutJson)` (JSON `[{roomId,x,y,w,h}]`, values clamped 0–100; rooms omitted → plan fields null), `OnPostBackgroundAsync(int floorId, IFormFile file)` (same storage folder, size limit and image types as `Pages/Locations/Plan.cshtml.cs:OnPostBackgroundAsync`), `OnPostAspectAsync(int floorId, double ratio)`.

`floorplan.js` reuses the drag/resize approach of `wwwroot/js/plan.js` (percent coordinates); room boxes show name + ✅/⚠️ and link to the room; rooms of the floor without a box listed beside the plan for dragging on. No new JS library.

- [ ] **Step 1: Implement page + JS**
- [ ] **Step 2: Build — expect 0 errors**
- [ ] **Step 3: Browser check** on scratch DB: upload an image, drag 3 rooms on, resize one, save, reload → positions kept; remove one → back in the side list; check at 400 px width.
- [ ] **Step 4: Commit** — `feat(rooms): per-floor plan with rooms placed`

---

### Task 9: Optional summaries and release notes

**Files:**
- Modify: `SchoolNetworkManager/Pages/Index.cshtml(.cs)` (Dashboard "Ανά όροφο" card from `BuildingQueries.LoadAsync`), `SchoolNetworkManager/Pages/Report/Index.cshtml(.cs)` and `Pages/Print/Index.cshtml(.cs)` (group by building › floor › room when buildings exist; unchanged otherwise), `CHANGELOG.md`, `README.md` (feature list line), `SchoolNetworkManager/Pages/Help/Index.cshtml` (short "Κτίρια" section)

- [ ] **Step 1: Dashboard card** — one row per floor: name, rooms, PCs actual/planned, boards actual/planned.
- [ ] **Step 2: Report/Print grouping**
- [ ] **Step 3: CHANGELOG entry** under the next version: Κτίρια/όροφοι/χώροι, τύποι χώρων, προγραμματισμένος εξοπλισμός, κάτοψη ορόφου, ιστορικό LabLock, ασφαλέστερη αντιστοίχιση LabLock.
- [ ] **Step 4: Full test suite + build — PASS; browser check of Dashboard, Report, Print**
- [ ] **Step 5: Commit** — `feat(rooms): per-floor summaries, docs`

---

### Task 10: End-to-end verification

- [ ] **Step 1:** Copy a real (or realistic) SNM database to a scratch `APP_DATA_DIR`, start SNM on port 5099, confirm conversion ran once (log + `pre-rooms` backup), every device that had a `Location` now has a `RoomId`, and room count == distinct normalised location names.
- [ ] **Step 2:** Walk the flow: structure the 3 floors, place rooms, set capacities, move devices, rename a room (Locations room view, plan, seats still show), delete a room (devices kept), try deleting a non-empty floor (refused).
- [ ] **Step 3:** Restart SNM → no second conversion, no duplicate rooms.
- [ ] **Step 4:** `dotnet test` all green; `dotnet build` 0 errors; Docker build `docker build SchoolNetworkManager` succeeds if Docker is available (test project not in image).
- [ ] **Step 5:** Report results to the user; no push or release without their go-ahead.
