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
public class PacijentControllerTddTests
{
    [TestMethod]
    [TestCategory("TDD-F1")]
    public async Task ObnovaTerapije_DuplicatePendingRequest_CreatesOnlyOneRequest()
    {
        await using var context = CreateContext();
        var patient = CreatePatient("patient-1");
        var therapy = new Terapija
        {
            Naziv = "Terapija protiv bolova",
            PacijentId = patient.Id,
            Pacijent = patient,
            Status = StatusTerapije.Cekanje,
            Pocetak = DateTime.Today,
            Kraj = DateTime.Today.AddDays(3),
            DnevnaDoza = 1,
            UkupanBrojDoza = 3,
            BrojDozaPoObnovi = 3,
            IntervalSati = 24
        };
        context.AddRange(patient, therapy, new TerapijskaDoza
        {
            Terapija = therapy,
            RedniBroj = 1,
            VrijemeUzimanja = DateTime.Now.AddHours(1),
            OriginalnoVrijemeUzimanja = DateTime.Now.AddHours(1),
            VrijemePodsjetnika = DateTime.Now.AddMinutes(56),
            Status = StatusDoze.Cekanje
        });
        await context.SaveChangesAsync();
        var controller = CreateController(context, patient);
        var model = new RenewTherapyViewModel { TerapijaId = therapy.Id, PreostaloDoza = 1 };

        await controller.ObnovaTerapije(model);
        await controller.ObnovaTerapije(model);

        Assert.AreEqual(1, await context.Zahtjevi.CountAsync());
    }

    [TestMethod]
    [TestCategory("TDD-F2")]
    public async Task Odgodi_EveningDose_MovesDoseToNextAllowedMorning()
    {
        await using var context = CreateContext();
        var patient = CreatePatient("patient-2");
        var therapy = new Terapija
        {
            Naziv = "Vecernja terapija",
            PacijentId = patient.Id,
            Pacijent = patient,
            Status = StatusTerapije.Cekanje,
            Pocetak = DateTime.Today,
            Kraj = DateTime.Today,
            DnevnaDoza = 1,
            UkupanBrojDoza = 1,
            BrojDozaPoObnovi = 1,
            IntervalSati = 24
        };
        var dose = new TerapijskaDoza
        {
            Terapija = therapy,
            RedniBroj = 1,
            VrijemeUzimanja = DateTime.Today.AddHours(22).AddMinutes(30),
            OriginalnoVrijemeUzimanja = DateTime.Today.AddHours(22).AddMinutes(30),
            VrijemePodsjetnika = DateTime.Today.AddHours(22).AddMinutes(25),
            Status = StatusDoze.Cekanje
        };
        context.AddRange(patient, therapy, dose);
        await context.SaveChangesAsync();
        var controller = CreateController(context, patient);

        await controller.Odgodi(dose.Id);

        Assert.AreEqual(DateTime.Today.AddDays(1).AddHours(6), dose.VrijemeUzimanja);
        Assert.AreEqual(DateTime.Today.AddDays(1).AddHours(5).AddMinutes(55), dose.VrijemePodsjetnika);
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static PacijentController CreateController(
        ApplicationDbContext context,
        Pacijent patient)
    {
        var userManager = CreateUserManager(patient);
        var workflow = new Mock<IDoseWorkflowService>();
        workflow.Setup(service => service.RefreshMissedDosesAsync())
            .Returns(Task.CompletedTask);
        var controller = new PacijentController(
            context,
            workflow.Object,
            new Mock<IEmailService>(MockBehavior.Strict).Object,
            Options.Create(new EmailSettings()),
            new Mock<IWebHostEnvironment>().Object,
            NullLogger<PacijentController>.Instance,
            userManager.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, patient.Id)]))
                }
            }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            new Mock<ITempDataProvider>().Object);

        return controller;
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManager(Pacijent patient)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var manager = new Mock<UserManager<ApplicationUser>>(
            store.Object,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
        manager.Setup(service => service.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(patient.Id);
        manager.Setup(service => service.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(patient);

        return manager;
    }

    private static Pacijent CreatePatient(string id) => new()
    {
        Id = id,
        UserName = "patient@example.test",
        Email = "patient@example.test",
        Ime = "Amina",
        Prezime = "Kovac",
        DatumRodjenja = new DateTime(1990, 1, 1),
        Spol = Spol.Zensko,
        KontaktOsoba = null!
    };
}
