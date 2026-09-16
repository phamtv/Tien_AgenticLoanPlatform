-- Servicing Service database
-- Owns: active loans (with a real customer-facing account number,
-- payment method, and billing cycle day) and their payment history.

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'ServicingDb')
BEGIN
    CREATE DATABASE ServicingDb;
END
GO

USE ServicingDb;
GO

CREATE TABLE dbo.Loans (
    LoanId               VARCHAR(20)     NOT NULL PRIMARY KEY,
    AccountNumber        VARCHAR(10)     NOT NULL UNIQUE, -- customer-facing account number, distinct from the internal LoanId
    ApplicationId        VARCHAR(20)     NOT NULL UNIQUE,
    PrincipalAmount      DECIMAL(12,2)   NOT NULL,
    InterestRate         DECIMAL(5,2)    NOT NULL,
    TermMonths           INT             NOT NULL,
    CurrentBalance       DECIMAL(12,2)   NOT NULL,
    PaymentMethod        VARCHAR(20)     NOT NULL, -- AutoPayACH | Manual
    DueDayOfMonth        INT             NOT NULL,
    Status               VARCHAR(20)     NOT NULL DEFAULT 'Active', -- Active | Delinquent | PaidOff
    OriginatedAt         DATETIME2       NOT NULL,

    CONSTRAINT CK_Loans_BalanceNonNegative CHECK (CurrentBalance >= 0),
    CONSTRAINT CK_Loans_Status CHECK (Status IN ('Active', 'Delinquent', 'PaidOff')),
    CONSTRAINT CK_Loans_DueDayOfMonth CHECK (DueDayOfMonth BETWEEN 1 AND 28)
);
GO

CREATE TABLE dbo.Payments (
    PaymentId    VARCHAR(20)     NOT NULL PRIMARY KEY,
    LoanId       VARCHAR(20)     NOT NULL,
    Amount       DECIMAL(12,2)   NOT NULL,
    PaidAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_Payments_Loan FOREIGN KEY (LoanId) REFERENCES dbo.Loans(LoanId),
    CONSTRAINT CK_Payments_AmountPositive CHECK (Amount > 0)
);
GO

CREATE INDEX IX_Payments_LoanId ON dbo.Payments(LoanId);
GO
