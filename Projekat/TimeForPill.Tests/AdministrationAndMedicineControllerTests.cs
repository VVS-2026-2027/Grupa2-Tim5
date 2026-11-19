using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TimeForPill.Data;
using TimeForPill.Models;
using TimeForPill.Services;
using TimeForPill.ViewModels;

namespace TimeForPill.Tests;

[TestClass]
public class AdministrationAndMedicineControllerTests
{
    [TestMethod]
    public async Task LijeksController_BlocksPatientsAndSupportsDoctorCatalogCrud()
    {
        await using var context = CreateContext();
        var patient = new Pacijent { Id = "patient", Ime = "Amina", Prezime = "Kovac" };
        var doctor = new Ljekar { Id = "doctor", Ime = "Doktor", Prezime = "Kovac" };
        context.AddRange(patient, doctor);
        context.Add(new Lijek { Naziv = "Aspirin", Kategorija = "Analgetik" });
        await context.SaveChangesAsync();

        var blocked = CreateMedicineController(context, patient);
        blocked.ControllerContext = AuthenticatedContext();
        Assert.IsInstanceOfType(await blocked.Index(), typeof(ForbidResult));

        var controller = CreateMedicineController(context, doctor);
        controller.ControllerContext = AuthenticatedContext();
        var index = await controller.Index() as ViewResult;
        Assert.AreEqual("Aspirin", ((List<Lijek>)index!.Model!).Single().Naziv);
        Assert.IsInstanceOfType(await controller.Details(null), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Details(999), typeof(NotFoundResult));

        var invalid = new Lijek { Naziv = "", Kategorija = "" };
        controller.ModelState.AddModelError(nameof(Lijek.Naziv), "required");
        Assert.IsInstanceOfType(await controller.Create(invalid, null), typeof(ViewResult));

        var created = new Lijek { Naziv = "Vitamin C", Kategorija = "Vitamin" };
        var createResult = await CreateMedicineController(context, doctor).Create(created, null);
        Assert.IsInstanceOfType(createResult, typeof(RedirectToActionResult));
        Assert.IsTrue(created.Slika!.Contains("Vitamin%20C", StringComparison.Ordinal));

        var saved = await context.Lijekovi.SingleAsync(l => l.Naziv == "Vitamin C");
        var edited = new Lijek { Id = saved.Id, Naziv = "Vitamin C Forte", Kategorija = "Vitamin" };
        Assert.IsInstanceOfType(await CreateMedicineController(context, doctor).Edit(saved.Id, edited, null), typeof(RedirectToActionResult));
        Assert.AreEqual("Vitamin C Forte", (await context.Lijekovi.FindAsync(saved.Id))!.Naziv);
        Assert.IsInstanceOfType(await CreateMedicineController(context, doctor).Edit(saved.Id + 20, edited, null), typeof(NotFoundResult));

        Assert.IsInstanceOfType(await CreateMedicineController(context, doctor).DeleteConfirmed(saved.Id), typeof(RedirectToActionResult));
        Assert.IsNull(await context.Lijekovi.FindAsync(saved.Id));
    }

    [TestMethod]
    public async Task LijeksController_RejectsInvalidImageAndMedicationUsedByTherapy()
    {
        await using var context = CreateContext();
        var doctor = new Ljekar { Id = "doctor", Ime = "Doktor", Prezime = "Kovac" };
        var patient = new Pacijent { Id = "patient", Ime = "Amina", Prezime = "Kovac" };
        var medicine = new Lijek { Naziv = "Koristi se", Kategorija = "Test" };
        context.AddRange(doctor, patient, medicine);
        await context.SaveChangesAsync();
        context.Terapije.Add(new Terapija { Naziv = "Terapija", PacijentId = patient.Id, LijekId = medicine.Id, Pocetak = DateTime.Today, Kraj = DateTime.Today.AddDays(2), DnevnaDoza = 1, UkupanBrojDoza = 2, BrojDozaPoObnovi = 2, IntervalSati = 24 });
        await context.SaveChangesAsync();

        var controller = CreateMedicineController(context, doctor);
        var formFile = new FormFile(new MemoryStream([1, 2]), 0, 2, "image", "unsafe.exe");
        var create = await controller.Create(new Lijek { Naziv = "Nova", Kategorija = "Test" }, formFile) as ViewResult;
        Assert.IsNotNull(create);
        Assert.IsTrue(controller.ModelState.ContainsKey(nameof(Lijek.Slika)));

        var delete = await CreateMedicineController(context, doctor).DeleteConfirmed(medicine.Id) as ViewResult;
        Assert.AreEqual("Delete", delete!.ViewName);
        Assert.IsNotNull(delete.ViewData.ModelState[string.Empty]);
        Assert.IsNotNull(await context.Lijekovi.FindAsync(medicine.Id));
    }

    [TestMethod]
    public async Task AdministratorsController_MapsValidationIdentityResultsAndEditState()
    {
        await using var context = CreateContext();
        var existing = new Administrator { Id = "admin", Ime = "Stari", Prezime = "Admin", Email = "old@test", UserName = "old@test", DatumRodjenja = new DateTime(1980, 1, 1), datumImenovanja = DateTime.Today, Spol = Spol.Musko };
        context.Add(existing);
        await context.SaveChangesAsync();
        var manager = CreateUserManager();
        manager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        manager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        manager.Setup(m => m.FindByIdAsync("admin")).ReturnsAsync(existing);
        manager.Setup(m => m.DeleteAsync(existing)).ReturnsAsync(IdentityResult.Success);
        var controller = new global::TimeForPill.AdministratorsController(context, manager.Object);

        var index = await controller.Index() as ViewResult;
        Assert.AreEqual("Stari", ((List<Administrator>)index!.Model!).Single().Ime);
        Assert.IsInstanceOfType(await controller.Details(null), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Details("missing"), typeof(NotFoundResult));
        var defaults = controller.Create() as ViewResult;
        Assert.AreEqual(DateTime.Today, ((Administrator)defaults!.Model!).datumImenovanja);

        controller.ModelState.AddModelError("x", "invalid");
        Assert.IsInstanceOfType(await controller.Create(new Administrator(), "pass"), typeof(ViewResult));
        var candidate = new Administrator { Ime = "Novi", Prezime = "Admin", Email = "new@test", DatumRodjenja = new DateTime(1990, 1, 1), datumImenovanja = DateTime.Today, Spol = Spol.Zensko };
        var create = await new global::TimeForPill.AdministratorsController(context, manager.Object).Create(candidate, "pass");
        Assert.IsInstanceOfType(create, typeof(RedirectToActionResult));
        Assert.AreEqual(candidate.Email, candidate.UserName);

        var update = new Administrator { Id = existing.Id, Ime = "Novo", Prezime = "Prezime", Email = "new-admin@test", DatumRodjenja = existing.DatumRodjenja, datumImenovanja = existing.datumImenovanja, Spol = existing.Spol };
        Assert.IsInstanceOfType(await new global::TimeForPill.AdministratorsController(context, manager.Object).Edit(existing.Id, update), typeof(RedirectToActionResult));
        Assert.AreEqual("Novo", existing.Ime);
        Assert.AreEqual("new-admin@test", existing.UserName);
        Assert.IsInstanceOfType(await new global::TimeForPill.AdministratorsController(context, manager.Object).Edit("wrong", update), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await new global::TimeForPill.AdministratorsController(context, manager.Object).DeleteConfirmed(existing.Id), typeof(RedirectToActionResult));
    }

    [TestMethod]
    public async Task AdministratorsController_ExposesIdentityErrors()
    {
        await using var context = CreateContext();
        var manager = CreateUserManager();
        manager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Email already exists" }));
        var controller = new global::TimeForPill.AdministratorsController(context, manager.Object);
        var candidate = new Administrator { Ime = "Novi", Prezime = "Admin", Email = "new@test", DatumRodjenja = new DateTime(1990, 1, 1), datumImenovanja = DateTime.Today, Spol = Spol.Zensko };

        var result = await controller.Create(candidate, "pass") as ViewResult;

        Assert.IsNotNull(result);
        Assert.IsTrue(controller.ModelState[string.Empty]!.Errors.Any(e => e.ErrorMessage == "Email already exists"));
    }

    [TestMethod]
    public async Task AdminController_BuildsDashboardAndProtectsActionsForNonAdmins()
    {
        await using var context = CreateContext();
        var administrator = new Administrator { Id = "admin", Ime = "Sara", Prezime = "Admin", Email = "admin@test", UserName = "admin@test" };
        var doctor = new Ljekar { Id = "doctor", Ime = "Dino", Prezime = "Doktor", Email = "doctor@test", UserName = "doctor@test", EmailConfirmed = false, Specijalizacija = Specijalizacija.Kardiolog };
        context.AddRange(administrator, doctor, new Pacijent { Id = "patient", Ime = "Amina", Prezime = "Pacijent" });
        context.AdminAkcije.Add(new AdminAkcija { AdministratorId = administrator.Id, AdministratorNaziv = "Sara Admin", VrstaAkcije = "Potvrdjen", TipRacuna = "Ljekar", RacunId = "old", RacunNaziv = "Stari", RacunEmail = "old@test", DatumAkcije = DateTime.Now });
        await context.SaveChangesAsync();
        var manager = CreateUserManager();
        manager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(administrator);
        manager.Setup(m => m.FindByIdAsync(doctor.Id)).ReturnsAsync(doctor);
        manager.SetupGet(m => m.Users).Returns(context.Users);
        var controller = CreateAdminController(context, manager.Object, new Mock<IEmailService>().Object);

        var home = await controller.Home() as ViewResult;
        var dashboard = (AdminDashboardViewModel)home!.Model!;
        Assert.AreEqual(1, dashboard.BrojPacijenata);
        Assert.AreEqual(1, dashboard.BrojLjekara);
        Assert.AreEqual(1, dashboard.BrojZahtjevaZaPotvrdu);
        Assert.AreEqual("Sara", dashboard.Ime);

        var requests = await controller.ZahtjeviNaloga() as ViewResult;
        Assert.IsTrue(((List<AccountApprovalRequestViewModel>)requests!.Model!).Any(r => r.Id == doctor.Id && r.Status == "Neobraden"));
        var details = await controller.ZahtjevNalogaDetalji(doctor.Id) as ViewResult;
        Assert.AreEqual("Ljekar", ((AccountApprovalRequestViewModel)details!.Model!).Uloga);
        Assert.IsInstanceOfType(await controller.ZahtjevNalogaDetalji("missing"), typeof(NotFoundResult));

        manager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(new Pacijent());
        Assert.IsInstanceOfType(await controller.PregledPacijenata(), typeof(RedirectToActionResult));
    }

    [TestMethod]
    public async Task AdminController_ApprovesRejectsAndUpdatesProfileWithObservableStateChanges()
    {
        await using var context = CreateContext();
        var administrator = new Administrator { Id = "admin", Ime = "Sara", Prezime = "Admin", Email = "admin@test", UserName = "admin@test" };
        var approved = new Ljekar { Id = "approve", Ime = "Dino", Prezime = "Doktor", Email = "doctor@test", UserName = "doctor@test", EmailConfirmed = false };
        var rejected = new Ljekar { Id = "reject", Ime = "Lejla", Prezime = "Doktor", Email = "reject@test", UserName = "reject@test" };
        context.Add(administrator);
        await context.SaveChangesAsync();
        var manager = CreateUserManager();
        manager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(administrator);
        manager.Setup(m => m.FindByIdAsync("approve")).ReturnsAsync(approved);
        manager.Setup(m => m.FindByIdAsync("reject")).ReturnsAsync(rejected);
        manager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        manager.Setup(m => m.DeleteAsync(rejected)).ReturnsAsync(IdentityResult.Success);
        var email = new Mock<IEmailService>();
        email.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true)).Returns(Task.CompletedTask);
        var controller = CreateAdminController(context, manager.Object, email.Object);

        Assert.IsInstanceOfType(await controller.PotvrdiNalog(approved.Id), typeof(RedirectToActionResult));
        Assert.IsTrue(approved.EmailConfirmed);
        Assert.IsTrue(await context.AdminAkcije.AnyAsync(a => a.RacunId == approved.Id && a.VrstaAkcije == "Potvrdjen"));
        Assert.IsInstanceOfType(await controller.OdbijNalog(rejected.Id), typeof(RedirectToActionResult));
        Assert.IsTrue(await context.AdminAkcije.AnyAsync(a => a.RacunId == rejected.Id && a.VrstaAkcije == "Odbijen"));
        Assert.IsInstanceOfType(await controller.PotvrdiNalog("missing"), typeof(NotFoundResult));

        var profile = new ProfileViewModel { Ime = "Novo", Prezime = "Ime", Email = "new@test", DatumRodjenja = new DateTime(1985, 1, 1), Spol = Spol.Zensko };
        Assert.IsInstanceOfType(await controller.Profil(profile), typeof(RedirectToActionResult));
        Assert.AreEqual("Novo", administrator.Ime);
        Assert.AreEqual("new@test", administrator.UserName);
    }

    private static global::TimeForPill.LijeksController CreateMedicineController(ApplicationDbContext context, ApplicationUser user)
    {
        var manager = CreateUserManager();
        manager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.WebRootPath).Returns(Path.GetTempPath());
        return new global::TimeForPill.LijeksController(context, environment.Object, manager.Object);
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static global::TimeForPill.Controllers.AdminController CreateAdminController(ApplicationDbContext context, UserManager<ApplicationUser> manager, IEmailService email)
    {
        var controller = new global::TimeForPill.Controllers.AdminController(
            context,
            Options.Create(new EmailSettings { AppUrl = "https://timeforpill.test" }),
            email,
            NullLogger<global::TimeForPill.Controllers.AdminController>.Instance,
            manager)
        {
            ControllerContext = AuthenticatedContext()
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static ControllerContext AuthenticatedContext() => new()
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "id")], "Test")) }
    };

    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
