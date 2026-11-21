using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TimeForPill.Controllers;
using TimeForPill.Data;
using TimeForPill.Models;
using TimeForPill.Services;
using TimeForPill.ViewModels;

namespace TimeForPill.Tests;

[TestClass]
public class LjekarControllerWorkflowTests : UnitTestBase
{
    [TestMethod]
    public async Task DoctorViews_OnlyExposeAssignedPatientsRequestsAndMedicationState()
    {
        await using var context = Context();
        var doctor = new Ljekar { Id = "doctor", Ime = "Dino", Prezime = "Doktor" };
        var patient = new Pacijent { Id = "patient", Ime = "Amina", Prezime = "Kovac", Ljekar = doctor, Email = "patient@test" };
        var medicine = new Lijek { Naziv = "Aspirin", Kategorija = "Analgetik" };
        var therapy = new Terapija { Naziv = "Terapija", Pacijent = patient, Lijek = medicine, Pocetak = DateTime.Today, Kraj = DateTime.Today.AddDays(1), UkupanBrojDoza = 2, IntervalSati = 24 };
        context.AddRange(doctor, patient, medicine, therapy);
        await context.SaveChangesAsync();
        context.AddRange(new TerapijskaDoza { TerapijaId = therapy.Id, RedniBroj = 1, VrijemeUzimanja = DateTime.Now.AddHours(1), VrijemePodsjetnika = DateTime.Now, Status = StatusDoze.Cekanje }, new Zahtjev { TerapijaId = therapy.Id, Naziv = "Obnova", Sadrzaj = "Poruka\nDoza koje se dodaju obnovom: 2", DatumKreiranja = DateTime.Now, Status = StatusZahtjeva.Neobraden });
        await context.SaveChangesAsync();
        var controller = Controller(context, doctor);

        var home = await controller.Home() as ViewResult;
        Assert.AreEqual(1, ((DoctorDashboardViewModel)home!.Model!).BrojZahtjeva);
        var patients = await controller.MojiPacijenti() as ViewResult;
        Assert.AreEqual(1, ((List<DoctorPatientListItemViewModel>)patients!.Model!).Single().BrojCekajucihDoza);
        var medication = await controller.PacijentLijekovi(patient.Id) as ViewResult;
        Assert.AreEqual("Aspirin", ((DoctorPatientMedicationViewModel)medication!.Model!).Lijekovi.Single().Naziv);
        var request = await controller.ZahtjevDetalji((await context.Zahtjevi.SingleAsync()).Id) as ViewResult;
        Assert.IsFalse(((RequestDetailsViewModel)request!.Model!).Sadrzaj.Contains("Doza koje", StringComparison.Ordinal));
        Assert.IsInstanceOfType(await controller.PacijentLijekovi("other"), typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task DoctorDecision_ExtendsTherapyUpdatesRequestAndNotifiesPatient()
    {
        await using var context = Context();
        var doctor = new Ljekar { Id = "doctor", Ime = "Dino", Prezime = "Doktor" };
        var patient = new Pacijent { Id = "patient", Ime = "Amina", Prezime = "Kovac", Ljekar = doctor, Email = "patient@test" };
        var therapy = new Terapija { Naziv = "T", Pacijent = patient, Pocetak = DateTime.Today, Kraj = DateTime.Today.AddDays(1), UkupanBrojDoza = 1, BrojDozaPoObnovi = 2, IntervalSati = 24 };
        context.AddRange(doctor, patient, therapy);
        await context.SaveChangesAsync();
        context.AddRange(new TerapijskaDoza { TerapijaId = therapy.Id, RedniBroj = 1, VrijemeUzimanja = DateTime.Today.AddHours(8), VrijemePodsjetnika = DateTime.Today.AddHours(7), Status = StatusDoze.Cekanje }, new Zahtjev { TerapijaId = therapy.Id, Naziv = "O", Sadrzaj = "S", Status = StatusZahtjeva.Neobraden });
        await context.SaveChangesAsync();
        var email = new Mock<IEmailService>(); email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false)).Returns(Task.CompletedTask);
        var controller = Controller(context, doctor, email.Object);

        var result = await controller.Potvrdi((await context.Zahtjevi.SingleAsync()).Id);

        Assert.IsInstanceOfType(result, typeof(RedirectToActionResult));
        Assert.AreEqual(StatusZahtjeva.Obraden, (await context.Zahtjevi.SingleAsync()).Status);
        Assert.AreEqual(3, await context.TerapijskeDoze.CountAsync());
        Assert.AreEqual(StatusTerapije.Cekanje, therapy.Status);
    }

    [TestMethod]
    public void DoctorSchedulingHelpers_ProtectQuietHoursAndNormalizeRenewalText()
    {
        var schedule = Invoke<IReadOnlyList<DateTime>>("GenerateRenewalDoseSchedule", new DateTime(2026, 1, 1, 22, 45, 0), 2, 24, Array.Empty<DateTime>());
        Assert.AreEqual(new DateTime(2026, 1, 1, 22, 30, 0), schedule[0]);
        Assert.AreEqual(new DateTime(2026, 1, 2, 22, 30, 0), schedule[1]);
        Assert.AreEqual(6, Invoke<DateTime>("MoveToAllowedDoseTime", new DateTime(2026, 1, 1, 4, 0, 0)).Hour);
        Assert.AreEqual($"A{Environment.NewLine}B", Invoke<string>("RemoveRenewalDoseLine", "A\nDoza koje se dodaju obnovom: 2\nB"));
        Assert.AreEqual(1, Invoke<int>("GetRenewalDoseCount", new Terapija { UkupanBrojDoza = 0 }));
    }

    [TestMethod]
    public async Task DoctorProfileSideEffectsAndRejection_CoverOwnedAndMissingRecords()
    {
        await using var context = Context();
        var doctor = new Ljekar { Id = "doctor", Ime = "Dino", Prezime = "Doktor", Email = "doctor@test", UserName = "doctor@test", DatumRodjenja = new DateTime(1980, 1, 1) };
        var patient = new Pacijent { Id = "patient", Ime = "Amina", Prezime = "Kovac", Ljekar = doctor, LjekarId = doctor.Id, Email = "patient@test" };
        var medicine = new Lijek { Naziv = "Aspirin", Kategorija = "Analgetik" };
        var therapy = new Terapija { Naziv = "Aspirin", Pacijent = patient, Lijek = medicine, Pocetak = DateTime.Today, Kraj = DateTime.Today, UkupanBrojDoza = 1, BrojDozaPoObnovi = 1, IntervalSati = 24 };
        var request = new Zahtjev { Terapija = therapy, Naziv = "Obnova", Sadrzaj = "Potreban recept", Status = StatusZahtjeva.Neobraden };
        context.AddRange(doctor, patient, medicine, therapy, request);
        context.Nuspojave.Add(new Nuspojava { Pacijent = patient, PacijentId = patient.Id, Terapija = therapy, NazivLijeka = "Aspirin", Opis = "Osip" });
        await context.SaveChangesAsync();
        var email = new Mock<IEmailService>();
        email.Setup(service => service.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false)).Returns(Task.CompletedTask);
        var controller = Controller(context, doctor, email.Object);

        var requests = await controller.Zahtjevi() as ViewResult;
        Assert.AreEqual(1, ((List<RequestListItemViewModel>)requests!.Model!).Count);
        var effects = await controller.PacijentNuspojave(patient.Id) as ViewResult;
        Assert.AreEqual(1, ((DoctorPatientSideEffectsViewModel)effects!.Model!).Nuspojave.Count);
        Assert.IsInstanceOfType(await controller.PacijentNuspojave("other"), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Profil(), typeof(ViewResult));

        var profile = new ProfileViewModel
        {
            Ime = "Novo", Prezime = "Doktor", Email = "new-doctor@example.test",
            DatumRodjenja = new DateTime(1980, 1, 1), Spol = Spol.Musko,
            Specijalizacija = Specijalizacija.Kardiolog
        };
        controller.ModelState.Clear();
        Assert.IsInstanceOfType(await controller.Profil(profile), typeof(RedirectToActionResult));
        Assert.AreEqual("new-doctor@example.test", doctor.UserName);

        Assert.IsInstanceOfType(await controller.Odbij(request.Id), typeof(RedirectToActionResult));
        Assert.AreEqual(StatusZahtjeva.Odbijen, request.Status);
        email.Verify(service => service.SendEmailAsync("patient@test", It.IsAny<string>(), It.IsAny<string>(), false), Times.Once);
    }

    private static LjekarController Controller(ApplicationDbContext context, Ljekar doctor, IEmailService? email = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>(); var users = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(u => u.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(doctor);
        users.Setup(u => u.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns(doctor.Id);
        users.Setup(u => u.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        var controller = new LjekarController(context, email ?? Mock.Of<IEmailService>(), NullLogger<LjekarController>.Instance, users.Object) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, doctor.Id)], "Test")) } } };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Mock.Of<ITempDataProvider>()); return controller;
    }
    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
    private static T Invoke<T>(string name, params object?[] values) => (T)typeof(LjekarController).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values)!;
}
