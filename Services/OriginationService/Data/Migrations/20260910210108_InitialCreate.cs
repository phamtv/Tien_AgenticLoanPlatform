using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OriginationService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoanApplications",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    SsnLastFour = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    ZipCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    EmployerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MonthlyIncome = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    EmploymentMonths = table.Column<int>(type: "int", nullable: false),
                    VehicleYear = table.Column<int>(type: "int", nullable: false),
                    VehicleMake = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    VehicleModel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    VehicleVin = table.Column<string>(type: "nvarchar(17)", maxLength: 17, nullable: false),
                    VehicleMileage = table.Column<int>(type: "int", nullable: false),
                    VehicleCondition = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    VehicleSalePrice = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    DownPayment = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    TermMonths = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DealerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanApplications", x => x.ApplicationId);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationDocuments",
                columns: table => new
                {
                    DocumentId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApplicationId = table.Column<string>(type: "nvarchar(20)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExtractedDataJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationDocuments", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_ApplicationDocuments_LoanApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "LoanApplications",
                        principalColumn: "ApplicationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CreditBureauResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationId = table.Column<string>(type: "nvarchar(20)", nullable: false),
                    BureauName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ScoreModel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreditScore = table.Column<int>(type: "int", nullable: false),
                    RiskTier = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InquiriesLast6Months = table.Column<int>(type: "int", nullable: false),
                    TotalMonthlyDebtPayments = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    HasBankruptcy = table.Column<bool>(type: "bit", nullable: false),
                    HasLien = table.Column<bool>(type: "bit", nullable: false),
                    PulledAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditBureauResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditBureauResults_LoanApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "LoanApplications",
                        principalColumn: "ApplicationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IdentityVerificationResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationId = table.Column<string>(type: "nvarchar(20)", nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IdentityConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    FraudFlagRaised = table.Column<bool>(type: "bit", nullable: false),
                    AddressMatch = table.Column<bool>(type: "bit", nullable: false),
                    SsnMatch = table.Column<bool>(type: "bit", nullable: false),
                    DobMatch = table.Column<bool>(type: "bit", nullable: false),
                    VerificationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityVerificationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdentityVerificationResults_LoanApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "LoanApplications",
                        principalColumn: "ApplicationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tradelines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreditBureauResultId = table.Column<int>(type: "int", nullable: false),
                    CreditorName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccountType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    CreditLimitOrOriginalAmount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    MonthlyPayment = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OpenedDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tradelines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tradelines_CreditBureauResults_CreditBureauResultId",
                        column: x => x.CreditBureauResultId,
                        principalTable: "CreditBureauResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocuments_ApplicationId",
                table: "ApplicationDocuments",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditBureauResults_ApplicationId",
                table: "CreditBureauResults",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityVerificationResults_ApplicationId",
                table: "IdentityVerificationResults",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_CustomerId",
                table: "LoanApplications",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_Status",
                table: "LoanApplications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Tradelines_CreditBureauResultId",
                table: "Tradelines",
                column: "CreditBureauResultId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationDocuments");

            migrationBuilder.DropTable(
                name: "IdentityVerificationResults");

            migrationBuilder.DropTable(
                name: "Tradelines");

            migrationBuilder.DropTable(
                name: "CreditBureauResults");

            migrationBuilder.DropTable(
                name: "LoanApplications");
        }
    }
}
