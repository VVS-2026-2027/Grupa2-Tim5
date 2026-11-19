using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using TimeForPill.Data;

namespace TimeForPill.Tests;

[TestClass]
public class MigrationCoverageTests
{
    private static readonly HashSet<string> ExpectedNoOpMigrations =
    [
        "Nesto",
        "RemoveCollectionNavigations",
        "SpajanjenanovubazuMonsertAsp"
    ];

    public static IEnumerable<object[]> MigrationTypes()
    {
        return typeof(ApplicationDbContext).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(Migration)))
            .OrderBy(type => type.FullName)
            .Select(type => new object[] { type });
    }

    [DataTestMethod]
    [DynamicData(nameof(MigrationTypes), DynamicDataSourceType.Method)]
    public void Migration_UpAndDown_DescribeSchemaOperations(Type migrationType)
    {
        var migration = (Migration)Activator.CreateInstance(migrationType, nonPublic: true)!;
        var upBuilder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var downBuilder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");

        InvokeMigrationMethod(migration, "Up", upBuilder);
        InvokeMigrationMethod(migration, "Down", downBuilder);

        var operationCount = upBuilder.Operations.Count + downBuilder.Operations.Count;
        if (ExpectedNoOpMigrations.Contains(migrationType.Name))
        {
            Assert.AreEqual(0, operationCount, $"{migrationType.Name} treba ostati no-op migracija.");
        }
        else
        {
            Assert.IsTrue(operationCount > 0, $"{migrationType.Name} nema operacija.");
        }

        var targetModelMethod = migration.GetType().GetMethod(
            "BuildTargetModel",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (targetModelMethod != null)
        {
            var targetModel = migration.GetType()
                .GetProperty("TargetModel", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(migration) as IModel;
            Assert.IsNotNull(targetModel, $"{migrationType.Name} nije izgradio ciljni model.");
        }
    }

    [TestMethod]
    public void CurrentModelSnapshot_BuildsModelWithTherapyEntities()
    {
        var snapshotType = typeof(ApplicationDbContext).Assembly
            .GetTypes()
            .Single(type => !type.IsAbstract && type.IsSubclassOf(typeof(ModelSnapshot)));
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;

        var model = snapshot.Model;

        Assert.IsNotNull(model.FindEntityType("TimeForPill.Models.Terapija"));
        Assert.IsNotNull(model.FindEntityType("TimeForPill.Models.TerapijskaDoza"));
        Assert.IsNotNull(model.FindEntityType("TimeForPill.Models.Pacijent"));
    }

    private static void InvokeMigrationMethod(
        Migration migration,
        string methodName,
        MigrationBuilder builder)
    {
        var method = migration.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"{migration.GetType().Name} nema metodu {methodName}.");

        method.Invoke(migration, [builder]);
    }
}
