-- Funding Service database
-- Owns: funding/disbursement records, including term and disbursement
-- method (ACH vs. dealer wire).

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'FundingDb')
BEGIN
    CREATE DATABASE FundingDb;
END
GO

USE FundingDb;
GO

CREATE TABLE dbo.Fundings (
    LoanId               VARCHAR(20)     NOT NULL PRIMARY KEY,
    ApplicationId        VARCHAR(20)     NOT NULL UNIQUE, -- one funding per application
    FundedAmount         DECIMAL(12,2)   NOT NULL,
    InterestRate         DECIMAL(5,2)    NOT NULL,
    TermMonths           INT             NOT NULL,
    DisbursementMethod   VARCHAR(20)     NOT NULL, -- ACH | DealerWire
    FundedAt             DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT CK_Fundings_DisbursementMethod CHECK (DisbursementMethod IN ('ACH', 'DealerWire'))
);
GO

CREATE INDEX IX_Fundings_ApplicationId ON dbo.Fundings(ApplicationId);
GO
