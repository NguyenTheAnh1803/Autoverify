using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Services;
using AutoVerify.Api.Verification;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class DatabaseCredentialServiceTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"autoverify-credentials-{Guid.NewGuid():N}");

    [Fact]
    public void ProtectPassword_PreservesLeadingAndTrailingWhitespace()
    {
        var service = CreateService(Path.Combine(tempRoot, "keys"));
        const string password = "  pass word  ";

        var encrypted = service.ProtectPassword(password);

        Assert.Equal(password, service.UnprotectPassword(encrypted));
        Assert.Equal("Protected", service.GetCredentialStatus(encrypted));
    }

    [Fact]
    public void UnprotectPassword_WithDifferentKeyRing_ReportsRecoveryAction()
    {
        var firstService = CreateService(Path.Combine(tempRoot, "keys-a"));
        var secondService = CreateService(Path.Combine(tempRoot, "keys-b"));
        var encrypted = firstService.ProtectPassword("secret");

        var error = Assert.Throws<DatabaseCredentialException>(() => secondService.UnprotectPassword(encrypted, Guid.Empty));

        Assert.Contains("Restore the AutoVerify Data Protection key ring or re-enter the password", error.Message);
        Assert.Equal("Unreadable", secondService.GetCredentialStatus(encrypted));
    }

    [Fact]
    public void BuildMariaDbConnectionString_UsesDecryptedPassword()
    {
        var service = CreateService(Path.Combine(tempRoot, "keys"));
        var connection = new DatabaseConnectionEntity
        {
            Id = Guid.NewGuid(),
            Host = "db.internal",
            Port = 3307,
            DatabaseName = "production",
            Username = "operator",
            EncryptedPassword = service.ProtectPassword("secret value")
        };

        var connectionString = service.BuildMariaDbConnectionString(connection);
        var parsed = new MySqlConnector.MySqlConnectionStringBuilder(connectionString);

        Assert.Equal("secret value", parsed.Password);
        Assert.Equal("db.internal", parsed.Server);
        Assert.Equal((uint)3307, parsed.Port);
    }

    [Fact]
    public void GetCredentialStatus_RecognizesMissingPassword()
    {
        var service = CreateService(Path.Combine(tempRoot, "keys"));

        Assert.Equal("Missing", service.GetCredentialStatus(string.Empty));
        Assert.Equal("Missing", service.GetCredentialStatus(null));
    }

    [Fact]
    public async Task UpdateConnection_WithBlankPassword_KeepsExistingCredential()
    {
        var service = CreateService(Path.Combine(tempRoot, "keys"));
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase($"credential-update-{Guid.NewGuid():N}")
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        var connectionId = Guid.NewGuid();
        var encrypted = service.ProtectPassword("saved password");
        db.DatabaseConnections.Add(new DatabaseConnectionEntity
        {
            Id = connectionId,
            Name = "Production",
            Engine = "MariaDB",
            MachineCode = "MACHINE-A",
            LineCode = "LINE-1",
            Host = "db.internal",
            Port = 3306,
            DatabaseName = "production",
            Username = "operator",
            EncryptedPassword = encrypted,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var store = new AutoVerifyStore(
            db,
            service,
            new AnomalyVerificationService(),
            new QueryEngineService(),
            new ImportValidationService());

        var response = await store.UpdateDatabaseConnectionAsync(
            connectionId,
            new CreateDatabaseConnectionRequest(
                "Production",
                "MariaDB",
                "MACHINE-A",
                "LINE-1",
                "db.internal",
                3306,
                "production",
                "operator",
                Password: null));

        var saved = await db.DatabaseConnections.SingleAsync(x => x.Id == connectionId);
        Assert.Equal(encrypted, saved.EncryptedPassword);
        Assert.Equal("saved password", service.UnprotectPassword(saved.EncryptedPassword));
        Assert.Equal("Protected", response?.CredentialStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
    }

    private static DatabaseCredentialService CreateService(string keysPath)
    {
        Directory.CreateDirectory(keysPath);
        var provider = DataProtectionProvider.Create(
            new DirectoryInfo(keysPath),
            configuration => configuration.SetApplicationName("AutoVerify.Tests"));
        return new DatabaseCredentialService(provider);
    }
}
