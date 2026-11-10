using Xunit;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Tests;

public class RoomServiceTests
{
    private const string Lab = "Εργαστήριο 1";
    private static int _ip;
    private static Computer Pc(string name, string? location = null) =>
        new() { Name = name, Location = location, IpAddress = "10.0.0." + System.Threading.Interlocked.Increment(ref _ip) };

    [Fact]
    public async Task Assign_SetsRoomIdAndSyncedLocation()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync(Lab);
        var pc = Pc("PC1");
        db.Computers.Add(pc);

        await svc.AssignAsync(pc, room.Id);
        await db.SaveChangesAsync();

        Assert.Equal(room.Id, pc.RoomId);
        Assert.Equal(Lab, pc.Location);

        await svc.AssignAsync(pc, null);
        Assert.Null(pc.RoomId);
        Assert.Null(pc.Location);
    }

    [Fact]
    public async Task AssignByName_ReusesRoomIgnoringCaseAndSpaces()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync(Lab);
        var pc = Pc("PC1");
        db.Computers.Add(pc);

        var created = await svc.AssignByNameAsync(pc, "  εργαστήριο 1 ");
        await db.SaveChangesAsync();

        Assert.False(created);
        Assert.Equal(room.Id, pc.RoomId);
        Assert.Equal(Lab, pc.Location);
        Assert.Equal(1, await db.Rooms.CountAsync());
    }

    [Fact]
    public async Task AssignByName_ReusesRoomIgnoringAccents()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync(Lab);
        var pc = Pc("PC1");
        db.Computers.Add(pc);

        var created = await svc.AssignByNameAsync(pc, "ΕΡΓΑΣΤΗΡΙΟ 1");
        await db.SaveChangesAsync();

        Assert.False(created);
        Assert.Equal(room.Id, pc.RoomId);
        Assert.Equal(Lab, pc.Location);
        Assert.Equal(1, await db.Rooms.CountAsync());
    }

    [Fact]
    public async Task Rename_RejectsNameDifferingOnlyByAccents()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var a = await svc.CreateAsync(Lab);
        await svc.CreateAsync("Αίθουσα καθηγητών");

        await Assert.ThrowsAsync<RoomNameTakenException>(() => svc.RenameAsync(a.Id, "ΑΙΘΟΥΣΑ ΚΑΘΗΓΗΤΩΝ"));

        db.ChangeTracker.Clear();
        Assert.Equal(Lab, (await db.Rooms.FindAsync(a.Id))!.Name);
    }

    [Fact]
    public async Task AssignByName_CreatesUnplacedRoom()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var pc = Pc("PC1");
        db.Computers.Add(pc);

        var created = await svc.AssignByNameAsync(pc, "Νέος χώρος");
        await db.SaveChangesAsync();

        Assert.True(created);
        var room = await db.Rooms.SingleAsync();
        Assert.Null(room.FloorId);
        Assert.Equal(room.Id, pc.RoomId);
        Assert.Equal("Νέος χώρος", pc.Location);
    }

    [Fact]
    public async Task AssignByName_DoesNotSave()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var pc = Pc("PC1");
        db.Computers.Add(pc);
        await db.SaveChangesAsync();

        var created = await svc.AssignByNameAsync(pc, "Νέος χώρος");

        Assert.True(created);
        using (var other = new SchoolNetworkManager.Data.AppDbContext(
            new DbContextOptionsBuilder<SchoolNetworkManager.Data.AppDbContext>().UseSqlite(conn).Options))
        {
            Assert.Equal(0, await other.Rooms.CountAsync());
            Assert.Null((await other.Computers.SingleAsync()).RoomId);
        }

        await db.SaveChangesAsync();

        var room = await db.Rooms.SingleAsync();
        Assert.Equal(room.Id, pc.RoomId);
        Assert.Equal("Νέος χώρος", pc.Location);
    }

    [Fact]
    public async Task AssignByName_TwoNewVariantsBeforeSave_CreateOneRoom()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var a = Pc("A");
        var b = Pc("B");
        db.Computers.AddRange(a, b);

        var first = await svc.AssignByNameAsync(a, "Lab X");
        var second = await svc.AssignByNameAsync(b, " lab x ");
        await db.SaveChangesAsync();

        Assert.True(first);
        Assert.False(second);
        var room = await db.Rooms.SingleAsync();
        Assert.Equal(room.Id, a.RoomId);
        Assert.Equal(room.Id, b.RoomId);
        Assert.Equal("Lab X", b.Location);
    }

    [Fact]
    public async Task Rename_UpdatesDevicesPlansClasses()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync(Lab);
        var pc = Pc("PC1");
        var legacy = Pc("PC2", Lab);
        db.Computers.AddRange(pc, legacy);
        await svc.AssignAsync(pc, room.Id);
        db.PlanItems.Add(new PlanItem { Location = Lab });
        db.RoomPlans.Add(new RoomPlan { Location = Lab });
        db.SchoolClasses.Add(new SchoolClass { Location = Lab, Name = "Β1" });
        await db.SaveChangesAsync();

        await svc.RenameAsync(room.Id, "  Εργαστήριο 2 ");

        db.ChangeTracker.Clear();
        Assert.Equal("Εργαστήριο 2", (await db.Rooms.SingleAsync()).Name);
        Assert.All(await db.Computers.ToListAsync(), c => Assert.Equal("Εργαστήριο 2", c.Location));
        Assert.Equal("Εργαστήριο 2", (await db.PlanItems.SingleAsync()).Location);
        Assert.Equal("Εργαστήριο 2", (await db.RoomPlans.SingleAsync()).Location);
        Assert.Equal("Εργαστήριο 2", (await db.SchoolClasses.SingleAsync()).Location);
    }

    [Fact]
    public async Task Rename_UpdatesLinksDifferingByCaseOrAccent()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync(Lab);
        db.PlanItems.Add(new PlanItem { Location = "ΕΡΓΑΣΤΗΡΙΟ 1" });
        db.RoomPlans.Add(new RoomPlan { Location = "εργαστηριο 1" });
        db.SchoolClasses.Add(new SchoolClass { Location = " Εργαστήριο 1", Name = "Β1" });
        db.PlanItems.Add(new PlanItem { Location = "Γραφείο" });
        await db.SaveChangesAsync();

        await svc.RenameAsync(room.Id, "Εργαστήριο 2");

        db.ChangeTracker.Clear();
        Assert.Equal(new[] { "Εργαστήριο 2", "Γραφείο" }, (await db.PlanItems.OrderBy(p => p.Id).ToListAsync()).Select(p => p.Location).ToArray());
        Assert.Equal("Εργαστήριο 2", (await db.RoomPlans.SingleAsync()).Location);
        Assert.Equal("Εργαστήριο 2", (await db.SchoolClasses.SingleAsync()).Location);
    }

    [Fact]
    public async Task Rename_RejectsExistingNameIgnoringCase()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var a = await svc.CreateAsync(Lab);
        await svc.CreateAsync("Γραφείο");
        var pc = Pc("PC1");
        db.Computers.Add(pc);
        await svc.AssignAsync(pc, a.Id);
        db.PlanItems.Add(new PlanItem { Location = Lab });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<RoomNameTakenException>(() => svc.RenameAsync(a.Id, " γραφείο "));

        db.ChangeTracker.Clear();
        Assert.Equal(Lab, (await db.Rooms.FindAsync(a.Id))!.Name);
        Assert.Equal(Lab, (await db.Computers.SingleAsync()).Location);
        Assert.Equal(Lab, (await db.PlanItems.SingleAsync()).Location);
    }

    [Fact]
    public async Task Delete_UnassignsDevicesKeepsThem()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync(Lab);
        var other = await svc.CreateAsync("Γραφείο");
        var pc = Pc("PC1");
        var otherPc = Pc("PC2");
        db.Computers.AddRange(pc, otherPc);
        await svc.AssignAsync(pc, room.Id);
        await svc.AssignAsync(otherPc, other.Id);
        var cls = new SchoolClass { Location = Lab, Name = "Β1" };
        var otherCls = new SchoolClass { Location = "Γραφείο", Name = "Γ1" };
        db.SchoolClasses.AddRange(cls, otherCls);
        db.PlanItems.AddRange(new PlanItem { Location = Lab }, new PlanItem { Location = "Γραφείο" });
        db.RoomPlans.Add(new RoomPlan { Location = Lab });
        await db.SaveChangesAsync();
        db.Seats.AddRange(
            new Seat { SchoolClassId = cls.Id, ComputerId = pc.Id, StudentName = "Α" },
            new Seat { SchoolClassId = otherCls.Id, ComputerId = otherPc.Id, StudentName = "Β" });
        db.RoomCapacities.Add(new RoomCapacity { RoomId = room.Id, MachineType = MachineTypes.Default, PlannedCount = 3 });
        await db.SaveChangesAsync();

        await svc.DeleteAsync(room.Id);

        db.ChangeTracker.Clear();
        var kept = await db.Computers.SingleAsync(c => c.Name == "PC1");
        Assert.Null(kept.RoomId);
        Assert.Null(kept.Location);
        Assert.Equal("Γραφείο", (await db.Computers.SingleAsync(c => c.Name == "PC2")).Location);
        Assert.Equal(other.Id, (await db.Rooms.SingleAsync()).Id);
        Assert.Equal("Γ1", (await db.SchoolClasses.SingleAsync()).Name);
        Assert.Equal("Β", (await db.Seats.SingleAsync()).StudentName);
        Assert.Equal("Γραφείο", (await db.PlanItems.SingleAsync()).Location);
        Assert.Empty(await db.RoomPlans.ToListAsync());
        Assert.Empty(await db.RoomCapacities.ToListAsync());
    }

    [Fact]
    public async Task SetCapacities_RejectsUnknownTypeAndNegative()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync(Lab);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.SetCapacitiesAsync(room.Id, new[] { ("Τοστιέρα", 2) }));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SetCapacitiesAsync(room.Id, new[] { (MachineTypes.Default, -1) }));

        await svc.SetCapacitiesAsync(room.Id, new[] { (MachineTypes.Default, 5), ("Laptop", 0) });
        var rows = await db.RoomCapacities.ToListAsync();
        Assert.Equal(MachineTypes.Default, Assert.Single(rows).MachineType);
        Assert.Equal(5, rows[0].PlannedCount);

        await svc.SetCapacitiesAsync(room.Id, new[] { ("Laptop", 2) });
        Assert.Equal("Laptop", (await db.RoomCapacities.SingleAsync()).MachineType);
    }

    [Fact]
    public void NormaliseName_CollapsesInnerWhitespace()
    {
        Assert.Equal("Αίθουσα 3", RoomService.NormaliseName("  Αίθουσα  \t 3 "));
        Assert.True(RoomService.SameName("Αίθουσα 3", "Αίθουσα  3"));
    }

    [Fact]
    public async Task AssignByName_ReusesRoomIgnoringDoubleSpaces()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync("Αίθουσα 3");
        var pc = Pc("PC1");
        db.Computers.Add(pc);

        var created = await svc.AssignByNameAsync(pc, "Αίθουσα  3");
        await db.SaveChangesAsync();

        Assert.False(created);
        Assert.Equal(room.Id, pc.RoomId);
        Assert.Equal(1, await db.Rooms.CountAsync());
    }

    [Fact]
    public async Task Merge_MovesEverythingToTargetAndDeletesSource()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var source = await svc.CreateAsync("Lab A", description: "παλιά περιγραφή");
        source.AutoShutdown = "21:00";
        source.RoomTypeId = null;
        var target = await svc.CreateAsync("Lab B");
        var pcA = Pc("PC1");
        var pcB = Pc("PC2");
        var legacy = Pc("PC3", "lab a");
        db.Computers.AddRange(pcA, pcB, legacy);
        await svc.AssignAsync(pcA, source.Id);
        await svc.AssignAsync(pcB, target.Id);
        var cls = new SchoolClass { Location = "Lab A", Name = "Β1" };
        db.SchoolClasses.Add(cls);
        db.PlanItems.Add(new PlanItem { Location = "LAB A", Label = "πόρτα", Kind = "label" });
        db.RoomPlans.Add(new RoomPlan { Location = "Lab A", AspectRatio = 2 });
        await db.SaveChangesAsync();
        db.Seats.Add(new Seat { SchoolClassId = cls.Id, ComputerId = pcA.Id, StudentName = "Α" });
        db.RoomCapacities.AddRange(
            new RoomCapacity { RoomId = source.Id, MachineType = MachineTypes.Default, PlannedCount = 9 },
            new RoomCapacity { RoomId = source.Id, MachineType = "Laptop", PlannedCount = 2 },
            new RoomCapacity { RoomId = target.Id, MachineType = MachineTypes.Default, PlannedCount = 5 });
        await db.SaveChangesAsync();

        await svc.MergeAsync(source.Id, target.Id);

        db.ChangeTracker.Clear();
        var room = await db.Rooms.SingleAsync();
        Assert.Equal(target.Id, room.Id);
        Assert.Equal("21:00", room.AutoShutdown);
        Assert.Equal("παλιά περιγραφή", room.Description);
        Assert.All(await db.Computers.ToListAsync(), c => { Assert.Equal(target.Id, c.RoomId); Assert.Equal("Lab B", c.Location); });
        Assert.Equal("Lab B", (await db.SchoolClasses.SingleAsync()).Location);
        Assert.Equal("Α", (await db.Seats.SingleAsync()).StudentName);
        Assert.Equal("Lab B", (await db.PlanItems.SingleAsync()).Location);
        var plan = await db.RoomPlans.SingleAsync();
        Assert.Equal("Lab B", plan.Location);
        Assert.Equal(2, plan.AspectRatio);
        var caps = await db.RoomCapacities.OrderBy(c => c.MachineType).ToListAsync();
        Assert.All(caps, c => Assert.Equal(target.Id, c.RoomId));
        Assert.Equal(5, caps.Single(c => c.MachineType == MachineTypes.Default).PlannedCount);
        Assert.Equal(2, caps.Single(c => c.MachineType == "Laptop").PlannedCount);
    }

    [Fact]
    public async Task Merge_KeepsTargetPlanAndSettings()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var building = new Building { Name = "Κ1" };
        db.Buildings.Add(building);
        await db.SaveChangesAsync();
        var floor1 = new Floor { Name = "Ισόγειο", BuildingId = building.Id };
        var floor2 = new Floor { Name = "1ος", BuildingId = building.Id, Level = 1 };
        db.Floors.AddRange(floor1, floor2);
        await db.SaveChangesAsync();
        var source = await svc.CreateAsync("Lab A", floor1.Id, description: "Α");
        source.AutoShutdown = "21:00";
        var target = await svc.CreateAsync("Lab B", floor2.Id, description: "Β");
        target.AutoShutdown = "20:00";
        db.RoomPlans.AddRange(new RoomPlan { Location = "Lab A", AspectRatio = 2 }, new RoomPlan { Location = "Lab B", AspectRatio = 1 });
        await db.SaveChangesAsync();

        await svc.MergeAsync(source.Id, target.Id);

        db.ChangeTracker.Clear();
        var room = await db.Rooms.SingleAsync();
        Assert.Equal(floor2.Id, room.FloorId);
        Assert.Equal("Β", room.Description);
        Assert.Equal("20:00", room.AutoShutdown);
        var plan = await db.RoomPlans.SingleAsync();
        Assert.Equal("Lab B", plan.Location);
        Assert.Equal(1, plan.AspectRatio);
    }

    [Fact]
    public async Task Merge_RejectsSameRoom()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync(Lab);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.MergeAsync(room.Id, room.Id));
        Assert.Equal(1, await db.Rooms.CountAsync());
    }

    [Fact]
    public async Task DeleteImpact_CountsWhatDeleteRemoves()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync(Lab);
        await svc.CreateAsync("Γραφείο");
        var pc = Pc("PC1");
        db.Computers.AddRange(pc, Pc("PC2", "Γραφείο"));
        await svc.AssignAsync(pc, room.Id);
        var c1 = new SchoolClass { Location = "ΕΡΓΑΣΤΗΡΙΟ 1", Name = "Β1" };
        var c2 = new SchoolClass { Location = Lab, Name = "Β2" };
        db.SchoolClasses.AddRange(c1, c2, new SchoolClass { Location = "Γραφείο", Name = "Γ1" });
        db.PlanItems.Add(new PlanItem { Location = Lab, Label = "x", Kind = "label" });
        await db.SaveChangesAsync();
        db.Seats.AddRange(
            new Seat { SchoolClassId = c1.Id, ComputerId = pc.Id, StudentName = "Α" },
            new Seat { SchoolClassId = c2.Id, ComputerId = pc.Id, StudentName = "Β" });
        await db.SaveChangesAsync();

        var impact = await svc.DeleteImpactAsync(room.Id);

        Assert.Equal(1, impact.Devices);
        Assert.Equal(2, impact.Classes);
        Assert.Equal(2, impact.Seats);
        Assert.True(impact.HasPlan);
        Assert.Equal("Θα διαγραφούν επίσης: 2 τμήματα (με 2 θέσεις μαθητών) και η κάτοψη του χώρου. Η 1 συσκευή του μένει χωρίς χώρο (δεν διαγράφεται).",
            impact.Describe());
    }

    [Fact]
    public async Task DeleteImpact_EmptyRoomHasShortText()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync(Lab);

        var impact = await svc.DeleteImpactAsync(room.Id);

        Assert.Equal(new RoomDeleteImpact(0, 0, 0, false), impact);
        Assert.Equal(string.Empty, impact.Describe());
        Assert.Equal("Θα διαγραφούν επίσης: 1 τμήμα και η κάτοψη του χώρου.", new RoomDeleteImpact(0, 1, 0, true).Describe());
    }

    [Fact]
    public async Task Assign_LegacyValueKeepsOldLocationText()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var pc = Pc("PC1", "Αίθουσα 3");
        var blank = Pc("PC2", "  ");
        db.Computers.AddRange(pc, blank);
        await db.SaveChangesAsync();

        await svc.AssignAsync(pc, RoomService.LegacyRoomId);
        await svc.AssignAsync(blank, RoomService.LegacyRoomId);
        await db.SaveChangesAsync();

        var room = await db.Rooms.SingleAsync();
        Assert.Equal("Αίθουσα 3", room.Name);
        Assert.Equal(room.Id, pc.RoomId);
        Assert.Equal("Αίθουσα 3", pc.Location);
        Assert.Null(blank.RoomId);
        Assert.Null(blank.Location);
    }

    [Fact]
    public async Task Selection_PreservesUnconvertedLocation()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync(Lab);

        Assert.Equal((room.Id, (string?)null), await svc.SelectionAsync(room.Id, Lab));
        Assert.Equal((room.Id, (string?)null), await svc.SelectionAsync(null, "εργαστήριο 1"));
        Assert.Equal((RoomService.LegacyRoomId, "Αίθουσα 3"), await svc.SelectionAsync(null, " Αίθουσα 3 "));
        Assert.Equal(((int?)null, (string?)null), await svc.SelectionAsync(null, "  "));

        // Επανεμφάνιση μετά από POST: κρατιέται η επιλογή του χρήστη, ο παλιός χώρος μένει διαθέσιμος
        Assert.Equal(((int?)null, "Αίθουσα 3"), await svc.SelectionAsync(null, "Αίθουσα 3", posted: true));
        Assert.Equal((room.Id, "Αίθουσα 3"), await svc.SelectionAsync(room.Id, "Αίθουσα 3", posted: true));
        Assert.Equal((RoomService.LegacyRoomId, "Αίθουσα 3"), await svc.SelectionAsync(RoomService.LegacyRoomId, "Αίθουσα 3", posted: true));
    }

    [Fact]
    public async Task MoveDevices_SetsRoomAndUpdatedBy()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var svc = new RoomService(db);
        var room = await svc.CreateAsync(Lab);
        var a = Pc("A");
        var b = Pc("B");
        db.Computers.AddRange(a, b);
        await db.SaveChangesAsync();

        var n = await svc.MoveDevicesAsync(new[] { a.Id, b.Id }, room.Id, "admin");

        Assert.Equal(2, n);
        db.ChangeTracker.Clear();
        Assert.All(await db.Computers.ToListAsync(), c => { Assert.Equal(Lab, c.Location); Assert.Equal("admin", c.UpdatedBy); });
    }
}
