using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TimeForPill.Data;
using TimeForPill.Models;
using TimeForPill.Services;
using TimeForPill.ViewModels;
using Moq;

namespace TimeForPill.Tests;

[TestClass]
public class StartupBackfillTests
{
    [TestMethod]
    public async Task BackfillMissingDoses_CreatesScheduledDosesAndNormalizesLegacyValues()
    {
        await using var context = CreateContext();
        var patient = NewPatient();
        var therapy = new Terapija
        {
            Naziv = "Stara terapija",
            Pacijent = patient,
            PacijentId = patient.Id,
            Pocetak = new DateTime(2026, 1, 2, 8, 0, 0),
            DnevnaDoza = 1,
            UkupanBrojDoza = 0,
            BrojDozaPoObnovi = 0,
            IntervalSati = 0,
            Status = StatusTerapije.Cekanje
        };
        var existing = new Terapija
        {
            Naziv = "Već raspoređena",
            Pacijent = patient,
            PacijentId = patient.Id,
            Pocetak = DateTime.Today,
            UkupanBrojDoza = 2,
            BrojDozaPoObnovi = 2,
            IntervalSati = 8,
            Status = StatusTerapije.Cekanje
        };
        context.AddRange(patient, therapy, existing);
        context.TerapijskeDoze.Add(new TerapijskaDoza
        {
            Terapija = existing,
            RedniBroj = 1,
            VrijemeUzimanja = DateTime.Today,
            Status = StatusDoze.Cekanje
        });
        await context.SaveChangesAsync();

        InvokeStartupHelper("BackfillMissingDoses", context);

        var generated = await context.TerapijskeDoze
            .Where(d => d.TerapijaId == therapy.Id)
            .SingleAsync();
        Assert.AreEqual(1, therapy.UkupanBrojDoza);
        Assert.AreEqual(1, therapy.BrojDozaPoObnovi);
        Assert.AreEqual(24, therapy.IntervalSati);
        Assert.AreEqual(new DateTime(2026, 1, 2, 8, 0, 0), generated.VrijemeUzimanja);
        Assert.AreEqual(generated.VrijemeUzimanja.AddMinutes(-5), generated.VrijemePodsjetnika);
        Assert.AreEqual(1, await context.TerapijskeDoze.CountAsync(d => d.TerapijaId == existing.Id));
    }

    [TestMethod]
    public async Task BackfillDailyStatistics_GroupsByOriginalDoseDateAndKeepsHigherCounts()
    {
        await using var context = CreateContext();
        var patient = NewPatient();
        var therapy = new Terapija
        {
            Naziv = "Terapija",
            Pacijent = patient,
            PacijentId = patient.Id,
            Pocetak = DateTime.Today,
            Kraj = DateTime.Today,
            UkupanBrojDoza = 3,
            BrojDozaPoObnovi = 3,
            IntervalSati = 8,
            Status = StatusTerapije.Cekanje
        };
        var taken = Dose(therapy, new DateTime(2026, 1, 2, 8, 0, 0), StatusDoze.Uzeto);
        var missed = Dose(therapy, new DateTime(2026, 1, 2, 12, 0, 0), StatusDoze.Propusteno);
        missed.OriginalnoVrijemeUzimanja = new DateTime(2026, 1, 2, 12, 0, 0);
        missed.VrijemeUzimanja = new DateTime(2026, 1, 3, 6, 0, 0);
        context.AddRange(patient, therapy, taken, missed);
        context.PacijentDnevneStatistike.Add(new PacijentDnevnaStatistika
        {
            PacijentId = patient.Id,
            Datum = new DateTime(2026, 1, 2),
            BrojUzetih = 5,
            BrojPropustenih = 0
        });
        await context.SaveChangesAsync();

        InvokeStartupHelper("BackfillDailyStatistics", context);

        var statistic = await context.PacijentDnevneStatistike.SingleAsync();
        Assert.AreEqual(5, statistic.BrojUzetih);
        Assert.AreEqual(1, statistic.BrojPropustenih);
    }

    [TestMethod]
    public async Task SeedDefaultAdministrator_CreatesUpdatesAndSkipsAnotherAccountType()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var manager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        ApplicationUser? created = null;
        manager.Setup(userManager => userManager.FindByEmailAsync("ebosnjakov2@etf.unsa.ba"))
            .ReturnsAsync((ApplicationUser?)null);
        manager.Setup(userManager => userManager.CreateAsync(It.IsAny<ApplicationUser>(), "Admin123"))
            .Callback<ApplicationUser, string>((user, _) => created = user)
            .ReturnsAsync(IdentityResult.Success);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(manager.Object);
        using var provider = services.BuildServiceProvider();

        await InvokeSeedAdministrator(provider);

        Assert.IsInstanceOfType(created, typeof(Administrator));
        Assert.IsTrue(created!.EmailConfirmed);

        manager.Setup(userManager => userManager.FindByEmailAsync("ebosnjakov2@etf.unsa.ba"))
            .ReturnsAsync(new Pacijent { Id = "other-user" });
        await InvokeSeedAdministrator(provider);

        var existing = new Administrator
        {
            Id = "existing-admin", Ime = "Old", Prezime = "Name",
            Email = "ebosnjakov2@etf.unsa.ba", UserName = "old-name"
        };
        manager.Setup(userManager => userManager.FindByEmailAsync("ebosnjakov2@etf.unsa.ba"))
            .ReturnsAsync(existing);
        manager.Setup(userManager => userManager.UpdateAsync(existing))
            .ReturnsAsync(IdentityResult.Success);
        manager.Setup(userManager => userManager.RemovePasswordAsync(existing))
            .ReturnsAsync(IdentityResult.Success);
        manager.Setup(userManager => userManager.AddPasswordAsync(existing, "Admin123"))
            .ReturnsAsync(IdentityResult.Success);
        await InvokeSeedAdministrator(provider);
        Assert.AreEqual("Eldar", existing.Ime);
        Assert.AreEqual("Bosnjakovic", existing.Prezime);
        Assert.AreEqual(DateTime.Today, existing.datumImenovanja);
    }

    [TestMethod]
    public async Task BuildApplication_RegistersServicesAndMapsDefaultRouteWithoutConnectingToDatabase()
    {
        var program = typeof(ApplicationDbContext).Assembly.GetType("Program");
        Assert.IsNotNull(program);
        var build = program.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name.Contains("BuildApplication", StringComparison.Ordinal));
        const string connection = "Server=localhost;Database=TimeForPillCoverage;User Id=test;Password=test;TrustServerCertificate=True";
        var app = (WebApplication)build.Invoke(null, [new[]
        {
            "--environment=Development",
            "--applicationName=TimeForPill",
            $"--ConnectionStrings:DefaultConnection={connection}"
        }])!;

        try
        {
            using var scope = app.Services.CreateScope();
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IDoseWorkflowService>());
            var endpoints = ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .ToList();
            Assert.IsTrue(endpoints.Any(endpoint =>
                endpoint.RoutePattern.RawText == "{controller=Account}/{action=Login}/{id?}"));
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CompiledViews_RenderMainPatientAdminAndDoctorScreens()
    {
        var program = typeof(ApplicationDbContext).Assembly.GetType("Program")!;
        var build = program.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name.Contains("BuildApplication", StringComparison.Ordinal));
        var app = (WebApplication)build.Invoke(null, [new[]
        {
            "--environment=Development",
            "--applicationName=TimeForPill",
            "--ConnectionStrings:DefaultConnection=Server=localhost;Database=TimeForPillCoverage;User Id=test;Password=test;TrustServerCertificate=True"
        }])!;

        try
        {
            await RenderViewAsync(app, "/Views/Account/Login.cshtml", new LoginViewModel());
            await RenderViewAsync(app, "/Views/Account/Register.cshtml", new RegisterViewModel());
            await RenderViewAsync(app, "/Views/Pacijent/Home.cshtml", new PatientDashboardViewModel
            {
                Ime = "Amina",
                TrenutnaDoza = new DosePopupViewModel { NazivLijeka = "Aspirin", VrijemeUzimanja = "08:00" },
                DanasnjeTerapije = [new MedicineListItemViewModel { Naziv = "Aspirin", UkupanBrojDoza = 2, IntervalSati = 8 }]
            }, isMainPage: true);
            await RenderViewAsync(app, "/Views/Pacijent/MojiLijekovi.cshtml", Array.Empty<MedicineListItemViewModel>());
            await RenderViewAsync(app, "/Views/Pacijent/Historija.cshtml", new HistoryViewModel());
            await RenderViewAsync(app, "/Views/Pacijent/LijekForma.cshtml", new MedicineFormViewModel());
            await RenderViewAsync(app, "/Views/Pacijent/ObnovaTerapije.cshtml", new RenewTherapyViewModel());
            await RenderViewAsync(app, "/Views/Pacijent/Profil.cshtml", new ProfileViewModel());
            await RenderViewAsync(app, "/Views/Pacijent/PrijaviNuspojavu.cshtml", new SideEffectFormViewModel());
            await RenderViewAsync(app, "/Views/Pacijent/Nuspojave.cshtml", Array.Empty<SideEffectListItemViewModel>());
            await RenderViewAsync(app, "/Views/Pacijent/Raspored.cshtml", Array.Empty<ScheduleDayViewModel>());
            await RenderViewAsync(app, "/Views/Admin/Home.cshtml", new AdminDashboardViewModel
            {
                ZadnjiZahtjevZaPotvrdu = new AccountApprovalRequestViewModel
                {
                    Id = "doctor", ImePrezime = "Novi doktor", Email = "doctor@example.test", Uloga = "Ljekar"
                },
                ZadnjeAkcije = ["Potvrdjen nalog"]
            });
            await RenderViewAsync(app, "/Views/Admin/PregledLjekara.cshtml", Array.Empty<Ljekar>());
            await RenderViewAsync(app, "/Views/Admin/PregledPacijenata.cshtml", Array.Empty<Pacijent>());
            await RenderViewAsync(app, "/Views/Admin/Profil.cshtml", new ProfileViewModel());
            await RenderViewAsync(app, "/Views/Admin/ZahtjeviNaloga.cshtml", Array.Empty<AccountApprovalRequestViewModel>());
            await RenderViewAsync(app, "/Views/Ljekar/Home.cshtml", new DoctorDashboardViewModel
            {
                ZadnjaCetiriPotvrdjena = [new RequestListItemViewModel { Naziv = "Obnova", Pacijent = "Amina", Lijek = "Aspirin" }]
            });
            await RenderViewAsync(app, "/Views/Ljekar/Zahtjevi.cshtml", Array.Empty<RequestListItemViewModel>());
            await RenderViewAsync(app, "/Views/Ljekar/Profil.cshtml", new ProfileViewModel());
            await RenderViewAsync(app, "/Views/Ljekar/PacijentNuspojave.cshtml", new DoctorPatientSideEffectsViewModel());
            await RenderViewAsync(app, "/Views/Administrators/Index.cshtml", new[]
            {
                new Administrator { Id = "admin", Ime = "Eldar", Prezime = "Admin", datumImenovanja = DateTime.Today }
            });
            await RenderViewAsync(app, "/Views/Lijeks/Index.cshtml", new[]
            {
                new Lijek { Id = 1, Naziv = "Aspirin", Kategorija = "Analgetik" }
            });
            await RenderViewAsync(app, "/Views/Notifikacijas/Index.cshtml", Array.Empty<Notifikacija>());
            await RenderViewAsync(app, "/Views/Terapijas/Index.cshtml", Array.Empty<Terapija>());
            await RenderViewAsync(app, "/Views/Zahtjevs/Index.cshtml", Array.Empty<Zahtjev>());
            await RenderViewAsync(app, "/Views/Ljekars/Index.cshtml", Array.Empty<Ljekar>());
            await RenderViewAsync(app, "/Views/Pacijents/Index.cshtml", Array.Empty<Pacijent>());
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static async Task RenderViewAsync(
        WebApplication app,
        string path,
        object model,
        bool isMainPage = false)
    {
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var routeData = new RouteData();
        routeData.Values["controller"] = segments[^2];
        routeData.Values["action"] = Path.GetFileNameWithoutExtension(path);
        routeData.Routers.Add(new ViewTestRouter());
        var actionContext = new ActionContext(http, routeData, new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var engine = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();
        var found = engine.FindView(actionContext, Path.GetFileNameWithoutExtension(path), isMainPage);
        Assert.IsTrue(found.Success, $"Compiled view was not found: {path}");
        var metadata = scope.ServiceProvider.GetRequiredService<IModelMetadataProvider>();
        var viewData = new ViewDataDictionary(metadata, new ModelStateDictionary()) { Model = model };
        var tempData = new TempDataDictionary(http, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext,
            found.View!,
            viewData,
            tempData,
            writer,
            new HtmlHelperOptions());

        await found.View!.RenderAsync(viewContext);
        Assert.IsTrue(writer.ToString().Length > 0, $"View rendered no content: {path}");
    }

    private sealed class ViewTestRouter : IRouter
    {
        public Task RouteAsync(RouteContext context) => Task.CompletedTask;

        public VirtualPathData? GetVirtualPath(VirtualPathContext context) =>
            new(this, "/test-route");
    }

    private static void InvokeStartupHelper(string name, ApplicationDbContext context)
    {
        var program = typeof(ApplicationDbContext).Assembly.GetType("Program");
        Assert.IsNotNull(program, "Top-level Program type was not found.");
        var helper = program.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name.Contains(name, StringComparison.Ordinal));
        helper.Invoke(null, [context]);
    }

    private static async Task InvokeSeedAdministrator(IServiceProvider services)
    {
        var program = typeof(ApplicationDbContext).Assembly.GetType("Program");
        Assert.IsNotNull(program);
        var helper = program.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name.Contains("SeedDefaultAdministratorAsync", StringComparison.Ordinal));
        var task = helper.Invoke(null, [services]) as Task;
        Assert.IsNotNull(task);
        await task;
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Pacijent NewPatient() => new()
    {
        Id = "startup-patient",
        UserName = "startup@example.test",
        Email = "startup@example.test",
        Ime = "Test",
        Prezime = "Pacijent",
        DatumRodjenja = new DateTime(1990, 1, 1),
        Spol = Spol.Zensko
    };

    private static TerapijskaDoza Dose(Terapija therapy, DateTime time, StatusDoze status) => new()
    {
        Terapija = therapy,
        RedniBroj = 1,
        VrijemeUzimanja = time,
        OriginalnoVrijemeUzimanja = time,
        VrijemePodsjetnika = time.AddMinutes(-5),
        Status = status
    };
}
