using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeForPill.Controllers;
using TimeForPill.Data;
using TimeForPill.Models;

namespace TimeForPill.Tests;

[TestClass]
public class CrudControllerTests
{
    [TestMethod]
    public async Task TerapijasController_GetActions_ReturnExpectedModelsAndNotFoundForInvalidIds()
    {
        await using var context = CreateContext();
        var therapy = await SeedTherapyAsync(context);
        var controller = new TerapijasController(context);

        var index = await controller.Index() as ViewResult;
        Assert.IsInstanceOfType(index!.Model, typeof(List<Terapija>));

        var details = await controller.Details(therapy.Id) as ViewResult;
        Assert.AreEqual(therapy.Id, ((Terapija)details!.Model!).Id);
        var create = await controller.Create() as ViewResult;
        Assert.AreEqual(StatusTerapije.Cekanje, ((Terapija)create!.Model!).Status);
        Assert.IsInstanceOfType(await controller.Details(null), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Details(999), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Edit(null), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Delete(null), typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task TerapijasController_CreateEditAndDelete_ChangesPersistedTherapy()
    {
        await using var context = CreateContext();
        var seeded = await SeedTherapyAsync(context);
        var controller = new TerapijasController(context);
        var invalid = new Terapija { Pocetak = DateTime.Today, Kraj = DateTime.Today.AddDays(-1) };

        var invalidResult = await controller.Create(invalid) as ViewResult;

        Assert.IsNotNull(invalidResult);
        Assert.IsTrue(controller.ModelState.ContainsKey(nameof(Terapija.Kraj)));

        var newTherapy = NewTherapy(seeded.LijekId!.Value, seeded.PacijentId!);
        var createResult = await new TerapijasController(context).Create(newTherapy);
        Assert.IsInstanceOfType(createResult, typeof(RedirectToActionResult));
        Assert.AreEqual(2, await context.Terapije.CountAsync());

        var edit = NewTherapy(seeded.LijekId!.Value, seeded.PacijentId!);
        edit.Id = seeded.Id;
        edit.Naziv = "Izmijenjena terapija";
        context.Entry(seeded).State = EntityState.Detached;
        var editResult = await new TerapijasController(context).Edit(seeded.Id, edit);
        Assert.IsInstanceOfType(editResult, typeof(RedirectToActionResult));
        Assert.AreEqual("Izmijenjena terapija", (await context.Terapije.FindAsync(seeded.Id))!.Naziv);
        Assert.IsInstanceOfType(await new TerapijasController(context).Edit(999, edit), typeof(NotFoundResult));

        var deleteResult = await new TerapijasController(context).DeleteConfirmed(seeded.Id);
        Assert.IsInstanceOfType(deleteResult, typeof(RedirectToActionResult));
        Assert.IsNull(await context.Terapije.FindAsync(seeded.Id));
        Assert.IsInstanceOfType(await new TerapijasController(context).Delete(999), typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task NotifikacijasController_ValidatesReferencesAndPerformsCrud()
    {
        await using var context = CreateContext();
        var therapy = await SeedTherapyAsync(context);
        var controller = new global::TimeForPill.NotifikacijasController(context);

        var create = await controller.Create() as ViewResult;
        Assert.IsInstanceOfType(create!.Model, typeof(Notifikacija));
        Assert.IsInstanceOfType(await controller.Details(null), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Details(999), typeof(NotFoundResult));

        var invalid = new Notifikacija { Naziv = "Podsjetnik", Poruka = "Poruka" };
        Assert.IsInstanceOfType(await controller.Create(invalid), typeof(ViewResult));
        Assert.IsTrue(controller.ModelState.ContainsKey(nameof(Notifikacija.TerapijaId)));

        var notification = new Notifikacija { Naziv = "Podsjetnik", Poruka = "Vrijeme za terapiju", TerapijaId = therapy.Id };
        Assert.IsInstanceOfType(await new global::TimeForPill.NotifikacijasController(context).Create(notification), typeof(RedirectToActionResult));
        Assert.AreEqual(1, await context.Notifikacije.CountAsync());

        notification.Naziv = "Izmijenjen podsjetnik";
        Assert.IsInstanceOfType(await new global::TimeForPill.NotifikacijasController(context).Edit(notification.Id, notification), typeof(RedirectToActionResult));
        Assert.IsInstanceOfType(await new global::TimeForPill.NotifikacijasController(context).Edit(notification.Id + 1, notification), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await new global::TimeForPill.NotifikacijasController(context).DeleteConfirmed(notification.Id), typeof(RedirectToActionResult));
        Assert.AreEqual(0, await context.Notifikacije.CountAsync());
    }

    [TestMethod]
    public async Task ZahtjevsController_ValidatesReferencesAndPerformsCrud()
    {
        await using var context = CreateContext();
        var therapy = await SeedTherapyAsync(context);
        var controller = new global::TimeForPill.ZahtjevsController(context);

        var create = await controller.Create() as ViewResult;
        Assert.AreEqual(StatusZahtjeva.Neobraden, ((Zahtjev)create!.Model!).Status);
        Assert.IsInstanceOfType(await controller.Details(null), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Details(999), typeof(NotFoundResult));

        var invalid = new Zahtjev { Naziv = "Obnova", Sadrzaj = "Potreban novi recept", Status = StatusZahtjeva.Neobraden };
        Assert.IsInstanceOfType(await controller.Create(invalid), typeof(ViewResult));
        Assert.IsTrue(controller.ModelState.ContainsKey(nameof(Zahtjev.TerapijaId)));

        var request = new Zahtjev { Naziv = "Obnova", Sadrzaj = "Potreban novi recept", TerapijaId = therapy.Id, Status = StatusZahtjeva.Neobraden };
        Assert.IsInstanceOfType(await new global::TimeForPill.ZahtjevsController(context).Create(request), typeof(RedirectToActionResult));
        Assert.AreEqual(1, await context.Zahtjevi.CountAsync());

        request.Status = StatusZahtjeva.Obraden;
        Assert.IsInstanceOfType(await new global::TimeForPill.ZahtjevsController(context).Edit(request.Id, request), typeof(RedirectToActionResult));
        Assert.IsInstanceOfType(await new global::TimeForPill.ZahtjevsController(context).Edit(request.Id + 1, request), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await new global::TimeForPill.ZahtjevsController(context).DeleteConfirmed(request.Id), typeof(RedirectToActionResult));
        Assert.AreEqual(0, await context.Zahtjevi.CountAsync());
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<Terapija> SeedTherapyAsync(ApplicationDbContext context)
    {
        var patient = new Pacijent
        {
            Id = "patient-1",
            UserName = "patient@example.test",
            Email = "patient@example.test",
            Ime = "Amina",
            Prezime = "Kovac",
            DatumRodjenja = new DateTime(1990, 1, 1),
            Spol = Spol.Zensko,
            KontaktOsoba = null!
        };
        var medication = new Lijek { Naziv = "Paracetamol", Kategorija = "Analgetik" };
        var therapy = NewTherapy(0, patient.Id);
        therapy.Pacijent = patient;
        therapy.Lijek = medication;
        context.AddRange(patient, medication, therapy);
        await context.SaveChangesAsync();
        therapy.LijekId = medication.Id;
        await context.SaveChangesAsync();

        return therapy;
    }

    private static Terapija NewTherapy(int medicationId, string patientId) => new()
    {
        Naziv = "Terapija",
        LijekId = medicationId,
        PacijentId = patientId,
        Status = StatusTerapije.Cekanje,
        Pocetak = DateTime.Today,
        Kraj = DateTime.Today.AddDays(3),
        DnevnaDoza = 1,
        UkupanBrojDoza = 3,
        BrojDozaPoObnovi = 3,
        IntervalSati = 24
    };
}
