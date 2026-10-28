using Microsoft.EntityFrameworkCore;
using AutoVerify.Api.Persistence.Entities;

namespace AutoVerify.Api.Persistence;

public sealed class AutoVerifyDbContext(DbContextOptions<AutoVerifyDbContext> options) : DbContext(options)
{
    public DbSet<AppUserEntity> Users => Set<AppUserEntity>();
    public DbSet<DataSourceEntity> DataSources => Set<DataSourceEntity>();
    public DbSet<ScanListEntity> ScanLists => Set<ScanListEntity>();
    public DbSet<ScanExecutionEntity> ScanExecutions => Set<ScanExecutionEntity>();
    public DbSet<VerificationResultEntity> VerificationResults => Set<VerificationResultEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    public DbSet<StorageObjectEntity> StorageObjects => Set<StorageObjectEntity>();
    public DbSet<ProductQueryEntity> ProductQueries => Set<ProductQueryEntity>();
    public DbSet<QueryProfileEntity> QueryProfiles => Set<QueryProfileEntity>();
    public DbSet<DatabaseConnectionEntity> DatabaseConnections => Set<DatabaseConnectionEntity>();
    public DbSet<ProductRecordEntity> ProductRecords => Set<ProductRecordEntity>();
    public DbSet<DefectRecordEntity> DefectRecords => Set<DefectRecordEntity>();
    public DbSet<ScanListItemEntity> ScanListItems => Set<ScanListItemEntity>();
    public DbSet<VerificationHistoryEntity> VerificationHistory => Set<VerificationHistoryEntity>();
    public DbSet<OverkillPipelineConfigEntity> OverkillPipelineConfigs => Set<OverkillPipelineConfigEntity>();
    public DbSet<OverkillPipelineExecutionEntity> OverkillPipelineExecutions => Set<OverkillPipelineExecutionEntity>();
    public DbSet<LeakageBatchEntity> LeakageBatches => Set<LeakageBatchEntity>();
    public DbSet<MasterStationMappingEntity> MasterStationMappings => Set<MasterStationMappingEntity>();
    public DbSet<AiVerdictEntity> AiVerdicts => Set<AiVerdictEntity>();
    public DbSet<LeakageQueueItemEntity> LeakageQueueItems => Set<LeakageQueueItemEntity>();
    public DbSet<DefectPreviewSessionEntity> DefectPreviewSessions => Set<DefectPreviewSessionEntity>();
    public DbSet<DefectPreviewRecordEntity> DefectPreviewRecords => Set<DefectPreviewRecordEntity>();
    public DbSet<DefectPreviewFeatureEntity> DefectPreviewFeatures => Set<DefectPreviewFeatureEntity>();
    public DbSet<DefectAnalyticsJobEntity> DefectAnalyticsJobs => Set<DefectAnalyticsJobEntity>();
    public DbSet<MachineClassifierRuleEntity> MachineClassifierRules => Set<MachineClassifierRuleEntity>();
    public DbSet<OpDefectMappingEntity> OpDefectMappings => Set<OpDefectMappingEntity>();
    public DbSet<OpInspectionRecordEntity> OpInspectionRecords => Set<OpInspectionRecordEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureCommonEntityLengths(modelBuilder);

        modelBuilder.Entity<AppUserEntity>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<DataSourceEntity>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<ScanListEntity>().HasIndex(x => x.Checksum);
        modelBuilder.Entity<ScanExecutionEntity>().HasIndex(x => x.StartedAtUtc);
        modelBuilder.Entity<VerificationResultEntity>().HasIndex(x => x.ExecutionId);
        modelBuilder.Entity<AuditEventEntity>().HasIndex(x => x.OccurredAtUtc);
        modelBuilder.Entity<StorageObjectEntity>().HasIndex(x => x.CreatedAtUtc);
        modelBuilder.Entity<ProductQueryEntity>().HasIndex(x => x.Status);
        modelBuilder.Entity<ProductQueryEntity>().HasIndex(x => x.QueryKey);
        modelBuilder.Entity<QueryProfileEntity>().HasIndex(x => new { x.MachineCode, x.LineCode, x.Concept, x.SourceSystem });
        modelBuilder.Entity<DatabaseConnectionEntity>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<DataSourceEntity>().HasIndex(x => x.MachineCode);
        modelBuilder.Entity<ProductRecordEntity>().HasIndex(x => x.KeyId);
        modelBuilder.Entity<ProductRecordEntity>().HasIndex(x => x.PanelId);
        modelBuilder.Entity<ProductRecordEntity>().HasIndex(x => x.Judge);
        modelBuilder.Entity<ProductRecordEntity>().HasIndex(x => x.InspectedAtUtc);
        modelBuilder.Entity<ProductRecordEntity>().HasIndex(x => new { x.MachineCode, x.LineCode, x.InspectedAtUtc });
        modelBuilder.Entity<ProductRecordEntity>().HasIndex(x => new { x.Judge, x.InspectedAtUtc });
        modelBuilder.Entity<DefectRecordEntity>().HasIndex(x => x.KeyId);
        modelBuilder.Entity<DefectRecordEntity>().HasIndex(x => x.DefectTable);
        modelBuilder.Entity<DefectRecordEntity>().HasIndex(x => x.ModelCode);
        modelBuilder.Entity<DefectRecordEntity>().HasIndex(x => x.ConnectionId);
        modelBuilder.Entity<DefectRecordEntity>().HasIndex(x => x.MachineCode);
        modelBuilder.Entity<DefectRecordEntity>().HasIndex(x => x.LineCode);
        modelBuilder.Entity<DefectRecordEntity>().HasIndex(x => x.CreatedAtUtc);
        modelBuilder.Entity<ScanListItemEntity>().HasIndex(x => x.ScanListId);
        modelBuilder.Entity<ScanListItemEntity>().HasIndex(x => x.EntityId);
        modelBuilder.Entity<VerificationHistoryEntity>().HasIndex(x => x.CreatedAtUtc);
        modelBuilder.Entity<VerificationHistoryEntity>().HasIndex(x => new { x.Category, x.MachineCode, x.LineCode });
        modelBuilder.Entity<VerificationHistoryEntity>().HasIndex(x => x.EntityId);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().HasIndex(x => new { x.Enabled, x.NextRunAtUtc });
        modelBuilder.Entity<OverkillPipelineExecutionEntity>().HasIndex(x => x.ConfigId);
        modelBuilder.Entity<OverkillPipelineExecutionEntity>().HasIndex(x => x.StartedAtUtc);

        modelBuilder.Entity<MasterStationMappingEntity>().HasIndex(x => new { x.MachineCode, x.LineCode, x.ModelCode }).IsUnique();
        modelBuilder.Entity<AiVerdictEntity>().HasIndex(x => x.DefectId).IsUnique();
        modelBuilder.Entity<AiVerdictEntity>().HasIndex(x => x.KeyId);
        modelBuilder.Entity<AiVerdictEntity>().HasIndex(x => new { x.Status, x.MasterJobId });

        modelBuilder.Entity<LeakageBatchEntity>().HasIndex(x => x.CreatedAtUtc);
        modelBuilder.Entity<LeakageQueueItemEntity>().HasIndex(x => x.BatchId);
        modelBuilder.Entity<LeakageQueueItemEntity>().HasIndex(x => x.ProductId);
        modelBuilder.Entity<LeakageQueueItemEntity>().HasIndex(x => x.Status);
        modelBuilder.Entity<LeakageQueueItemEntity>().HasIndex(x => new { x.Status, x.CreatedAtUtc });

        modelBuilder.Entity<LeakageQueueItemEntity>()
            .HasOne(x => x.Batch)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DefectPreviewSessionEntity>(b =>
        {
            b.ToTable("defect_preview_sessions");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ExpiresAtUtc);
            b.HasIndex(x => x.Status);
            b.Property(x => x.StorageUri).HasMaxLength(1024).IsRequired();
            b.Property(x => x.KeyId).HasMaxLength(128);
            b.Property(x => x.Format).HasMaxLength(64);
            b.Property(x => x.DetectedFormat).HasMaxLength(64);
            b.Property(x => x.ProcessingStage).HasMaxLength(64);
            b.Property(x => x.Status).HasMaxLength(32);
            b.Ignore(x => x.SourceUri);
            b.Ignore(x => x.SourceType);
            b.Ignore(x => x.TotalRecords);
            b.Property(x => x.CreatedBy).HasMaxLength(128);
            b.Property(x => x.FilterSummaryJson).HasColumnType("longtext");
            b.Property(x => x.ErrorMessage).HasColumnType("text");
            b.Property(x => x.WarningMessage).HasColumnType("text");
            b.Property(x => x.MaxRows).HasDefaultValue(50000);
        });

        modelBuilder.Entity<DefectPreviewRecordEntity>(b =>
        {
            b.ToTable("defect_preview_records");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.DefectId).HasMaxLength(128);
            b.Property(x => x.KeyId).HasMaxLength(128);
            b.Property(x => x.DefectName).HasMaxLength(256);
            b.Property(x => x.DefectTable).HasMaxLength(128);
            b.Property(x => x.DefectType).HasMaxLength(64);
            b.Property(x => x.ComponentName).HasMaxLength(128);
            b.Property(x => x.Severity).HasMaxLength(32);
            b.Property(x => x.Judge).HasMaxLength(32);
            b.Property(x => x.Reason).HasColumnType("text");
            b.Property(x => x.EqpName).HasMaxLength(256);
            b.Property(x => x.EmNo).HasMaxLength(64);
            b.Property(x => x.SpecName).HasMaxLength(128);
            b.Property(x => x.SpecJudge).HasMaxLength(256);
            b.Property(x => x.SpecReason).HasMaxLength(256);
            b.Property(x => x.FeatureJsonSnippet).HasMaxLength(512);
            b.Property(x => x.RawFeatureUri).HasMaxLength(1024);
            b.Property(x => x.RawImageUri).HasMaxLength(1024);
            b.Property(x => x.LineCode).HasMaxLength(128);
            b.Property(x => x.MachineCode).HasMaxLength(128);
            b.Property(x => x.StationCode).HasMaxLength(128);
            b.Property(x => x.ClassifierRuleCode).HasMaxLength(128);
            b.Property(x => x.ClassifierRuleName).HasMaxLength(256);
            b.Property(x => x.ClassifierRuleVersion).HasMaxLength(64);
            b.Property(x => x.DefectFamily).HasMaxLength(128);
            b.Property(x => x.DefectClass).HasMaxLength(128);

            b.HasOne(x => x.Session)
                .WithMany(x => x.Records)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.SessionId, x.RowIndex }).HasDatabaseName("IX_DefectPreview_Session_RowIndex");
            b.HasIndex(x => new { x.SessionId, x.ObservationId }).HasDatabaseName("IX_DefectPreview_Session_ObservationId");
            b.HasIndex(x => new { x.SessionId, x.Judge }).HasDatabaseName("IX_DefectPreview_Session_Judge");
            b.HasIndex(x => new { x.SessionId, x.DefectName }).HasDatabaseName("IX_DefectPreview_Session_DefectName");
            b.HasIndex(x => new { x.SessionId, x.ComponentName }).HasDatabaseName("IX_DefectPreview_Session_Component");
            b.HasIndex(x => new { x.SessionId, x.LineCode, x.MachineCode }).HasDatabaseName("IX_DefectPreview_Session_Line_Machine");
            b.HasIndex(x => new { x.SessionId, x.ClassifierRuleCode, x.ClassifierRuleVersion }).HasDatabaseName("IX_DefectPreview_Session_Rule_Version");
            b.HasIndex(x => new { x.SessionId, x.DefectFamily }).HasDatabaseName("IX_DefectPreview_Session_DefectFamily");
        });

        modelBuilder.Entity<DefectPreviewFeatureEntity>(b =>
        {
            b.ToTable("defect_preview_features");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.FeatureName).HasMaxLength(128);

            b.HasOne(x => x.Session)
                .WithMany(x => x.Features)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.SessionId, x.FeatureName }).HasDatabaseName("IX_DefectPreview_Feature_Session_Name");
            b.HasIndex(x => new { x.SessionId, x.RowIndex }).HasDatabaseName("IX_DefectPreview_Feature_Session_RowIndex");
            b.HasIndex(x => new { x.SessionId, x.ObservationId }).HasDatabaseName("IX_DefectPreview_Feature_Session_ObservationId");
        });

        modelBuilder.Entity<MachineClassifierRuleEntity>(b =>
        {
            b.ToTable("machine_classifier_rules");
            b.HasKey(x => x.Id);
            b.Property(x => x.RuleCode).HasMaxLength(128).IsRequired();
            b.Property(x => x.RuleName).HasMaxLength(256).IsRequired();
            b.Property(x => x.Version).HasMaxLength(64).IsRequired();
            b.Property(x => x.ProductKey).HasMaxLength(128);
            b.Property(x => x.LineCode).HasMaxLength(128);
            b.Property(x => x.MachineCode).HasMaxLength(128);
            b.Property(x => x.StationCode).HasMaxLength(128);
            b.Property(x => x.DefectFamily).HasMaxLength(128);
            b.Property(x => x.DefectClass).HasMaxLength(128);
            b.Property(x => x.ThresholdsConfigJson).HasColumnType("longtext");
            b.Property(x => x.ModelMetadataJson).HasColumnType("longtext");
            b.Property(x => x.CreatedBy).HasMaxLength(128);
            b.Property(x => x.Description).HasMaxLength(1024);

            b.HasIndex(x => new { x.RuleCode, x.Version }).HasDatabaseName("IX_MachineClassifierRules_Code_Version");
            b.HasIndex(x => new { x.LineCode, x.MachineCode, x.StationCode }).HasDatabaseName("IX_MachineClassifierRules_Scope");
            b.HasIndex(x => new { x.DefectFamily, x.DefectClass }).HasDatabaseName("IX_MachineClassifierRules_Family_Class");
            b.HasIndex(x => new { x.IsActive, x.EffectiveFromUtc, x.EffectiveToUtc }).HasDatabaseName("IX_MachineClassifierRules_Active_Window");
        });

        modelBuilder.Entity<DefectAnalyticsJobEntity>(b =>
        {
            b.ToTable("defect_analytics_jobs");
            b.HasKey(x => x.Id);
            b.Property(x => x.AnalysisType).HasMaxLength(64);
            b.Property(x => x.Status).HasMaxLength(32);
            b.Property(x => x.RequestHash).HasMaxLength(64);
            b.Property(x => x.ConfigurationJson).HasColumnType("longtext");
            b.Property(x => x.ResultJson).HasColumnType("longtext");
            b.Property(x => x.WarningsJson).HasColumnType("longtext");
            b.Property(x => x.ErrorMessage).HasColumnType("text");
            b.Property(x => x.CreatedBy).HasMaxLength(128);

            b.HasOne(x => x.Session)
                .WithMany(x => x.AnalyticsJobs)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.SessionId, x.AnalysisType }).HasDatabaseName("IX_DefectAnalytics_Session_Type");
            b.HasIndex(x => new { x.SessionId, x.RequestHash }).HasDatabaseName("IX_DefectAnalytics_Session_Hash");
            b.HasIndex(x => new { x.Status, x.ExpiresAtUtc }).HasDatabaseName("IX_DefectAnalytics_Status_Expires");
        });

        modelBuilder.Entity<OpDefectMappingEntity>(b =>
        {
            b.ToTable("op_defect_mappings");
            b.HasKey(x => x.Id);
            b.Property(x => x.MachineCode).HasMaxLength(128).IsRequired();
            b.Property(x => x.PackageName).HasMaxLength(128).IsRequired();
            b.Property(x => x.OpDefectName).HasMaxLength(128).IsRequired();
            b.Property(x => x.MachineDefectName).HasMaxLength(128).IsRequired();
            b.Property(x => x.DefectFamily).HasMaxLength(128);
            b.Property(x => x.ComponentGroup).HasMaxLength(128);
            b.Property(x => x.Description).HasMaxLength(512);

            b.HasIndex(x => x.OpDefectName).HasDatabaseName("IX_OpDefectMappings_OpName");
            b.HasIndex(x => x.MachineDefectName).HasDatabaseName("IX_OpDefectMappings_MachineName");
            b.HasIndex(x => new { x.MachineCode, x.PackageName, x.OpDefectName }).HasDatabaseName("IX_OpDefectMappings_Package_OpName");
        });

        modelBuilder.Entity<OpInspectionRecordEntity>(b =>
        {
            b.ToTable("op_inspection_records");
            b.HasKey(x => x.Id);
            b.Property(x => x.KeyId).HasMaxLength(128).IsRequired();
            b.Property(x => x.OpJudge).HasMaxLength(32).IsRequired();
            b.Property(x => x.OpDefectName).HasMaxLength(128);
            b.Property(x => x.MappedMachineDefect).HasMaxLength(128);
            b.Property(x => x.MappedDefectFamily).HasMaxLength(128);
            b.Property(x => x.LineCode).HasMaxLength(128);
            b.Property(x => x.MachineCode).HasMaxLength(128);
            b.Property(x => x.Notes).HasMaxLength(512);

            b.HasIndex(x => x.KeyId).HasDatabaseName("IX_OpInspectionRecords_KeyId");
            b.HasIndex(x => x.BatchId).HasDatabaseName("IX_OpInspectionRecords_BatchId");
            b.HasIndex(x => x.ProductRecordId).IsUnique().HasDatabaseName("UX_OpInspectionRecords_ProductRecordId");
            b.HasOne(x => x.ProductRecord)
                .WithMany()
                .HasForeignKey(x => x.ProductRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCommonEntityLengths(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUserEntity>().Property(x => x.Username).HasMaxLength(128);
        modelBuilder.Entity<AppUserEntity>().Property(x => x.DisplayName).HasMaxLength(256);
        modelBuilder.Entity<AppUserEntity>().Property(x => x.PasswordHash).HasMaxLength(256);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.Name).HasMaxLength(256);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.Type).HasMaxLength(64);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.Environment).HasMaxLength(64);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.Health).HasMaxLength(64);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.MachineCode).HasMaxLength(128);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.VerificationCategory).HasMaxLength(32);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.SchedulePeriod).HasMaxLength(32);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.Host).HasMaxLength(256);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.DatabaseName).HasMaxLength(256);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.Username).HasMaxLength(256);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.ApiBaseUrl).HasColumnType("text");
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.EncryptedSecret).HasColumnType("longtext");
        modelBuilder.Entity<ScanListEntity>().Property(x => x.Name).HasMaxLength(256);
        modelBuilder.Entity<ScanListEntity>().Property(x => x.MappingName).HasMaxLength(128);
        modelBuilder.Entity<ScanListEntity>().Property(x => x.MachineCode).HasMaxLength(128);
        modelBuilder.Entity<ScanListEntity>().Property(x => x.LineCode).HasMaxLength(128);
        modelBuilder.Entity<ScanListEntity>().Property(x => x.VerificationCategory).HasMaxLength(32);
        modelBuilder.Entity<ScanListEntity>().Property(x => x.Checksum).HasMaxLength(128);
        modelBuilder.Entity<ScanListEntity>().Property(x => x.EvidenceUri).HasColumnType("text");
        modelBuilder.Entity<ScanListEntity>().Property(x => x.CreatedBy).HasMaxLength(128);
        modelBuilder.Entity<ScanExecutionEntity>().Property(x => x.Name).HasMaxLength(256);
        modelBuilder.Entity<ScanExecutionEntity>().Property(x => x.ScanListName).HasMaxLength(256);
        modelBuilder.Entity<ScanExecutionEntity>().Property(x => x.Status).HasMaxLength(64);
        modelBuilder.Entity<ScanExecutionEntity>().Property(x => x.SourceNamesJson).HasColumnType("longtext");
        modelBuilder.Entity<ScanExecutionEntity>().Property(x => x.EvidenceUri).HasColumnType("text");
        modelBuilder.Entity<VerificationResultEntity>().Property(x => x.EntityId).HasMaxLength(128);
        modelBuilder.Entity<VerificationResultEntity>().Property(x => x.SourceName).HasMaxLength(256);
        modelBuilder.Entity<VerificationResultEntity>().Property(x => x.Status).HasMaxLength(64);
        modelBuilder.Entity<VerificationResultEntity>().Property(x => x.Severity).HasMaxLength(64);
        modelBuilder.Entity<VerificationResultEntity>().Property(x => x.RuleCode).HasMaxLength(128);
        modelBuilder.Entity<VerificationResultEntity>().Property(x => x.Message).HasColumnType("text");
        modelBuilder.Entity<AuditEventEntity>().Property(x => x.Actor).HasMaxLength(128);
        modelBuilder.Entity<AuditEventEntity>().Property(x => x.Action).HasMaxLength(128);
        modelBuilder.Entity<AuditEventEntity>().Property(x => x.TargetType).HasMaxLength(128);
        modelBuilder.Entity<AuditEventEntity>().Property(x => x.TargetId).HasMaxLength(128);
        modelBuilder.Entity<AuditEventEntity>().Property(x => x.Message).HasColumnType("text");
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.Backend).HasMaxLength(64);
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.Bucket).HasMaxLength(256);
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.ObjectName).HasMaxLength(512);
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.ContentType).HasMaxLength(128);
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.Uri).HasColumnType("text");
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.Path).HasColumnType("text");
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.MetadataJson).HasColumnType("longtext");
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.Purpose).HasMaxLength(128);
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.QueryKey).HasMaxLength(256);
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.ProductId).HasMaxLength(128);
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.SourceSystem).HasMaxLength(128);
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.Status).HasMaxLength(32);
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.PayloadJson).HasColumnType("longtext");
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.JudgeResult).HasColumnType("longtext");
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.Message).HasColumnType("longtext");
        modelBuilder.Entity<QueryProfileEntity>().Property(x => x.Name).HasMaxLength(256);
        modelBuilder.Entity<QueryProfileEntity>().Property(x => x.MachineCode).HasMaxLength(128);
        modelBuilder.Entity<QueryProfileEntity>().Property(x => x.LineCode).HasMaxLength(128);
        modelBuilder.Entity<QueryProfileEntity>().Property(x => x.VerificationCategory).HasMaxLength(32);
        modelBuilder.Entity<QueryProfileEntity>().Property(x => x.Concept).HasMaxLength(128);
        modelBuilder.Entity<QueryProfileEntity>().Property(x => x.SourceSystem).HasMaxLength(128);
        modelBuilder.Entity<QueryProfileEntity>().Property(x => x.SqlTemplate).HasColumnType("longtext");
        modelBuilder.Entity<QueryProfileEntity>().Property(x => x.ParameterSchemaJson).HasColumnType("longtext");
        modelBuilder.Entity<QueryProfileEntity>().Property(x => x.Notes).HasColumnType("text");
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.KeyId).HasMaxLength(128);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.Judge).HasMaxLength(32);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.PanelId).HasMaxLength(128);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.PanelJudge).HasMaxLength(32);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.NameDefect).HasMaxLength(256);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.Zone).HasMaxLength(128);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.DefectName).HasMaxLength(256);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.MachineCode).HasMaxLength(128);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.LineCode).HasMaxLength(128);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.ModelCode).HasMaxLength(128);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.OpReviewMark).HasMaxLength(32);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.OpReviewJudge).HasMaxLength(16);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.OpReviewDefectName).HasMaxLength(256);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.OpReviewMappedDefect).HasMaxLength(256);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.OpReviewNote).HasMaxLength(1024);
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.MetadataJson).HasColumnType("longtext");
        modelBuilder.Entity<ProductRecordEntity>().Property(x => x.DefectPackUrl).HasMaxLength(1024);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.KeyId).HasMaxLength(128);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.DefectTable).HasMaxLength(128);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.DefectName).HasMaxLength(256);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.DefectType).HasMaxLength(64);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.ComponentName).HasMaxLength(128);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.Severity).HasMaxLength(32);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.Judge).HasMaxLength(32);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.ImageUri).HasColumnType("text");
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.ImageUrl).HasColumnType("text");
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.Feature1StorageUri).HasColumnType("text");
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.Feature2StorageUri).HasColumnType("text");
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.FeatureUrl).HasColumnType("text");
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.CustomFeatureUrl).HasColumnType("text");
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.FeatureJson).HasColumnType("longtext");
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.CustomFeatureJson).HasColumnType("longtext");
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.Reason).HasColumnType("text");
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.EqpName).HasMaxLength(256);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.EmNo).HasMaxLength(45);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.SpecName).HasMaxLength(50);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.SpecJudge).HasMaxLength(256);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.SpecReason).HasMaxLength(256);
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.ModelCode).HasMaxLength(128).HasDefaultValue("UNASSIGNED");
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.MachineCode).HasMaxLength(128).HasDefaultValue("MACHINE-A");
        modelBuilder.Entity<DefectRecordEntity>().Property(x => x.LineCode).HasMaxLength(128).HasDefaultValue("LINE-1");
        modelBuilder.Entity<ScanListItemEntity>().Property(x => x.EntityId).HasMaxLength(128);
        modelBuilder.Entity<ScanListItemEntity>().Property(x => x.LineCode).HasMaxLength(128);
        modelBuilder.Entity<ScanListItemEntity>().Property(x => x.Mapping).HasMaxLength(128);
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.Category).HasMaxLength(32);
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.EntityId).HasMaxLength(128);
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.LineCode).HasMaxLength(128);
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.Mapping).HasMaxLength(128);
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.MachineCode).HasMaxLength(128);
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.Judge).HasMaxLength(32);
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.PanelJudge).HasMaxLength(32);
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.NameDefect).HasMaxLength(256);
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.AnomalyStatus).HasMaxLength(64);
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.AnalysisNotes).HasColumnType("text");
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.ConnectionName).HasMaxLength(256);
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.QueryName).HasMaxLength(256);
        modelBuilder.Entity<VerificationHistoryEntity>().Property(x => x.PayloadJson).HasColumnType("longtext");
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.Name).HasMaxLength(256);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.MachineCode).HasMaxLength(128);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.LineCode).HasMaxLength(128);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.ModelCode).HasMaxLength(128);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.LastRunStatus).HasMaxLength(64);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.LastRunMessage).HasColumnType("text");
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.ProductTableName).HasMaxLength(128);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.IdColumnName).HasMaxLength(128);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.FkIdColumnName).HasMaxLength(128);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.JudgeColumnName).HasMaxLength(128);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.JudgeFilterValuesJson).HasColumnType("longtext");
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.TimestampColumnName).HasMaxLength(128);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.ZoneColumnName).HasMaxLength(128);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.DefectNameColumnName).HasMaxLength(128);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.DateTimeInspectionColumnName).HasMaxLength(128);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Ignore(x => x.InspectedAtColumnName);
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.DefectTablesJson).HasColumnType("longtext");
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.DefectBypassRuleJson).HasColumnType("longtext");
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.ImageCrawlerApiUrl).HasColumnType("text");
        modelBuilder.Entity<OverkillPipelineConfigEntity>().Property(x => x.ImageCrawlerVpMode).HasMaxLength(32);
        modelBuilder.Entity<DatabaseConnectionEntity>().Property(x => x.ImageCrawlerApiUrl).HasColumnType("text");
        modelBuilder.Entity<DatabaseConnectionEntity>().Property(x => x.ImageCrawlerVpMode).HasMaxLength(32);
        modelBuilder.Entity<DatabaseConnectionEntity>().Property(x => x.ModelCode).HasMaxLength(128);
        modelBuilder.Entity<OverkillPipelineExecutionEntity>().Property(x => x.PipelineName).HasMaxLength(256);
        modelBuilder.Entity<OverkillPipelineExecutionEntity>().Property(x => x.TriggerType).HasMaxLength(32);
        modelBuilder.Entity<OverkillPipelineExecutionEntity>().Property(x => x.Status).HasMaxLength(64);
        modelBuilder.Entity<OverkillPipelineExecutionEntity>().Property(x => x.BypassRuleSummary).HasMaxLength(512);
        modelBuilder.Entity<OverkillPipelineExecutionEntity>().Property(x => x.ErrorMessage).HasColumnType("text");
        modelBuilder.Entity<OverkillPipelineExecutionEntity>().Property(x => x.ExecutionSummaryJson).HasColumnType("longtext");

        modelBuilder.Entity<LeakageBatchEntity>().Property(x => x.Name).HasMaxLength(256);
        modelBuilder.Entity<LeakageBatchEntity>().Property(x => x.SourceType).HasMaxLength(32);
        modelBuilder.Entity<LeakageBatchEntity>().Property(x => x.RawInputSummary).HasColumnType("text");
        modelBuilder.Entity<LeakageBatchEntity>().Property(x => x.CreatedBy).HasMaxLength(128);

        modelBuilder.Entity<LeakageQueueItemEntity>().Property(x => x.ProductId).HasMaxLength(128);
        modelBuilder.Entity<LeakageQueueItemEntity>().Property(x => x.LineCode).HasMaxLength(128);
        modelBuilder.Entity<LeakageQueueItemEntity>().Property(x => x.Status).HasMaxLength(64);
        modelBuilder.Entity<LeakageQueueItemEntity>().Property(x => x.ResolvedFkId).HasMaxLength(128);
        modelBuilder.Entity<LeakageQueueItemEntity>().Property(x => x.StationJudge).HasMaxLength(32);
        modelBuilder.Entity<LeakageQueueItemEntity>().Property(x => x.TableScanned).HasMaxLength(256);
        modelBuilder.Entity<LeakageQueueItemEntity>().Property(x => x.ErrorMessage).HasColumnType("text");
    }
}
