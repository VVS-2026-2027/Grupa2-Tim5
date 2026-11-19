using System.Security.Claims;
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
public class AccountControllerTests
{
    [TestMethod]
    public async Task LoginGet_ReturnsFormForAnonymousAndRoleHomeForAuthenticatedUser()
    {
        await using var context = CreateContext();
        var (users, signIn) = CreateIdentity();
        var anonymous = CreateController(context, users.Object, signIn.Object, authenticated: false);
        var form = await anonymous.Login("/return") as ViewResult;
        Assert.AreEqual("/return", ((LoginViewModel)form!.Model!).ReturnUrl);

        var doctor = new Ljekar { Id = "doctor", Ime = "Dino", Prezime = "Doktor" };
        users.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(doctor);
        var authenticated = CreateController(context, users.Object, signIn.Object, authenticated: true);
        var result = await authenticated.Login() as RedirectToActionResult;
        Assert.AreEqual("Ljekar", result!.ControllerName);
        Assert.AreEqual("Home", result.ActionName);
    }

    [TestMethod]
    public async Task LoginPost_RejectsUnknownWrongPasswordRoleAndUnconfirmedAccount()
    {
        await using var context = CreateContext();
        var (users, signIn) = CreateIdentity();
        var controller = CreateController(context, users.Object, signIn.Object);
        var model = LoginModel();
        users.Setup(m => m.FindByEmailAsync(model.Email)).ReturnsAsync((ApplicationUser?)null);
        Assert.IsInstanceOfType(await controller.Login(model), typeof(ViewResult));
        Assert.IsTrue(controller.ModelState[string.Empty]!.Errors.Any());

        var doctor = new Ljekar { Email = model.Email, EmailConfirmed = true };
        users.Setup(m => m.FindByEmailAsync(model.Email)).ReturnsAsync(doctor);
        var wrongRole = LoginModel();
        wrongRole.Uloga = KorisnickaUloga.Pacijent;
        Assert.IsInstanceOfType(await CreateController(context, users.Object, signIn.Object).Login(wrongRole), typeof(ViewResult));

        users.Setup(m => m.CheckPasswordAsync(doctor, model.Password)).ReturnsAsync(false);
        Assert.IsInstanceOfType(await CreateController(context, users.Object, signIn.Object).Login(model), typeof(ViewResult));

        var waiting = new Ljekar { Email = model.Email, EmailConfirmed = false };
        users.Setup(m => m.FindByEmailAsync(model.Email)).ReturnsAsync(waiting);
        users.Setup(m => m.CheckPasswordAsync(waiting, model.Password)).ReturnsAsync(true);
        var pendingController = CreateController(context, users.Object, signIn.Object);
        Assert.IsInstanceOfType(await pendingController.Login(model), typeof(ViewResult));
        Assert.IsTrue(pendingController.ModelState[string.Empty]!.Errors.Any(e => e.ErrorMessage.Contains("ceka", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task LoginPost_SignsInConfirmedUserAndRejectsExternalReturnUrl()
    {
        await using var context = CreateContext();
        var (users, signIn) = CreateIdentity();
        var admin = new Administrator { Id = "admin", Email = "admin@test", EmailConfirmed = true };
        users.Setup(m => m.FindByEmailAsync(admin.Email!)).ReturnsAsync(admin);
        users.Setup(m => m.CheckPasswordAsync(admin, "Password1!")).ReturnsAsync(true);
        signIn.Setup(m => m.SignInAsync(admin, true, null)).Returns(Task.CompletedTask);
        var model = new LoginViewModel { Email = admin.Email!, Password = "Password1!", Uloga = KorisnickaUloga.Administrator, RememberMe = true, ReturnUrl = "https://unsafe.test" };

        var result = await CreateController(context, users.Object, signIn.Object).Login(model) as RedirectToActionResult;

        Assert.AreEqual("Admin", result!.ControllerName);
        Assert.AreEqual("Home", result.ActionName);
    }

    [TestMethod]
    public async Task Register_ValidatesPublicRoleAndContactRulesBeforeCreatingUser()
    {
        await using var context = CreateContext();
        var (users, signIn) = CreateIdentity();
        var noContact = RegisterModel(KorisnickaUloga.Pacijent);
        noContact.KontaktIme = "";
        noContact.KontaktPrezime = "";
        noContact.KontaktEmail = noContact.Email;
        noContact.KontaktTelefon = "";
        var controller = CreateController(context, users.Object, signIn.Object);
        Assert.IsInstanceOfType(await controller.Register(noContact), typeof(ViewResult));
        Assert.IsTrue(controller.ModelState.ErrorCount >= 4);

        var admin = RegisterModel(KorisnickaUloga.Administrator);
        Assert.IsInstanceOfType(await CreateController(context, users.Object, signIn.Object).Register(admin), typeof(ViewResult));

        var doctorWithoutSpecialty = RegisterModel(KorisnickaUloga.Ljekar);
        doctorWithoutSpecialty.Specijalizacija = null;
        Assert.IsInstanceOfType(await CreateController(context, users.Object, signIn.Object).Register(doctorWithoutSpecialty), typeof(ViewResult));
    }

    [TestMethod]
    public async Task Register_CreatesDoctorAndNotifiesConfirmedAdministrators()
    {
        await using var context = CreateContext();
        context.Administratori.AddRange(
            new Administrator { Id = "a1", Email = "admin@test", EmailConfirmed = true, Ime = "A", Prezime = "A" },
            new Administrator { Id = "a2", Email = "ADMIN@test", EmailConfirmed = true, Ime = "B", Prezime = "B" });
        await context.SaveChangesAsync();
        var (users, signIn) = CreateIdentity();
        ApplicationUser? created = null;
        users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).Callback<ApplicationUser, string>((u, _) => created = u).ReturnsAsync(IdentityResult.Success);
        var email = new Mock<IEmailService>();
        email.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true)).Returns(Task.CompletedTask);
        var model = RegisterModel(KorisnickaUloga.Ljekar);
        model.Specijalizacija = Specijalizacija.Kardiolog;

        var result = await CreateController(context, users.Object, signIn.Object, email.Object).Register(model);

        Assert.IsInstanceOfType(result, typeof(RedirectToActionResult));
        Assert.IsInstanceOfType(created, typeof(Ljekar));
        Assert.AreEqual(model.Email, created!.UserName);
        email.Verify(s => s.SendEmailAsync("admin@test", It.IsAny<string>(), It.IsAny<string>(), true), Times.Once);
    }

    [TestMethod]
    public async Task RegisterPatient_AssignsLeastRecentlyServedDoctorAndSendsConfirmation()
    {
        await using var context = CreateContext();
        var olderDoctor = new Ljekar { Id = "doctor-old", Ime = "A", Prezime = "Doctor", Email = "old-doctor@test" };
        var newerDoctor = new Ljekar { Id = "doctor-new", Ime = "B", Prezime = "Doctor", Email = "new-doctor@test" };
        var assignedPatient = new Pacijent
        {
            Id = "assigned", Ime = "Existing", Prezime = "Patient", Ljekar = olderDoctor,
            LjekarId = olderDoctor.Id, DatumDodjeleLjekara = DateTime.Today
        };
        context.AddRange(olderDoctor, newerDoctor, assignedPatient);
        await context.SaveChangesAsync();
        var (users, signIn) = CreateIdentity();
        Pacijent? created = null;
        users.Setup(manager => manager.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .Callback<ApplicationUser, string>((user, _) => created = user as Pacijent)
            .ReturnsAsync(IdentityResult.Success);
        users.Setup(manager => manager.GenerateEmailConfirmationTokenAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync("confirmation-token");
        var email = new Mock<IEmailService>();
        email.Setup(service => service.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true))
            .Returns(Task.CompletedTask);

        var result = await CreateController(context, users.Object, signIn.Object, email.Object)
            .Register(RegisterModel(KorisnickaUloga.Pacijent));

        Assert.IsInstanceOfType(result, typeof(RedirectToActionResult));
        Assert.IsNotNull(created);
        Assert.AreEqual(newerDoctor.Id, created.LjekarId);
        Assert.IsNotNull(created.KontaktOsoba);
        email.Verify(service => service.SendEmailAsync("new@test", "TimeForPill potvrda email adrese", It.IsAny<string>(), true), Times.Once);
    }

    [TestMethod]
    public async Task RegisterPatient_EmailFailureKeepsAccountAndReportsDeliveryProblem()
    {
        await using var context = CreateContext();
        var (users, signIn) = CreateIdentity();
        users.Setup(manager => manager.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        users.Setup(manager => manager.GenerateEmailConfirmationTokenAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync("confirmation-token");
        var email = new Mock<IEmailService>();
        email.Setup(service => service.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true))
            .ThrowsAsync(new InvalidOperationException("smtp unavailable"));
        var controller = CreateController(context, users.Object, signIn.Object, email.Object);

        Assert.IsInstanceOfType(await controller.Register(RegisterModel(KorisnickaUloga.Pacijent)), typeof(RedirectToActionResult));

        Assert.AreEqual("Nalog je kreiran, ali email za potvrdu nije poslan. Provjerite SMTP postavke.", controller.TempData["Error"]);
    }

    [TestMethod]
    public async Task ConfirmEmail_HandlesInvalidLinksUsersRolesAndIdentityResult()
    {
        await using var context = CreateContext();
        var (users, signIn) = CreateIdentity();
        var controller = CreateController(context, users.Object, signIn.Object);
        Assert.IsInstanceOfType(await controller.ConfirmEmail(null, null), typeof(RedirectToActionResult));
        users.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((ApplicationUser?)null);
        Assert.IsInstanceOfType(await CreateController(context, users.Object, signIn.Object).ConfirmEmail("missing", "token"), typeof(RedirectToActionResult));
        users.Setup(m => m.FindByIdAsync("doctor")).ReturnsAsync(new Ljekar());
        Assert.IsInstanceOfType(await CreateController(context, users.Object, signIn.Object).ConfirmEmail("doctor", "token"), typeof(RedirectToActionResult));

        var patient = new Pacijent { Id = "patient" };
        users.Setup(m => m.FindByIdAsync(patient.Id)).ReturnsAsync(patient);
        users.Setup(m => m.ConfirmEmailAsync(patient, "good")).ReturnsAsync(IdentityResult.Success);
        var confirmed = CreateController(context, users.Object, signIn.Object);
        Assert.IsInstanceOfType(await confirmed.ConfirmEmail(patient.Id, "good"), typeof(RedirectToActionResult));
        Assert.IsNotNull(confirmed.TempData["Success"]);
    }

    [TestMethod]
    public async Task Logout_SignsOutAndReturnsToLogin()
    {
        await using var context = CreateContext();
        var (users, signIn) = CreateIdentity();
        signIn.Setup(m => m.SignOutAsync()).Returns(Task.CompletedTask);
        var result = await CreateController(context, users.Object, signIn.Object).Logout() as RedirectToActionResult;
        Assert.AreEqual("Login", result!.ActionName);
    }

    private static LoginViewModel LoginModel() => new() { Email = "doctor@test", Password = "Password1!", Uloga = KorisnickaUloga.Ljekar };

    private static RegisterViewModel RegisterModel(KorisnickaUloga role) => new()
    {
        Ime = "Amina", Prezime = "Kovac", Email = "new@test", Password = "Password1!", ConfirmPassword = "Password1!", DatumRodjenja = new DateTime(1990, 1, 1), Spol = Spol.Zensko, Uloga = role,
        KontaktIme = "Kontakt", KontaktPrezime = "Osoba", KontaktEmail = "contact@test", KontaktTelefon = "061111111"
    };

    private static AccountController CreateController(ApplicationDbContext context, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IEmailService? email = null, bool authenticated = false)
    {
        var url = new Mock<IUrlHelper>();
        url.Setup(u => u.IsLocalUrl(It.IsAny<string>())).Returns(false);
        var controller = new AccountController(context, Options.Create(new EmailSettings { AppUrl = "https://timeforpill.test" }), email ?? Mock.Of<IEmailService>(), NullLogger<AccountController>.Instance, signIn, users)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(authenticated ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "id")], "Test") : new ClaimsIdentity()) } }
        };
        controller.Url = url.Object;
        controller.TempData = new TempDataDictionary(controller.HttpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static (Mock<UserManager<ApplicationUser>>, Mock<SignInManager<ApplicationUser>>) CreateIdentity()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var users = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var signIn = new Mock<SignInManager<ApplicationUser>>(users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(), null!, null!, null!, null!);
        return (users, signIn);
    }

    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
