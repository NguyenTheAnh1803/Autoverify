using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/op-defect-mappings")]
public sealed class OpDefectMappingsController(AutoVerifyDbContext db, ILogger<OpDefectMappingsController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<OpDefectMappingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<OpDefectMappingDto>>> ListMappings(
        [FromQuery] string? search = null,
        [FromQuery] string? machineCode = null,
        [FromQuery] string? packageName = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.OpDefectMappings.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(machineCode) && !machineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(m => m.MachineCode == machineCode.Trim());
        if (!string.IsNullOrWhiteSpace(packageName) && !packageName.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(m => m.PackageName == packageName.Trim());

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(m =>
                m.OpDefectName.Contains(s) ||
                m.MachineDefectName.Contains(s) ||
                m.DefectFamily.Contains(s) ||
                m.ComponentGroup.Contains(s));
        }

        var items = await query
            .OrderBy(m => m.OpDefectName)
            .Select(m => new OpDefectMappingDto(
                m.Id,
                m.MachineCode,
                m.PackageName,
                m.OpDefectName,
                m.MachineDefectName,
                m.DefectFamily,
                m.ComponentGroup,
                m.IsConfirmedDefect,
                m.Description,
                m.CreatedAtUtc,
                m.UpdatedAtUtc
            ))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost]
    [ProducesResponseType(typeof(OpDefectMappingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OpDefectMappingDto>> CreateMapping(
        [FromBody] SaveOpDefectMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OpDefectName) || string.IsNullOrWhiteSpace(request.MachineDefectName))
        {
            return BadRequest(new { error = "Both OpDefectName and MachineDefectName are required." });
        }

        var existing = await db.OpDefectMappings
            .FirstOrDefaultAsync(m => m.OpDefectName == request.OpDefectName.Trim()
                && m.MachineCode == NormalizeMachine(request.MachineCode)
                && m.PackageName == NormalizePackage(request.PackageName), cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (existing != null)
        {
            existing.MachineDefectName = request.MachineDefectName.Trim();
            existing.DefectFamily = string.IsNullOrWhiteSpace(request.DefectFamily) ? "Optical" : request.DefectFamily.Trim();
            existing.ComponentGroup = request.ComponentGroup?.Trim() ?? string.Empty;
            existing.IsConfirmedDefect = request.IsConfirmedDefect;
            existing.Description = request.Description?.Trim();
            existing.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            return Ok(MapToDto(existing));
        }

        var entity = new OpDefectMappingEntity
        {
            Id = Guid.NewGuid(),
            OpDefectName = request.OpDefectName.Trim(),
            MachineCode = NormalizeMachine(request.MachineCode),
            PackageName = NormalizePackage(request.PackageName),
            MachineDefectName = request.MachineDefectName.Trim(),
            DefectFamily = string.IsNullOrWhiteSpace(request.DefectFamily) ? "Optical" : request.DefectFamily.Trim(),
            ComponentGroup = request.ComponentGroup?.Trim() ?? string.Empty,
            IsConfirmedDefect = request.IsConfirmedDefect,
            Description = request.Description?.Trim(),
            CreatedAtUtc = now
        };

        db.OpDefectMappings.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Created($"/api/v1/op-defect-mappings/{entity.Id}", MapToDto(entity));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(OpDefectMappingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OpDefectMappingDto>> UpdateMapping(
        Guid id,
        [FromBody] SaveOpDefectMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await db.OpDefectMappings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entity == null)
        {
            return NotFound(new { error = $"Mapping with ID '{id}' was not found." });
        }

        entity.OpDefectName = request.OpDefectName.Trim();
        entity.MachineCode = NormalizeMachine(request.MachineCode);
        entity.PackageName = NormalizePackage(request.PackageName);
        entity.MachineDefectName = request.MachineDefectName.Trim();
        entity.DefectFamily = string.IsNullOrWhiteSpace(request.DefectFamily) ? "Optical" : request.DefectFamily.Trim();
        entity.ComponentGroup = request.ComponentGroup?.Trim() ?? string.Empty;
        entity.IsConfirmedDefect = request.IsConfirmedDefect;
        entity.Description = request.Description?.Trim();
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(MapToDto(entity));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMapping(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await db.OpDefectMappings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entity == null)
        {
            return NotFound(new { error = $"Mapping with ID '{id}' was not found." });
        }

        db.OpDefectMappings.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("batch")]
    [ProducesResponseType(typeof(List<OpDefectMappingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<OpDefectMappingDto>>> BatchSaveMappings(
        [FromBody] BatchSaveOpDefectMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Mappings == null || request.Mappings.Count == 0)
        {
            return BadRequest(new { error = "No mappings supplied." });
        }

        var results = new List<OpDefectMappingDto>();
        var now = DateTimeOffset.UtcNow;

        foreach (var req in request.Mappings)
        {
            if (string.IsNullOrWhiteSpace(req.OpDefectName) || string.IsNullOrWhiteSpace(req.MachineDefectName))
                continue;

            var opName = req.OpDefectName.Trim();
            var machineDefect = req.MachineDefectName.Trim();
            var family = string.IsNullOrWhiteSpace(req.DefectFamily) ? "Optical" : req.DefectFamily.Trim();
            var machine = NormalizeMachine(req.MachineCode ?? request.MachineCode);
            var package = NormalizePackage(req.PackageName ?? request.PackageName);

            var existing = await db.OpDefectMappings
                .FirstOrDefaultAsync(m => m.OpDefectName == opName && m.MachineCode == machine && m.PackageName == package, cancellationToken);

            if (existing != null)
            {
                existing.MachineDefectName = machineDefect;
                existing.DefectFamily = family;
                existing.ComponentGroup = req.ComponentGroup?.Trim() ?? string.Empty;
                existing.IsConfirmedDefect = req.IsConfirmedDefect;
                if (!string.IsNullOrWhiteSpace(req.Description)) existing.Description = req.Description.Trim();
                existing.UpdatedAtUtc = now;
                results.Add(MapToDto(existing));
            }
            else
            {
                var newEntity = new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = opName,
                    MachineCode = machine,
                    PackageName = package,
                    MachineDefectName = machineDefect,
                    DefectFamily = family,
                    ComponentGroup = req.ComponentGroup?.Trim() ?? string.Empty,
                    IsConfirmedDefect = req.IsConfirmedDefect,
                    Description = req.Description?.Trim(),
                    CreatedAtUtc = now
                };
                db.OpDefectMappings.Add(newEntity);
                results.Add(MapToDto(newEntity));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Batch saved {Count} OP defect mappings", results.Count);
        return Ok(results);
    }

    [HttpPost("analyze-clsf")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ClsfPackageAnalysisResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AnalyzeClsfPackage(
        IFormFile file,
        [FromForm] string? machineCode = null,
        [FromForm] string? modelCode = null,
        [FromForm] string? packageName = null,
        [FromForm] bool saveRules = true,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "CLSF file is required." });

        using var reader = new StreamReader(file.OpenReadStream());
        var json = await reader.ReadToEndAsync(cancellationToken);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid CLSF JSON uploaded: {FileName}", file.FileName);
            return BadRequest(new { error = $"Invalid CLSF JSON: {ex.Message}" });
        }
        using (doc)
        {
        if (!doc.RootElement.TryGetProperty("ClassifierRecipe", out var recipe)
            || !recipe.TryGetProperty("Rules", out var rulesElement)
            || rulesElement.ValueKind != JsonValueKind.Array)
        {
            return BadRequest(new { error = "Invalid CLSF format: ClassifierRecipe.Rules was not found." });
        }

        var detectedModel = !string.IsNullOrWhiteSpace(modelCode) ? modelCode.Trim().ToUpperInvariant() : DetectModelFromFileName(file.FileName);
        var detectedMachine = !string.IsNullOrWhiteSpace(machineCode) ? machineCode.Trim().ToUpperInvariant() : DetectMachineFromFileName(file.FileName);
        var detectedPackage = !string.IsNullOrWhiteSpace(packageName) ? packageName.Trim() : Path.GetFileNameWithoutExtension(file.FileName);

        var productKeys = db.ProductRecords.AsNoTracking().AsQueryable();
        if (detectedMachine != "ALL") productKeys = productKeys.Where(p => p.MachineCode == detectedMachine);
        if (detectedModel != "UNASSIGNED") productKeys = productKeys.Where(p => p.ModelCode == detectedModel);
        var distinctKeys = await productKeys.Select(p => p.KeyId).Distinct().ToArrayAsync(cancellationToken);
        var defectNames = distinctKeys.Length == 0
            ? []
            : await db.DefectRecords.AsNoTracking()
                .Where(d => distinctKeys.Contains(d.KeyId))
                .Select(d => d.DefectName)
                .ToListAsync(cancellationToken);
        var defectCounts = defectNames
            .GroupBy(NormalizeRuleName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var totalOccurrences = defectCounts.Values.Sum();

        var results = new List<ClsfRuleOccurrenceDto>();
        var now = DateTimeOffset.UtcNow;
        foreach (var item in rulesElement.EnumerateArray())
        {
            var priority = item.TryGetProperty("Priority", out var priorityEl) ? priorityEl.GetInt32() : 0;
            var name = item.TryGetProperty("Name", out var nameEl) ? nameEl.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var defectTypeId = item.TryGetProperty("DefectTypeID", out var typeEl) ? typeEl.GetString()?.Trim() : null;
            var script = item.TryGetProperty("Script", out var scriptEl) ? scriptEl.GetString() : null;
            var normalizedRuleName = NormalizeRuleName(name);
            var count = defectCounts.TryGetValue(normalizedRuleName, out var exactCount) ? exactCount : 0;
            var percent = totalOccurrences == 0 ? 0 : Math.Round(count * 100d / totalOccurrences, 2);
            // CLSF recipes express VP scope either positively (== 1) or as an early-return guard (!= 1).
            var vpMatches = Regex.Matches(script ?? string.Empty, @"G010_VpID\s*(?:==|!=)\s*(\d+)")
                .Select(x => x.Groups[1].Value)
                .Distinct()
                .ToArray();

            results.Add(new ClsfRuleOccurrenceDto(priority, name, defectTypeId, count > 0, count, percent, vpMatches.Length > 0 ? string.Join(",", vpMatches) : null, script));

            if (saveRules)
            {
                var ruleCode = $"{detectedMachine}:{detectedModel}:{name}";
                var entity = await db.MachineClassifierRules.FirstOrDefaultAsync(r => r.RuleCode == ruleCode, cancellationToken);
                if (entity == null)
                {
                    entity = new MachineClassifierRuleEntity { Id = Guid.NewGuid(), RuleCode = ruleCode, CreatedAtUtc = now };
                    db.MachineClassifierRules.Add(entity);
                }
                entity.RuleName = name;
                entity.MachineCode = detectedMachine;
                entity.ProductKey = detectedModel;
                entity.DefectClass = name;
                entity.DefectTypeId = defectTypeId;
                entity.ScriptContent = script;
                entity.Priority = priority;
                entity.Version = recipe.TryGetProperty("Version", out var versionEl) && !string.IsNullOrWhiteSpace(versionEl.GetString()) ? versionEl.GetString()! : "1.0.0";
                entity.IsActive = true;
                entity.Description = $"CLSF package: {detectedPackage}";
                entity.UpdatedAtUtc = now;
            }
        }

        if (saveRules) await db.SaveChangesAsync(cancellationToken);

        return Ok(new ClsfPackageAnalysisResponse(
            true,
            file.FileName,
            detectedPackage,
            detectedMachine,
            detectedModel,
            results.Count,
            results.Count(x => x.IsOccurring),
            totalOccurrences,
            results.OrderByDescending(x => x.OccurrenceCount).ThenBy(x => x.Priority).ToArray(),
            $"Analyzed {results.Count} rules; {results.Count(x => x.IsOccurring)} currently have matching defects."
        ));
        }
    }

    private static string NormalizeMachine(string? value) => string.IsNullOrWhiteSpace(value) ? "ALL" : value.Trim().ToUpperInvariant();
    private static string NormalizePackage(string? value) => string.IsNullOrWhiteSpace(value) ? "Default" : value.Trim();
    private static string NormalizeRuleName(string value) => Regex.Replace(value.Trim().ToUpperInvariant(), @"[^A-Z0-9]", string.Empty);
    private static string DetectModelFromFileName(string fileName) => Regex.Match(fileName, @"(?<![A-Za-z0-9])([A-Za-z]\d{2,5})(?![A-Za-z0-9])").Groups[1].Value.ToUpperInvariant() is { Length: > 0 } model ? model : "UNASSIGNED";
    private static string DetectMachineFromFileName(string fileName)
    {
        var upper = fileName.ToUpperInvariant();
        if (upper.Contains("MCL")) return "MCL";
        if (upper.Contains("CP_AOI") || upper.Contains("CP-AOI")) return "CP_AOI";
        if (upper.Contains("SPI")) return "SPI";
        return "ALL";
    }

    private static OpDefectMappingDto MapToDto(OpDefectMappingEntity m) =>
        new(m.Id, m.MachineCode, m.PackageName, m.OpDefectName, m.MachineDefectName, m.DefectFamily, m.ComponentGroup, m.IsConfirmedDefect, m.Description, m.CreatedAtUtc, m.UpdatedAtUtc);
}
