using System.Security.Cryptography;
using AutoVerify.Api.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;

namespace AutoVerify.Api.Services;

public sealed class DatabaseCredentialService(IDataProtectionProvider dataProtectionProvider) : IDatabaseCredentialService
{
    public const string ProtectorPurpose = "AutoVerify.DataSourceSecrets.v1";

    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public string ProtectPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Database password cannot be empty.", nameof(password));
        }

        return protector.Protect(password);
    }

    public string UnprotectPassword(string encryptedPassword, Guid? connectionId = null)
    {
        if (string.IsNullOrWhiteSpace(encryptedPassword))
        {
            throw new DatabaseCredentialException("The database connection does not have a saved password.");
        }

        try
        {
            return protector.Unprotect(encryptedPassword);
        }
        catch (CryptographicException ex)
        {
            var connectionLabel = connectionId.HasValue ? $" '{connectionId}'" : string.Empty;
            throw new DatabaseCredentialException(
                $"The saved password for database connection{connectionLabel} cannot be decrypted. Restore the AutoVerify Data Protection key ring or re-enter the password.",
                ex);
        }
    }

    public string GetCredentialStatus(string? encryptedPassword)
    {
        if (string.IsNullOrWhiteSpace(encryptedPassword)) return "Missing";

        try
        {
            _ = protector.Unprotect(encryptedPassword);
            return "Protected";
        }
        catch (CryptographicException)
        {
            return "Unreadable";
        }
    }

    public string BuildMariaDbConnectionString(
        DatabaseConnectionEntity connection,
        uint connectionTimeoutSeconds = 10,
        uint commandTimeoutSeconds = 30)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var builder = new MySqlConnectionStringBuilder
        {
            Server = string.IsNullOrWhiteSpace(connection.Host) ? "localhost" : connection.Host,
            Port = connection.Port > 0 ? (uint)connection.Port : 3306,
            Database = connection.DatabaseName,
            UserID = connection.Username,
            Password = UnprotectPassword(connection.EncryptedPassword, connection.Id),
            ConnectionTimeout = connectionTimeoutSeconds,
            DefaultCommandTimeout = commandTimeoutSeconds,
            SslMode = MySqlSslMode.Preferred,
            AllowUserVariables = true
        };

        return builder.ConnectionString;
    }
}
