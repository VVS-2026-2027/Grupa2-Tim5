using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TimeForPill.Services;

namespace TimeForPill.Tests;

[TestClass]
public class ServiceEdgeCaseTests
{
    [TestMethod]
    public async Task EmailService_EmptyRecipient_RejectsRequestBeforeConfigurationCheck()
    {
        var service = CreateEmailService(new EmailSettings());

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => service.SendEmailAsync(" ", "Predmet", "Poruka"));

        StringAssert.Contains(exception.Message, "primalac");
    }

    [TestMethod]
    public async Task EmailService_IncompleteConfiguration_RejectsSendWithoutNetworkCall()
    {
        var service = CreateEmailService(new EmailSettings
        {
            SmtpServer = "smtp.example.test",
            SenderEmail = "sender@example.test"
        });

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => service.SendEmailAsync("patient@example.test", "Predmet", "Poruka", true));

        StringAssert.Contains(exception.Message, "SMTP");
    }

    [TestMethod]
    public void EmailService_PrivateConfigurationRules_RequireCompleteSettingsAndCreateSenderAddress()
    {
        var configured = CreateEmailService(new EmailSettings
        {
            SmtpServer = "smtp.example.test",
            Port = 587,
            SenderEmail = "sender@example.test",
            Username = "user@example.test",
            Password = "test-password",
            SenderName = "TimeForPill"
        });
        var incomplete = CreateEmailService(new EmailSettings { SenderEmail = "sender@example.test" });

        Assert.IsTrue(InvokePrivate<bool>(configured, "IsConfigured"));
        Assert.IsFalse(InvokePrivate<bool>(incomplete, "IsConfigured"));
        var sender = InvokePrivate<System.Net.Mail.MailAddress>(configured, "CreateFromAddress");
        Assert.AreEqual("sender@example.test", sender.Address);
        Assert.AreEqual("TimeForPill", sender.DisplayName);
    }

    [TestMethod]
    public async Task DoseReminderWorker_ActiveIteration_InvokesBothWorkflowOperations()
    {
        using var cancellation = new CancellationTokenSource();
        var workflow = new Mock<IDoseWorkflowService>(MockBehavior.Strict);
        workflow.Setup(service => service.RefreshMissedDosesAsync())
            .Callback(cancellation.Cancel)
            .Returns(Task.CompletedTask);
        workflow.Setup(service => service.SendDueReminderEmailsAsync())
            .Returns(Task.CompletedTask);
        var worker = CreateWorker(workflow.Object);

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            () => worker.RunAsync(cancellation.Token));

        workflow.Verify(service => service.RefreshMissedDosesAsync(), Times.Once);
        workflow.Verify(service => service.SendDueReminderEmailsAsync(), Times.Once);
    }

    [TestMethod]
    public async Task DoseReminderWorker_WorkflowFailure_IsHandledBeforeNextDelay()
    {
        using var cancellation = new CancellationTokenSource();
        var workflow = new Mock<IDoseWorkflowService>();
        workflow.Setup(service => service.RefreshMissedDosesAsync())
            .Callback(cancellation.Cancel)
            .ThrowsAsync(new InvalidOperationException("test failure"));
        var worker = CreateWorker(workflow.Object);

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            () => worker.RunAsync(cancellation.Token));

        workflow.Verify(service => service.SendDueReminderEmailsAsync(), Times.Never);
    }

    private static EmailService CreateEmailService(EmailSettings settings) => new(
        Options.Create(settings),
        NullLogger<EmailService>.Instance);

    private static TestableDoseReminderWorker CreateWorker(IDoseWorkflowService workflow)
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(service => service.GetService(typeof(IDoseWorkflowService)))
            .Returns(workflow);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(value => value.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(scope.Object);

        return new TestableDoseReminderWorker(
            scopeFactory.Object,
            NullLogger<DoseReminderWorker>.Instance);
    }

    private static T InvokePrivate<T>(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);

        return (T)method.Invoke(instance, null)!;
    }

    private sealed class TestableDoseReminderWorker : DoseReminderWorker
    {
        public TestableDoseReminderWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<DoseReminderWorker> logger)
            : base(scopeFactory, logger)
        {
        }

        public Task RunAsync(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);
    }
}
