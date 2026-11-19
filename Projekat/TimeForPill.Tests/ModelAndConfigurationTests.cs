using TimeForPill.Models;
using TimeForPill.Services;
using TimeForPill.ViewModels;

namespace TimeForPill.Tests;

[TestClass]
public class ModelAndConfigurationTests
{
    [DataTestMethod]
    [DynamicData(nameof(TestDataLoader.CsvTherapyCases), typeof(TestDataLoader), DynamicDataSourceType.Method)]
    public void Terapija_CsvScenario_RespectsDataAnnotations(string naziv, int dnevnaDoza, int ukupanBrojDoza, int intervalSati, bool expectedValid)
    {
        var therapy = new Terapija
        {
            Naziv = naziv,
            DnevnaDoza = dnevnaDoza,
            UkupanBrojDoza = ukupanBrojDoza,
            BrojDozaPoObnovi = Math.Max(1, ukupanBrojDoza),
            IntervalSati = intervalSati,
            Pocetak = new DateTime(2026, 11, 19),
            Kraj = new DateTime(2026, 11, 20),
            Status = StatusTerapije.Cekanje
        };

        Assert.AreEqual(expectedValid, ValidationTestHelper.Validate(therapy).Count == 0);
    }

    [DataTestMethod]
    [DynamicData(nameof(TestDataLoader.XmlContactCases), typeof(TestDataLoader), DynamicDataSourceType.Method)]
    public void KontaktOsoba_XmlScenario_RespectsDataAnnotations(string ime, string prezime, string email, string telefon, bool expectedValid)
    {
        var contact = new KontaktOsoba { Ime = ime, Prezime = prezime, Email = email, BrojTelefona = telefon };

        Assert.AreEqual(expectedValid, ValidationTestHelper.Validate(contact).Count == 0);
    }

    [TestMethod]
    public void Lijek_ValidValues_AreAccepted()
    {
        var medication = new Lijek { Naziv = "Paracetamol", Kategorija = "Analgetik", Slika = "/images/paracetamol.png" };

        Assert.AreEqual(0, ValidationTestHelper.Validate(medication).Count);
    }

    [TestMethod]
    public void Zahtjev_ShortContent_IsRejected()
    {
        var request = new Zahtjev { Naziv = "Obnova", Sadrzaj = "Krat", Status = StatusZahtjeva.Neobraden };

        Assert.IsTrue(ValidationTestHelper.Validate(request).Any(result => result.MemberNames.Contains(nameof(Zahtjev.Sadrzaj))));
    }

    [DataTestMethod]
    [DataRow("smtp.example.test", "", "", "smtp.example.test")]
    [DataRow("", "legacy.example.test", "", "legacy.example.test")]
    public void EmailSettings_EffectiveHost_UsesFirstConfiguredValue(string smtpServer, string host, string senderEmail, string expected)
    {
        var settings = new EmailSettings { SmtpServer = smtpServer, Host = host, SenderEmail = senderEmail };

        Assert.AreEqual(expected, settings.EffectiveHost);
    }

    [TestMethod]
    public void EmailSettings_EffectiveFrom_UsesSenderEmailWhenConfigured()
    {
        var settings = new EmailSettings { SenderEmail = "sender@example.test" };

        Assert.AreEqual("sender@example.test", settings.EffectiveFrom);
    }

    [TestMethod]
    public void EmailSettings_PublicHost_IsNormalizedToPublicUrl()
    {
        var settings = new EmailSettings { AppUrl = "https://TimeForPill.RunAsp.Net/" };

        Assert.AreEqual(EmailSettings.PublicAppUrl, settings.EffectiveAppUrl);
    }

    [TestMethod]
    public void HistoryViewModel_CalculatesPercentagesFromCompletedDoses()
    {
        var model = new HistoryViewModel { Uzeto = 3, NijeUzeto = 1 };

        Assert.AreEqual(4, model.Ukupno);
        Assert.AreEqual(75, model.ProcenatUzeto);
        Assert.AreEqual(25, model.ProcenatNijeUzeto);
    }
}
