using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using TimeForPill.Data;
using TimeForPill.Models;
using TimeForPill.Services;
using Moq;

namespace TimeForPill.Tests;

[TestClass]
public class StartupBackfillTests : UnitTestBase
{
    [TestMethod]
    public async Task BackfillMissingDoses_CreatesScheduledDosesAndNormalizesLegacyValues()
    {
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("startup-missing-doses");
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

        TestInfrastructure.InvokeProgramHelper("BackfillMissingDoses", context);

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
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("startup-statistics");
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
        var taken = TestInfrastructure.CreateDose(therapy, new DateTime(2026, 1, 2, 8, 0, 0));
        taken.Status = StatusDoze.Uzeto;
        var missed = TestInfrastructure.CreateDose(therapy, new DateTime(2026, 1, 2, 12, 0, 0));
        missed.Status = StatusDoze.Propusteno;
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

        TestInfrastructure.InvokeProgramHelper("BackfillDailyStatistics", context);

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

        await TestInfrastructure.InvokeSeedAdministratorAsync(provider);

        Assert.IsInstanceOfType(created, typeof(Administrator));
        Assert.IsTrue(created!.EmailConfirmed);

        manager.Setup(userManager => userManager.FindByEmailAsync("ebosnjakov2@etf.unsa.ba"))
            .ReturnsAsync(new Pacijent { Id = "other-user" });
        await TestInfrastructure.InvokeSeedAdministratorAsync(provider);

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
        await TestInfrastructure.InvokeSeedAdministratorAsync(provider);
        Assert.AreEqual("Eldar", existing.Ime);
        Assert.AreEqual("Bosnjakovic", existing.Prezime);
        Assert.AreEqual(DateTime.Today, existing.datumImenovanja);
    }

}
