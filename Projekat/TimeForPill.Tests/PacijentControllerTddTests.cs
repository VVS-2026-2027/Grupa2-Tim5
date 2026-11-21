using Microsoft.EntityFrameworkCore;

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
        var scenario = await TestInfrastructure.CreateRenewalScenarioAsync();
        await using var context = scenario.Context;
        await scenario.Controller.ObnovaTerapije(scenario.Model);
        await scenario.Controller.ObnovaTerapije(scenario.Model);
        Assert.AreEqual(1, await context.Zahtjevi.CountAsync());
    }

    [DataTestMethod]
    [DataRow(22, 30, 6, 0)]
    [DataRow(23, 15, 6, 0)]
    [TestCategory("TDD-F2")]
    public async Task Odgodi_EveningDose_MovesDoseToNextAllowedMorning(
        int hour, int minute, int expectedHour, int expectedMinute)
    {
        var scenario = await TestInfrastructure.CreateEveningPostponementScenarioAsync(hour, minute);
        await using var context = scenario.Context;
        await scenario.Controller.Odgodi(scenario.Dose.Id);
        Assert.AreEqual(DateTime.Today.AddDays(1).AddHours(expectedHour).AddMinutes(expectedMinute), scenario.Dose.VrijemeUzimanja);
    }
}
