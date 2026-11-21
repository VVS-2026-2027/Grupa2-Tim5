using System.Reflection;
using TimeForPill.Controllers;
using TimeForPill.Models;
using TimeForPill.ViewModels;

namespace TimeForPill.Tests;

[TestClass]
public class PacijentSchedulingTests : UnitTestBase
{
    [TestMethod]
    public void SchedulingHelpers_NormalizeQuietHoursAndAvoidTwoDoseCollision()
    {
        var beforeMorning = Invoke<DateTime>("MoveToAllowedDoseTime", new DateTime(2026, 1, 2, 5, 48, 0));
        var afterEvening = Invoke<DateTime>("MoveToAllowedDoseTime", new DateTime(2026, 1, 2, 22, 31, 0));
        Assert.AreEqual(new DateTime(2026, 1, 2, 6, 0, 0), beforeMorning);
        Assert.AreEqual(new DateTime(2026, 1, 2, 22, 30, 0), afterEvening);
        Assert.IsTrue(Invoke<bool>("IsAllowedDoseTime", new DateTime(2026, 1, 2, 6, 0, 0)));
        Assert.IsFalse(Invoke<bool>("IsAllowedDoseTime", new DateTime(2026, 1, 2, 23, 0, 0)));

        var occupied = new[] { new DateTime(2026, 1, 2, 8, 0, 0), new DateTime(2026, 1, 2, 8, 15, 0) };
        var schedule = Invoke<IReadOnlyList<DateTime>>("GenerateDoseSchedule", new DateTime(2026, 1, 2, 8, 1, 0), 2, 24, occupied, true);
        Assert.AreEqual(new DateTime(2026, 1, 2, 8, 30, 0), schedule[0]);
        Assert.AreEqual(new DateTime(2026, 1, 3, 8, 30, 0), schedule[1]);
    }

    [TestMethod]
    public void ExactSchedulingHelpers_RejectQuietHoursAndPostponementUpdatesReminder()
    {
        var exception = Assert.ThrowsException<TargetInvocationException>(() => Invoke<IReadOnlyList<DateTime>>("GenerateExactDoseSchedule", new DateTime(2026, 1, 2, 23, 0, 0), 1, 24, Array.Empty<DateTime>()));
        Assert.AreEqual("DoseScheduleException", exception.InnerException!.GetType().Name);
        var exact = Invoke<IReadOnlyList<DateTime>>("GenerateExactDoseSchedule", new DateTime(2026, 1, 2, 21, 15, 44), 2, 2, Array.Empty<DateTime>());
        Assert.AreEqual(new DateTime(2026, 1, 2, 21, 15, 0), exact[0]);
        Assert.AreEqual(new DateTime(2026, 1, 3, 7, 15, 0), exact[1]);

        var dose = new TerapijskaDoza { VrijemeUzimanja = new DateTime(2026, 1, 2, 22, 30, 0) };
        Invoke<object?>("ApplyPostponement", dose);
        Assert.AreEqual(new DateTime(2026, 1, 3, 6, 0, 0), dose.VrijemeUzimanja);
        Assert.AreEqual(new DateTime(2026, 1, 3, 5, 55, 0), dose.VrijemePodsjetnika);
    }

    [TestMethod]
    public void ModelCandidateAndFormattingHelpers_ProduceDeterministicValues()
    {
        var fixedModel = new MedicineFormViewModel { AutomatskiPocetak = false, Pocetak = new DateTime(2026, 2, 1, 10, 17, 31) };
        Assert.AreEqual(fixedModel.Pocetak, Invoke<DateTime>("GetFirstDoseCandidateFromModel", fixedModel));
        Assert.AreEqual(3, Invoke<int>("CalculateLegacyDailyDose", 8));
        Assert.AreEqual(1, Invoke<int>("CalculateLegacyDailyDose", 0));
        Assert.AreEqual("2h 5min", Invoke<string>("FormatRemaining", new DateTime(2026, 1, 1, 10, 0, 0), new DateTime(2026, 1, 1, 12, 5, 0)));
    }

    private static T Invoke<T>(string name, params object?[] values) => (T)typeof(PacijentController)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, values)!;
}
