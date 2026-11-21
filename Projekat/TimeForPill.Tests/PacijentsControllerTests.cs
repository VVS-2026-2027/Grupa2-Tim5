using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using TimeForPill.Controllers;
using TimeForPill.Data;
using TimeForPill.Models;

namespace TimeForPill.Tests;

[TestClass]
public class PacijentsControllerTests : UnitTestBase
{
    [TestMethod]
    public async Task GetActions_ReturnModelsAndNotFoundForInvalidPatients()
    {
        await using var context = CreateContext();
        var (_, patient, _) = await SeedAsync(context);
        var controller = CreateController(context);

        var index = await controller.Index() as ViewResult;
        Assert.AreEqual(patient.Id, ((List<Pacijent>)index!.Model!).Single().Id);
        var details = await controller.Details(patient.Id) as ViewResult;
        Assert.AreEqual(patient.KontaktOsoba.Email, ((Pacijent)details!.Model!).KontaktOsoba!.Email);
        var create = await controller.Create() as ViewResult;
        Assert.IsNotNull(((Pacijent)create!.Model!).KontaktOsoba);
        Assert.IsInstanceOfType(await controller.Details(null), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Details("missing"), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Edit(null), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Delete("missing"), typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task Create_ValidatesDuplicateContactEmailAndAssignsLeastRecentlyUsedDoctor()
    {
        await using var context = CreateContext();
        var (firstDoctor, _, secondDoctor) = await SeedAsync(context);
        var manager = CreateManager();
        manager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        var controller = CreateController(context, manager.Object);
        var duplicate = NewPatient("duplicate", "same@test", "same@test");

        var invalid = await controller.Create(duplicate, "Password1!") as ViewResult;
        Assert.IsNotNull(invalid);
        Assert.IsTrue(controller.ModelState.ContainsKey("KontaktOsoba.Email"));

        var candidate = NewPatient("new", "new@test", "contact@test");
        var result = await CreateController(context, manager.Object).Create(candidate, "Password1!");
        Assert.IsInstanceOfType(result, typeof(RedirectToActionResult));
        Assert.AreEqual(firstDoctor.Id, candidate.LjekarId);
        Assert.AreEqual(candidate.Email, candidate.UserName);
        Assert.IsTrue(candidate.DatumDodjeleLjekara > DateTime.Now.AddMinutes(-1));
        Assert.AreNotEqual(secondDoctor.Id, candidate.LjekarId);
    }

    [TestMethod]
    public async Task EditAndDelete_UpdatePatientAndRemoveDependentRecords()
    {
        await using var context = CreateContext();
        var (doctor, patient, secondDoctor) = await SeedAsync(context);
        var medicine = new Lijek { Naziv = "Lijek", Kategorija = "Test" };
        var therapy = new Terapija { Naziv = "Terapija", PacijentId = patient.Id, Lijek = medicine, Pocetak = DateTime.Today, Kraj = DateTime.Today.AddDays(2), DnevnaDoza = 1, UkupanBrojDoza = 2, BrojDozaPoObnovi = 2, IntervalSati = 24 };
        context.AddRange(medicine, therapy, new Ticket { KorisnikId = patient.Id, Naslov = "Pomoc", Opis = "Tekst" });
        await context.SaveChangesAsync();
        context.AddRange(
            new TerapijskaDoza { TerapijaId = therapy.Id, RedniBroj = 1, VrijemeUzimanja = DateTime.Now, VrijemePodsjetnika = DateTime.Now.AddMinutes(-5), Status = StatusDoze.Cekanje },
            new Notifikacija { TerapijaId = therapy.Id, Naziv = "N", Poruka = "P" },
            new Zahtjev { TerapijaId = therapy.Id, Naziv = "Z", Sadrzaj = "S" });
        await context.SaveChangesAsync();
        var admin = new Administrator { Id = "admin", Ime = "Sara", Prezime = "Admin" };
        var manager = CreateManager();
        manager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(admin);
        manager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        var edited = NewPatient(patient.Id, "changed@test", "contact-changed@test");
        edited.Ime = "Izmijenjeno";
        edited.LjekarId = secondDoctor.Id;

        var editResult = await CreateController(context, manager.Object).Edit(patient.Id, edited);
        Assert.IsInstanceOfType(editResult, typeof(RedirectToActionResult));
        Assert.AreEqual("Izmijenjeno", (await context.Pacijenti.FindAsync(patient.Id))!.Ime);
        Assert.AreEqual(secondDoctor.Id, (await context.Pacijenti.FindAsync(patient.Id))!.LjekarId);
        Assert.IsTrue(await context.AdminAkcije.AnyAsync(a => a.VrstaAkcije == "Uredjen"));
        Assert.IsInstanceOfType(await CreateController(context, manager.Object).Edit("wrong", edited), typeof(NotFoundResult));

        var deleting = CreateController(context, manager.Object);
        var deleteResult = await deleting.DeleteConfirmed(patient.Id);
        Assert.IsInstanceOfType(deleteResult, typeof(RedirectToActionResult));
        Assert.IsNull(await context.Pacijenti.FindAsync(patient.Id));
        Assert.AreEqual(0, await context.Terapije.CountAsync());
        Assert.AreEqual(0, await context.TerapijskeDoze.CountAsync());
        Assert.AreEqual(0, await context.Notifikacije.CountAsync());
        Assert.AreEqual(0, await context.Zahtjevi.CountAsync());
        Assert.AreEqual(0, await context.Tickets.CountAsync());
    }

    private static async Task<(Ljekar First, Pacijent Patient, Ljekar Second)> SeedAsync(ApplicationDbContext context)
    {
        var first = new Ljekar { Id = "doctor-a", Ime = "Amar", Prezime = "Doktor" };
        var second = new Ljekar { Id = "doctor-b", Ime = "Belma", Prezime = "Doktor" };
        var patient = NewPatient("patient", "patient@test", "contact@test");
        patient.Ljekar = second;
        patient.DatumDodjeleLjekara = DateTime.Today;
        context.AddRange(first, second, patient);
        await context.SaveChangesAsync();
        return (first, patient, second);
    }

    private static Pacijent NewPatient(string id, string email, string contactEmail) => new()
    {
        Id = id,
        Ime = "Amina",
        Prezime = "Kovac",
        Email = email,
        UserName = email,
        DatumRodjenja = new DateTime(1990, 1, 1),
        Spol = Spol.Zensko,
        KontaktOsoba = new KontaktOsoba { Ime = "Kontakt", Prezime = "Osoba", Email = contactEmail, BrojTelefona = "061111111" }
    };

    private static PacijentsController CreateController(ApplicationDbContext context, UserManager<ApplicationUser>? manager = null)
    {
        manager ??= CreateManager().Object;
        var controller = new PacijentsController(context, manager)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin")], "Test")) } }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static Mock<UserManager<ApplicationUser>> CreateManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
