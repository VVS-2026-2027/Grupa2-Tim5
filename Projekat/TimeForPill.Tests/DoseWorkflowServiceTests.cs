using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TimeForPill.Data;
using TimeForPill.Models;
using TimeForPill.Services;

namespace TimeForPill.Tests;

[TestClass]
public class DoseWorkflowServiceTests
{
    [TestMethod]
    public async Task SendDueReminderEmailsAsync_DueDoseWithEmail_SendsReminderAndMarksDose()
    {
        await using var context = CreateContext();
        var (_, _, dose) = await SeedDoseAsync(context, "patient@example.test", DateTime.Now.AddMinutes(4));
        var email = new Mock<IEmailService>(MockBehavior.Strict);
        email.Setup(service => service.SendEmailAsync("patient@example.test", "Vrijeme je za terapiju", It.Is<string>(body => body.Contains("Paracetamol")), true)).Returns(Task.CompletedTask);
        var service = CreateService(context, email);

        await service.SendDueReminderEmailsAsync();

        Assert.IsTrue(dose.EmailPodsjetnikPoslan);
        email.VerifyAll();
    }

    [TestMethod]
    public async Task SendDueReminderEmailsAsync_DueDoseWithoutEmail_MarksDoseWithoutSending()
    {
        await using var context = CreateContext();
        var (_, _, dose) = await SeedDoseAsync(context, string.Empty, DateTime.Now.AddMinutes(4));
        var email = new Mock<IEmailService>(MockBehavior.Strict);

        await CreateService(context, email).SendDueReminderEmailsAsync();

        Assert.IsTrue(dose.EmailPodsjetnikPoslan);
        email.Verify(service => service.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task SendDueReminderEmailsAsync_EmailFailure_LeavesDoseEligibleForRetry()
    {
        await using var context = CreateContext();
        var (_, _, dose) = await SeedDoseAsync(context, "patient@example.test", DateTime.Now.AddMinutes(4));
        var email = new Mock<IEmailService>();
        email.Setup(service => service.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).ThrowsAsync(new InvalidOperationException("SMTP nije dostupan"));

        await CreateService(context, email).SendDueReminderEmailsAsync();

        Assert.IsFalse(dose.EmailPodsjetnikPoslan);
        email.Verify(service => service.SendEmailAsync("patient@example.test", It.IsAny<string>(), It.IsAny<string>(), true), Times.Once);
    }

    [TestMethod]
    public async Task RefreshMissedDosesAsync_ExpiredWaitingDose_MarksDoseTherapyAndDailyStatistic()
    {
        await using var context = CreateContext();
        var (_, therapy, dose) = await SeedDoseAsync(context, "patient@example.test", DateTime.Now.AddHours(-2));

        await CreateService(context, new Mock<IEmailService>()).RefreshMissedDosesAsync();

        Assert.AreEqual(StatusDoze.Propusteno, dose.Status);
        Assert.IsNotNull(dose.VrijemeEvidentiranja);
        Assert.AreEqual(StatusTerapije.Propusteno, therapy.Status);
        Assert.AreEqual(1, (await context.PacijentDnevneStatistike.SingleAsync()).BrojPropustenih);
    }

    [TestMethod]
    public async Task RefreshMissedDosesAsync_ThreeConsecutiveMisses_NotifiesContactAndRecordsMarkers()
    {
        await using var context = CreateContext();
        var contact = new KontaktOsoba { Ime = "Kontakt", Prezime = "Osoba", Email = "contact@example.test", BrojTelefona = "061222333" };
        var patient = CreatePatient("patient-contact", "patient@example.test");
        patient.KontaktOsoba = contact;
        var medication = new Lijek { Naziv = "Paracetamol", Kategorija = "Analgetik" };
        var therapy = new Terapija { Naziv = "Terapija", Pacijent = patient, Lijek = medication, PacijentId = patient.Id, Status = StatusTerapije.Propusteno, Pocetak = DateTime.Today, Kraj = DateTime.Today, DnevnaDoza = 1, UkupanBrojDoza = 3, BrojDozaPoObnovi = 3, IntervalSati = 8 };
        context.AddRange(patient, medication, therapy);
        context.AddRange(CreateMissedDose(therapy, 1, DateTime.Today.AddHours(8)), CreateMissedDose(therapy, 2, DateTime.Today.AddHours(12)), CreateMissedDose(therapy, 3, DateTime.Today.AddHours(16)));
        await context.SaveChangesAsync();
        var email = new Mock<IEmailService>(MockBehavior.Strict);
        email.Setup(service => service.SendEmailAsync("contact@example.test", "Upozorenje o terapiji", It.Is<string>(body => body.Contains("3 uzastopnih")), false)).Returns(Task.CompletedTask);

        await CreateService(context, email).RefreshMissedDosesAsync();

        Assert.IsTrue(context.TerapijskeDoze.All(d => d.KontaktObavijestPoslana));
        email.VerifyAll();
    }

    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static DoseWorkflowService CreateService(ApplicationDbContext context, Mock<IEmailService> email) => new(context, email.Object, Options.Create(new EmailSettings { AppUrl = "https://example.test" }), NullLogger<DoseWorkflowService>.Instance);

    private static async Task<(Pacijent Patient, Terapija Therapy, TerapijskaDoza Dose)> SeedDoseAsync(ApplicationDbContext context, string email, DateTime scheduledAt)
    {
        var patient = CreatePatient("patient-1", email);
        var medication = new Lijek { Naziv = "Paracetamol", Kategorija = "Analgetik" };
        var therapy = new Terapija { Naziv = "Terapija protiv bolova", Pacijent = patient, PacijentId = patient.Id, Lijek = medication, Status = StatusTerapije.Cekanje, Pocetak = scheduledAt.Date, Kraj = scheduledAt.Date, DnevnaDoza = 1, UkupanBrojDoza = 1, BrojDozaPoObnovi = 1, IntervalSati = 24 };
        var dose = new TerapijskaDoza { Terapija = therapy, RedniBroj = 1, VrijemeUzimanja = scheduledAt, OriginalnoVrijemeUzimanja = scheduledAt, VrijemePodsjetnika = scheduledAt.AddMinutes(-5), Status = StatusDoze.Cekanje };
        context.AddRange(patient, medication, therapy, dose);
        await context.SaveChangesAsync();
        return (patient, therapy, dose);
    }

    private static Pacijent CreatePatient(string id, string email) => new() { Id = id, UserName = email, Email = email, Ime = "Amina", Prezime = "Kovac", DatumRodjenja = new DateTime(1990, 1, 1), Spol = Spol.Zensko, KontaktOsoba = null! };

    private static TerapijskaDoza CreateMissedDose(Terapija therapy, int sequence, DateTime at) => new() { Terapija = therapy, RedniBroj = sequence, VrijemeUzimanja = at, OriginalnoVrijemeUzimanja = at, VrijemePodsjetnika = at.AddMinutes(-5), VrijemeEvidentiranja = at.AddHours(1), Status = StatusDoze.Propusteno };
}
