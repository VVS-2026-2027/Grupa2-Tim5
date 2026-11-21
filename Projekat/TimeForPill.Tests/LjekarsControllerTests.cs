using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using TimeForPill.Controllers;
using TimeForPill.Data;
using TimeForPill.Models;

namespace TimeForPill.Tests;

[TestClass]
public class LjekarsControllerTests : UnitTestBase
{
    [TestMethod]
    public async Task LjekarsCrud_ValidatesCreatesUpdatesAuditsAndRemovesDependencies()
    {
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var doctor = NewDoctor("doctor", "old@test");
        var patient = new Pacijent { Id = "patient", Ime = "Amina", Prezime = "Kovac", Ljekar = doctor };
        context.AddRange(doctor, patient, new Ticket { KorisnikId = doctor.Id, Naslov = "Pomoc", Opis = "Opis" });
        await context.SaveChangesAsync();
        var admin = new Administrator { Id = "admin", Ime = "Sara", Prezime = "Admin" };
        var manager = Manager(admin);
        manager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        manager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        var controller = Controller(context, manager.Object);

        var index = await controller.Index() as ViewResult;
        Assert.AreEqual(doctor.Id, ((List<Ljekar>)index!.Model!).Single().Id);
        Assert.IsInstanceOfType(await controller.Details(null), typeof(NotFoundResult));
        Assert.IsInstanceOfType(await controller.Details("missing"), typeof(NotFoundResult));
        Assert.AreEqual(DateTime.Today.AddYears(-35), ((Ljekar)(controller.Create() as ViewResult)!.Model!).DatumRodjenja);
        controller.ModelState.AddModelError("x", "invalid");
        Assert.IsInstanceOfType(await controller.Create(NewDoctor("new", "new@test"), "pass"), typeof(ViewResult));

        var newDoctor = NewDoctor("new", "new@test");
        var creation = await Controller(context, manager.Object).Create(newDoctor, "pass");
        Assert.IsInstanceOfType(creation, typeof(RedirectToActionResult));
        Assert.AreEqual("new@test", newDoctor.UserName);

        var changed = NewDoctor(doctor.Id, "changed@test");
        changed.Ime = "Izmijenjen";
        Assert.IsInstanceOfType(await Controller(context, manager.Object).Edit(doctor.Id, changed), typeof(RedirectToActionResult));
        Assert.AreEqual("Izmijenjen", doctor.Ime);
        Assert.IsTrue(await context.AdminAkcije.AnyAsync(a => a.VrstaAkcije == "Uredjen"));
        Assert.IsInstanceOfType(await Controller(context, manager.Object).Edit("wrong", changed), typeof(NotFoundResult));

        var deleted = await Controller(context, manager.Object).DeleteConfirmed(doctor.Id);
        Assert.IsInstanceOfType(deleted, typeof(RedirectToActionResult));
        Assert.IsNull(await context.Ljekari.FindAsync(doctor.Id));
        Assert.IsNull((await context.Pacijenti.FindAsync(patient.Id))!.LjekarId);
        Assert.AreEqual(0, await context.Tickets.CountAsync());
        Assert.IsTrue(await context.AdminAkcije.AnyAsync(a => a.VrstaAkcije == "Obrisan"));
    }

    private static Ljekar NewDoctor(string id, string email) => new() { Id = id, Ime = "Doktor", Prezime = "Kovac", Email = email, UserName = email, DatumRodjenja = new DateTime(1980, 1, 1), Spol = Spol.Musko, Specijalizacija = Specijalizacija.Kardiolog };

    private static LjekarsController Controller(ApplicationDbContext context, UserManager<ApplicationUser> manager)
    {
        var controller = new LjekarsController(context, manager) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin")], "Test")) } } };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static Mock<UserManager<ApplicationUser>> Manager(Administrator admin)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var manager = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        manager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(admin);
        return manager;
    }
}
