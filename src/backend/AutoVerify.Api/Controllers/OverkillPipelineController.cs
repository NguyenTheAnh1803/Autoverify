using System.Text.Json;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/overkill-pipeline")]
public sealed class OverkillPipelineController(
    AutoVerifyDbContext db,
    IOverkillQueryEngineService queryEngine,
    AutoVerifyStore store) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [HttpGet("configs")]
    [ProducesResponseType(typeof(IReadOnlyCollection<OverkillPipelineConfigResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfigs(CancellationToken cancellationToken)
    {
        var configs = await db.OverkillPipelineConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.LastRunAtUtc ?? x.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        var connectionIds = configs.Select(c => c.DatabaseConnectionId).Distinct().ToList();
        var connections = await db.DatabaseConnections
            .AsNoTracking()
            .Where(c => connectionIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var responses = configs.Select(c =>
        {
            var connection = connections.GetValueOrDefault(c.DatabaseConnectionId);
            if (connection != null) c.ModelCode = connection.ModelCode;
            return ToResponse(c, connection?.Name ?? "Unknown Connection");
        }).ToList();

        return Ok(responses);
    }

    [HttpGet("configs/{id:guid}")]
    [ProducesResponseType(typeof(OverkillPipelineConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConfigById(Guid id, CancellationToken cancellationToken)
    {
        var config = await db.OverkillPipelineConfigs
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (config is null) return NotFound();

        var connection = await db.DatabaseConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == config.DatabaseConnectionId, cancellationToken);

        if (connection != null) config.ModelCode = connection.ModelCode;
        return Ok(ToResponse(config, connection?.Name ?? "Unknown Connection"));
    }

    [HttpPost("configs")]
    [ProducesResponseType(typeof(OverkillPipelineConfigResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateConfig(
        [FromBody] CreateOverkillPipelineConfigRequest request,
        CancellationToken cancellationToken)
    {
        var connection = await db.DatabaseConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == request.DatabaseConnectionId, cancellationToken);

        if (connection is null)
        {
            return BadRequest(new { error = $"Database connection with ID '{request.DatabaseConnectionId}' does not exist." });
        }

        var bypassRule = request.BypassRule;
        if (bypassRule is null && !string.IsNullOrWhiteSpace(request.DefectBypassRuleJson))
        {
            bypassRule = DefectBypassRuleEvaluator.ParseRule(request.DefectBypassRuleJson);
        }

        var (isValidBypass, bypassError) = DefectBypassRuleValidator.Validate(bypassRule);
        if (!isValidBypass)
        {
            return BadRequest(new { error = bypassError });
        }

        var now = DateTimeOffset.UtcNow;
        var machineCode = !string.IsNullOrWhiteSpace(request.MachineCode)
            ? request.MachineCode.Trim()
            : (!string.IsNullOrWhiteSpace(connection.MachineCode) ? connection.MachineCode.Trim() : "MACHINE-A");

        var lineCode = !string.IsNullOrWhiteSpace(request.LineCode)
            ? request.LineCode.Trim()
            : (!string.IsNullOrWhiteSpace(connection.LineCode) ? connection.LineCode.Trim() : "LINE-1");

        var entity = new OverkillPipelineConfigEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            DatabaseConnectionId = request.DatabaseConnectionId,
            MachineCode = machineCode,
            LineCode = lineCode,
            ModelCode = connection.ModelCode,
            Enabled = request.Enabled,
            ScheduleIntervalMinutes = Math.Max(5, request.ScheduleIntervalMinutes),
            NextRunAtUtc = request.Enabled ? now.AddMinutes(Math.Max(5, request.ScheduleIntervalMinutes)) : null,
            ProductTableName = string.IsNullOrWhiteSpace(request.ProductTableName) ? "Product" : request.ProductTableName.Trim(),
            IdColumnName = string.IsNullOrWhiteSpace(request.IdColumnName) ? "ID" : request.IdColumnName.Trim(),
            FkIdColumnName = string.IsNullOrWhiteSpace(request.FkIdColumnName) ? "Fk_ID" : request.FkIdColumnName.Trim(),
            JudgeColumnName = string.IsNullOrWhiteSpace(request.JudgeColumnName) ? "Judge" : request.JudgeColumnName.Trim(),
            JudgeFilterValuesJson = JsonSerializer.Serialize(request.JudgeFilterValues is { Count: > 0 } ? request.JudgeFilterValues : ["N", "NG"]),
            IncludeOkProducts = request.IncludeOkProducts,
            TimestampColumnName = string.IsNullOrWhiteSpace(request.TimestampColumnName) ? null : request.TimestampColumnName.Trim(),
            ZoneColumnName = string.IsNullOrWhiteSpace(request.ZoneColumnName) ? "zone" : request.ZoneColumnName.Trim(),
            DefectNameColumnName = string.IsNullOrWhiteSpace(request.DefectNameColumnName) ? "defect_name" : request.DefectNameColumnName.Trim(),
            DateTimeInspectionColumnName = string.IsNullOrWhiteSpace(request.DateTimeInspectionColumnName ?? request.InspectedAtColumnName) ? "datetime_inspection" : (request.DateTimeInspectionColumnName ?? request.InspectedAtColumnName)!.Trim(),
            LookbackHours = Math.Max(1, request.LookbackHours),
            BatchSize = Math.Clamp(request.BatchSize, 10, 10000),
            DefectTablesJson = JsonSerializer.Serialize(request.DefectTables ?? []),
            OffloadDefectsToSeaweedFs = request.OffloadDefectsToSeaweedFs,
            ImageCrawlerApiUrl = string.IsNullOrWhiteSpace(request.ImageCrawlerApiUrl) ? null : request.ImageCrawlerApiUrl.Trim(),
            AutoCrawlImages = request.AutoCrawlImages,
            ImageCrawlerVpMode = string.IsNullOrWhiteSpace(request.ImageCrawlerVpMode) ? "0" : request.ImageCrawlerVpMode.Trim(),
            DefectBypassRuleJson = bypassRule is not null ? JsonSerializer.Serialize(bypassRule) : null,
            LastRunDefectsBypassedCount = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.OverkillPipelineConfigs.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Created($"/api/v1/overkill-pipeline/configs/{entity.Id}", ToResponse(entity, connection.Name));
    }

    [HttpPut("configs/{id:guid}")]
    [ProducesResponseType(typeof(OverkillPipelineConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateConfig(
        Guid id,
        [FromBody] CreateOverkillPipelineConfigRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.OverkillPipelineConfigs
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null) return NotFound();

        var connection = await db.DatabaseConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == request.DatabaseConnectionId, cancellationToken);

        if (connection is null)
        {
            return BadRequest(new { error = $"Database connection with ID '{request.DatabaseConnectionId}' does not exist." });
        }

        var bypassRule = request.BypassRule;
        if (bypassRule is null && !string.IsNullOrWhiteSpace(request.DefectBypassRuleJson))
        {
            bypassRule = DefectBypassRuleEvaluator.ParseRule(request.DefectBypassRuleJson);
        }

        var (isValidBypass, bypassError) = DefectBypassRuleValidator.Validate(bypassRule);
        if (!isValidBypass)
        {
            return BadRequest(new { error = bypassError });
        }

        var now = DateTimeOffset.UtcNow;
        var machineCode = !string.IsNullOrWhiteSpace(request.MachineCode)
            ? request.MachineCode.Trim()
            : (!string.IsNullOrWhiteSpace(connection.MachineCode) ? connection.MachineCode.Trim() : (!string.IsNullOrWhiteSpace(entity.MachineCode) ? entity.MachineCode : "MACHINE-A"));

        var lineCode = !string.IsNullOrWhiteSpace(request.LineCode)
            ? request.LineCode.Trim()
            : (!string.IsNullOrWhiteSpace(connection.LineCode) ? connection.LineCode.Trim() : (!string.IsNullOrWhiteSpace(entity.LineCode) ? entity.LineCode : "LINE-1"));

        entity.Name = request.Name.Trim();
        entity.DatabaseConnectionId = request.DatabaseConnectionId;
        entity.MachineCode = machineCode;
        entity.LineCode = lineCode;
        entity.ModelCode = connection.ModelCode;
        entity.Enabled = request.Enabled;
        entity.ScheduleIntervalMinutes = Math.Max(5, request.ScheduleIntervalMinutes);
        entity.ProductTableName = string.IsNullOrWhiteSpace(request.ProductTableName) ? "Product" : request.ProductTableName.Trim();
        entity.IdColumnName = string.IsNullOrWhiteSpace(request.IdColumnName) ? "ID" : request.IdColumnName.Trim();
        entity.FkIdColumnName = string.IsNullOrWhiteSpace(request.FkIdColumnName) ? "Fk_ID" : request.FkIdColumnName.Trim();
        entity.JudgeColumnName = string.IsNullOrWhiteSpace(request.JudgeColumnName) ? "Judge" : request.JudgeColumnName.Trim();
        entity.JudgeFilterValuesJson = JsonSerializer.Serialize(request.JudgeFilterValues is { Count: > 0 } ? request.JudgeFilterValues : ["N", "NG"]);
        entity.IncludeOkProducts = request.IncludeOkProducts;
        entity.TimestampColumnName = string.IsNullOrWhiteSpace(request.TimestampColumnName) ? null : request.TimestampColumnName.Trim();
        entity.ZoneColumnName = string.IsNullOrWhiteSpace(request.ZoneColumnName) ? "zone" : request.ZoneColumnName.Trim();
        entity.DefectNameColumnName = string.IsNullOrWhiteSpace(request.DefectNameColumnName) ? "defect_name" : request.DefectNameColumnName.Trim();
        entity.DateTimeInspectionColumnName = string.IsNullOrWhiteSpace(request.DateTimeInspectionColumnName ?? request.InspectedAtColumnName) ? "datetime_inspection" : (request.DateTimeInspectionColumnName ?? request.InspectedAtColumnName)!.Trim();
        entity.LookbackHours = Math.Max(1, request.LookbackHours);
        entity.BatchSize = Math.Clamp(request.BatchSize, 10, 10000);
        entity.DefectTablesJson = JsonSerializer.Serialize(request.DefectTables ?? []);
        entity.OffloadDefectsToSeaweedFs = request.OffloadDefectsToSeaweedFs;
        entity.ImageCrawlerApiUrl = string.IsNullOrWhiteSpace(request.ImageCrawlerApiUrl) ? null : request.ImageCrawlerApiUrl.Trim();
        entity.AutoCrawlImages = request.AutoCrawlImages;
        if (!string.IsNullOrWhiteSpace(request.ImageCrawlerVpMode)) entity.ImageCrawlerVpMode = request.ImageCrawlerVpMode.Trim();
        entity.DefectBypassRuleJson = bypassRule is not null ? JsonSerializer.Serialize(bypassRule) : null;
        entity.UpdatedAtUtc = now;

        if (entity.Enabled && entity.NextRunAtUtc == null)
        {
            entity.NextRunAtUtc = now.AddMinutes(entity.ScheduleIntervalMinutes);
        }
        else if (!entity.Enabled)
        {
            entity.NextRunAtUtc = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(entity, connection.Name));
    }

    [HttpDelete("configs/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteConfig(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.OverkillPipelineConfigs
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null) return NotFound();

        db.OverkillPipelineConfigs.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("batch-provision-from-connections")]
    [ProducesResponseType(typeof(BatchProvisionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BatchProvisionFromConnections(
        [FromBody] BatchGeneratePipelinesForConnectionsRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await store.BatchGeneratePipelinesForConnectionsAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("batch-delete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchDeletePipelines(
        [FromBody] BatchDeletePipelinesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PipelineIds == null || request.PipelineIds.Count == 0)
        {
            return Ok(new { deletedCount = 0 });
        }

        var entities = await db.OverkillPipelineConfigs
            .Where(p => request.PipelineIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var count = entities.Count;
        if (count > 0)
        {
            db.OverkillPipelineConfigs.RemoveRange(entities);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new { deletedCount = count, message = $"Successfully deleted {count} pipeline(s)." });
    }

    [HttpPost("configs/{id:guid}/toggle")]
    [ProducesResponseType(typeof(OverkillPipelineConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleConfig(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.OverkillPipelineConfigs
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null) return NotFound();

        entity.Enabled = !entity.Enabled;
        entity.NextRunAtUtc = entity.Enabled
            ? DateTimeOffset.UtcNow.AddMinutes(entity.ScheduleIntervalMinutes)
            : null;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var connection = await db.DatabaseConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == entity.DatabaseConnectionId, cancellationToken);

        return Ok(ToResponse(entity, connection?.Name ?? "Unknown Connection"));
    }

    [HttpPost("configs/{id:guid}/run")]
    [ProducesResponseType(typeof(OverkillRunResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RunPipeline(Guid id, CancellationToken cancellationToken)
    {
        var configExists = await db.OverkillPipelineConfigs
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!configExists) return NotFound();

        var execution = await queryEngine.ExecutePipelineAsync(id, "Manual", cancellationToken);

        return Ok(new OverkillRunResultDto(
            execution.Id,
            execution.ConfigId,
            execution.Status,
            execution.Status == "Success"
                ? $"Pipeline completed successfully in {execution.LatencyMs}ms."
                : (execution.ErrorMessage ?? "Pipeline execution failed."),
            execution.ProductsScanned,
            execution.DefectsExtracted,
            execution.OverkillCandidatesCount
        ));
    }

    [HttpPost("test-mapping")]
    [ProducesResponseType(typeof(TestPipelineMappingResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestMapping(
        [FromBody] TestPipelineMappingRequest request,
        CancellationToken cancellationToken)
    {
        var (isValidBypass, bypassError) = DefectBypassRuleValidator.Validate(request.BypassRule);
        if (!isValidBypass)
        {
            return BadRequest(new { error = bypassError });
        }

        var result = await queryEngine.TestMappingAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("executions")]
    [ProducesResponseType(typeof(IReadOnlyCollection<OverkillPipelineExecutionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExecutions(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var maxLimit = Math.Clamp(limit, 1, 200);
        var executions = await db.OverkillPipelineExecutions
            .AsNoTracking()
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(maxLimit)
            .ToListAsync(cancellationToken);

        return Ok(executions.Select(ToExecutionResponse).ToList());
    }

    [HttpGet("configs/{id:guid}/executions")]
    [ProducesResponseType(typeof(IReadOnlyCollection<OverkillPipelineExecutionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfigExecutions(
        Guid id,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var maxLimit = Math.Clamp(limit, 1, 200);
        var executions = await db.OverkillPipelineExecutions
            .AsNoTracking()
            .Where(x => x.ConfigId == id)
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(maxLimit)
            .ToListAsync(cancellationToken);

        return Ok(executions.Select(ToExecutionResponse).ToList());
    }

    private static OverkillPipelineConfigResponse ToResponse(OverkillPipelineConfigEntity entity, string connectionName)
    {
        var judgeFilters = ParseJudgeFilterValues(entity.JudgeFilterValuesJson);
        var defectTables = ParseDefectTables(entity.DefectTablesJson);
        var bypassRule = DefectBypassRuleEvaluator.ParseRule(entity.DefectBypassRuleJson);

        return new OverkillPipelineConfigResponse(
            entity.Id,
            entity.Name,
            entity.DatabaseConnectionId,
            connectionName,
            entity.MachineCode,
            entity.LineCode,
            entity.Enabled,
            entity.ScheduleIntervalMinutes,
            entity.LastRunAtUtc,
            entity.NextRunAtUtc,
            entity.LastRunStatus,
            entity.LastRunMessage,
            entity.LastRunProductsCount,
            entity.LastRunDefectsCount,
            entity.ProductTableName,
            entity.IdColumnName,
            entity.FkIdColumnName,
            entity.JudgeColumnName,
            judgeFilters,
            entity.TimestampColumnName,
            entity.LookbackHours,
            entity.BatchSize,
            defectTables,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            entity.ZoneColumnName,
            entity.DefectNameColumnName,
            entity.DateTimeInspectionColumnName,
            entity.DateTimeInspectionColumnName,
            entity.OffloadDefectsToSeaweedFs,
            bypassRule,
            entity.LastRunDefectsBypassedCount,
            entity.IncludeOkProducts,
            entity.ImageCrawlerApiUrl,
            entity.AutoCrawlImages,
            entity.ImageCrawlerVpMode ?? "0",
            entity.ModelCode ?? "UNASSIGNED"
        );
    }

    private static OverkillPipelineExecutionResponse ToExecutionResponse(OverkillPipelineExecutionEntity entity)
    {
        return new OverkillPipelineExecutionResponse(
            entity.Id,
            entity.ConfigId,
            entity.PipelineName,
            entity.TriggerType,
            entity.StartedAtUtc,
            entity.CompletedAtUtc,
            entity.Status,
            entity.ProductsScanned,
            entity.DefectsExtracted,
            entity.OverkillCandidatesCount,
            entity.LatencyMs,
            entity.ErrorMessage,
            entity.ExecutionSummaryJson,
            entity.DefectsBypassed,
            entity.BypassRuleSummary,
            entity.DefectsScanned
        );
    }

    private static List<string> ParseJudgeFilterValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return ["N", "NG"];
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            return parsed is { Count: > 0 } ? parsed : ["N", "NG"];
        }
        catch
        {
            return ["N", "NG"];
        }
    }

    private static List<DefectTableMapping> ParseDefectTables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<DefectTableMapping>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
