namespace AutoVerify.Api.Persistence;

public sealed class DatabaseBootstrapOptions
{
    public const string SectionName = "DatabaseBootstrap";

    public bool Enabled { get; set; } = true;
    public string ScriptPath { get; set; } = "Database/bootstrap.sql";
    public bool RunOnlyWhenDatabaseIsEmpty { get; set; } = true;
    public bool FailOnError { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 300;
}
