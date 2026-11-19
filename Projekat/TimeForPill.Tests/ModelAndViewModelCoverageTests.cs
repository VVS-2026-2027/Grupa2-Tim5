using TimeForPill.Models;
using TimeForPill.ViewModels;

namespace TimeForPill.Tests;

[TestClass]
public class ModelAndViewModelCoverageTests
{
    [TestMethod]
    public void DomainModels_ValidInstances_SatisfyValidationAndDefaults()
    {
        var adminAction = new AdminAkcija
        {
            AdministratorId = "admin-1",
            AdministratorNaziv = "Administrator",
            VrstaAkcije = "Izmjena",
            TipRacuna = "Pacijent",
            RacunId = "patient-1",
            RacunNaziv = "Amina Kovac",
            RacunEmail = "amina@example.test"
        };
        var ticket = new Ticket { Naslov = "Podsjetnik", Opis = "Podsjetnik nije stigao." };
        var notification = new Notifikacija { Naziv = "Podsjetnik", Poruka = "Vrijeme je za terapiju." };
        var sideEffect = new Nuspojava
        {
            PacijentId = "patient-1",
            NazivLijeka = "Paracetamol",
            Kategorija = "Analgetik",
            Opis = "Blaga glavobolja"
        };
        var statistic = new PacijentDnevnaStatistika
        {
            PacijentId = "patient-1",
            Datum = DateTime.Today,
            BrojUzetih = 2,
            BrojPropustenih = 1
        };
        var administrator = new Administrator
        {
            Ime = "Admin",
            Prezime = "Korisnik",
            DatumRodjenja = new DateTime(1980, 1, 1),
            Spol = Spol.Musko,
            datumImenovanja = DateTime.Today
        };
        var doctor = new Ljekar
        {
            Ime = "Doktor",
            Prezime = "Korisnik",
            DatumRodjenja = new DateTime(1980, 1, 1),
            Spol = Spol.Zensko,
            Specijalizacija = Specijalizacija.Kardiolog
        };

        Assert.AreEqual(0, ValidationTestHelper.Validate(adminAction).Count);
        Assert.AreEqual(0, ValidationTestHelper.Validate(ticket).Count);
        Assert.AreEqual(0, ValidationTestHelper.Validate(notification).Count);
        Assert.AreEqual(0, ValidationTestHelper.Validate(sideEffect).Count);
        Assert.AreEqual(0, ValidationTestHelper.Validate(statistic).Count);
        Assert.AreEqual(0, ValidationTestHelper.Validate(administrator).Count);
        Assert.AreEqual(0, ValidationTestHelper.Validate(doctor).Count);
        Assert.AreEqual(StatusTicketa.Otvoren, ticket.Status);
        Assert.AreEqual(PrioritetTicketa.Srednji, ticket.Prioritet);
    }

    [TestMethod]
    public void ErrorViewModel_ReportsRequestIdOnlyWhenPresent()
    {
        var empty = new ErrorViewModel();
        var populated = new ErrorViewModel { RequestId = "request-123" };

        Assert.IsFalse(empty.ShowRequestId);
        Assert.IsTrue(populated.ShowRequestId);
    }

    [TestMethod]
    public void AccountViewModels_ValidateCredentialsAndMatchingPassword()
    {
        var invalidLogin = new LoginViewModel { Email = "nije-email", Password = string.Empty };
        var registration = new RegisterViewModel
        {
            Ime = "Amina",
            Prezime = "Kovac",
            DatumRodjenja = new DateTime(1995, 3, 3),
            Spol = Spol.Zensko,
            Email = "amina@example.test",
            Password = "Lozinka1",
            ConfirmPassword = "Lozinka1"
        };

        Assert.IsTrue(ValidationTestHelper.Validate(invalidLogin).Count > 0);
        Assert.AreEqual(0, ValidationTestHelper.Validate(registration).Count);
        registration.ConfirmPassword = "DrugaLozinka1";
        Assert.IsTrue(ValidationTestHelper.Validate(registration).Any(result => result.MemberNames.Contains(nameof(RegisterViewModel.ConfirmPassword))));
    }

    [TestMethod]
    public void TherapyFormViewModels_EnforceRequiredMedicineAndRanges()
    {
        var invalid = new MedicineFormViewModel { LijekId = null, UkupanBrojDoza = 0, IntervalSati = 0 };
        var valid = new MedicineFormViewModel
        {
            LijekId = 4,
            UkupanBrojDoza = 10,
            IntervalSati = 8,
            Pocetak = DateTime.Today.AddHours(8)
        };
        var renewal = new RenewTherapyViewModel { TerapijaId = 4, PreostaloDoza = 0, Napomena = "Potrebna obnova." };

        Assert.IsTrue(ValidationTestHelper.Validate(invalid).Count > 0);
        Assert.AreEqual(0, ValidationTestHelper.Validate(valid).Count);
        Assert.AreEqual(0, ValidationTestHelper.Validate(renewal).Count);
    }

    [TestMethod]
    public void DashboardAndDoctorViewModels_ExposeSafeDefaults()
    {
        var dashboard = new PatientDashboardViewModel();
        var popup = new DosePopupViewModel();
        var medicineItem = new MedicineListItemViewModel();
        var catalog = new MedicineCatalogOptionViewModel();
        var sideEffectForm = new SideEffectFormViewModel();
        var sideEffectItem = new SideEffectListItemViewModel();
        var therapyOption = new TherapyOptionViewModel();
        var profile = new ProfileViewModel();
        var day = new ScheduleDayViewModel();
        var scheduleItem = new ScheduleItemViewModel();
        var doctorDashboard = new DoctorDashboardViewModel();
        var request = new RequestListItemViewModel();
        var requestDetails = new RequestDetailsViewModel();
        var adminDashboard = new AdminDashboardViewModel();
        var approval = new AccountApprovalRequestViewModel();
        var patientItem = new DoctorPatientListItemViewModel();
        var medicationView = new DoctorPatientMedicationViewModel();
        var doctorMedication = new DoctorMedicationItemViewModel();
        var patientSideEffects = new DoctorPatientSideEffectsViewModel();

        Assert.AreEqual("Nema aktivne terapije", dashboard.SljedeciLijekNaziv);
        Assert.AreEqual("-", dashboard.SljedeciLijekVrijeme);
        Assert.AreEqual(0, dashboard.DanasnjeTerapije.Count);
        Assert.AreEqual(string.Empty, popup.NazivLijeka);
        Assert.AreEqual("-", medicineItem.SljedecaDoza);
        Assert.AreEqual(string.Empty, catalog.Naziv);
        Assert.AreEqual(string.Empty, sideEffectForm.NazivLijeka);
        Assert.AreEqual(string.Empty, sideEffectItem.Opis);
        Assert.AreEqual(string.Empty, therapyOption.Naziv);
        Assert.AreEqual(string.Empty, profile.Ime);
        Assert.AreEqual(string.Empty, day.NazivDana);
        Assert.AreEqual(string.Empty, scheduleItem.NazivLijeka);
        Assert.AreEqual(0, doctorDashboard.ZadnjaCetiriPotvrdjena.Count);
        Assert.AreEqual(string.Empty, request.Naziv);
        Assert.AreEqual(string.Empty, requestDetails.Sadrzaj);
        Assert.AreEqual(0, adminDashboard.ZadnjeAkcije.Count);
        Assert.AreEqual("Neobraden", approval.Status);
        Assert.AreEqual(string.Empty, patientItem.Email);
        Assert.AreEqual(0, medicationView.Lijekovi.Count);
        Assert.AreEqual("-", doctorMedication.SljedecaDoza);
        Assert.AreEqual(0, patientSideEffects.Nuspojave.Count);
    }
}
