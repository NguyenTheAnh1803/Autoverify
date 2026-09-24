using System.Text.RegularExpressions;
using MySqlConnector;

namespace AutoVerify.Api.Services;

public interface IQueryEngineService
{
    IReadOnlyCollection<string> ValidateSqlTemplate(string sql);
    RenderedQueryResponse RenderQuery(Guid profileId, string sqlTemplate, IReadOnlyDictionary<string, string> parameters);
    Task<QueryTestResponse> ExecuteTestQueryAsync(Guid profileId, string host, int port, string databaseName, string username, string password, string sqlTemplate, int timeoutSeconds, IReadOnlyDictionary<string, string> parameters);
}

public sealed partial class QueryEngineService : IQueryEngineService
{
    public IReadOnlyCollection<string> ValidateSqlTemplate(string sql)
    {
        var warnings = new List<string>();
        var normalized = sql.TrimStart();
        if (!normalized.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && !normalized.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Blocked: only SELECT or WITH query templates are allowed.");
        }

        var forbidden = new[] { "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE", "CREATE", "REPLACE", "MERGE", "CALL", "EXEC" };
        foreach (var keyword in forbidden)
        {
            if (Regex.IsMatch(sql, $"\\b{keyword}\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                warnings.Add($"Blocked: keyword '{keyword}' is not allowed in query templates.");
            }
        }

        if (!QueryParameterRegex().IsMatch(sql))
        {
            warnings.Add("Warning: no {{parameter}} placeholders were found.");
        }

        return warnings;
    }

    public RenderedQueryResponse RenderQuery(Guid profileId, string sqlTemplate, IReadOnlyDictionary<string, string> parameters)
    {
        var missing = new List<string>();
        var rendered = QueryParameterRegex().Replace(sqlTemplate, match =>
        {
            var parameterName = match.Groups[1].Value;
            if (!parameters.TryGetValue(parameterName, out var value) || string.IsNullOrWhiteSpace(value))
            {
                missing.Add(parameterName);
                return match.Value;
            }

            return EscapeSqlLiteral(value);
        });

        return new RenderedQueryResponse(
            profileId,
            rendered,
            missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ValidateSqlTemplate(sqlTemplate)
        );
    }

    public async Task<QueryTestResponse> ExecuteTestQueryAsync(
        Guid profileId,
        string host,
        int port,
        string databaseName,
        string username,
        string password,
        string sqlTemplate,
        int timeoutSeconds,
        IReadOnlyDictionary<string, string> parameters)
    {
        var warnings = ValidateSqlTemplate(sqlTemplate);
        if (warnings.Any(x => x.StartsWith("Blocked", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(string.Join(" ", warnings));
        }

        var missing = QueryParameterRegex().Matches(sqlTemplate)
            .Select(x => x.Groups[1].Value)
            .Where(x => !parameters.ContainsKey(x) || string.IsNullOrWhiteSpace(parameters[x]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Missing required parameters: {string.Join(", ", missing)}.");
        }

        var sql = QueryParameterRegex().Replace(sqlTemplate, match => $"@{match.Groups[1].Value}");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            Database = databaseName,
            UserID = username,
            Password = password,
            ConnectionTimeout = 10,
            DefaultCommandTimeout = (uint)timeoutSeconds,
            SslMode = MySqlSslMode.Preferred
        };

        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = timeoutSeconds };
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue($"@{parameter.Key}", parameter.Value);
        }

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync() && rows.Count < 50)
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }

        stopwatch.Stop();
        return new QueryTestResponse(
            profileId,
            true,
            rows.Count,
            rows,
            $"SELECT completed successfully. Returned up to 50 rows.",
            (int)stopwatch.ElapsedMilliseconds
        );
    }

    private static string EscapeSqlLiteral(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    [GeneratedRegex("\\{\\{([A-Za-z0-9_]+)\\}\\}", RegexOptions.CultureInvariant)]
    private static partial Regex QueryParameterRegex();
}
