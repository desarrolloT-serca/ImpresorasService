-- SQL Server - Esquema inicial V1 para Impresoras Service
-- Nota: script base, ajustar longitudes y tipos segun volumen real.

CREATE TABLE dbo.PrintJobs (
    JobId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    SourceSystem NVARCHAR(50) NOT NULL,
    ExternalJobId NVARCHAR(120) NOT NULL,
    StoreId INT NOT NULL,
    DocumentType NVARCHAR(80) NOT NULL,
    Channel NVARCHAR(40) NOT NULL CONSTRAINT DF_PrintJobs_Channel DEFAULT ('DEFAULT'),
    PdfBlob VARBINARY(MAX) NOT NULL,
    PdfSha256 CHAR(64) NOT NULL,
    Status NVARCHAR(40) NOT NULL,
    AttemptCount INT NOT NULL CONSTRAINT DF_PrintJobs_AttemptCount DEFAULT (0),
    NextRetryAtUtc DATETIME2 NULL,
    LastErrorCode NVARCHAR(60) NULL,
    LastErrorMessage NVARCHAR(1000) NULL,
    CorrelationId UNIQUEIDENTIFIER NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_PrintJobs_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_PrintJobs_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    RowVersion ROWVERSION NOT NULL
);
GO

-- Idempotencia fuerte por origen y id externo
CREATE UNIQUE INDEX UX_PrintJobs_Source_External
ON dbo.PrintJobs(SourceSystem, ExternalJobId);
GO

-- Defensa adicional contra duplicados por contenido/tienda/tipo
CREATE NONCLUSTERED INDEX IX_PrintJobs_DedupHash
ON dbo.PrintJobs(StoreId, DocumentType, PdfSha256, CreatedAtUtc);
GO

CREATE NONCLUSTERED INDEX IX_PrintJobs_Status_NextRetry
ON dbo.PrintJobs(Status, NextRetryAtUtc);
GO

CREATE TABLE dbo.PrintJobEvents (
    EventId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    JobId UNIQUEIDENTIFIER NOT NULL,
    EventType NVARCHAR(60) NOT NULL,
    OldStatus NVARCHAR(40) NULL,
    NewStatus NVARCHAR(40) NULL,
    ErrorCode NVARCHAR(60) NULL,
    Message NVARCHAR(1000) NULL,
    ActorType NVARCHAR(30) NOT NULL, -- system/user
    ActorId NVARCHAR(120) NULL,
    MetadataJson NVARCHAR(MAX) NULL,
    OccurredAtUtc DATETIME2 NOT NULL CONSTRAINT DF_PrintJobEvents_OccurredAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_PrintJobEvents_PrintJobs FOREIGN KEY (JobId) REFERENCES dbo.PrintJobs(JobId)
);
GO

CREATE NONCLUSTERED INDEX IX_PrintJobEvents_JobId_OccurredAt
ON dbo.PrintJobEvents(JobId, OccurredAtUtc DESC);
GO

CREATE TABLE dbo.Printers (
    PrinterId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PrinterName NVARCHAR(120) NOT NULL,
    SpoolQueue NVARCHAR(200) NOT NULL, -- ejemplo: \\server\queue
    StoreId INT NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Printers_IsActive DEFAULT (1),
    CapabilitiesJson NVARCHAR(MAX) NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_Printers_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_Printers_UpdatedAt DEFAULT (SYSUTCDATETIME())
);
GO

CREATE UNIQUE INDEX UX_Printers_Store_Queue
ON dbo.Printers(StoreId, SpoolQueue);
GO

CREATE TABLE dbo.RoutingRules (
    RuleId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Priority INT NOT NULL, -- menor valor = mayor prioridad
    StoreId INT NULL,
    DocumentType NVARCHAR(80) NULL,
    Channel NVARCHAR(40) NULL,
    PrinterId INT NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_RoutingRules_IsActive DEFAULT (1),
    ValidFromUtc DATETIME2 NOT NULL CONSTRAINT DF_RoutingRules_ValidFrom DEFAULT (SYSUTCDATETIME()),
    ValidToUtc DATETIME2 NULL,
    CreatedBy NVARCHAR(120) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_RoutingRules_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_RoutingRules_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_RoutingRules_Printers FOREIGN KEY (PrinterId) REFERENCES dbo.Printers(PrinterId)
);
GO

CREATE NONCLUSTERED INDEX IX_RoutingRules_Resolve
ON dbo.RoutingRules(IsActive, Priority, StoreId, DocumentType, Channel);
GO

-- Tabla de alertas operativas (ErrorFinal y otras futuras)
CREATE TABLE dbo.OperationalAlerts (
    AlertId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    JobId UNIQUEIDENTIFIER NULL,
    StoreId INT NULL,
    Severity NVARCHAR(20) NOT NULL, -- High/Medium/Low
    AlertType NVARCHAR(60) NOT NULL, -- ErrorFinal, etc.
    Message NVARCHAR(1000) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_OperationalAlerts_IsActive DEFAULT (1),
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_OperationalAlerts_CreatedAt DEFAULT (SYSUTCDATETIME()),
    AcknowledgedBy NVARCHAR(120) NULL,
    AcknowledgedAtUtc DATETIME2 NULL
);
GO

CREATE NONCLUSTERED INDEX IX_OperationalAlerts_Store_Active
ON dbo.OperationalAlerts(StoreId, IsActive, CreatedAtUtc DESC);
GO
