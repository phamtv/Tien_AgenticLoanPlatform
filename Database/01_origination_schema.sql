-- Origination Service database
-- Owns: loan applications (with real applicant/employment/vehicle detail),
-- credit bureau pull results (including tradelines), identity verification
-- results, and uploaded document metadata.
--
-- Run against SQL Server 2012+ (matches the JD's stated minimum). Each
-- microservice owns its own database — no foreign keys reach into another
-- service's schema; cross-service references (e.g. ApplicationId showing
-- up in the Underwriting database) are just string identifiers, resolved
-- at query time via the owning service's API if needed, never a database
-- join across service boundaries.

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'OriginationDb')
BEGIN
    CREATE DATABASE OriginationDb;
END
GO

USE OriginationDb;
GO

-- One row per application, with real LOS intake fields — applicant
-- demographics, employment, and vehicle detail — not just a bare amount
-- and VIN.
CREATE TABLE dbo.LoanApplications (
    ApplicationId       VARCHAR(20)     NOT NULL PRIMARY KEY,
    CustomerId          VARCHAR(50)     NOT NULL,

    -- Applicant
    FirstName           NVARCHAR(100)   NOT NULL,
    LastName             NVARCHAR(100)   NOT NULL,
    DateOfBirth          DATE            NOT NULL,
    SsnLastFour          VARCHAR(4)      NOT NULL, -- never store a full SSN, even in a demo schema
    Email                NVARCHAR(200)   NOT NULL,
    Phone                VARCHAR(20)     NOT NULL,
    AddressLine1         NVARCHAR(200)   NOT NULL,
    City                 NVARCHAR(100)   NOT NULL,
    State                VARCHAR(2)      NOT NULL,
    ZipCode              VARCHAR(10)     NOT NULL,

    -- Employment
    EmployerName         NVARCHAR(200)   NOT NULL,
    JobTitle             NVARCHAR(100)   NOT NULL,
    MonthlyIncome        DECIMAL(12,2)   NOT NULL,
    EmploymentMonths     INT             NOT NULL,

    -- Vehicle
    VehicleYear          INT             NOT NULL,
    VehicleMake          NVARCHAR(50)    NOT NULL,
    VehicleModel         NVARCHAR(50)    NOT NULL,
    VehicleVin           VARCHAR(17)     NOT NULL,
    VehicleMileage       INT             NOT NULL,
    VehicleCondition     VARCHAR(10)     NOT NULL, -- New | Used
    VehicleSalePrice     DECIMAL(12,2)   NOT NULL,

    -- Loan terms
    RequestedAmount      DECIMAL(12,2)   NOT NULL,
    DownPayment          DECIMAL(12,2)   NOT NULL,
    TermMonths           INT             NOT NULL,
    Channel              VARCHAR(20)     NOT NULL, -- Online | Dealer | Branch
    DealerName           NVARCHAR(200)   NULL,

    Status               VARCHAR(30)     NOT NULL, -- Submitted | UnderwritingInProgress | Approved | Denied | Funded
    SubmittedAt          DATETIME2       NOT NULL,
    UpdatedAt            DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT CK_LoanApplications_Status CHECK (Status IN ('Submitted', 'UnderwritingInProgress', 'Approved', 'Denied', 'Funded')),
    CONSTRAINT CK_LoanApplications_Condition CHECK (VehicleCondition IN ('New', 'Used')),
    CONSTRAINT CK_LoanApplications_Channel CHECK (Channel IN ('Online', 'Dealer', 'Branch'))
);
GO

CREATE INDEX IX_LoanApplications_CustomerId ON dbo.LoanApplications(CustomerId);
CREATE INDEX IX_LoanApplications_Status ON dbo.LoanApplications(Status);
GO

CREATE TABLE dbo.CreditBureauResults (
    Id                        INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
    ApplicationId             VARCHAR(20)        NOT NULL,
    BureauName                VARCHAR(30)        NOT NULL, -- Experian | TransUnion
    ScoreModel                VARCHAR(50)        NOT NULL, -- e.g. "FICO Auto Score 8", "VantageScore 4.0"
    CreditScore               INT                NOT NULL,
    RiskTier                  VARCHAR(20)        NOT NULL, -- Prime | Near-Prime | Subprime
    InquiriesLast6Months      INT                NOT NULL,
    TotalMonthlyDebtPayments  DECIMAL(12,2)      NOT NULL, -- sum of tradelines' MonthlyPayment — feeds DTI in underwriting
    HasBankruptcy             BIT                NOT NULL,
    HasLien                   BIT                NOT NULL,
    PulledAt                  DATETIME2          NOT NULL,

    CONSTRAINT FK_CreditBureauResults_Application FOREIGN KEY (ApplicationId)
        REFERENCES dbo.LoanApplications(ApplicationId)
);
GO

-- One row per tradeline on a pulled credit report — a real bureau pull
-- returns several of these per application, not a single flat record.
CREATE TABLE dbo.Tradelines (
    Id                            INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
    CreditBureauResultId          INT                NOT NULL,
    CreditorName                  NVARCHAR(100)      NOT NULL,
    AccountType                   VARCHAR(20)        NOT NULL, -- Revolving | Installment | Mortgage
    Balance                       DECIMAL(12,2)      NOT NULL,
    CreditLimitOrOriginalAmount   DECIMAL(12,2)      NOT NULL,
    MonthlyPayment                DECIMAL(12,2)      NOT NULL,
    PaymentStatus                 VARCHAR(20)        NOT NULL, -- Current | 30DaysLate | 60DaysLate | Charged Off
    OpenedDate                    DATE               NOT NULL,

    CONSTRAINT FK_Tradelines_CreditBureauResult FOREIGN KEY (CreditBureauResultId)
        REFERENCES dbo.CreditBureauResults(Id)
);
GO

CREATE TABLE dbo.IdentityVerificationResults (
    Id                  INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
    ApplicationId       VARCHAR(20)        NOT NULL,
    ProviderName        VARCHAR(30)        NOT NULL, -- LexisNexis | TrueID | Informed
    IdentityConfirmed   BIT                NOT NULL,
    FraudFlagRaised     BIT                NOT NULL,
    AddressMatch        BIT                NOT NULL,
    SsnMatch            BIT                NOT NULL,
    DobMatch            BIT                NOT NULL,
    VerificationId      VARCHAR(50)        NOT NULL,
    VerifiedAt          DATETIME2          NOT NULL,

    CONSTRAINT FK_IdentityVerificationResults_Application FOREIGN KEY (ApplicationId)
        REFERENCES dbo.LoanApplications(ApplicationId)
);
GO

CREATE TABLE dbo.ApplicationDocuments (
    DocumentId       VARCHAR(20)     NOT NULL PRIMARY KEY,
    ApplicationId    VARCHAR(20)     NOT NULL,
    FileName         NVARCHAR(255)   NOT NULL,
    DocumentType     VARCHAR(50)     NOT NULL,
    UploadedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_ApplicationDocuments_Application FOREIGN KEY (ApplicationId)
        REFERENCES dbo.LoanApplications(ApplicationId)
);
GO

CREATE INDEX IX_CreditBureauResults_ApplicationId ON dbo.CreditBureauResults(ApplicationId);
CREATE INDEX IX_Tradelines_CreditBureauResultId ON dbo.Tradelines(CreditBureauResultId);
CREATE INDEX IX_IdentityVerificationResults_ApplicationId ON dbo.IdentityVerificationResults(ApplicationId);
CREATE INDEX IX_ApplicationDocuments_ApplicationId ON dbo.ApplicationDocuments(ApplicationId);
GO
