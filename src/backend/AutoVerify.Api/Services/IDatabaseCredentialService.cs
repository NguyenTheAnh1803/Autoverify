using AutoVerify.Api.Persistence.Entities;

namespace AutoVerify.Api.Services;

public interface IDatabaseCredentialService
{
    string ProtectPassword(string password);
    string UnprotectPassword(string encryptedPassword, Guid? connectionId = null);
    string GetCredentialStatus(string? encryptedPassword);
    string BuildMariaDbConnectionString(
        DatabaseConnectionEntity connection,
        uint connectionTimeoutSeconds = 10,
        uint commandTimeoutSeconds = 30);
}

public sealed class DatabaseCredentialException : InvalidOperationException
{
    public DatabaseCredentialException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
