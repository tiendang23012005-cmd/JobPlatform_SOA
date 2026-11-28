using Contract.Api.Data;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Events;
using Shared.Infrastructure.Messaging;
namespace Contract.Api.Consumers;

public class ApplicationAcceptedConsumer(RabbitMqConnection mq, IServiceScopeFactory scopes,
    ILogger<ApplicationAcceptedConsumer> logger) : EventConsumer<ApplicationAcceptedEvent>(mq, scopes, logger)
{
    protected override string QueueName => "contract.application-accepted";
    protected override string RoutingKey => "ApplicationAccepted";

    protected override async Task HandleAsync(ApplicationAcceptedEvent e, IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ContractDbContext>();
        if (await db.AcceptedApplications.AnyAsync(a => a.ApplicationId == e.ApplicationId, ct)) return; // idempotent
        db.AcceptedApplications.Add(new AcceptedApplication
        {
            ApplicationId = e.ApplicationId, JobId = e.JobId, EmployerId = e.EmployerId,
            FreelancerId = e.FreelancerId, JobTitle = e.JobTitle, ProposedBudget = e.ProposedBudget
        });
        await db.SaveChangesAsync(ct);
    }
}
