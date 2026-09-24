using AutoVerify.Api.Controllers;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class OpReconciliationTests
{
    private const string RealClsfPath = @"C:\Users\Administrator\Documents\Project\AutoVerify_System\D992_MCL_V_M_260529_1.clsf";

    [Fact]
    public async Task AnalyzeClsf_RealD992File_ComputesCountsPercentVpAndSavesRulesIdempotently()
    {
        Assert.True(File.Exists(RealClsfPath));
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);

        // Target model/machine data: total 10 defect occurrences.
        db.ProductRecords.AddRange(
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "D992-001", MachineCode = "MCL", ModelCode = "D992" },
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "D992-002", MachineCode = "MCL", ModelCode = "D992" },
            // Historical duplicate product key must not double-count defects.
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "D992-001", MachineCode = "MCL", ModelCode = "D992" },
            // Other model must be excluded.
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "D111-001", MachineCode = "MCL", ModelCode = "D111" });

        for (var i = 0; i < 4; i++) db.DefectRecords.Add(new DefectRecordEntity { Id = Guid.NewGuid(), KeyId = "D992-001", DefectName = "MCL_AA_Bubble" });
        for (var i = 0; i < 3; i++) db.DefectRecords.Add(new DefectRecordEntity { Id = Guid.NewGuid(), KeyId = "D992-002", DefectName = "SR_Bubble" });
        for (var i = 0; i < 2; i++) db.DefectRecords.Add(new DefectRecordEntity { Id = Guid.NewGuid(), KeyId = "D992-002", DefectName = "DIC_Crack" });
        db.DefectRecords.Add(new DefectRecordEntity { Id = Guid.NewGuid(), KeyId = "D992-002", DefectName = "UNMAPPED_DEFECT" });
        db.DefectRecords.Add(new DefectRecordEntity { Id = Guid.NewGuid(), KeyId = "D111-001", DefectName = "MCL_AA_Bubble" });
        await db.SaveChangesAsync();

        var controller = new OpDefectMappingsController(db, NullLogger<OpDefectMappingsController>.Instance);

        async Task<ClsfPackageAnalysisResponse> AnalyzeOnceAsync()
        {
            await using var stream = File.OpenRead(RealClsfPath);
            var file = new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(RealClsfPath));
            var action = await controller.AnalyzeClsfPackage(file, saveRules: true);
            var ok = Assert.IsType<OkObjectResult>(action);
            return Assert.IsType<ClsfPackageAnalysisResponse>(ok.Value);
        }

        var first = await AnalyzeOnceAsync();
        Assert.True(first.Success);
        Assert.Equal("D992", first.ModelCode);
        Assert.Equal("MCL", first.MachineCode);
        Assert.Equal(48, first.TotalRules);
        Assert.Equal(10, first.TotalDefectOccurrences);
        Assert.Equal(3, first.ActiveRuleCount);

        var mclBubble = Assert.Single(first.Rules.Where(r => r.RuleName == "MCL_AA_Bubble"));
        Assert.Equal(4, mclBubble.OccurrenceCount);
        Assert.Equal(40, mclBubble.OccurrencePercent);
        Assert.Equal("1", mclBubble.VpScope);

        var srBubble = Assert.Single(first.Rules.Where(r => r.RuleName == "SR_Bubble"));
        Assert.Equal(3, srBubble.OccurrenceCount);
        Assert.Equal(30, srBubble.OccurrencePercent);
        Assert.Equal("2", srBubble.VpScope);

        var dicCrack = Assert.Single(first.Rules.Where(r => r.RuleName == "DIC_Crack"));
        Assert.Equal(2, dicCrack.OccurrenceCount);
        Assert.Equal(20, dicCrack.OccurrencePercent);

        Assert.Equal(48, await db.MachineClassifierRules.CountAsync());
        var second = await AnalyzeOnceAsync();
        Assert.Equal(48, second.TotalRules);
        Assert.Equal(48, await db.MachineClassifierRules.CountAsync());
    }

    [Fact]
    public async Task AnalyzeClsf_InvalidJson_ReturnsControlledBadRequest()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        var controller = new OpDefectMappingsController(db, NullLogger<OpDefectMappingsController>.Instance);
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{ invalid-json"));
        var file = new FormFile(stream, 0, stream.Length, "file", "broken.clsf");

        var result = await controller.AnalyzeClsfPackage(file);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.MachineClassifierRules.ToListAsync());
    }

    [Fact]
    public async Task BatchMappings_SameOpNameDifferentMachines_AreStoredSeparately()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        var controller = new OpDefectMappingsController(db, NullLogger<OpDefectMappingsController>.Instance);

        var request = new BatchSaveOpDefectMappingRequest([
            new SaveOpDefectMappingRequest("COP Bonding dị vật", "SR_Bonding_Particle", "Dị vật", "COP", "CP_AOI", "CP AOI D967"),
            new SaveOpDefectMappingRequest("COP Bonding dị vật", "COP_FOREIGN", "Optical", "COP", "AOI", "AOI Default")
        ]);

        var response = await controller.BatchSaveMappings(request);
        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var saved = Assert.IsType<List<OpDefectMappingDto>>(ok.Value);

        Assert.Equal(2, saved.Count);
        Assert.Equal(2, await db.OpDefectMappings.CountAsync());
        Assert.Contains(saved, x => x.MachineCode == "CP_AOI" && x.PackageName == "CP AOI D967");
        Assert.Contains(saved, x => x.MachineCode == "AOI" && x.MachineDefectName == "COP_FOREIGN");
    }

    [Fact]
    public void ExcelSheetReader_ReadDelimitedText_ParsesHeadersAndDataRowsAccurately()
    {
        var csv = """
            KeyId,OpJudge,OpDefect,Line,Machine
            PROD-001,NG,Corner Crack,LINE-1,MACHINE-A
            PROD-002,OK,Dust Particle,LINE-1,MACHINE-A
            PROD-003,OK,,LINE-2,MACHINE-B
            """;

        var sheet = ExcelSheetReader.ReadDelimitedText(csv, "TestSheet");

        Assert.Equal("TestSheet", sheet.SheetName);
        Assert.Equal(5, sheet.Headers.Count);
        Assert.Contains("KeyId", sheet.Headers);
        Assert.Contains("OpDefect", sheet.Headers);

        Assert.Equal(3, sheet.Rows.Count);
        Assert.Equal("PROD-001", sheet.Rows[0]["KeyId"]);
        Assert.Equal("Corner Crack", sheet.Rows[0]["OpDefect"]);
        Assert.Equal("PROD-002", sheet.Rows[1]["KeyId"]);
        Assert.Equal("Dust Particle", sheet.Rows[1]["OpDefect"]);
        Assert.Equal("PROD-003", sheet.Rows[2]["KeyId"]);
    }

    [Fact]
    public async Task GetReconciliationAnalysis_SeparatesRealDefectFromOverkill_UsingOpMapping()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);
        var now = DateTimeOffset.UtcNow;

        // 1. Setup OP Defect Mappings
        // "Corner Crack" -> DIC_Crack (Real Defect: IsConfirmedDefect = true)
        // "Dust Particle" -> Dust (False Alarm: IsConfirmedDefect = false -> Overkill)
        db.OpDefectMappings.AddRange(
            new OpDefectMappingEntity
            {
                Id = Guid.NewGuid(),
                OpDefectName = "Corner Crack",
                MachineDefectName = "DIC_Crack",
                DefectFamily = "Optical",
                IsConfirmedDefect = true,
                CreatedAtUtc = now
            },
            new OpDefectMappingEntity
            {
                Id = Guid.NewGuid(),
                OpDefectName = "Dust Particle",
                MachineDefectName = "Dust",
                DefectFamily = "Optical",
                IsConfirmedDefect = false, // OP evaluated as false alarm / cleanable
                CreatedAtUtc = now
            }
        );

        // 2. Machine Products:
        // P1: Machine NG (DIC_Crack)
        // P2: Machine NG (DIC_Crack)
        // P3: Machine OK
        db.ProductRecords.AddRange(
            new ProductRecordEntity
            {
                Id = Guid.NewGuid(),
                KeyId = "UNIT-001",
                Judge = "NG",
                PanelId = "P1",
                PanelJudge = "NG",
                NameDefect = "DIC_Crack",
                LineCode = "LINE-1",
                MachineCode = "MACHINE-A",
                InspectedAtUtc = now.AddMinutes(-20)
            },
            new ProductRecordEntity
            {
                Id = Guid.NewGuid(),
                KeyId = "UNIT-002",
                Judge = "NG",
                PanelId = "P2",
                PanelJudge = "NG",
                NameDefect = "DIC_Crack",
                LineCode = "LINE-1",
                MachineCode = "MACHINE-A",
                InspectedAtUtc = now.AddMinutes(-15)
            },
            new ProductRecordEntity
            {
                Id = Guid.NewGuid(),
                KeyId = "UNIT-003",
                Judge = "OK",
                PanelId = "P3",
                PanelJudge = "OK",
                NameDefect = "None",
                LineCode = "LINE-1",
                MachineCode = "MACHINE-A",
                InspectedAtUtc = now.AddMinutes(-10)
            }
        );

        // Machine defect features:
        // UNIT-001: PxlArea = 150 (Real Defect)
        // UNIT-002: PxlArea = 25 (Overkill / Small dust)
        db.DefectRecords.AddRange(
            new DefectRecordEntity
            {
                Id = Guid.NewGuid(),
                KeyId = "UNIT-001",
                DefectTable = "AOI_DEFECTS",
                DefectName = "DIC_Crack",
                DefectType = "Optical",
                Judge = "NG",
                ConfidenceScore = 0.95,
                FeatureJson = "{\"G020_PxlArea\": 150, \"G038_PrcGrayMin\": 20}",
                CreatedAtUtc = now.AddMinutes(-20)
            },
            new DefectRecordEntity
            {
                Id = Guid.NewGuid(),
                KeyId = "UNIT-002",
                DefectTable = "AOI_DEFECTS",
                DefectName = "DIC_Crack",
                DefectType = "Optical",
                Judge = "NG",
                ConfidenceScore = 0.88,
                FeatureJson = "{\"G020_PxlArea\": 25, \"G038_PrcGrayMin\": 40}",
                CreatedAtUtc = now.AddMinutes(-15)
            }
        );

        // 3. OP Inspection Records from Excel:
        // UNIT-001: OP evaluated as "Corner Crack" (NG) -> Confirms Real Defect
        // UNIT-002: OP evaluated as "Dust Particle" (OK/False alarm) -> Overkill!
        // UNIT-003: OP evaluated as "Corner Crack" (NG) -> Machine passed but OP found defect -> Leakage!
        var batchId = Guid.NewGuid();
        db.OpInspectionRecords.AddRange(
            new OpInspectionRecordEntity
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                KeyId = "UNIT-001",
                OpJudge = "NG",
                OpDefectName = "Corner Crack",
                MappedMachineDefect = "DIC_Crack",
                MappedDefectFamily = "Optical",
                IsRealDefectConfirmed = true,
                LineCode = "LINE-1",
                MachineCode = "MACHINE-A",
                InspectedAtUtc = now.AddMinutes(-20),
                CreatedAtUtc = now
            },
            new OpInspectionRecordEntity
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                KeyId = "UNIT-002",
                OpJudge = "OK",
                OpDefectName = "Dust Particle",
                MappedMachineDefect = "Dust",
                MappedDefectFamily = "Optical",
                IsRealDefectConfirmed = false,
                LineCode = "LINE-1",
                MachineCode = "MACHINE-A",
                InspectedAtUtc = now.AddMinutes(-15),
                CreatedAtUtc = now
            },
            new OpInspectionRecordEntity
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                KeyId = "UNIT-003",
                OpJudge = "NG",
                OpDefectName = "Corner Crack",
                MappedMachineDefect = "DIC_Crack",
                MappedDefectFamily = "Optical",
                IsRealDefectConfirmed = true,
                LineCode = "LINE-1",
                MachineCode = "MACHINE-A",
                InspectedAtUtc = now.AddMinutes(-10),
                CreatedAtUtc = now
            }
        );

        await db.SaveChangesAsync();

        var controller = new OpReconciliationController(db, NullLogger<OpReconciliationController>.Instance);
        var actionResult = await controller.GetReconciliationAnalysis(
            machineCode: "MACHINE-A",
            lineCode: "LINE-1",
            targetDefect: "DIC_Crack"
        );

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<OpReconciliationAnalysisResponse>(okResult.Value);

        // Verification of Summary
        Assert.NotNull(response.Summary);
        Assert.Equal(3, response.Summary.TotalReconciled);
        Assert.Equal(2, response.Summary.TotalMachineNg);
        Assert.Equal(1, response.Summary.RealDefectCount); // UNIT-001
        Assert.Equal(1, response.Summary.OverkillCount);   // UNIT-002
        Assert.Equal(1, response.Summary.LeakageCount);    // UNIT-003

        Assert.Equal(0.5, response.Summary.RealDefectRate); // 1 out of 2 machine NG
        Assert.Equal(0.5, response.Summary.OverkillRate);   // 1 out of 2 machine NG

        // Verification of Defect Breakdown
        var dicCrackItem = response.DefectBreakdown.FirstOrDefault(d => d.MachineDefectName == "DIC_Crack");
        Assert.NotNull(dicCrackItem);
        Assert.Equal(2, dicCrackItem.TotalMachineNg);
        Assert.Equal(1, dicCrackItem.RealDefectCount);
        Assert.Equal(1, dicCrackItem.OverkillCount);
        Assert.Equal(0.5, dicCrackItem.OverkillRate);

        // Verification of Overkill Deep Dive
        Assert.NotNull(response.DeepDive);
        Assert.Equal("DIC_Crack", response.DeepDive.TargetDefectName);
        Assert.Equal(1, response.DeepDive.OverkillCount);
        Assert.Equal(1, response.DeepDive.RealDefectCount);

        // Feature distribution separation check
        // Overkill PxlArea = 25, Real Defect PxlArea = 150 -> Completely separated!
        var areaDist = response.DeepDive.FeatureDistributions.FirstOrDefault(f => f.FeatureName == "G020_PxlArea");
        Assert.NotNull(areaDist);
        Assert.Equal(25, areaDist.OverkillMean);
        Assert.Equal(150, areaDist.RealDefectMean);
        Assert.Contains("Completely Separated", areaDist.SeparationAssessment);

        // Threshold tuning recommendations should identify gold threshold between 25 and 150
        Assert.Contains(response.DeepDive.ThresholdTuningRecommendations, r => r.Contains("G020_PxlArea") && r.Contains("Gold threshold"));
    }

    [Fact]
    public async Task PasteOpRecords_DualBilingualHeaderAndConceptData_CorrectlyParsedAndMapped()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);

        // Pre-seed mappings
        db.OpDefectMappings.AddRange(
            new OpDefectMappingEntity { Id = Guid.NewGuid(), OpDefectName = "FOP bonding vết nhấn", MachineDefectName = "FOP_Dent", DefectFamily = "Bonding", IsConfirmedDefect = true },
            new OpDefectMappingEntity { Id = Guid.NewGuid(), OpDefectName = "POL đâm", MachineDefectName = "POL_Damage", DefectFamily = "Polarizer", IsConfirmedDefect = true },
            new OpDefectMappingEntity { Id = Guid.NewGuid(), OpDefectName = "B/P vết nhấn", MachineDefectName = "BP_Dent", DefectFamily = "Backplane", IsConfirmedDefect = true },
            new OpDefectMappingEntity { Id = Guid.NewGuid(), OpDefectName = "COP Bonding dị vật", MachineDefectName = "COP_Particle", DefectFamily = "Particle", IsConfirmedDefect = true },
            new OpDefectMappingEntity { Id = Guid.NewGuid(), OpDefectName = "POL vết nhấn", MachineDefectName = "POL_Dent", DefectFamily = "Polarizer", IsConfirmedDefect = true }
        );
        await db.SaveChangesAsync();

        var controller = new OpReconciliationController(db, NullLogger<OpReconciliationController>.Instance);

        var pasteText = "FOG ID\tTÊN LỖI\tPhán định\n" +
                        "FOG ID\t불량명(Vietnam)\t판정\n" +
                        "A0E7670S19B134214228\tFOP bonding vết nhấn\tNG\n" +
                        "A0B265KC29B125613228\tPOL  đâm\tNG\n" +
                        "A0AE65D318A125713228\tB/P vết nhấn\tNG\n" +
                        "A0DH65DB16B125713228\tCOP  Bonding dị vật\tNG\n" +
                        "A0AJ65DB16B125713228\tPOL vết nhấn\tNG\n" +
                        "A0D9670P15B134314228\tFOP bonding vết nhấn\tNG\n" +
                        "A0B7670P15B134314228\tFOP bonding vết nhấn\tNG\n" +
                        "A0B6670P15B134314228\tFOP bonding vết nhấn\tNG\n" +
                        "A0E5670P15B134314228\tFOP bonding vết nhấn\tNG";

        var action = await controller.PasteOpRecords(new PasteOpRecordsRequest(
            Text: pasteText,
            MachineCode: "CP_AOI",
            LineCode: "LINE-1",
            DefaultJudge: "NG"
        ));

        var okResult = Assert.IsType<OkObjectResult>(action);
        var res = Assert.IsType<UploadOpExcelResponse>(okResult.Value);

        Assert.True(res.Success);
        Assert.Equal(9, res.TotalRecordsSaved);
        Assert.Equal(9, res.CreatedCount);
        Assert.Equal(0, res.UpdatedCount);
        Assert.Equal(9, res.RejectedCount);
        Assert.Equal(9, res.MappedCount); // All 9 mapped (including double space POL  đâm and COP  Bonding dị vật)
        Assert.Equal(0, res.UnmappedCount);

        var records = await db.OpInspectionRecords.ToListAsync();
        Assert.Equal(9, records.Count);
        Assert.All(records, record => Assert.Equal("No Detect", record.Notes));

        var products = await db.ProductRecords.ToListAsync();
        Assert.Empty(products);

        var unifiedAction = await controller.GetReconciliationRecords(page: 1, pageSize: 50);
        var unifiedOk = Assert.IsType<OkObjectResult>(unifiedAction.Result);
        var unified = Assert.IsType<OpReconciliationRecordsResponse>(unifiedOk.Value);
        Assert.Equal(9, unified.TotalCount);
        Assert.Equal(9, unified.NotFoundCount);
        Assert.Equal(9, unified.NoDetectCount);
        Assert.All(unified.Items, item => Assert.Equal("No Detect", item.Classification));
    }

    [Fact]
    public async Task PasteOpRecords_ShortNumericIds_AreDropped()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        var controller = new OpReconciliationController(db, NullLogger<OpReconciliationController>.Instance);

        var action = await controller.PasteOpRecords(new PasteOpRecordsRequest(
            Text: "FOG ID\tTÊN LỖI\tPhán định\n1\tDust\tNG\n9\tScratch\tNG\n10\tDent\tNG\nFOG-KEEP\tDent\tNG",
            MachineCode: "CP_AOI",
            DefaultJudge: "NG"));

        var okResult = Assert.IsType<OkObjectResult>(action);
        var res = Assert.IsType<UploadOpExcelResponse>(okResult.Value);
        Assert.Equal(2, res.TotalRecordsSaved);

        var keys = await db.OpInspectionRecords.Select(r => r.KeyId).ToListAsync();
        Assert.Equal(new[] { "10", "FOG-KEEP" }, keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetReconciliationRecords_FlattensBatchesAndComputesRatesFromMatchedMachineNgOnly()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var batchA = Guid.NewGuid();
        var batchB = Guid.NewGuid();

        db.ProductRecords.AddRange(
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "REAL-1", Judge = "NG", PanelId = "P1", PanelJudge = "NG", NameDefect = "Crack", DefectName = "Crack", MachineCode = "M1", LineCode = "L1", InspectedAtUtc = now },
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "OVERKILL-1", Judge = "NG", PanelId = "P2", PanelJudge = "NG", NameDefect = "Dust", DefectName = "Dust", MachineCode = "M1", LineCode = "L1", InspectedAtUtc = now },
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "LEAK-1", Judge = "OK", PanelId = "P3", PanelJudge = "OK", NameDefect = "None", DefectName = "None", MachineCode = "M1", LineCode = "L1", InspectedAtUtc = now },
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "OK-1", Judge = "OK", PanelId = "P4", PanelJudge = "OK", NameDefect = "None", DefectName = "None", MachineCode = "M1", LineCode = "L1", InspectedAtUtc = now });

        db.OpInspectionRecords.AddRange(
            new OpInspectionRecordEntity { Id = Guid.NewGuid(), BatchId = batchA, KeyId = "REAL-1", OpJudge = "NG", OpDefectName = "Crack", MachineCode = "M1", LineCode = "L1", InspectedAtUtc = now, CreatedAtUtc = now },
            new OpInspectionRecordEntity { Id = Guid.NewGuid(), BatchId = batchA, KeyId = "OVERKILL-1", OpJudge = "OK", OpDefectName = "No defect", MachineCode = "M1", LineCode = "L1", InspectedAtUtc = now, CreatedAtUtc = now },
            new OpInspectionRecordEntity { Id = Guid.NewGuid(), BatchId = batchB, KeyId = "LEAK-1", OpJudge = "NG", OpDefectName = "Missed defect", MachineCode = "M1", LineCode = "L1", InspectedAtUtc = now, CreatedAtUtc = now },
            new OpInspectionRecordEntity { Id = Guid.NewGuid(), BatchId = batchB, KeyId = "OK-1", OpJudge = "OK", OpDefectName = "No defect", MachineCode = "M1", LineCode = "L1", InspectedAtUtc = now, CreatedAtUtc = now },
            new OpInspectionRecordEntity { Id = Guid.NewGuid(), BatchId = batchB, KeyId = "MISSING-1", OpJudge = "NG", OpDefectName = "Unknown", MachineCode = "M1", LineCode = "L1", InspectedAtUtc = now, CreatedAtUtc = now });
        await db.SaveChangesAsync();

        var controller = new OpReconciliationController(db, NullLogger<OpReconciliationController>.Instance);
        var action = await controller.GetReconciliationRecords(page: 1, pageSize: 50);
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<OpReconciliationRecordsResponse>(ok.Value);

        Assert.Equal(5, response.TotalCount);
        Assert.Equal(4, response.MatchedCount);
        Assert.Equal(1, response.NotFoundCount);
        Assert.Equal(1, response.RealDefectCount);
        Assert.Equal(1, response.OverkillCount);
        Assert.Equal(1, response.LeakageCount);
        Assert.Equal(1, response.ConsistentOkCount);
        Assert.Equal(0.5, response.RealDefectRate);
        Assert.Equal(0.5, response.OverkillRate);
        Assert.Equal(2, response.Items.Select(x => x.BatchId).Distinct().Count());
    }

    [Fact]
    public async Task PasteOpRecords_AutomaticallyResolvesLineFromLatestProductForSelectedMachine()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        var now = DateTimeOffset.UtcNow;

        db.ProductRecords.AddRange(
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "FOG-001", Judge = "NG", PanelId = "P1", PanelJudge = "NG", NameDefect = "Dent", MachineCode = "CP_AOI", LineCode = "LINE-OLD", InspectedAtUtc = now.AddHours(-2) },
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "FOG-001", Judge = "NG", PanelId = "P1", PanelJudge = "NG", NameDefect = "Dent", MachineCode = "OTHER", LineCode = "LINE-OTHER", InspectedAtUtc = now.AddHours(-1) },
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "FOG-001", Judge = "NG", PanelId = "P1", PanelJudge = "NG", NameDefect = "Dent", MachineCode = "CP_AOI", LineCode = "LINE-LATEST", InspectedAtUtc = now });
        await db.SaveChangesAsync();

        var controller = new OpReconciliationController(db, NullLogger<OpReconciliationController>.Instance);
        var action = await controller.PasteOpRecords(new PasteOpRecordsRequest(
            Text: "FOG ID\tTÊN LỖI\tPhán định\nFOG-001\tFOP bonding vết nhấn\tNG",
            MachineCode: "CP_AOI",
            LineCode: "LINE-LATEST",
            DefaultJudge: "NG"));

        Assert.IsType<OkObjectResult>(action);
        var record = Assert.Single(await db.OpInspectionRecords.ToListAsync());
        Assert.Equal("CP_AOI", record.MachineCode);
        Assert.Equal("LINE-LATEST", record.LineCode);
        var markedProduct = await db.ProductRecords.SingleAsync(p => p.KeyId == "FOG-001" && p.MachineCode == "CP_AOI" && p.LineCode == "LINE-LATEST");
        Assert.Equal("Overkill", markedProduct.OpReviewMark);
        Assert.Null((await db.ProductRecords.SingleAsync(p => p.MachineCode == "OTHER")).OpReviewMark);
    }

    [Fact]
    public async Task PasteOpRecords_ProductNgAndOpOk_MarksOverkillAndLeavesOtherColumnsUnchanged()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        var product = new ProductRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = "FOG-OVERKILL",
            Judge = "NG",
            PanelId = "PANEL-1",
            PanelJudge = "NG",
            NameDefect = "Dust",
            Zone = null,
            DefectName = null,
            MachineCode = "CP_AOI",
            LineCode = "LINE-7",
            InspectedAtUtc = DateTimeOffset.UtcNow
        };
        db.ProductRecords.Add(product);
        await db.SaveChangesAsync();

        var controller = new OpReconciliationController(db, NullLogger<OpReconciliationController>.Instance);
        var action = await controller.PasteOpRecords(new PasteOpRecordsRequest(
            Text: "FOG ID\tTÊN LỖI\tPhán định\nFOG-OVERKILL\tNo defect\tOK",
            MachineCode: "CP_AOI",
            LineCode: "LINE-7",
            DefaultJudge: "NG"));

        Assert.IsType<OkObjectResult>(action);
        var updated = await db.ProductRecords.SingleAsync();
        Assert.Equal("Overkill", updated.OpReviewMark);
        Assert.Equal("Dust", updated.NameDefect);
        Assert.Null(updated.Zone);
        Assert.Null(updated.DefectName);
        Assert.Equal("OK", updated.OpReviewJudge);
        Assert.Equal("No defect", updated.OpReviewDefectName);
        Assert.NotNull(updated.OpReviewedAtUtc);
        Assert.Contains("Product defect remains unchanged", updated.OpReviewNote);
    }

    [Fact]
    public async Task PasteOpRecords_SameProductTwice_UpdatesExistingReviewInsteadOfCreatingDuplicate()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        var product = new ProductRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = "FOG-UPSERT",
            Judge = "NG",
            PanelId = "PANEL-UPSERT",
            PanelJudge = "NG",
            NameDefect = "Dent",
            MachineCode = "CP_AOI",
            LineCode = "LINE-9",
            InspectedAtUtc = DateTimeOffset.UtcNow
        };
        db.ProductRecords.Add(product);
        await db.SaveChangesAsync();

        var controller = new OpReconciliationController(db, NullLogger<OpReconciliationController>.Instance);
        var firstAction = await controller.PasteOpRecords(new PasteOpRecordsRequest(
            Text: "FOG ID\tTÊN LỖI\tPhán định\nFOG-UPSERT\tDent\tNG",
            MachineCode: "CP_AOI", LineCode: "LINE-9"));
        var first = Assert.IsType<UploadOpExcelResponse>(Assert.IsType<OkObjectResult>(firstAction).Value);
        Assert.Equal(1, first.CreatedCount);
        Assert.Equal(0, first.UpdatedCount);
        Assert.Equal("Real Defect", (await db.ProductRecords.SingleAsync()).OpReviewMark);

        var secondAction = await controller.PasteOpRecords(new PasteOpRecordsRequest(
            Text: "FOG ID\tTÊN LỖI\tPhán định\nFOG-UPSERT\tNo defect\tOK",
            MachineCode: "CP_AOI", LineCode: "LINE-9"));
        var second = Assert.IsType<UploadOpExcelResponse>(Assert.IsType<OkObjectResult>(secondAction).Value);

        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(1, second.UpdatedCount);
        Assert.Equal(0, second.RejectedCount);
        var review = Assert.Single(await db.OpInspectionRecords.ToListAsync());
        Assert.Equal(product.Id, review.ProductRecordId);
        Assert.Equal("OK", review.OpJudge);
        Assert.Equal("No defect", review.OpDefectName);
        Assert.Equal("Overkill", (await db.ProductRecords.SingleAsync()).OpReviewMark);
    }

    [Fact]
    public async Task PasteOpRecords_MachineNgAndOpNgWithMatchingDefect_MarksRealDefect()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        db.ProductRecords.Add(new ProductRecordEntity
        {
            Id = Guid.NewGuid(), KeyId = "MATCH-1", Judge = "NG", PanelId = "P1", PanelJudge = "NG",
            NameDefect = "FOP_Dent", DefectName = "FOP_Dent", MachineCode = "CP_AOI", LineCode = "LINE-1",
            InspectedAtUtc = DateTimeOffset.UtcNow
        });
        db.OpDefectMappings.Add(new OpDefectMappingEntity
        {
            Id = Guid.NewGuid(), MachineCode = "CP_AOI", PackageName = "Default",
            OpDefectName = "FOP bonding vết nhấn", MachineDefectName = "FOP_Dent",
            DefectFamily = "Bonding", IsConfirmedDefect = true, CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = new OpReconciliationController(db, NullLogger<OpReconciliationController>.Instance);
        var action = await controller.PasteOpRecords(new PasteOpRecordsRequest(
            "FOG ID\tTÊN LỖI\tPhán định\nMATCH-1\tFOP bonding vết nhấn\tNG", "CP_AOI", "LINE-1"));

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal("Real Defect", (await db.ProductRecords.SingleAsync()).OpReviewMark);
    }

    [Fact]
    public async Task PasteOpRecords_MachineOkAndOpNg_IsLeakageAndProductMarkRemainsEmpty()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        db.ProductRecords.Add(new ProductRecordEntity
        {
            Id = Guid.NewGuid(), KeyId = "LEAKAGE-1", Judge = "OK", PanelId = "P1", PanelJudge = "OK",
            NameDefect = "None", MachineCode = "CP_AOI", LineCode = "LINE-1", InspectedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = new OpReconciliationController(db, NullLogger<OpReconciliationController>.Instance);
        var action = await controller.PasteOpRecords(new PasteOpRecordsRequest(
            "FOG ID\tTÊN LỖI\tPhán định\nLEAKAGE-1\tFOP bonding vết nhấn\tNG", "CP_AOI", "LINE-1"));

        Assert.IsType<OkObjectResult>(action);
        Assert.Null((await db.ProductRecords.SingleAsync()).OpReviewMark);
        var recordsAction = await controller.GetReconciliationRecords(page: 1, pageSize: 50);
        var response = Assert.IsType<OpReconciliationRecordsResponse>(Assert.IsType<OkObjectResult>(recordsAction.Result).Value);
        Assert.Equal(1, response.LeakageCount);
        Assert.Equal("Leakage", Assert.Single(response.Items).Classification);
    }

    [Fact]
    public async Task PasteOpRecords_SameKeyOnDifferentLines_UpdatesOnlySelectedLineProduct()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        db.ProductRecords.AddRange(
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "MULTI-LINE", Judge = "NG", PanelId = "P1", PanelJudge = "NG", NameDefect = "Dent", DefectName = "Dent", MachineCode = "M1", LineCode = "L1", InspectedAtUtc = DateTimeOffset.UtcNow },
            new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "MULTI-LINE", Judge = "NG", PanelId = "P2", PanelJudge = "NG", NameDefect = "Scratch", DefectName = "Scratch", MachineCode = "M1", LineCode = "L2", InspectedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var controller = new OpReconciliationController(db, NullLogger<OpReconciliationController>.Instance);
        var action = await controller.PasteOpRecords(new PasteOpRecordsRequest(
            "FOG ID\tTÊN LỖI\tPhán định\nMULTI-LINE\tScratch\tNG", "M1", "L2"));

        var response = Assert.IsType<UploadOpExcelResponse>(Assert.IsType<OkObjectResult>(action).Value);
        Assert.Equal(1, response.CreatedCount);
        var l1 = await db.ProductRecords.SingleAsync(p => p.LineCode == "L1");
        var l2 = await db.ProductRecords.SingleAsync(p => p.LineCode == "L2");
        Assert.Null(l1.OpReviewMark);
        Assert.Equal("Real Defect", l2.OpReviewMark);
        Assert.Equal(l2.Id, (await db.OpInspectionRecords.SingleAsync()).ProductRecordId);
    }

    [Fact]
    public async Task PasteOpRecords_OpDefectMatchesAnyMachineDefectRow_MarksRealDefect()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        var product = new ProductRecordEntity
        {
            Id = Guid.NewGuid(), KeyId = "MULTI-DEFECT", Judge = "NG", PanelId = "P1", PanelJudge = "NG",
            NameDefect = "FirstWrongCode", DefectName = "FirstWrongCode", MachineCode = "M1", LineCode = "L1",
            InspectedAtUtc = DateTimeOffset.UtcNow
        };
        db.ProductRecords.Add(product);
        db.DefectRecords.AddRange(
            new DefectRecordEntity { Id = Guid.NewGuid(), KeyId = product.KeyId, DefectName = "FirstWrongCode", DefectTable = "D1", DefectType = "Optical", Severity = "High", Judge = "NG", CreatedAtUtc = DateTimeOffset.UtcNow },
            new DefectRecordEntity { Id = Guid.NewGuid(), KeyId = product.KeyId, DefectName = "TARGET_CODE", DefectTable = "D2", DefectType = "Optical", Severity = "High", Judge = "NG", CreatedAtUtc = DateTimeOffset.UtcNow });
        db.OpDefectMappings.Add(new OpDefectMappingEntity
        {
            Id = Guid.NewGuid(), MachineCode = "M1", PackageName = "Default", OpDefectName = "OP Target",
            MachineDefectName = "TARGET_CODE", DefectFamily = "Optical", IsConfirmedDefect = true, CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = new OpReconciliationController(db, NullLogger<OpReconciliationController>.Instance);
        var action = await controller.PasteOpRecords(new PasteOpRecordsRequest(
            "FOG ID\tTÊN LỖI\tPhán định\nMULTI-DEFECT\tOP Target\tNG", "M1", "L1"));

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal("Real Defect", (await db.ProductRecords.SingleAsync()).OpReviewMark);
        var recordsAction = await controller.GetReconciliationRecords(page: 1, pageSize: 50);
        var response = Assert.IsType<OpReconciliationRecordsResponse>(Assert.IsType<OkObjectResult>(recordsAction.Result).Value);
        Assert.Equal("Real Defect", Assert.Single(response.Items).Classification);
    }

    [Theory]
    [InlineData("N/A")]
    [InlineData("NA")]
    [InlineData("NO NG")]
    [InlineData("NO_NG")]
    [InlineData("No Detect")]
    [InlineData("Not Applicable")]
    public async Task PasteOpRecords_NoDetectMarkers_AreExcludedFromRates(string marker)
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        var product = new ProductRecordEntity
        {
            Id = Guid.NewGuid(), KeyId = "NO-DETECT-1", Judge = "NG", PanelId = "P1", PanelJudge = "NG",
            NameDefect = marker, DefectName = marker, MachineCode = "M1", LineCode = "L1", InspectedAtUtc = DateTimeOffset.UtcNow
        };
        db.ProductRecords.Add(product);
        db.DefectRecords.Add(new DefectRecordEntity
        {
            Id = Guid.NewGuid(), KeyId = product.KeyId, DefectName = marker, DefectTable = "D1", DefectType = "Optical",
            Severity = "High", Judge = "NG", CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = new OpReconciliationController(db, NullLogger<OpReconciliationController>.Instance);
        var action = await controller.PasteOpRecords(new PasteOpRecordsRequest(
            $"FOG ID\tTÊN LỖI\tPhán định\nNO-DETECT-1\t{marker}\tNG", "M1", "L1"));

        Assert.IsType<OkObjectResult>(action);
        Assert.Null((await db.ProductRecords.SingleAsync()).OpReviewMark);
        var recordsAction = await controller.GetReconciliationRecords(page: 1, pageSize: 50);
        var response = Assert.IsType<OpReconciliationRecordsResponse>(Assert.IsType<OkObjectResult>(recordsAction.Result).Value);
        Assert.Equal(1, response.NoDetectCount);
        Assert.Equal(0, response.RealDefectCount);
        Assert.Equal(0, response.OverkillCount);
        Assert.Equal(0, response.LeakageCount);
        Assert.Equal(0, response.RealDefectRate);
        Assert.Equal(0, response.OverkillRate);
        Assert.Equal("No Detect", Assert.Single(response.Items).Classification);
    }
}
