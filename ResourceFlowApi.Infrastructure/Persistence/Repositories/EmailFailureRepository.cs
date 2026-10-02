using CustomAccessibility.Attributes;
using Microsoft.EntityFrameworkCore;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence.Repositories;

[OnlyAccessibleBy("ResourceFlowApi.Extensions.ServiceCollectionExtensions")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.BookingConfirmationServiceTests")]
[OnlyAccessibleBy("ResourceFlowApi.Tests.Services.EmailSettingsServiceTests")]
[ExternalAccessAllowed]
internal class EmailFailureRepository(AppDbContext db) : IEmailFailureRepository
{
    private readonly AppDbContext _db = db;

    public async Task AddAsync(EmailFailure failure)
    {
        _db.EmailFailures.Add(failure);
        await _db.SaveChangesAsync();
    }

    public async Task<List<EmailFailure>> GetRecentAsync(int count = 50)
    {
        return await _db.EmailFailures
            .OrderByDescending(f => f.AttemptedAt)
            .Take(count)
            .ToListAsync();
    }
}
