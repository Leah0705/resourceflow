using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;

namespace ResourceFlowApi.Tests.Integration;

/// <summary>
/// An admin session is checked against the account on every request, so changes an Owner
/// makes to a user apply to that user's existing tokens straight away.
/// </summary>
public class AdminSessionValidationTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory = factory;

    private async Task<AdminCredential> SeedUserAsync(string email, string role)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IPasswordService passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        (string hash, string salt) = passwords.Hash("seeded-password");
        var user = new AdminCredential { Email = email, PasswordHash = hash, PasswordSalt = salt, Role = role };
        db.AdminCredentials.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task UpdateUserAsync(int id, Action<AdminCredential> change)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        AdminCredential user = await db.AdminCredentials.SingleAsync(c => c.Id == id);
        change(user);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ADeactivatedUsersToken_IsRejected()
    {
        AdminCredential manager = await SeedUserAsync("session-deactivated@test.com", UserRoles.Manager);
        HttpClient client = _factory.CreateClientWithToken(
            TestWebAppFactory.GenerateJwt(manager.Id, manager.Email, manager.Role));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/overview")).StatusCode);

        await UpdateUserAsync(manager.Id, u => u.IsActive = false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/overview")).StatusCode);
    }

    [Fact]
    public async Task ADemotedOwnersToken_LosesOwnerAccess()
    {
        AdminCredential owner = await SeedUserAsync("session-demoted@test.com", UserRoles.Owner);
        HttpClient client = _factory.CreateClientWithToken(
            TestWebAppFactory.GenerateJwt(owner.Id, owner.Email, owner.Role));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/users")).StatusCode);

        await UpdateUserAsync(owner.Id, u => u.Role = UserRoles.Manager);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/overview")).StatusCode);
    }

    [Fact]
    public async Task AReactivatedUsersToken_WorksAgain()
    {
        AdminCredential manager = await SeedUserAsync("session-reactivated@test.com", UserRoles.Manager);
        HttpClient client = _factory.CreateClientWithToken(
            TestWebAppFactory.GenerateJwt(manager.Id, manager.Email, manager.Role));

        await UpdateUserAsync(manager.Id, u => u.IsActive = false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/overview")).StatusCode);

        await UpdateUserAsync(manager.Id, u => u.IsActive = true);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/overview")).StatusCode);
    }
}
