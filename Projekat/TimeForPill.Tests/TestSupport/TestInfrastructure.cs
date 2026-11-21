using System.Reflection;
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

/// <summary>
/// Zajednička, namjerno mala infrastruktura za unit testove. Test klase ovdje
/// samo uzimaju gotov kontekst i zavisnosti; pomoćne metode ne žive u samim
/// testnim klasama.
/// </summary>
public static class TestInfrastructure
{
    public static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    public static Pacijent CreatePatient(string id = "patient-test") => new()
    {
        Id = id,
        UserName = $"{id}@example.test",
        Email = $"{id}@example.test",
        Ime = "Amina",
        Prezime = "Kovac",
        DatumRodjenja = new DateTime(1990, 1, 1),
        Spol = Spol.Zensko,
        KontaktOsoba = null!
    };

    public static Terapija CreateTherapy(Pacijent patient, Lijek medicine) => new()
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

    public static TerapijskaDoza CreateDose(Terapija therapy, DateTime time) => new()
    {
        Terapija = therapy,
        RedniBroj = 1,
        VrijemeUzimanja = time,
        OriginalnoVrijemeUzimanja = time,
        VrijemePodsjetnika = time.AddMinutes(-5),
        Status = StatusDoze.Cekanje
    };

    public static async Task<(Pacijent Patient, Lijek Medicine, Terapija Therapy, TerapijskaDoza Dose)>
        SeedPatientTherapyAsync(ApplicationDbContext context, string patientId = "patient-test")
    {
        var patient = CreatePatient(patientId);
        var medicine = new Lijek { Naziv = "Test lijek", Kategorija = "Test" };
        var therapy = CreateTherapy(patient, medicine);
        var dose = CreateDose(therapy, DateTime.Today.AddHours(10));
        context.AddRange(patient, medicine, therapy, dose);
        await context.SaveChangesAsync();
        return (patient, medicine, therapy, dose);
    }

    public static ProfileViewModel CreateValidProfile(Pacijent patient) => new()
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

    public static async Task<(ApplicationDbContext Context, PacijentController Controller, RenewTherapyViewModel Model)>
        CreateRenewalScenarioAsync()
    {
        var context = CreateContext();
        var patient = CreatePatient("tdd-renewal");
        var medicine = new Lijek { Naziv = "Terapija protiv bolova", Kategorija = "Bol" };
        var therapy = CreateTherapy(patient, medicine);
        context.AddRange(patient, medicine, therapy, CreateDose(therapy, DateTime.Now.AddHours(1)));
        await context.SaveChangesAsync();
        return (context, CreatePatientController(context, patient), new RenewTherapyViewModel { TerapijaId = therapy.Id, PreostaloDoza = 1 });
    }

    public static async Task<(ApplicationDbContext Context, PacijentController Controller, TerapijskaDoza Dose)>
        CreateEveningPostponementScenarioAsync(int hour, int minute)
    {
        var context = CreateContext();
        var seeded = await SeedPatientTherapyAsync(context, "tdd-postpone");
        seeded.Dose.VrijemeUzimanja = DateTime.Today.AddHours(hour).AddMinutes(minute);
        seeded.Dose.OriginalnoVrijemeUzimanja = seeded.Dose.VrijemeUzimanja;
        seeded.Dose.VrijemePodsjetnika = seeded.Dose.VrijemeUzimanja.AddMinutes(-5);
        await context.SaveChangesAsync();
        return (context, CreatePatientController(context, seeded.Patient), seeded.Dose);
    }

    public static PacijentController CreatePatientController(
        ApplicationDbContext context,
        Pacijent patient,
        IEmailService? emailService = null,
        string? webRoot = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var users = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(x => x.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns(patient.Id);
        users.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(patient);
        users.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        var workflow = new Mock<IDoseWorkflowService>();
        workflow.Setup(x => x.RefreshMissedDosesAsync()).Returns(Task.CompletedTask);
        workflow.Setup(x => x.SendDueReminderEmailsAsync()).Returns(Task.CompletedTask);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, patient.Id)], "test"))
        };
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.WebRootPath).Returns(webRoot ?? Path.GetTempPath());

        return new PacijentController(
            context,
            workflow.Object,
            emailService ?? Mock.Of<IEmailService>(),
            Options.Create(new EmailSettings()),
            environment.Object,
            NullLogger<PacijentController>.Instance,
            users.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
        };
    }

    public static async Task<T> InvokePrivateAsync<T>(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"Private method '{methodName}' was not found.");
        var task = method.Invoke(instance, arguments) as Task<T>;
        Assert.IsNotNull(task, $"Private method '{methodName}' did not return Task<{typeof(T).Name}>.");
        return await task;
    }

    public static void InvokeProgramHelper(string methodName, ApplicationDbContext context)
    {
        var program = typeof(ApplicationDbContext).Assembly.GetType("Program");
        Assert.IsNotNull(program);
        var helper = program.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(x => x.Name.Contains(methodName, StringComparison.Ordinal));
        helper.Invoke(null, [context]);
    }

    public static async Task InvokeSeedAdministratorAsync(IServiceProvider services)
    {
        var program = typeof(ApplicationDbContext).Assembly.GetType("Program");
        Assert.IsNotNull(program);
        var helper = program.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(x => x.Name.Contains("SeedDefaultAdministratorAsync", StringComparison.Ordinal));
        var task = helper.Invoke(null, [services]) as Task;
        Assert.IsNotNull(task);
        await task;
    }
}

public abstract class UnitTestBase
{
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void InitializeTest()
    {
        Assert.IsTrue(TestRunLifecycle.IsInitialized, "Assembly test initialization did not run.");
    }

    [TestCleanup]
    public void CleanupTest()
    {
        // Svaki test ima vlastitu InMemory bazu. Ovdje ostaje jedno mjesto za
        // buduće čišćenje resursa bez kopiranja koda kroz testne klase.
    }
}

[TestClass]
public sealed class TestRunLifecycle
{
    internal static bool IsInitialized { get; private set; }

    [AssemblyInitialize]
    public static void InitializeAssembly(TestContext _) => IsInitialized = true;

    [AssemblyCleanup]
    public static void CleanupAssembly() => IsInitialized = false;
}
