using AutoVerify.Api.Analytics;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class DefectFeatureAnalyticsServiceTests
{
    private static (DefectFeatureAnalyticsService Service, AutoVerifyDbContext Db) CreateService()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();

        services.AddDbContext<AutoVerifyDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var db = sp.GetRequiredService<AutoVerifyDbContext>();
        var service = new DefectFeatureAnalyticsService(scopeFactory, NullLogger<DefectFeatureAnalyticsService>.Instance);

        return (service, db);
    }

    [Fact]
    public async Task GetScopeFacetsAsync_ReturnsDistinctCountsForScopeDimensions()
    {
        var (service, db) = CreateService();
        var sessionId = Guid.NewGuid();

        db.DefectPreviewSessions.Add(new DefectPreviewSessionEntity
        {
            Id = sessionId,
            StorageUri = "seaweedfs://bucket/test1",
            Status = "Completed",
            TotalRowsCount = 3,
            LoadedRowsCount = 3
        });

        db.DefectPreviewRecords.AddRange(
            new DefectPreviewRecordEntity
            {
                Id = 1,
                SessionId = sessionId,
                RowIndex = 1,
                ObservationId = Guid.NewGuid(),
                LineCode = "L1",
                MachineCode = "M1",
                StationCode = "S1",
                ClassifierRuleCode = "R1",
                ClassifierRuleVersion = "v1",
                DefectFamily = "Bridge",
                DefectClass = "Critical"
            },
            new DefectPreviewRecordEntity
            {
                Id = 2,
                SessionId = sessionId,
                RowIndex = 2,
                ObservationId = Guid.NewGuid(),
                LineCode = "L1",
                MachineCode = "M2",
                StationCode = "S1",
                ClassifierRuleCode = "R1",
                ClassifierRuleVersion = "v1",
                DefectFamily = "Bridge",
                DefectClass = "Minor"
            },
            new DefectPreviewRecordEntity
            {
                Id = 3,
                SessionId = sessionId,
                RowIndex = 3,
                ObservationId = Guid.NewGuid(),
                LineCode = "L2",
                MachineCode = "M3",
                StationCode = "S2",
                ClassifierRuleCode = "R2",
                ClassifierRuleVersion = "v2",
                DefectFamily = "Short",
                DefectClass = "Critical"
            }
        );

        await db.SaveChangesAsync();

        var facets = await service.GetScopeFacetsAsync(sessionId);
        Assert.NotNull(facets);
        Assert.Equal(sessionId, facets.SessionId);
        Assert.Equal(3, facets.TotalRecords);

        Assert.Equal(2, facets.Lines.Count);
        Assert.Contains(facets.Lines, l => l.Value == "L1" && l.Count == 2);
        Assert.Contains(facets.Lines, l => l.Value == "L2" && l.Count == 1);

        Assert.Equal(3, facets.Machines.Count);
        Assert.Equal(2, facets.Stations.Count);
        Assert.Equal(2, facets.ClassifierRules.Count);
        Assert.Equal(2, facets.ClassifierRuleVersions.Count);
        Assert.Equal(2, facets.DefectFamilies.Count);
        Assert.Equal(2, facets.DefectTypes.Count);

        service.Dispose();
    }

    [Fact]
    public async Task SpcSafeguard_FailsWhenRuleVersionsMixedAndNotAllowed()
    {
        var (service, db) = CreateService();
        var sessionId = Guid.NewGuid();

        db.DefectPreviewSessions.Add(new DefectPreviewSessionEntity
        {
            Id = sessionId,
            StorageUri = "seaweedfs://bucket/test2",
            Status = "Completed",
            TotalRowsCount = 2,
            LoadedRowsCount = 2
        });

        var r1Id = Guid.NewGuid();
        var r2Id = Guid.NewGuid();

        db.DefectPreviewRecords.AddRange(
            new DefectPreviewRecordEntity
            {
                Id = 10,
                SessionId = sessionId,
                RowIndex = 1,
                ObservationId = r1Id,
                ClassifierRuleCode = "RULE-X",
                ClassifierRuleVersion = "v1"
            },
            new DefectPreviewRecordEntity
            {
                Id = 11,
                SessionId = sessionId,
                RowIndex = 2,
                ObservationId = r2Id,
                ClassifierRuleCode = "RULE-X",
                ClassifierRuleVersion = "v2"
            }
        );

        db.DefectPreviewFeatures.AddRange(
            new DefectPreviewFeatureEntity
            {
                Id = 1,
                SessionId = sessionId,
                RowIndex = 1,
                ObservationId = r1Id,
                FeatureName = "Area",
                FeatureValue = 10.0
            },
            new DefectPreviewFeatureEntity
            {
                Id = 2,
                SessionId = sessionId,
                RowIndex = 2,
                ObservationId = r2Id,
                FeatureName = "Area",
                FeatureValue = 12.0
            }
        );

        await db.SaveChangesAsync();

        var job = await service.CreateJobAsync(sessionId, new CreateAnalyticsJobRequest
        {
            AnalysisType = "SPC",
            FeatureName = "Area",
            AllowRuleVersionMixing = false
        });

        // Wait briefly for job execution
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(100);
            var status = await service.GetJobStatusAsync(sessionId, job.JobId);
            if (status?.Status is "Failed" or "Completed")
            {
                break;
            }
        }

        var finishedJob = await service.GetJobStatusAsync(sessionId, job.JobId);
        Assert.NotNull(finishedJob);
        Assert.Equal("Failed", finishedJob.Status);
        Assert.Contains("rule versions detected", finishedJob.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        service.Dispose();
    }

    [Fact]
    public async Task SpcSafeguard_SucceedsWhenRuleVersionsMixingAllowed()
    {
        var (service, db) = CreateService();
        var sessionId = Guid.NewGuid();

        db.DefectPreviewSessions.Add(new DefectPreviewSessionEntity
        {
            Id = sessionId,
            StorageUri = "seaweedfs://bucket/test3",
            Status = "Completed",
            TotalRowsCount = 2,
            LoadedRowsCount = 2
        });

        var r1Id = Guid.NewGuid();
        var r2Id = Guid.NewGuid();

        db.DefectPreviewRecords.AddRange(
            new DefectPreviewRecordEntity
            {
                Id = 20,
                SessionId = sessionId,
                RowIndex = 1,
                ObservationId = r1Id,
                ClassifierRuleCode = "RULE-X",
                ClassifierRuleVersion = "v1"
            },
            new DefectPreviewRecordEntity
            {
                Id = 21,
                SessionId = sessionId,
                RowIndex = 2,
                ObservationId = r2Id,
                ClassifierRuleCode = "RULE-X",
                ClassifierRuleVersion = "v2"
            }
        );

        db.DefectPreviewFeatures.AddRange(
            new DefectPreviewFeatureEntity
            {
                Id = 10,
                SessionId = sessionId,
                RowIndex = 1,
                ObservationId = r1Id,
                FeatureName = "Area",
                FeatureValue = 10.0
            },
            new DefectPreviewFeatureEntity
            {
                Id = 11,
                SessionId = sessionId,
                RowIndex = 2,
                ObservationId = r2Id,
                FeatureName = "Area",
                FeatureValue = 12.0
            }
        );

        await db.SaveChangesAsync();

        var job = await service.CreateJobAsync(sessionId, new CreateAnalyticsJobRequest
        {
            AnalysisType = "SPC",
            FeatureName = "Area",
            AllowRuleVersionMixing = true
        });

        // Wait briefly for job execution
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(100);
            var status = await service.GetJobStatusAsync(sessionId, job.JobId);
            if (status?.Status is "Failed" or "Completed")
            {
                break;
            }
        }

        var finishedJob = await service.GetJobResultAsync(sessionId, job.JobId);
        Assert.NotNull(finishedJob);
        Assert.Equal("Completed", finishedJob.Status);

        service.Dispose();
    }

    [Fact]
    public async Task KMeansJob_ExecutesAndReturnsAssignments()
    {
        var (service, db) = CreateService();
        var sessionId = Guid.NewGuid();

        db.DefectPreviewSessions.Add(new DefectPreviewSessionEntity
        {
            Id = sessionId,
            StorageUri = "seaweedfs://bucket/kmeans",
            Status = "Completed",
            TotalRowsCount = 6,
            LoadedRowsCount = 6
        });

        for (int i = 1; i <= 6; i++)
        {
            var obsId = Guid.NewGuid();
            db.DefectPreviewRecords.Add(new DefectPreviewRecordEntity
            {
                Id = 100 + i,
                SessionId = sessionId,
                RowIndex = i,
                ObservationId = obsId,
                LineCode = "L1"
            });

            db.DefectPreviewFeatures.AddRange(
                new DefectPreviewFeatureEntity
                {
                    Id = 200 + (i * 2),
                    SessionId = sessionId,
                    RowIndex = i,
                    ObservationId = obsId,
                    FeatureName = "F1",
                    FeatureValue = i < 4 ? 1.0 + (i * 0.1) : 10.0 + (i * 0.1)
                },
                new DefectPreviewFeatureEntity
                {
                    Id = 200 + (i * 2) + 1,
                    SessionId = sessionId,
                    RowIndex = i,
                    ObservationId = obsId,
                    FeatureName = "F2",
                    FeatureValue = i < 4 ? 2.0 + (i * 0.1) : 20.0 + (i * 0.1)
                }
            );
        }

        await db.SaveChangesAsync();

        var job = await service.CreateJobAsync(sessionId, new CreateAnalyticsJobRequest
        {
            AnalysisType = "KMEANS",
            FeatureNames = ["F1", "F2"],
            K = 2,
            Seed = 42
        });

        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(100);
            var status = await service.GetJobStatusAsync(sessionId, job.JobId);
            if (status?.Status is "Failed" or "Completed")
            {
                break;
            }
        }

        var finishedJob = await service.GetJobResultAsync(sessionId, job.JobId);
        Assert.NotNull(finishedJob);
        Assert.Equal("Completed", finishedJob.Status);
        Assert.NotNull(finishedJob.Result);

        service.Dispose();
    }
}
