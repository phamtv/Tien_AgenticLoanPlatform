-- Underwriting Service database
-- Owns: underwriting decisions, including real DTI/LTV ratios and
-- stipulations, not just an approve/deny flag.

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'UnderwritingDb')
BEGIN
    CREATE DATABASE UnderwritingDb;
END
GO

USE UnderwritingDb;
GO

CREATE TABLE dbo.UnderwritingDecisions (
    ApplicationId          VARCHAR(20)     NOT NULL PRIMARY KEY,
    Approved               BIT             NOT NULL,
    Reason                 NVARCHAR(500)   NOT NULL,
    ApprovedAmount         DECIMAL(12,2)   NULL, -- NULL when denied
    InterestRate           DECIMAL(5,2)    NULL, -- NULL when denied
    CreditScore            INT             NOT NULL,
    DebtToIncomeRatio      DECIMAL(5,4)    NOT NULL, -- e.g. 0.4500 = 45.00%
    LoanToValueRatio       DECIMAL(5,4)    NOT NULL,
    DecidedAt              DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),

    -- A denied application must not carry an approved amount or rate —
    -- enforced at the database level, not just in application code.
    CONSTRAINT CK_UnderwritingDecisions_ApprovalConsistency CHECK (
        (Approved = 1 AND ApprovedAmount IS NOT NULL AND InterestRate IS NOT NULL)
        OR
        (Approved = 0 AND ApprovedAmount IS NULL AND InterestRate IS NULL)
    )
);
GO

-- One row per stipulation attached to a decision (e.g. "Proof of income
-- required") — a real underwriting decision can carry several, not just
-- a single condition string.
CREATE TABLE dbo.Stipulations (
    Id              INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
    ApplicationId   VARCHAR(20)        NOT NULL,
    Description     NVARCHAR(300)      NOT NULL,

    CONSTRAINT FK_Stipulations_Decision FOREIGN KEY (ApplicationId)
        REFERENCES dbo.UnderwritingDecisions(ApplicationId)
);
GO

CREATE INDEX IX_UnderwritingDecisions_Approved ON dbo.UnderwritingDecisions(Approved);
CREATE INDEX IX_Stipulations_ApplicationId ON dbo.Stipulations(ApplicationId);
GO
