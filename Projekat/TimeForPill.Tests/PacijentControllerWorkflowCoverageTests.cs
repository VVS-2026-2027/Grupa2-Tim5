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
public class PacijentControllerWorkflowCoverageTests : UnitTestBase
{
    private static bool _classInitialized;

    [ClassInitialize]
    public static void InitializeClass(TestContext _) => _classInitialized = true;

    [ClassCleanup]
    public static void CleanupClass() => _classInitialized = false;

    [TestMethod]
    public async Task Home_AuthenticatedPatient_ReturnsDashboardView()
    {
        Assert.IsTrue(_classInitialized);
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "home");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        Assert.IsInstanceOfType(await controller.Home(), typeof(ViewResult));
    }

    [TestMethod]
    public async Task HomeStatus_AuthenticatedPatient_ReturnsJsonStatus()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "home-status");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        Assert.IsInstanceOfType(await controller.HomeStatus(), typeof(JsonResult));
    }

    [TestMethod]
    public async Task MojiLijekovi_ActiveTherapy_ReturnsMedicineListView()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "my-medicines");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        Assert.IsInstanceOfType(await controller.MojiLijekovi(), typeof(ViewResult));
    }

    [TestMethod]
    public async Task LijekForma_NoTherapyId_ReturnsNewMedicineForm()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "new-form");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        Assert.IsInstanceOfType(await controller.LijekForma((int?)null), typeof(ViewResult));
    }

    [TestMethod]
    public async Task LijekForma_OwnedTherapyId_ReturnsExistingMedicineForm()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "existing-form");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        Assert.IsInstanceOfType(await controller.LijekForma(seeded.Therapy.Id), typeof(ViewResult));
    }

    [TestMethod]
    public async Task ObnovaTerapije_Get_WithActiveTherapy_ReturnsRenewalView()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "renew-get");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        Assert.IsInstanceOfType(await controller.ObnovaTerapije(), typeof(ViewResult));
    }

    [TestMethod]
    public async Task Profil_Get_AuthenticatedPatient_ReturnsProfileView()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "profile-get");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        Assert.IsInstanceOfType(await controller.Profil(), typeof(ViewResult));
    }

    [TestMethod]
    public async Task Raspored_AuthenticatedPatient_ReturnsScheduleView()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "schedule");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        Assert.IsInstanceOfType(await controller.Raspored(), typeof(ViewResult));
    }

    [DataTestMethod]
    [DataRow("mjesecna")]
    [DataRow("godisnja")]
    public async Task Historija_SupportedPeriod_ReturnsHistoryView(string period)
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, $"history-{period}");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        Assert.IsInstanceOfType(await controller.Historija(period), typeof(ViewResult));
    }

    [TestMethod]
    public async Task PrijaviNuspojavu_UnknownTherapy_ReturnsNotFound()
    {
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("side-effect-missing");
        context.Pacijenti.Add(patient);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, patient);

        Assert.IsInstanceOfType(await controller.PrijaviNuspojavu(999), typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task NuspojavaDetalji_UnknownReport_ReturnsNotFound()
    {
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("side-effect-detail");
        context.Pacijenti.Add(patient);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, patient);

        Assert.IsInstanceOfType(await controller.NuspojavaDetalji(999), typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task ObrisiLijek_UnknownTherapy_ReturnsNotFound()
    {
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("delete-missing");
        context.Pacijenti.Add(patient);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, patient);

        Assert.IsInstanceOfType(await controller.ObrisiLijek(999), typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task ObrisiLijek_OwnedTherapy_RemovesTherapy()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "delete-owned");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        await controller.ObrisiLijek(seeded.Therapy.Id);

        Assert.IsFalse(await context.Terapije.AnyAsync(x => x.Id == seeded.Therapy.Id));
    }

    [TestMethod]
    public async Task LijekForma_PostWithoutMedicine_ReturnsValidationView()
    {
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("medicine-invalid");
        context.Pacijenti.Add(patient);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, patient);
        var model = new MedicineFormViewModel { UkupanBrojDoza = 2, IntervalSati = 8 };

        Assert.IsInstanceOfType(await controller.LijekForma(model), typeof(ViewResult));
    }

    [TestMethod]
    public async Task LijekForma_PostWithActiveMedicine_ReturnsDuplicateValidationView()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "medicine-duplicate");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);
        var model = new MedicineFormViewModel
        {
            LijekId = seeded.Medicine.Id,
            Pocetak = DateTime.Today.AddDays(1).AddHours(8),
            UkupanBrojDoza = 1,
            IntervalSati = 24
        };

        Assert.IsInstanceOfType(await controller.LijekForma(model), typeof(ViewResult));
    }

    [TestMethod]
    public async Task LijekForma_PostWithAvailableMedicine_CreatesTherapy()
    {
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("medicine-create");
        var medicine = new Lijek { Naziv = "Vitamin", Kategorija = "Suplement" };
        context.AddRange(patient, medicine);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, patient);
        var model = new MedicineFormViewModel
        {
            LijekId = medicine.Id,
            Pocetak = DateTime.Now.AddDays(1).Date.AddHours(8),
            UkupanBrojDoza = 2,
            IntervalSati = 8
        };

        await controller.LijekForma(model);

        Assert.AreEqual(1, await context.Terapije.CountAsync());
    }

    [TestMethod]
    public async Task LijekForma_PostForExistingTherapy_ReplacesDoseSchedule()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "medicine-update");
        seeded.Dose.Status = StatusDoze.Uzeto;
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);
        var model = new MedicineFormViewModel
        {
            TerapijaId = seeded.Therapy.Id,
            LijekId = seeded.Medicine.Id,
            Pocetak = DateTime.Now.AddDays(1).Date.AddHours(9),
            UkupanBrojDoza = 3,
            IntervalSati = 12
        };

        await controller.LijekForma(model);

        Assert.AreEqual(3, await context.TerapijskeDoze.CountAsync(x => x.TerapijaId == seeded.Therapy.Id));
    }

    [TestMethod]
    public async Task Profil_PostWithOwnContactEmail_ReturnsValidationView()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "profile-same-email");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);
        var model = TestInfrastructure.CreateValidProfile(seeded.Patient);
        model.KontaktEmail = seeded.Patient.Email!;

        Assert.IsInstanceOfType(await controller.Profil(model), typeof(ViewResult));
    }

    [TestMethod]
    public async Task Profil_PostWithValidContact_UpdatesPatientName()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "profile-update");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        await controller.Profil(TestInfrastructure.CreateValidProfile(seeded.Patient));

        Assert.AreEqual("Nova", seeded.Patient.Ime);
    }

    [TestMethod]
    public async Task ObnovaTerapije_PostWithDoctor_CreatesPendingRequest()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "renew-create");
        var doctor = new Ljekar
        {
            Id = "renew-doctor", UserName = "doctor@example.test", Email = "doctor@example.test",
            Ime = "Lana", Prezime = "Doktor"
        };
        seeded.Patient.Ljekar = doctor;
        seeded.Patient.LjekarId = doctor.Id;
        context.Ljekari.Add(doctor);
        await context.SaveChangesAsync();
        var email = new Mock<IEmailService>(MockBehavior.Strict);
        email.Setup(service => service.SendEmailAsync(
                "doctor@example.test", "Pacijent zeli obnovu lijeka", It.IsAny<string>(), true))
            .Returns(Task.CompletedTask);
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient, email.Object);

        await controller.ObnovaTerapije(new RenewTherapyViewModel
        {
            TerapijaId = seeded.Therapy.Id,
            PreostaloDoza = 0,
            Napomena = "Molim obnovu"
        });

        Assert.AreEqual(1, await context.Zahtjevi.CountAsync());
    }

    [TestMethod]
    public async Task Uzmi_UnknownDose_ReturnsNotFound()
    {
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("take-missing");
        context.Pacijenti.Add(patient);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, patient);

        Assert.IsInstanceOfType(await controller.Uzmi(999), typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task Uzmi_DoseInTakingWindow_MarksDoseTaken()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "take-due");
        seeded.Dose.VrijemeUzimanja = DateTime.Now.AddMinutes(-1);
        seeded.Dose.VrijemePodsjetnika = DateTime.Now.AddMinutes(-6);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        await controller.Uzmi(seeded.Dose.Id);

        Assert.AreEqual(StatusDoze.Uzeto, seeded.Dose.Status);
    }

    [TestMethod]
    public async Task Odgodi_FirstPostponement_IncrementsPostponementCount()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "postpone-once");
        seeded.Dose.VrijemeUzimanja = DateTime.Now.AddHours(2);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        await controller.Odgodi(seeded.Dose.Id);

        Assert.AreEqual(1, seeded.Dose.BrojOdgoda);
    }

    [TestMethod]
    public async Task Odgodi_ThirdPostponement_MarksDoseMissed()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "postpone-twice");
        seeded.Dose.VrijemeUzimanja = DateTime.Now.AddHours(2);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        await controller.Odgodi(seeded.Dose.Id);
        await controller.Odgodi(seeded.Dose.Id);
        await controller.Odgodi(seeded.Dose.Id);

        Assert.AreEqual(StatusDoze.Propusteno, seeded.Dose.Status);
    }

    [TestMethod]
    public async Task PrijaviNuspojavu_ValidDescription_CreatesSideEffectReport()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "side-effect-create");
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        await controller.PrijaviNuspojavu(new SideEffectFormViewModel
        {
            TerapijaId = seeded.Therapy.Id, Opis = "Osip", ZavrsenaTerapija = false
        });

        Assert.AreEqual(1, await context.Nuspojave.CountAsync());
    }

    [TestMethod]
    public async Task ObrisiNuspojavu_OwnedReport_RemovesReport()
    {
        await using var context = TestInfrastructure.CreateContext();
        var seeded = await TestInfrastructure.SeedPatientTherapyAsync(context, "side-effect-delete");
        var report = new Nuspojava { PacijentId = seeded.Patient.Id, TerapijaId = seeded.Therapy.Id, LijekId = seeded.Medicine.Id, Opis = "Osip" };
        context.Nuspojave.Add(report);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, seeded.Patient);

        await controller.ObrisiNuspojavu(report.Id);

        Assert.AreEqual(0, await context.Nuspojave.CountAsync());
    }

    [TestMethod]
    public async Task TrySaveImageAsync_UnsupportedFile_ReturnsNull()
    {
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("upload-invalid");
        context.Pacijenti.Add(patient);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, patient);
        var unsupported = new FormFile(new MemoryStream([1, 2]), 0, 2, "file", "malware.exe");

        Assert.IsNull(await TestInfrastructure.InvokePrivateAsync<string?>(controller, "TrySaveImageAsync", unsupported));
    }

    [TestMethod]
    public async Task TrySaveImageAsync_FileOverLimit_ReturnsNull()
    {
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("upload-large");
        context.Pacijenti.Add(patient);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, patient);
        var largeContents = new MemoryStream(new byte[2 * 1024 * 1024 + 1]);
        var oversized = new FormFile(largeContents, 0, largeContents.Length, "file", "large.png");

        Assert.IsNull(await TestInfrastructure.InvokePrivateAsync<string?>(controller, "TrySaveImageAsync", oversized));
    }

    [TestMethod]
    public async Task TrySaveImageAsync_AllowedFile_SavesImage()
    {
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("upload-valid");
        context.Pacijenti.Add(patient);
        await context.SaveChangesAsync();
        var webRoot = Path.Combine(Path.GetTempPath(), $"tfp-image-{Guid.NewGuid():N}");
        var controller = TestInfrastructure.CreatePatientController(context, patient, webRoot: webRoot);
        var image = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", "allowed.PNG");
        var saved = await TestInfrastructure.InvokePrivateAsync<string?>(controller, "TrySaveImageAsync", image);
        try
        {
            Assert.IsTrue(File.Exists(Path.Combine(webRoot, saved!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));
        }
        finally
        {
            if (Directory.Exists(webRoot)) Directory.Delete(webRoot, recursive: true);
        }
    }

}
