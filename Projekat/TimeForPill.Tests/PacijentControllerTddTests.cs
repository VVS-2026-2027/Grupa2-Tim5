using Microsoft.EntityFrameworkCore;
using TimeForPill.Models;
using TimeForPill.ViewModels;

namespace TimeForPill.Tests;

/// <summary>
/// Testovi za dvije funkcionalnosti implementirane TDD postupkom. Svaki test
/// provjerava jednu poslovnu posljedicu (RED-GREEN-REFACTOR historija je u Gitu).
/// </summary>
[TestClass]
public class PacijentControllerTddTests : UnitTestBase
{
    [TestMethod]
    [TestCategory("TDD-F1")]
    public async Task ObnovaTerapije_PendingRequestAlreadyExists_DoesNotCreateAnotherRequest()
    {
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("tdd-renewal");
        var medicine = new Lijek { Naziv = "Terapija protiv bolova", Kategorija = "Bol" };
        var therapy = TestInfrastructure.CreateTherapy(patient, medicine);
        var dose = TestInfrastructure.CreateDose(therapy, DateTime.Now.AddHours(1));
        context.AddRange(patient, medicine, therapy, dose);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, patient);
        var model = new RenewTherapyViewModel { TerapijaId = therapy.Id, PreostaloDoza = 1 };

        await controller.ObnovaTerapije(model);
        await controller.ObnovaTerapije(model);

        Assert.AreEqual(1, await context.Zahtjevi.CountAsync());
    }

    [DataTestMethod]
    [DataRow(22, 30, 6, 0)]
    [DataRow(23, 15, 6, 0)]
    [TestCategory("TDD-F2")]
    public async Task Odgodi_EveningDose_MovesDoseToNextAllowedMorning(
        int hour, int minute, int expectedHour, int expectedMinute)
    {
        await using var context = TestInfrastructure.CreateContext();
        var patient = TestInfrastructure.CreatePatient("tdd-postpone");
        var medicine = new Lijek { Naziv = "Vecernja terapija", Kategorija = "Test" };
        var therapy = TestInfrastructure.CreateTherapy(patient, medicine);
        var scheduledAt = DateTime.Today.AddHours(hour).AddMinutes(minute);
        var dose = TestInfrastructure.CreateDose(therapy, scheduledAt);
        context.AddRange(patient, medicine, therapy, dose);
        await context.SaveChangesAsync();
        var controller = TestInfrastructure.CreatePatientController(context, patient);

        await controller.Odgodi(dose.Id);

        Assert.AreEqual(
            DateTime.Today.AddDays(1).AddHours(expectedHour).AddMinutes(expectedMinute),
            dose.VrijemeUzimanja);
    }
}
