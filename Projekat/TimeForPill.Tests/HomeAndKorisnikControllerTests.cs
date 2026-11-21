using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using TimeForPill.Controllers;
using TimeForPill.Data;
using TimeForPill.Models;

namespace TimeForPill.Tests;

[TestClass]
public class HomeAndKorisnikControllerTests : UnitTestBase
{
    [TestMethod]
    public async Task HomeController_UnauthenticatedUser_RedirectsToLogin()
    {
        var controller = CreateHomeController(null, isAuthenticated: false);

        var result = await controller.Index() as RedirectToActionResult;

        Assert.AreEqual("Login", result!.ActionName);
        Assert.AreEqual("Account", result.ControllerName);
    }

    [DataTestMethod]
    [DynamicData(nameof(HomeUsers), DynamicDataSourceType.Method)]
    public async Task HomeController_AuthenticatedUser_RedirectsToRoleHome(ApplicationUser user, string controllerName)
    {
        var controller = CreateHomeController(user, isAuthenticated: true);

        var result = await controller.Index() as RedirectToActionResult;

        Assert.AreEqual("Home", result!.ActionName);
        Assert.AreEqual(controllerName, result.ControllerName);
    }

    [TestMethod]
    public void HomeController_PrivacyAndError_ReturnViewsWithTraceId()
    {
        var controller = CreateHomeController(null, isAuthenticated: false);
        controller.HttpContext.TraceIdentifier = "trace-123";

        var privacy = controller.Privacy();
        var error = controller.Error() as ViewResult;

        Assert.IsInstanceOfType(privacy, typeof(ViewResult));
        Assert.AreEqual("trace-123", ((ErrorViewModel)error!.Model!).RequestId);
    }

    [TestMethod]
    public async Task KorisniksController_RoutesUsersByConcreteRoleAndHandlesMissingIds()
    {
        await using var context = CreateContext();
        var patient = CreatePatient("patient-1");
        var doctor = new Ljekar
        {
            Id = "doctor-1",
            UserName = "doctor@example.test",
            Email = "doctor@example.test",
            Ime = "Doktor",
            Prezime = "Kovac",
            DatumRodjenja = new DateTime(1980, 1, 1),
            Spol = Spol.Musko,
            Specijalizacija = Specijalizacija.Kardiolog
        };
        var admin = new Administrator
        {
            Id = "admin-1",
            UserName = "admin@example.test",
            Email = "admin@example.test",
            Ime = "Admin",
            Prezime = "Kovac",
            DatumRodjenja = new DateTime(1980, 1, 1),
            Spol = Spol.Zensko,
            datumImenovanja = DateTime.Today
        };
        context.AddRange(patient, doctor, admin);
        await context.SaveChangesAsync();
        var controller = new KorisniksController(context);

        var index = await controller.Index() as ViewResult;
        Assert.IsInstanceOfType(index!.Model, typeof(List<ApplicationUser>));
        var details = await controller.Details(patient.Id) as ViewResult;
        var create = controller.Create() as RedirectToActionResult;
        var editPatient = await controller.Edit(patient.Id) as RedirectToActionResult;
        var editDoctor = await controller.Edit(doctor.Id) as RedirectToActionResult;
        var editAdmin = await controller.Edit(admin.Id) as RedirectToActionResult;
        var delete = await controller.Delete(patient.Id) as ViewResult;

        Assert.AreEqual(patient.Id, ((ApplicationUser)details!.Model!).Id);
        Assert.AreEqual("Pacijents", create!.ControllerName);
        Assert.AreEqual("Pacijents", editPatient!.ControllerName);
        Assert.AreEqual("Ljekars", editDoctor!.ControllerName);
        Assert.AreEqual("Administrators", editAdmin!.ControllerName);
        Assert.AreEqual(patient.Id, ((ApplicationUser)delete!.Model!).Id);
        Assert.IsInstanceOfType(await controller.Details(null), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Edit("missing"), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Delete("missing"), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.DeleteConfirmed("missing"), typeof(RedirectToActionResult));
    }

    public static IEnumerable<object[]> HomeUsers()
    {
        yield return [CreatePatient("patient"), "Pacijent"];
        yield return [new Ljekar { Ime = "Doktor", Prezime = "Kovac" }, "Ljekar"];
        yield return [new Administrator { Ime = "Admin", Prezime = "Kovac" }, "Admin"];
    }

    private static global::TimeForPill.HomeController CreateHomeController(
        ApplicationUser? user,
        bool isAuthenticated)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var manager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        manager.Setup(service => service.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);
        var identity = isAuthenticated
            ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "id")], "Test")
            : new ClaimsIdentity();
        var controller = new global::TimeForPill.HomeController(manager.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };

        return controller;
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Pacijent CreatePatient(string id) => new()
    {
        Id = id,
        UserName = "patient@example.test",
        Email = "patient@example.test",
        Ime = "Amina",
        Prezime = "Kovac",
        DatumRodjenja = new DateTime(1990, 1, 1),
        Spol = Spol.Zensko,
        KontaktOsoba = null!
    };
}
