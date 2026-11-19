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
using TimeForPill.Controllers;
using TimeForPill.Data;
using TimeForPill.Models;
using TimeForPill.Services;
using TimeForPill.ViewModels;

namespace TimeForPill.Tests;

[TestClass]
public class PacijentControllerWorkflowCoverageTests
{
    [TestMethod]
    public async Task PatientPages_LoadDashboardMedicineScheduleHistoryAndProfile()
    {
        await using var context = CreateContext();
        var patient = NewPatient();
        var medicine = new Lijek { Naziv = "Analgetik", Kategorija = "Bol" };
        var therapy = NewTherapy(patient, medicine);
        var dose = NewDose(therapy, DateTime.Today.AddHours(23));
        context.AddRange(patient, medicine, therapy, dose);
        context.PacijentDnevneStatistike.Add(new PacijentDnevnaStatistika
        {
            PacijentId = patient.Id,
            Datum = DateTime.Today,
            BrojUzetih = 2,
            BrojPropustenih = 1
        });
        await context.SaveChangesAsync();
        var controller = CreateController(context, patient);

        Assert.IsInstanceOfType(await controller.Home(), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.HomeStatus(), typeof(JsonResult));
        Assert.IsInstanceOfType(await controller.MojiLijekovi(), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.LijekForma((int?)null), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.LijekForma(therapy.Id), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.ObnovaTerapije(), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.Profil(), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.Raspored(), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.Historija("mjesecna"), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.Historija("godisnja"), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.Nuspojave(), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.PrijaviNuspojavu(therapy.Id), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.PrijaviNuspojavu(999), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.NuspojavaDetalji(999), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.ObrisiLijek(999), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.ObrisiLijek(therapy.Id), typeof(RedirectToActionResult));
        Assert.IsFalse(await context.Terapije.AnyAsync(t => t.Id == therapy.Id));
    }

    [TestMethod]
    public async Task MedicineForm_CreateUpdateAndValidationPaths_AreHandled()
    {
        await using var context = CreateContext();
        var patient = NewPatient();
        var medicine = new Lijek { Naziv = "Vitamin", Kategorija = "Suplement" };
        var therapy = NewTherapy(patient, medicine);
        var originalDose = NewDose(therapy, DateTime.Today.AddDays(1).AddHours(8));
        context.AddRange(patient, medicine, therapy, originalDose);
        await context.SaveChangesAsync();
        var controller = CreateController(context, patient);

        var invalid = new MedicineFormViewModel { UkupanBrojDoza = 2, IntervalSati = 8 };
        Assert.IsInstanceOfType(await controller.LijekForma(invalid), typeof(ViewResult));
        Assert.IsTrue(controller.ModelState.ContainsKey(nameof(MedicineFormViewModel.LijekId)));

        controller.ModelState.Clear();
        originalDose.Status = StatusDoze.Uzeto;
        await context.SaveChangesAsync();
        var create = new MedicineFormViewModel
        {
            LijekId = medicine.Id,
            Pocetak = DateTime.Today.AddDays(2).AddHours(8),
            UkupanBrojDoza = 2,
            IntervalSati = 8
        };
        Assert.IsInstanceOfType(await controller.LijekForma(create), typeof(RedirectToActionResult));
        Assert.AreEqual(2, await context.Terapije.CountAsync());
        Assert.AreEqual(3, await context.TerapijskeDoze.CountAsync());

        var createdTherapy = await context.Terapije
            .Where(t => t.Id != therapy.Id)
            .SingleAsync();
        foreach (var createdDose in context.TerapijskeDoze.Where(d => d.TerapijaId == createdTherapy.Id))
        {
            createdDose.Status = StatusDoze.Uzeto;
        }
        await context.SaveChangesAsync();

        controller.ModelState.Clear();
        var activeCreatedDose = await context.TerapijskeDoze
            .FirstAsync(d => d.TerapijaId == createdTherapy.Id);
        activeCreatedDose.Status = StatusDoze.Cekanje;
        await context.SaveChangesAsync();
        var duplicate = new MedicineFormViewModel
        {
            LijekId = medicine.Id,
            Pocetak = DateTime.Today.AddDays(3).AddHours(8),
            UkupanBrojDoza = 1,
            IntervalSati = 24
        };
        Assert.IsInstanceOfType(await controller.LijekForma(duplicate), typeof(ViewResult));
        Assert.IsTrue(controller.ModelState.ContainsKey(nameof(MedicineFormViewModel.LijekId)));

        controller.ModelState.Clear();
        activeCreatedDose.Status = StatusDoze.Uzeto;
        await context.SaveChangesAsync();
        var update = new MedicineFormViewModel
        {
            TerapijaId = therapy.Id,
            LijekId = medicine.Id,
            Pocetak = DateTime.Today.AddDays(4).AddHours(9),
            UkupanBrojDoza = 3,
            IntervalSati = 12
        };
        Assert.IsInstanceOfType(await controller.LijekForma(update), typeof(RedirectToActionResult));
        Assert.AreEqual(3, therapy.UkupanBrojDoza);
        Assert.AreEqual(3, await context.TerapijskeDoze.CountAsync(d => d.TerapijaId == therapy.Id));
    }

    [TestMethod]
    public async Task ProfileAndRenewal_SaveValidDataAndHandleEmailOutcomes()
    {
        await using var context = CreateContext();
        var doctor = new Ljekar { Id = "doctor-1", UserName = "doctor@example.test", Email = "doctor@example.test", Ime = "Lana", Prezime = "Doktor" };
        var patient = NewPatient();
        patient.Ljekar = doctor;
        patient.LjekarId = doctor.Id;
        var medicine = new Lijek { Naziv = "Terapija", Kategorija = "Test" };
        var therapy = NewTherapy(patient, medicine);
        context.AddRange(doctor, patient, medicine, therapy, NewDose(therapy, DateTime.Now.AddHours(2)));
        await context.SaveChangesAsync();
        var controller = CreateController(context, patient);

        var sameEmail = ValidProfile(patient);
        sameEmail.KontaktEmail = patient.Email!;
        Assert.IsInstanceOfType(await controller.Profil(sameEmail), typeof(ViewResult));
        Assert.IsTrue(controller.ModelState.ContainsKey(nameof(ProfileViewModel.KontaktEmail)));

        controller.ModelState.Clear();
        var profile = ValidProfile(patient);
        Assert.IsInstanceOfType(await controller.Profil(profile), typeof(RedirectToActionResult));
        Assert.AreEqual("Nova", patient.Ime);
        Assert.AreEqual("kontakt@example.test", patient.KontaktOsoba.Email);

        var email = new Mock<IEmailService>(MockBehavior.Strict);
        email.Setup(service => service.SendEmailAsync(
                "doctor@example.test", "Pacijent zeli obnovu lijeka", It.IsAny<string>(), true))
            .Returns(Task.CompletedTask);
        var renewing = CreateController(context, patient, email.Object);
        var renewResult = await renewing.ObnovaTerapije(new RenewTherapyViewModel
        {
            TerapijaId = therapy.Id,
            PreostaloDoza = 0,
            Napomena = "Molim obnovu"
        });
        Assert.IsInstanceOfType(renewResult, typeof(RedirectToActionResult));
        Assert.AreEqual(1, await context.Zahtjevi.CountAsync());
        Assert.AreEqual(StatusZahtjeva.Neobraden, (await context.Zahtjevi.SingleAsync()).Status);
        email.VerifyAll();

        var mailFailure = new Mock<IEmailService>();
        mailFailure.Setup(service => service.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true))
            .ThrowsAsync(new InvalidOperationException("smtp unavailable"));
        var secondTherapy = NewTherapy(patient, medicine);
        context.Terapije.Add(secondTherapy);
        context.TerapijskeDoze.Add(NewDose(secondTherapy, DateTime.Now.AddHours(3)));
        await context.SaveChangesAsync();
        var failureController = CreateController(context, patient, mailFailure.Object);
        Assert.IsInstanceOfType(await failureController.ObnovaTerapije(new RenewTherapyViewModel
        {
            TerapijaId = secondTherapy.Id,
            PreostaloDoza = 1
        }), typeof(RedirectToActionResult));
        Assert.AreEqual(2, await context.Zahtjevi.CountAsync());
    }

    [TestMethod]
    public async Task DoseAndSideEffectWorkflows_RecordCompletionDeferralAndReports()
    {
        await using var context = CreateContext();
        var patient = NewPatient();
        var medicine = new Lijek { Naziv = "Lijek", Kategorija = "Kategorija", Slika = "/images/test.png" };
        var therapy = NewTherapy(patient, medicine);
        therapy.UkupanBrojDoza = 2;
        var first = NewDose(therapy, DateTime.Now.AddMinutes(-1));
        var second = NewDose(therapy, DateTime.Now.AddHours(2));
        second.RedniBroj = 2;
        second.VrijemePodsjetnika = second.VrijemeUzimanja.AddMinutes(-5);
        context.AddRange(patient, medicine, therapy, first, second);
        await context.SaveChangesAsync();
        var controller = CreateController(context, patient);

        Assert.IsInstanceOfType(await controller.Uzmi(999), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Uzmi(first.Id), typeof(RedirectToActionResult));
        Assert.AreEqual(StatusDoze.Uzeto, first.Status);
        Assert.AreEqual(1, await context.PacijentDnevneStatistike.SumAsync(s => s.BrojUzetih));

        Assert.IsInstanceOfType(await controller.Odgodi(second.Id), typeof(RedirectToActionResult));
        Assert.AreEqual(1, second.BrojOdgoda);
        Assert.AreEqual(1, await context.Notifikacije.CountAsync());

        Assert.IsInstanceOfType(await controller.PrijaviNuspojavu(new SideEffectFormViewModel
        {
            TerapijaId = therapy.Id,
            Opis = "Osip",
            ZavrsenaTerapija = false
        }), typeof(RedirectToActionResult));
        var report = await context.Nuspojave.SingleAsync();
        Assert.AreEqual("Osip", report.Opis);
        Assert.IsInstanceOfType(await controller.NuspojavaDetalji(report.Id), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.Nuspojave(), typeof(ViewResult));
        Assert.IsInstanceOfType(await controller.ObrisiNuspojavu(report.Id), typeof(RedirectToActionResult));
        Assert.AreEqual(0, await context.Nuspojave.CountAsync());
    }

    [TestMethod]
    public async Task DoseWorkflow_HandlesEarlyCompletionSecondDeferralAndNoSideEffectsAnswer()
    {
        await using var context = CreateContext();
        var patient = NewPatient();
        var medicine = new Lijek { Naziv = "Lijek", Kategorija = "Kategorija" };
        var therapy = NewTherapy(patient, medicine);
        var futureDose = NewDose(therapy, DateTime.Now.AddHours(1));
        var completedTherapy = NewTherapy(patient, medicine);
        completedTherapy.Naziv = "Zavrsena terapija";
        var lastDose = NewDose(completedTherapy, DateTime.Now.AddMinutes(-10));
        context.AddRange(patient, medicine, therapy, futureDose, completedTherapy, lastDose);
        await context.SaveChangesAsync();
        var controller = CreateController(context, patient);

        Assert.IsInstanceOfType(await controller.Uzmi(futureDose.Id), typeof(RedirectToActionResult));
        Assert.AreEqual(StatusDoze.Cekanje, futureDose.Status);
        var prompt = await controller.Uzmi(lastDose.Id) as RedirectToActionResult;
        Assert.IsNotNull(prompt);
        Assert.AreEqual(nameof(PacijentController.PrijaviNuspojavu), prompt.ActionName);

        var deferredTherapy = NewTherapy(patient, medicine);
        deferredTherapy.Naziv = "Odlozena terapija";
        var deferredDose = NewDose(deferredTherapy, DateTime.Now.AddHours(3));
        context.AddRange(deferredTherapy, deferredDose);
        await context.SaveChangesAsync();
        Assert.IsInstanceOfType(await controller.Odgodi(deferredDose.Id), typeof(RedirectToActionResult));
        Assert.IsInstanceOfType(await controller.Odgodi(deferredDose.Id), typeof(RedirectToActionResult));
        Assert.AreEqual(StatusDoze.Propusteno, deferredDose.Status);
        Assert.AreEqual(1, await context.PacijentDnevneStatistike.SumAsync(s => s.BrojPropustenih));

        var answer = new SideEffectFormViewModel
        {
            TerapijaId = lastDose.TerapijaId,
            ZavrsenaTerapija = true,
            ImaNuspojave = false
        };
        Assert.IsInstanceOfType(await controller.PrijaviNuspojavu(answer), typeof(RedirectToActionResult));
        Assert.AreEqual(1, await context.Nuspojave.CountAsync(n => n.BezNuspojava));
        Assert.IsInstanceOfType(await controller.PrijaviNuspojavu(answer), typeof(RedirectToActionResult));
        Assert.AreEqual(1, await context.Nuspojave.CountAsync(n => n.BezNuspojava));
    }

    [TestMethod]
    public async Task ImageUpload_RejectsUnsupportedOrLargeFilesAndSavesAllowedImage()
    {
        await using var context = CreateContext();
        var patient = NewPatient();
        context.Pacijenti.Add(patient);
        await context.SaveChangesAsync();
        var webRoot = Path.Combine(Path.GetTempPath(), $"tfp-image-{Guid.NewGuid():N}");
        var controller = CreateController(context, patient, webRoot: webRoot);
        var unsupported = new FormFile(new MemoryStream([1, 2]), 0, 2, "file", "malware.exe");
        Assert.IsNull(await InvokePrivateAsync<string?>(controller, "TrySaveImageAsync", unsupported));
        Assert.IsTrue(controller.ModelState.ContainsKey(nameof(MedicineFormViewModel.SlikaFile)));

        controller.ModelState.Clear();
        var largeContents = new MemoryStream(new byte[2 * 1024 * 1024 + 1]);
        var oversized = new FormFile(largeContents, 0, largeContents.Length, "file", "large.png");
        Assert.IsNull(await InvokePrivateAsync<string?>(controller, "TrySaveImageAsync", oversized));
        Assert.IsTrue(controller.ModelState.ContainsKey(nameof(MedicineFormViewModel.SlikaFile)));

        controller.ModelState.Clear();
        var image = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", "allowed.PNG");
        var saved = await InvokePrivateAsync<string?>(controller, "TrySaveImageAsync", image);
        Assert.IsNotNull(saved);
        Assert.IsTrue(File.Exists(Path.Combine(webRoot, saved.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));
        Directory.Delete(webRoot, recursive: true);
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static PacijentController CreateController(
        ApplicationDbContext context,
        Pacijent patient,
        IEmailService? email = null,
        string? webRoot = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(manager => manager.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns(patient.Id);
        userManager.Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(patient);
        userManager.Setup(manager => manager.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        var workflow = new Mock<IDoseWorkflowService>();
        workflow.Setup(service => service.RefreshMissedDosesAsync()).Returns(Task.CompletedTask);
        workflow.Setup(service => service.SendDueReminderEmailsAsync()).Returns(Task.CompletedTask);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, patient.Id)], "test"))
        };
        var environment = new Mock<IWebHostEnvironment>();
        if (webRoot != null)
        {
            environment.SetupGet(value => value.WebRootPath).Returns(webRoot);
        }
        var controller = new PacijentController(
            context,
            workflow.Object,
            email ?? Mock.Of<IEmailService>(),
            Options.Create(new EmailSettings()),
            environment.Object,
            NullLogger<PacijentController>.Instance,
            userManager.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
        };

        return controller;
    }

    private static async Task<T> InvokePrivateAsync<T>(object instance, string name, params object?[] args)
    {
        var method = instance.GetType().GetMethod(
            name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        var task = method.Invoke(instance, args) as Task<T>;
        Assert.IsNotNull(task);
        return await task;
    }

    private static Pacijent NewPatient() => new()
    {
        Id = "patient-coverage",
        UserName = "patient@example.test",
        Email = "patient@example.test",
        Ime = "Amina",
        Prezime = "Kovac",
        DatumRodjenja = new DateTime(1990, 1, 1),
        Spol = Spol.Zensko
    };

    private static Terapija NewTherapy(Pacijent patient, Lijek medicine) => new()
    {
        Naziv = medicine.Naziv,
        Lijek = medicine,
        Pacijent = patient,
        PacijentId = patient.Id,
        Status = StatusTerapije.Cekanje,
        Pocetak = DateTime.Today,
        Kraj = DateTime.Today.AddDays(2),
        DnevnaDoza = 1,
        UkupanBrojDoza = 1,
        BrojDozaPoObnovi = 1,
        IntervalSati = 24
    };

    private static TerapijskaDoza NewDose(Terapija therapy, DateTime time) => new()
    {
        Terapija = therapy,
        RedniBroj = 1,
        VrijemeUzimanja = time,
        OriginalnoVrijemeUzimanja = time,
        VrijemePodsjetnika = time.AddMinutes(-5),
        Status = StatusDoze.Cekanje
    };

    private static ProfileViewModel ValidProfile(Pacijent patient) => new()
    {
        KorisnikId = patient.Id,
        Ime = "Nova",
        Prezime = "Osoba",
        DatumRodjenja = patient.DatumRodjenja,
        Email = patient.Email!,
        KontaktIme = "Kontakt",
        KontaktPrezime = "Osoba",
        KontaktEmail = "kontakt@example.test",
        KontaktTelefon = "061000000",
        Uloga = KorisnickaUloga.Pacijent,
        PrikaziKontaktOsobu = true
    };
}
