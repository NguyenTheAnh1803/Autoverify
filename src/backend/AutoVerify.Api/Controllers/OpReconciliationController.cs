using System.Text.RegularExpressions;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/op-reconciliation")]
public sealed class OpReconciliationController(AutoVerifyDbContext db, ILogger<OpReconciliationController> logger) : ControllerBase
{
    /// <summary>
    /// Uploads an Excel (.xlsx) or CSV file containing OP verification data and/or OP defect mappings.
    /// </summary>
    [HttpPost("upload-excel")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadOpExcelResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadOpExcel(
        IFormFile? file,
        [FromForm] string? machineCode = null,
        [FromForm] string? lineCode = null,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file uploaded or file is empty." });
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".csv" && ext != ".tsv" && ext != ".txt")
        {
            return BadRequest(new { error = "Supported file formats are Excel (.xlsx) and CSV/TSV (.csv, .tsv, .txt)." });
        }

        var batchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var sheets = new List<ExcelSheetReader.ParsedWorksheet>();

        await using var stream = file.OpenReadStream();
        if (ext == ".xlsx")
        {
            sheets = ExcelSheetReader.ReadXlsx(stream);
        }
        else
        {
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            var text = await reader.ReadToEndAsync(cancellationToken);
            sheets.Add(ExcelSheetReader.ReadDelimitedText(text, "OP_Data"));
        }

        if (sheets.Count == 0)
        {
            return BadRequest(new { error = "No worksheets or readable rows found in the uploaded file." });
        }

        // 1. Process mapping sheet if present (e.g. named "Mapping", "QuyDoi", "Rules", "Defect_Mapping")
        int mappingRulesImported = 0;
        var mappingSheet = sheets.FirstOrDefault(s =>
            s.SheetName.Contains("Mapping", StringComparison.OrdinalIgnoreCase) ||
            s.SheetName.Contains("QuyDoi", StringComparison.OrdinalIgnoreCase) ||
            s.SheetName.Contains("Rule", StringComparison.OrdinalIgnoreCase));

        if (mappingSheet != null)
        {
            mappingRulesImported = await ImportMappingsFromSheetAsync(mappingSheet, cancellationToken);
        }

        // 2. Load all current mappings into memory for fast lookup
        var mappings = await db.OpDefectMappings.AsNoTracking().ToListAsync(cancellationToken);

        // 3. Process data sheet
        var dataSheet = sheets.FirstOrDefault(s => s != mappingSheet) ?? sheets[0];
        var recordsToSave = new List<OpInspectionRecordEntity>();
        var mappedCount = 0;
        var unmappedCount = 0;
        foreach (var row in dataSheet.Rows)
        {
            var keyId = ExtractColumnValue(row, "FOG ID", "FOG_ID", "FOGID", "FOG", "KeyId", "Key_ID", "Barcode", "Serial", "ID", "PanelId", "Panel_ID", "Product_ID", "ProductID");
            if (string.IsNullOrWhiteSpace(keyId) || IsHeaderToken(keyId) || IsShortNumericId(keyId)) continue;

            var rawJudge = ExtractColumnValue(row, "Phán định", "Phan dinh", "PHAN_DINH", "판정", "Judge", "OpJudge", "OP_Judge", "Result", "Status", "Kết quả", "Ket qua") ?? "OK";
            var opDefectName = ExtractColumnValue(row, "Tên lỗi", "TÊN LỖI", "TEN LOI", "TEN_LOI", "불량명(Vietnam)", "불량명(VN)", "불량명", "OpDefect", "OpDefectName", "OperatorDefect", "DefectName", "Defect", "Lỗi");
            var rowMachine = ExtractColumnValue(row, "Machine", "MachineCode") ?? machineCode;
            var rowLine = ExtractColumnValue(row, "Line", "LineCode") ?? lineCode;

            var map = FindMapping(mappings, rowMachine, opDefectName);

            string? mappedMachineDefect = null;
            string? mappedFamily = null;
            bool? isRealDefect = null;

            if (map != null)
            {
                mappedMachineDefect = map.MachineDefectName;
                mappedFamily = map.DefectFamily;
                isRealDefect = map.IsConfirmedDefect;
                mappedCount++;
            }
            else if (!string.IsNullOrWhiteSpace(opDefectName))
            {
                unmappedCount++;
                isRealDefect = NormalizeJudge(rawJudge) == "NG";
            }
            else
            {
                isRealDefect = NormalizeJudge(rawJudge) == "NG";
            }

            recordsToSave.Add(new OpInspectionRecordEntity
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                KeyId = keyId.Trim(),
                OpJudge = NormalizeJudge(rawJudge),
                OpDefectName = opDefectName?.Trim(),
                MappedMachineDefect = mappedMachineDefect,
                MappedDefectFamily = mappedFamily,
                IsRealDefectConfirmed = isRealDefect,
                LineCode = rowLine?.Trim(),
                MachineCode = rowMachine?.Trim(),
                InspectedAtUtc = now,
                CreatedAtUtc = now
            });
        }

        var upsert = await UpsertProductReviewsAsync(recordsToSave, batchId, now, machineCode, lineCode, cancellationToken);

        if (upsert.RecordsToCreate.Count > 0)
        {
            db.OpInspectionRecords.AddRange(upsert.RecordsToCreate);
        }
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Batch {BatchId}: Excel OP review upsert created {Created}, updated {Updated}, rejected {Rejected}",
            batchId, upsert.CreatedCount, upsert.UpdatedCount, upsert.RejectedCount);

        return Ok(new UploadOpExcelResponse(
            Success: true,
            BatchId: batchId,
            TotalRecordsParsed: dataSheet.Rows.Count,
            TotalRecordsSaved: upsert.CreatedCount + upsert.UpdatedCount,
            CreatedCount: upsert.CreatedCount,
            UpdatedCount: upsert.UpdatedCount,
            RejectedCount: upsert.RejectedCount,
            MappedCount: mappedCount,
            UnmappedCount: unmappedCount,
            MappingRulesImported: mappingRulesImported,
            SheetNames: sheets.Select(s => s.SheetName).ToList(),
            Message: $"Processed '{dataSheet.SheetName}': {upsert.CreatedCount} created, {upsert.UpdatedCount} updated, {upsert.RejectedCount} marked No Detect because Product was not found."
        ));
    }

    /// <summary>
    /// Parses and ingests OP inspection records pasted from clipboard or text (e.g. FOG ID | TÊN LỖI | Phán định).
    /// Supports Vietnamese, Korean, and English column headers with automatic multi-header skipping.
    /// </summary>
    [HttpPost("paste")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(UploadOpExcelResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PasteOpRecords(
        [FromBody] PasteOpRecordsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { error = "Pasted text cannot be empty." });
        }

        var lines = request.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return BadRequest(new { error = "No readable lines found in pasted text." });
        }

        var batchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var defaultMachine = string.IsNullOrWhiteSpace(request.MachineCode) ? "CP_AOI" : request.MachineCode.Trim();
        var targetLine = request.LineCode?.Trim();
        var defaultJudge = string.IsNullOrWhiteSpace(request.DefaultJudge) ? "NG" : request.DefaultJudge.Trim();

        var mappings = await db.OpDefectMappings.AsNoTracking().ToListAsync(cancellationToken);

        // Detect column positions from header row if present
        int keyColIdx = 0;
        int defectColIdx = 1;
        int judgeColIdx = 2;
        int lineColIdx = -1;
        int machineColIdx = -1;

        int startIndex = 0;

        // Inspect line 0 for headers
        var line0Parts = SplitDelimitedLine(lines[0]);
        if (IsHeaderLine(line0Parts))
        {
            DetectColumnIndices(line0Parts, ref keyColIdx, ref defectColIdx, ref judgeColIdx, ref lineColIdx, ref machineColIdx);
            startIndex = 1;

            // Check if line 1 is also a header (e.g., dual Vietnamese/Korean headers: row 0 = "FOG ID\tTÊN LỖI\tPhán định", row 1 = "FOG ID\t불량명(Vietnam)\t판정")
            if (lines.Length > 1)
            {
                var line1Parts = SplitDelimitedLine(lines[1]);
                if (IsHeaderLine(line1Parts) || IsHeaderToken(line1Parts.FirstOrDefault()))
                {
                    startIndex = 2;
                }
            }
        }

        var recordsToSave = new List<OpInspectionRecordEntity>();
        var mappedCount = 0;
        var unmappedCount = 0;
        for (int i = startIndex; i < lines.Length; i++)
        {
            var parts = SplitDelimitedLine(lines[i]);
            if (parts.Count == 0) continue;

            var keyId = parts.Count > keyColIdx ? parts[keyColIdx] : parts[0];
            if (string.IsNullOrWhiteSpace(keyId) || IsHeaderToken(keyId) || IsShortNumericId(keyId)) continue;

            var opDefectName = defectColIdx >= 0 && parts.Count > defectColIdx ? parts[defectColIdx] : (parts.Count > 1 ? parts[1] : null);
            var rawJudge = judgeColIdx >= 0 && parts.Count > judgeColIdx ? parts[judgeColIdx] : (parts.Count > 2 ? parts[2] : defaultJudge);
            var rowMachine = machineColIdx >= 0 && parts.Count > machineColIdx ? parts[machineColIdx] : defaultMachine;

            var map = FindMapping(mappings, rowMachine, opDefectName);

            string? mappedMachineDefect = null;
            string? mappedFamily = null;
            bool? isRealDefect = null;

            if (map != null)
            {
                mappedMachineDefect = map.MachineDefectName;
                mappedFamily = map.DefectFamily;
                isRealDefect = map.IsConfirmedDefect;
                mappedCount++;
            }
            else if (!string.IsNullOrWhiteSpace(opDefectName))
            {
                unmappedCount++;
                isRealDefect = NormalizeJudge(rawJudge) == "NG";
            }
            else
            {
                isRealDefect = NormalizeJudge(rawJudge) == "NG";
            }

            recordsToSave.Add(new OpInspectionRecordEntity
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                KeyId = keyId.Trim(),
                OpJudge = NormalizeJudge(rawJudge),
                OpDefectName = opDefectName?.Trim(),
                MappedMachineDefect = mappedMachineDefect,
                MappedDefectFamily = mappedFamily,
                IsRealDefectConfirmed = isRealDefect,
                LineCode = null,
                MachineCode = rowMachine?.Trim(),
                InspectedAtUtc = now,
                CreatedAtUtc = now
            });
        }

        if (recordsToSave.Count == 0)
        {
            return BadRequest(new { error = "No valid data rows found in pasted text." });
        }

        var upsert = await UpsertProductReviewsAsync(recordsToSave, batchId, now, defaultMachine, targetLine, cancellationToken);

        if (upsert.RecordsToCreate.Count > 0)
        {
            db.OpInspectionRecords.AddRange(upsert.RecordsToCreate);
        }
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Batch {BatchId}: Pasted OP review upsert created {Created}, updated {Updated}, rejected {Rejected}",
            batchId, upsert.CreatedCount, upsert.UpdatedCount, upsert.RejectedCount);

        return Ok(new UploadOpExcelResponse(
            Success: true,
            BatchId: batchId,
            TotalRecordsParsed: lines.Length - startIndex,
            TotalRecordsSaved: upsert.CreatedCount + upsert.UpdatedCount,
            CreatedCount: upsert.CreatedCount,
            UpdatedCount: upsert.UpdatedCount,
            RejectedCount: upsert.RejectedCount,
            MappedCount: mappedCount,
            UnmappedCount: unmappedCount,
            MappingRulesImported: 0,
            SheetNames: new[] { "Clipboard_Paste" },
            Message: $"OP review synchronized: {upsert.CreatedCount} created, {upsert.UpdatedCount} updated, {upsert.RejectedCount} marked No Detect because Product was not found."
        ));
    }

    /// <summary>
    /// Returns every OP record across all import batches in one searchable table, reconciled with the latest Product row by KeyId.
    /// BatchId remains available for traceability but never partitions the result set unless explicitly filtered.
    /// </summary>
    [HttpGet("records")]
    [ProducesResponseType(typeof(OpReconciliationRecordsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpReconciliationRecordsResponse>> GetReconciliationRecords(
        [FromQuery] string? search = null,
        [FromQuery] string? machineCode = null,
        [FromQuery] string? lineCode = null,
        [FromQuery] string? classification = null,
        [FromQuery] Guid? batchId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 500);

        var opQuery = db.OpInspectionRecords.AsNoTracking().AsQueryable();
        if (batchId.HasValue)
            opQuery = opQuery.Where(o => o.BatchId == batchId.Value);
        if (!string.IsNullOrWhiteSpace(machineCode) && !machineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
            opQuery = opQuery.Where(o => o.MachineCode == machineCode.Trim());
        if (!string.IsNullOrWhiteSpace(lineCode) && !lineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
            opQuery = opQuery.Where(o => o.LineCode == lineCode.Trim());
        var opRecords = await opQuery
            .OrderByDescending(o => o.InspectedAtUtc)
            .ThenByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var linkedProductIds = opRecords.Where(o => o.ProductRecordId.HasValue).Select(o => o.ProductRecordId!.Value).Distinct().ToList();
        var legacyKeyIds = opRecords.Where(o => !o.ProductRecordId.HasValue).Select(o => o.KeyId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var productRows = linkedProductIds.Count == 0 && legacyKeyIds.Count == 0
            ? []
            : await db.ProductRecords.AsNoTracking()
                .Where(p => linkedProductIds.Contains(p.Id) || legacyKeyIds.Contains(p.KeyId))
                .OrderByDescending(p => p.InspectedAtUtc)
                .ToListAsync(cancellationToken);

        var productById = productRows.ToDictionary(p => p.Id);
        var latestProductByKey = productRows
            .GroupBy(p => p.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matchedKeys = productRows.Select(p => p.KeyId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var defectRows = matchedKeys.Count == 0
            ? []
            : await db.DefectRecords.AsNoTracking().Where(d => matchedKeys.Contains(d.KeyId)).ToListAsync(cancellationToken);
        var defectsByKey = defectRows
            .GroupBy(d => d.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var reconciled = opRecords.Select(op =>
        {
            ProductRecordEntity? product = null;
            if (op.ProductRecordId.HasValue) productById.TryGetValue(op.ProductRecordId.Value, out product);
            if (product == null) latestProductByKey.TryGetValue(op.KeyId, out product);
            var productDefectName = product?.DefectName ?? product?.NameDefect;
            var productDefects = product != null && defectsByKey.TryGetValue(product.KeyId, out var rows)
                ? rows
                : [];
            var productDefectNames = productDefects.Count > 0
                ? productDefects.Select(d => (string?)d.DefectName)
                : new[] { productDefectName };
            var defectDtos = productDefects.Select(d => new OpReconciliationDefectDto(
                d.Id,
                d.DefectName,
                d.DefectType,
                d.DefectTable,
                d.Severity)).ToList();
            var result = product == null
                ? "No Detect"
                : ClassifyReconciliation(product.Judge, op.OpJudge, productDefectNames, op.MappedMachineDefect ?? op.OpDefectName);
            return new OpReconciliationRecordDto(
                op.Id,
                op.BatchId,
                op.KeyId,
                op.OpJudge,
                op.OpDefectName,
                op.MappedMachineDefect,
                op.MappedDefectFamily,
                op.MachineCode,
                op.LineCode,
                op.InspectedAtUtc,
                product != null,
                product?.Judge,
                product?.NameDefect,
                productDefectName,
                defectDtos,
                product?.MachineCode,
                product?.LineCode,
                product?.InspectedAtUtc,
                result);
        }).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            reconciled = reconciled.Where(r =>
                r.KeyId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (r.OpDefectName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.MappedMachineDefect?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.MappedDefectFamily?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.ProductNameDefect?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.ProductDefectName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                r.Defects.Any(d => d.DefectName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                   d.DefectType.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                   d.DefectTable.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(classification) && !classification.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            reconciled = reconciled
                .Where(r => r.Classification.Equals(classification.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var totalCount = reconciled.Count;
        var matchedCount = reconciled.Count(r => r.ProductFound);
        var notFoundCount = reconciled.Count(r => !r.ProductFound);
        var realDefectCount = reconciled.Count(r => r.Classification == "Real Defect");
        var overkillCount = reconciled.Count(r => r.Classification == "Overkill");
        var leakageCount = reconciled.Count(r => r.Classification == "Leakage");
        var consistentOkCount = reconciled.Count(r => r.Classification == "Consistent OK");
        var noDetectCount = reconciled.Count(r => r.Classification == "No Detect");
        var machineNgCompared = realDefectCount + overkillCount;
        var realDefectRate = machineNgCompared > 0 ? Math.Round((double)realDefectCount / machineNgCompared, 4) : 0;
        var overkillRate = machineNgCompared > 0 ? Math.Round((double)overkillCount / machineNgCompared, 4) : 0;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);
        var items = reconciled.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new OpReconciliationRecordsResponse(
            items,
            page,
            pageSize,
            totalCount,
            totalPages,
            matchedCount,
            notFoundCount,
            realDefectCount,
            overkillCount,
            leakageCount,
            consistentOkCount,
            noDetectCount,
            realDefectRate,
            overkillRate));
    }

    /// <summary>
    /// Reconciles Machine inspection vs OP manual review to analyze Overkill vs Real Defect and perform deep-dive feature analysis.
    /// </summary>
    [HttpGet("analysis")]
    [ProducesResponseType(typeof(OpReconciliationAnalysisResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpReconciliationAnalysisResponse>> GetReconciliationAnalysis(
        [FromQuery] string? machineCode = null,
        [FromQuery] string? lineCode = null,
        [FromQuery] string? targetDefect = null,
        [FromQuery] Guid? batchId = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Query every OP record across batches, then reconcile only against real Product rows.
        var opQuery = db.OpInspectionRecords.AsNoTracking().AsQueryable();
        if (batchId.HasValue)
            opQuery = opQuery.Where(o => o.BatchId == batchId.Value);
        if (!string.IsNullOrWhiteSpace(lineCode) && !lineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
            opQuery = opQuery.Where(o => o.LineCode == lineCode.Trim());
        if (!string.IsNullOrWhiteSpace(machineCode) && !machineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
            opQuery = opQuery.Where(o => o.MachineCode == machineCode.Trim());

        var opRecords = await opQuery.OrderByDescending(o => o.InspectedAtUtc).Take(20000).ToListAsync(cancellationToken);
        var opByProductId = opRecords
            .Where(o => o.ProductRecordId.HasValue)
            .GroupBy(o => o.ProductRecordId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var legacyOpByKey = opRecords
            .Where(o => !o.ProductRecordId.HasValue)
            .GroupBy(o => o.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var linkedIds = opByProductId.Keys.ToList();
        var legacyKeys = legacyOpByKey.Keys.ToList();
        var prodQuery = db.ProductRecords.AsNoTracking().Where(p => linkedIds.Contains(p.Id) || legacyKeys.Contains(p.KeyId));
        if (!string.IsNullOrWhiteSpace(machineCode) && !machineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
            prodQuery = prodQuery.Where(p => p.MachineCode == machineCode.Trim());
        if (!string.IsNullOrWhiteSpace(lineCode) && !lineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
            prodQuery = prodQuery.Where(p => p.LineCode == lineCode.Trim());
        if (fromUtc.HasValue)
            prodQuery = prodQuery.Where(p => p.InspectedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            prodQuery = prodQuery.Where(p => p.InspectedAtUtc <= toUtc.Value);

        var productRows = await prodQuery.OrderByDescending(p => p.InspectedAtUtc).ToListAsync(cancellationToken);
        var products = productRows
            .GroupBy(p => p.KeyId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        var productKeys = products.Select(p => p.KeyId).ToList();

        // 2. Query Machine Defects for matched products.
        var defects = productKeys.Count == 0
            ? []
            : await db.DefectRecords.AsNoTracking().Where(d => productKeys.Contains(d.KeyId)).ToListAsync(cancellationToken);

        var defectsByKey = defects
            .GroupBy(d => d.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // 3. Reconcile Machine vs OP per Product. Products without OP data never enter the denominator.
        var totalReconciled = 0;
        var totalMachineNg = 0;
        var realDefectCount = 0;
        var overkillCount = 0;
        var leakageCount = 0;
        var consistentOkCount = 0;
        var noDetectCount = 0;

        // Group metrics by Machine Defect Name
        var defectBuckets = new Dictionary<string, (int TotalNg, int RealDefect, int Overkill, HashSet<string> OpNames, string Family)>(StringComparer.OrdinalIgnoreCase);

        // Track items for deep dive
        var overkillSamples = new List<OverkillSampleDto>();
        var realDefectSamples = new List<(string DefectName, Dictionary<string, double> Features)>();
        var overkillFeatureData = new List<(string DefectName, Dictionary<string, double> Features)>();

        foreach (var prod in products)
        {
            var isMachineNg = prod.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase) ||
                              prod.Judge.Equals("N", StringComparison.OrdinalIgnoreCase);

            var hasOpRecord = opByProductId.TryGetValue(prod.Id, out var opRec) || legacyOpByKey.TryGetValue(prod.KeyId, out opRec);
            defectsByKey.TryGetValue(prod.KeyId, out var prodDefects);
            var dominantDefectName = prodDefects?.FirstOrDefault()?.DefectName ?? prod.DefectName ?? "GeneralDefect";
            var dominantFamily = prodDefects?.FirstOrDefault()?.DefectType ?? "Optical";
            var features = prodDefects?.FirstOrDefault() != null ? AnomalyClassifierEngine.ExtractAllFeatures(prodDefects[0]) : [];
            var opDefectForComparison = opRec?.MappedMachineDefect ?? opRec?.OpDefectName;
            var productDefectNames = prodDefects is { Count: > 0 }
                ? prodDefects.Select(d => (string?)d.DefectName)
                : new[] { prod.DefectName ?? prod.NameDefect };
            var classification = hasOpRecord
                ? ClassifyReconciliation(prod.Judge, opRec!.OpJudge, productDefectNames, opDefectForComparison)
                : "Not Found";

            if (classification == "No Detect")
            {
                noDetectCount++;
                continue;
            }

            if (isMachineNg)
            {
                totalMachineNg++;
                totalReconciled++;

                if (!defectBuckets.TryGetValue(dominantDefectName, out var bucket))
                {
                    bucket = (0, 0, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase), dominantFamily);
                }
                bucket.TotalNg++;
                if (hasOpRecord && !string.IsNullOrWhiteSpace(opRec!.OpDefectName))
                {
                    bucket.OpNames.Add(opRec.OpDefectName);
                }

                if (classification == "Overkill")
                {
                    // Confirmed Overkill: Machine judged NG but OP confirmed OK / Clean
                    overkillCount++;
                    bucket.Overkill++;

                    overkillFeatureData.Add((dominantDefectName, features));

                    var thumbUrl = prodDefects?.FirstOrDefault() != null
                        ? $"/api/v1/storage/defect-images/{prodDefects[0].Id}"
                        : null;

                    var opNameStr = opRec?.OpDefectName ?? "None";
                    overkillSamples.Add(new OverkillSampleDto(
                        prod.KeyId,
                        dominantDefectName,
                        dominantFamily,
                        opRec?.OpDefectName ?? "No Defect",
                        opRec?.OpJudge ?? "OK",
                        prod.InspectedAtUtc,
                        prod.LineCode,
                        prod.MachineCode,
                        features,
                        thumbUrl,
                        $"Machine judged NG ({dominantDefectName}) but OP judged OK (OP Defect: {opNameStr})."
                    ));
                }
                else if (classification == "Real Defect")
                {
                    // Confirmed Real Defect: Both Machine and OP confirmed defect
                    realDefectCount++;
                    bucket.RealDefect++;
                    realDefectSamples.Add((dominantDefectName, features));
                }
                defectBuckets[dominantDefectName] = bucket;
            }
            else
            {
                // Machine judged OK
                totalReconciled++;
                if (classification == "Leakage")
                {
                    leakageCount++;
                }
                else
                {
                    consistentOkCount++;
                }
            }
        }

        var realDefectRate = totalMachineNg > 0 ? Math.Round((double)realDefectCount / totalMachineNg, 4) : 0.0;
        var overkillRate = totalMachineNg > 0 ? Math.Round((double)overkillCount / totalMachineNg, 4) : 0.0;
        var leakageRate = totalReconciled > 0 ? Math.Round((double)leakageCount / totalReconciled, 4) : 0.0;

        var summary = new OpReconciliationSummaryDto(
            totalReconciled,
            totalMachineNg,
            realDefectCount,
            overkillCount,
            leakageCount,
            consistentOkCount,
            noDetectCount,
            realDefectRate,
            overkillRate,
            leakageRate
        );

        // Build Defect Breakdown list
        var defectBreakdown = defectBuckets.Select(kv =>
        {
            var name = kv.Key;
            var (tNg, rDef, oKill, opNames, fam) = kv.Value;
            var oRate = tNg > 0 ? Math.Round((double)oKill / tNg, 4) : 0.0;
            var rRate = tNg > 0 ? Math.Round((double)rDef / tNg, 4) : 0.0;

            string assessment = oRate >= 0.5
                ? $"Severe Overkill ({oRate:P0}): OP confirmed clean/acceptable for over half of detections. Raising defect threshold is recommended."
                : (oRate >= 0.2 ? $"Significant Overkill ({oRate:P0}): Minor threshold tuning recommended to reduce OP re-inspection load." : $"Dominant Real Defect ({rRate:P0}): Highly accurate machine detection.");

            return new OpDefectReconciliationItemDto(
                name,
                fam,
                tNg,
                rDef,
                oKill,
                rRate,
                oRate,
                opNames.ToList(),
                assessment
            );
        })
        .OrderByDescending(d => d.OverkillCount)
        .ThenByDescending(d => d.TotalMachineNg)
        .ToList();

        // 6. Deep-Dive into Overkill
        var chosenDefect = targetDefect;
        if (string.IsNullOrWhiteSpace(chosenDefect) || chosenDefect.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            chosenDefect = defectBreakdown.OrderByDescending(d => d.OverkillCount).FirstOrDefault()?.MachineDefectName ?? "DIC_Crack";
        }

        var deepDiveOverkillFeatures = overkillFeatureData
            .Where(x => string.Equals(x.DefectName, chosenDefect, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Features)
            .ToList();

        var deepDiveRealFeatures = realDefectSamples
            .Where(x => string.Equals(x.DefectName, chosenDefect, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Features)
            .ToList();

        var featureDistributions = new List<OverkillFeatureDistributionDto>();
        var tuningRecommendations = new List<string>();

        // Collect all numeric feature keys
        var allFeatureKeys = deepDiveOverkillFeatures
            .Concat(deepDiveRealFeatures)
            .SelectMany(f => f.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var fKey in allFeatureKeys)
        {
            var oVals = deepDiveOverkillFeatures.Where(f => f.ContainsKey(fKey)).Select(f => f[fKey]).ToList();
            var rVals = deepDiveRealFeatures.Where(f => f.ContainsKey(fKey)).Select(f => f[fKey]).ToList();

            if (oVals.Count == 0 || rVals.Count == 0) continue;

            var oMean = Math.Round(oVals.Average(), 2);
            var oMin = Math.Round(oVals.Min(), 2);
            var oMax = Math.Round(oVals.Max(), 2);

            var rMean = Math.Round(rVals.Average(), 2);
            var rMin = Math.Round(rVals.Min(), 2);
            var rMax = Math.Round(rVals.Max(), 2);

            string separation;
            if (oMax < rMin)
            {
                separation = $"Completely Separated: Overkill max ({oMax}) < Real Defect min ({rMin}). Setting threshold > {oMax:F1} eliminates 100% Overkill without defect leakage!";
                tuningRecommendations.Add($"Gold threshold recommendation for [{fKey}]: Set threshold > {Math.Round((oMax + rMin) / 2.0, 1)} (Current Overkill max={oMax}, Real min={rMin}).");
            }
            else if (oMean < rMean && Math.Abs(rMean - oMean) > (oMax - oMin) * 0.5)
            {
                separation = $"Significant shift: Overkill mean ({oMean}) is noticeably lower than Real Defect mean ({rMean}). Increasing threshold will suppress majority of false alarms.";
                tuningRecommendations.Add($"Review threshold for [{fKey}]: Consider increasing threshold closer to {rMean:F1} to reduce false alarms.");
            }
            else
            {
                separation = $"Overlapped: Overkill ({oMean:F1}) and Real Defect ({rMean:F1}) values are intertwined. Avoid using as a solitary filter.";
            }

            featureDistributions.Add(new OverkillFeatureDistributionDto(
                fKey,
                oMean,
                oMin,
                oMax,
                rMean,
                rMin,
                rMax,
                separation
            ));
        }

        if (tuningRecommendations.Count == 0 && deepDiveOverkillFeatures.Count > 0)
        {
            tuningRecommendations.Add($"For defect [{chosenDefect}], Overkill feature values overlap with Real Defect. Combining deep learning (MLClass) or lighting conditions is recommended.");
        }

        var deepDive = new OverkillDeepDiveAnalysisDto(
            chosenDefect,
            deepDiveOverkillFeatures.Count,
            deepDiveRealFeatures.Count,
            (deepDiveOverkillFeatures.Count + deepDiveRealFeatures.Count) > 0
                ? Math.Round((double)deepDiveOverkillFeatures.Count / (deepDiveOverkillFeatures.Count + deepDiveRealFeatures.Count), 4)
                : 0.0,
            featureDistributions.OrderByDescending(f => f.SeparationAssessment.Contains("Completely Separated")).Take(8).ToList(),
            tuningRecommendations,
            overkillSamples.Where(s => string.Equals(s.MachineDefectName, chosenDefect, StringComparison.OrdinalIgnoreCase)).Take(24).ToList()
        );

        var recommendations = new List<string>();
        if (overkillRate >= 0.4)
        {
            recommendations.Add($"Overall factory Overkill rate is {overkillRate:P1}: {overkillCount} units flagged NG by machine were confirmed OK by OP. Factory capacity is impacted by manual re-inspection.");
        }
        if (defectBreakdown.Count > 0 && defectBreakdown[0].OverkillRate >= 0.4)
        {
            recommendations.Add($"Top Overkill Hotspot: Defect [{defectBreakdown[0].MachineDefectName}] has the highest false alarm count ({defectBreakdown[0].OverkillCount} cases, {defectBreakdown[0].OverkillRate:P0}). Prioritize tuning this recipe.");
        }
        if (leakageCount > 0)
        {
            recommendations.Add($"Defect Escape Alert: {leakageCount} units passed by machine were detected with defects by OP! Inspect optical blind spots.");
        }
        if (recommendations.Count == 0)
        {
            recommendations.Add("Reconciliation balanced: Low overkill rate and machine defect detection aligns well with OP evaluations.");
        }

        return Ok(new OpReconciliationAnalysisResponse(
            summary,
            defectBreakdown,
            deepDive,
            defectBuckets.Keys.ToList(),
            recommendations
        ));
    }

    /// <summary>
    /// Seeds realistic sample OP inspection records to allow instant preview and verification.
    /// </summary>
    [HttpPost("seed-sample-data")]
    public async Task<IActionResult> SeedSampleOpData(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var batchId = Guid.NewGuid();

        var sampleOps = new List<OpInspectionRecordEntity>
        {
            new() { Id = Guid.NewGuid(), BatchId = batchId, KeyId = "CUS-1002", OpJudge = "NG", OpDefectName = "Corner Crack", MappedMachineDefect = "DIC_Crack", MappedDefectFamily = "Optical", IsRealDefectConfirmed = true, LineCode = "LINE-1", MachineCode = "MACHINE-A", InspectedAtUtc = now.AddMinutes(-30), CreatedAtUtc = now },
            new() { Id = Guid.NewGuid(), BatchId = batchId, KeyId = "CUS-1003", OpJudge = "OK", OpDefectName = "Dust Particle", MappedMachineDefect = "NONE", MappedDefectFamily = "Optical", IsRealDefectConfirmed = false, LineCode = "LINE-1", MachineCode = "MACHINE-A", InspectedAtUtc = now.AddMinutes(-20), CreatedAtUtc = now },
            new() { Id = Guid.NewGuid(), BatchId = batchId, KeyId = "CUS-1005", OpJudge = "NG", OpDefectName = "Solder Bridge", MappedMachineDefect = "Bridging", MappedDefectFamily = "Solder", IsRealDefectConfirmed = true, LineCode = "LINE-2", MachineCode = "MACHINE-B", InspectedAtUtc = now.AddMinutes(-5), CreatedAtUtc = now },
        };

        db.OpInspectionRecords.AddRange(sampleOps);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"Seeded {sampleOps.Count} sample OP inspection records with BatchId {batchId}." });
    }

    private async Task<int> ImportMappingsFromSheetAsync(ExcelSheetReader.ParsedWorksheet sheet, CancellationToken ct)
    {
        int count = 0;
        var now = DateTimeOffset.UtcNow;
        // Looking for columns like OpDefect, MachineDefect, DefectFamily, IsRealDefect
        foreach (var row in sheet.Rows)
        {
            var opName = ExtractColumnValue(row, "OpDefect", "OpDefectName", "OperatorDefect", "OP", "Op_Defect");
            var machineDefect = ExtractColumnValue(row, "MachineDefect", "MachineDefectName", "AoiDefect", "Machine", "Machine_Defect");
            if (string.IsNullOrWhiteSpace(opName) || string.IsNullOrWhiteSpace(machineDefect)) continue;

            var family = ExtractColumnValue(row, "Family", "DefectFamily", "Category") ?? "Optical";
            var realStr = ExtractColumnValue(row, "IsReal", "IsRealDefect", "IsConfirmed", "RealDefect") ?? "true";
            var isReal = !realStr.Equals("false", StringComparison.OrdinalIgnoreCase) && !realStr.Equals("0") && !realStr.Equals("Overkill", StringComparison.OrdinalIgnoreCase);

            var existing = await db.OpDefectMappings.FirstOrDefaultAsync(m => m.OpDefectName == opName.Trim(), ct);
            if (existing != null)
            {
                existing.MachineDefectName = machineDefect.Trim();
                existing.DefectFamily = family.Trim();
                existing.IsConfirmedDefect = isReal;
                existing.UpdatedAtUtc = now;
            }
            else
            {
                db.OpDefectMappings.Add(new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = opName.Trim(),
                    MachineDefectName = machineDefect.Trim(),
                    DefectFamily = family.Trim(),
                    IsConfirmedDefect = isReal,
                    CreatedAtUtc = now
                });
            }
            count++;
        }

        if (count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return count;
    }

    private sealed record OpReviewUpsertResult(
        List<OpInspectionRecordEntity> RecordsToCreate,
        int CreatedCount,
        int UpdatedCount,
        int RejectedCount);

    private async Task<OpReviewUpsertResult> UpsertProductReviewsAsync(
        IReadOnlyCollection<OpInspectionRecordEntity> records,
        Guid batchId,
        DateTimeOffset now,
        string? targetMachineCode,
        string? targetLineCode,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0) return new([], 0, 0, 0);

        var keyIds = records.Select(r => r.KeyId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var products = await db.ProductRecords
            .Where(p => keyIds.Contains(p.KeyId))
            .OrderByDescending(p => p.InspectedAtUtc)
            .ToListAsync(cancellationToken);

        var productsByKey = products
            .GroupBy(p => p.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Within one import, the last row for the same Product wins.
        var selectedByProduct = new Dictionary<Guid, (OpInspectionRecordEntity Record, ProductRecordEntity Product)>();
        var unmatchedRecords = new List<OpInspectionRecordEntity>();
        var rejectedCount = 0;
        foreach (var record in records)
        {
            if (!productsByKey.TryGetValue(record.KeyId, out var candidates))
            {
                unmatchedRecords.Add(record);
                rejectedCount++;
                continue;
            }

            var effectiveMachine = !string.IsNullOrWhiteSpace(targetMachineCode) ? targetMachineCode : record.MachineCode;
            var matchedProduct = candidates.FirstOrDefault(p =>
                (string.IsNullOrWhiteSpace(effectiveMachine) || p.MachineCode.Equals(effectiveMachine, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(targetLineCode) || p.LineCode.Equals(targetLineCode, StringComparison.OrdinalIgnoreCase)));

            if (matchedProduct == null)
            {
                unmatchedRecords.Add(record);
                rejectedCount++;
                continue;
            }

            selectedByProduct[matchedProduct.Id] = (record, matchedProduct);
        }

        var productIds = selectedByProduct.Keys.ToList();
        var selectedKeys = selectedByProduct.Values.Select(x => x.Product.KeyId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var productDefects = selectedKeys.Count == 0
            ? []
            : await db.DefectRecords.Where(d => selectedKeys.Contains(d.KeyId)).ToListAsync(cancellationToken);
        var defectsByProductKey = productDefects
            .GroupBy(d => d.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<DefectRecordEntity>)g.ToList(), StringComparer.OrdinalIgnoreCase);
        var existingReviews = productIds.Count == 0
            ? []
            : await db.OpInspectionRecords
                .Where(o => o.ProductRecordId.HasValue && productIds.Contains(o.ProductRecordId.Value))
                .ToListAsync(cancellationToken);
        var reviewByProduct = existingReviews
            .Where(o => o.ProductRecordId.HasValue)
            .GroupBy(o => o.ProductRecordId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.InspectedAtUtc).First());

        // Adopt the newest legacy review that predates ProductRecordId linkage.
        var legacyReviews = await db.OpInspectionRecords
            .Where(o => !o.ProductRecordId.HasValue && keyIds.Contains(o.KeyId))
            .OrderByDescending(o => o.InspectedAtUtc)
            .ToListAsync(cancellationToken);
        var legacyReviewByKey = legacyReviews
            .GroupBy(o => o.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var recordsToCreate = new List<OpInspectionRecordEntity>();
        var createdCount = 0;
        var updatedCount = 0;

        foreach (var (productId, selected) in selectedByProduct)
        {
            var incoming = selected.Record;
            var product = selected.Product;
            defectsByProductKey.TryGetValue(product.KeyId, out var defectsForProduct);
            var classification = ClassifyReconciliation(
                product.Judge,
                incoming.OpJudge,
                GetProductDefectNames(product, defectsForProduct),
                incoming.MappedMachineDefect ?? incoming.OpDefectName);

            product.OpReviewMark = classification is "Real Defect" or "Overkill" ? classification : null;
            product.OpReviewJudge = incoming.OpJudge;
            product.OpReviewDefectName = incoming.OpDefectName;
            product.OpReviewMappedDefect = incoming.MappedMachineDefect;
            product.OpReviewedAtUtc = now;
            var machineDefects = string.Join(", ", GetProductDefectNames(product, defectsForProduct)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            product.OpReviewNote = $"OP comparison only. Product defect remains unchanged. Machine=[{machineDefects}], OP=[{incoming.OpDefectName ?? string.Empty}], OP mapped=[{incoming.MappedMachineDefect ?? string.Empty}], Result={classification}.";

            if (!reviewByProduct.TryGetValue(productId, out var existing))
            {
                existing = legacyReviews.FirstOrDefault(o =>
                    o.KeyId.Equals(product.KeyId, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(o.MachineCode) || o.MachineCode.Equals(product.MachineCode, StringComparison.OrdinalIgnoreCase)));
                if (existing != null)
                {
                    existing.ProductRecordId = product.Id;
                    existing.ProductRecord = product;
                    reviewByProduct[productId] = existing;
                }
            }

            if (existing != null)
            {
                existing.BatchId = batchId;
                existing.KeyId = product.KeyId;
                existing.OpJudge = incoming.OpJudge;
                existing.OpDefectName = incoming.OpDefectName;
                existing.MappedMachineDefect = incoming.MappedMachineDefect;
                existing.MappedDefectFamily = incoming.MappedDefectFamily;
                existing.IsRealDefectConfirmed = incoming.IsRealDefectConfirmed;
                existing.LineCode = product.LineCode;
                existing.MachineCode = product.MachineCode;
                existing.InspectedAtUtc = now;
                updatedCount++;
            }
            else
            {
                incoming.ProductRecordId = product.Id;
                incoming.ProductRecord = product;
                incoming.KeyId = product.KeyId;
                incoming.LineCode = product.LineCode;
                incoming.MachineCode = product.MachineCode;
                incoming.BatchId = batchId;
                incoming.InspectedAtUtc = now;
                recordsToCreate.Add(incoming);
                createdCount++;
            }
        }

        foreach (var incoming in unmatchedRecords)
        {
            incoming.ProductRecordId = null;
            incoming.ProductRecord = null;
            incoming.Notes = "No Detect";
            if (legacyReviewByKey.TryGetValue(incoming.KeyId, out var existing))
            {
                existing.BatchId = batchId;
                existing.KeyId = incoming.KeyId;
                existing.OpJudge = incoming.OpJudge;
                existing.OpDefectName = incoming.OpDefectName;
                existing.MappedMachineDefect = incoming.MappedMachineDefect;
                existing.MappedDefectFamily = incoming.MappedDefectFamily;
                existing.IsRealDefectConfirmed = incoming.IsRealDefectConfirmed;
                existing.MachineCode = incoming.MachineCode;
                existing.LineCode = incoming.LineCode;
                existing.Notes = "No Detect";
                existing.InspectedAtUtc = now;
                updatedCount++;
            }
            else
            {
                recordsToCreate.Add(incoming);
                createdCount++;
            }
        }

        return new(recordsToCreate, createdCount, updatedCount, rejectedCount);
    }

    private static string? ExtractColumnValue(Dictionary<string, string> row, params string[] candidates)
    {
        foreach (var cand in candidates)
        {
            var match = row.FirstOrDefault(kv => kv.Key.Trim().Equals(cand, StringComparison.OrdinalIgnoreCase) ||
                                                 kv.Key.Trim().Replace("_", "").Equals(cand.Replace("_", ""), StringComparison.OrdinalIgnoreCase) ||
                                                 kv.Key.Trim().Replace(" ", "").Equals(cand.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value)) return match.Value.Trim();
        }
        return null;
    }

    private static string NormalizeJudge(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "OK";
        var u = raw.Trim().ToUpperInvariant();
        if (u == "OK" || u == "PASS" || u == "G" || u == "GOOD" || u == "양호" || u == "정상" || u == "ĐẠT" || u == "DAT") return "OK";
        return "NG";
    }

    private static string ClassifyReconciliation(
        string? productJudge,
        string opJudge,
        IEnumerable<string?>? productDefectNames = null,
        string? opDefectName = null)
    {
        if (string.IsNullOrWhiteSpace(productJudge)) return "Not Found";
        var machineDefects = productDefectNames?.Where(value => !string.IsNullOrWhiteSpace(value)).ToList() ?? [];
        if (IsNoDetectMarker(opDefectName) || (machineDefects.Count > 0 && machineDefects.All(IsNoDetectMarker))) return "No Detect";

        var machineIsNg = NormalizeJudge(productJudge) == "NG";
        var opIsNg = NormalizeJudge(opJudge) == "NG";

        if (machineIsNg && opIsNg)
        {
            return DefectsMatch(machineDefects, opDefectName) ? "Real Defect" : "Overkill";
        }
        if (machineIsNg && !opIsNg) return "Overkill";
        if (!machineIsNg && opIsNg) return "Leakage";
        return "Consistent OK";
    }

    private static bool DefectsMatch(IEnumerable<string?>? productDefectNames, string? opDefectName)
    {
        var op = NormalizeDefectName(opDefectName);
        if (op.Length == 0 || productDefectNames == null) return false;

        foreach (var productName in productDefectNames)
        {
            var product = NormalizeDefectName(productName);
            if (product.Length == 0) continue;
            if (product == op) return true;
            if (product.Length >= 4 && op.Length >= 4 &&
                (product.Contains(op, StringComparison.Ordinal) || op.Contains(product, StringComparison.Ordinal))) return true;
        }
        return false;
    }

    private static IEnumerable<string?> GetProductDefectNames(ProductRecordEntity product, IReadOnlyCollection<DefectRecordEntity>? defects)
    {
        return defects is { Count: > 0 }
            ? defects.Select(d => (string?)d.DefectName)
            : new[] { product.DefectName ?? product.NameDefect };
    }

    private static string NormalizeDefectName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Trim().ToUpperInvariant();
        normalized = Regex.Replace(normalized, @"[^\p{L}\p{N}]+", string.Empty);
        return normalized is "NONE" or "NODEFECT" or "OK" or "GOOD" ? string.Empty : normalized;
    }

    private static bool IsNoDetectMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = Regex.Replace(value.Trim().ToUpperInvariant(), @"[^\p{L}\p{N}]+", string.Empty);
        return normalized is "NA" or "NANG" or "NONG" or "NODETECT" or "NOTDETECTED" or "NOTAPPLICABLE" or "UNDETECTED";
    }

    private static bool IsShortNumericId(string? keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId)) return false;
        var trimmed = keyId.Trim();
        return trimmed.Length > 0 && trimmed.All(char.IsDigit) && long.TryParse(trimmed, out var numeric) && numeric < 10;
    }

    private static bool IsHeaderToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var u = token.Trim().ToUpperInvariant();
        return u == "FOG ID" || u == "FOG_ID" || u == "FOGID" || u == "KEYID" || u == "KEY_ID" ||
               u == "BARCODE" || u == "SERIAL" || u == "PANELID" || u == "PANEL_ID" ||
               u == "PRODUCT_ID" || u == "PRODUCTID" || u == "ID" || u == "STT" || u == "NO" ||
               u.Contains("TÊN LỖI") || u.Contains("TEN LOI") || u.Contains("불량명") ||
               u.Contains("PHÁN ĐỊNH") || u.Contains("PHAN DINH") || u.Contains("판정");
    }

    private static bool IsHeaderLine(List<string> parts)
    {
        if (parts.Count == 0) return false;
        var joined = string.Join(" ", parts).ToUpperInvariant();
        return parts.Any(IsHeaderToken) || joined.Contains("TÊN LỖI") || joined.Contains("TEN LOI") ||
               joined.Contains("불량명") || joined.Contains("PHÁN ĐỊNH") || joined.Contains("PHAN DINH") ||
               joined.Contains("판정") || joined.Contains("JUDGE") || joined.Contains("BARCODE") ||
               joined.Contains("KEYID") || joined.Contains("KEY_ID");
    }

    private static void DetectColumnIndices(
        List<string> parts,
        ref int keyCol,
        ref int defectCol,
        ref int judgeCol,
        ref int lineCol,
        ref int machineCol)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i].Trim().ToUpperInvariant();
            if (p.Contains("FOG") || p.Contains("KEY") || p.Contains("BARCODE") || p.Contains("SERIAL") || p.Contains("PANEL") || p == "ID")
            {
                keyCol = i;
            }
            else if (p.Contains("TÊN LỖI") || p.Contains("TEN LOI") || p.Contains("불량명") || p.Contains("DEFECT") || p.Contains("LỖI"))
            {
                defectCol = i;
            }
            else if (p.Contains("PHÁN ĐỊNH") || p.Contains("PHAN DINH") || p.Contains("판정") || p.Contains("JUDGE") || p.Contains("RESULT") || p.Contains("STATUS"))
            {
                judgeCol = i;
            }
            else if (p.Contains("LINE"))
            {
                lineCol = i;
            }
            else if (p.Contains("MACHINE"))
            {
                machineCol = i;
            }
        }
    }

    private static List<string> SplitDelimitedLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        if (line.Contains('\t'))
        {
            return line.Split('\t').Select(x => x.Trim()).ToList();
        }
        if (line.Contains(','))
        {
            return line.Split(',').Select(x => x.Trim()).ToList();
        }
        if (line.Contains(';'))
        {
            return line.Split(';').Select(x => x.Trim()).ToList();
        }
        // Fallback: 2 or more whitespace characters
        return Regex.Split(line.Trim(), @"\s{2,}").Select(x => x.Trim()).ToList();
    }

    private static OpDefectMappingEntity? FindMapping(
        List<OpDefectMappingEntity> mappings,
        string? machineCode,
        string? opDefectName)
    {
        if (string.IsNullOrWhiteSpace(opDefectName)) return null;
        var cleanOp = Regex.Replace(opDefectName.Trim(), @"\s+", " ");
        var targetMachine = (machineCode ?? "ALL").Trim().ToUpperInvariant();

        // 1. Exact or clean match with matching machine
        var match = mappings.FirstOrDefault(m =>
            (m.MachineCode?.Trim().ToUpperInvariant() == targetMachine || string.IsNullOrEmpty(m.MachineCode) || m.MachineCode.Trim().ToUpperInvariant() == "ALL") &&
            (string.Equals(m.OpDefectName.Trim(), opDefectName.Trim(), StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Regex.Replace(m.OpDefectName.Trim(), @"\s+", " "), cleanOp, StringComparison.OrdinalIgnoreCase)));

        if (match != null) return match;

        // 2. Global fallback across any machine
        return mappings.FirstOrDefault(m =>
            string.Equals(m.OpDefectName.Trim(), opDefectName.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Regex.Replace(m.OpDefectName.Trim(), @"\s+", " "), cleanOp, StringComparison.OrdinalIgnoreCase));
    }
}
