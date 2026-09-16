using LoanPlatform.Contracts.Models;
using OriginationService.Data;

namespace OriginationService.Repositories;

/// <summary>
/// Real SQL Server-backed implementation of IApplicationRepository —
/// same interface as InMemoryApplicationRepository, so
/// ApplicationsController and UnderwritingDecisionEventHandler don't
/// change at all when this is swapped in via DI (see Program.cs's
/// conditional registration based on UseSqlServer).
///
/// Note on sync vs. async: the existing IApplicationRepository interface
/// is synchronous (matching the in-memory implementation it was written
/// against first). EF Core supports synchronous SaveChanges/query methods,
/// so this compiles and works correctly against that interface as-is —
/// but a real production version of this interface should be refactored
/// to async (SaveChangesAsync, ToListAsync, etc.) for proper scalability
/// under load. Not done here to avoid a much larger refactor across the
/// controller layer just for this addendum.
/// </summary>
public class SqlApplicationRepository : IApplicationRepository
{
    private readonly OriginationDbContext _db;

    public SqlApplicationRepository(OriginationDbContext db)
    {
        _db = db;
    }

    public LoanApplication Add(LoanApplication application)
    {
        var existing = _db.LoanApplications.Find(application.ApplicationId);
        if (existing is not null)
        {
            // Upsert semantics, matching InMemoryApplicationRepository's
            // Add() — also used by Update() and UpdateStatus() below,
            // which call Add() with a modified copy.
            ApplyToEntity(application, existing);
        }
        else
        {
            _db.LoanApplications.Add(ToEntity(application));
        }
        _db.SaveChanges();
        return application;
    }

    public LoanApplication? GetById(string applicationId)
    {
        var entity = _db.LoanApplications.Find(applicationId);
        return entity is null ? null : ToModel(entity);
    }

    public LoanApplication UpdateStatus(string applicationId, string newStatus)
    {
        var entity = _db.LoanApplications.Find(applicationId)
            ?? throw new InvalidOperationException($"Application {applicationId} not found.");
        entity.Status = newStatus;
        entity.UpdatedAt = DateTime.UtcNow;
        _db.SaveChanges();
        return ToModel(entity);
    }

    public IReadOnlyList<LoanApplication> GetAll() =>
        _db.LoanApplications.Select(e => ToModel(e)).ToList();

    private static LoanApplicationEntity ToEntity(LoanApplication m) => new()
    {
        ApplicationId = m.ApplicationId,
        CustomerId = m.CustomerId,
        FirstName = m.Applicant.FirstName,
        LastName = m.Applicant.LastName,
        DateOfBirth = m.Applicant.DateOfBirth,
        SsnLastFour = m.Applicant.SsnLastFour,
        Email = m.Applicant.Email,
        Phone = m.Applicant.Phone,
        AddressLine1 = m.Applicant.AddressLine1,
        City = m.Applicant.City,
        State = m.Applicant.State,
        ZipCode = m.Applicant.ZipCode,
        EmployerName = m.Employment.EmployerName,
        JobTitle = m.Employment.JobTitle,
        MonthlyIncome = m.Employment.MonthlyIncome,
        EmploymentMonths = m.Employment.EmploymentMonths,
        VehicleYear = m.Vehicle.Year,
        VehicleMake = m.Vehicle.Make,
        VehicleModel = m.Vehicle.Model,
        VehicleVin = m.Vehicle.Vin,
        VehicleMileage = m.Vehicle.Mileage,
        VehicleCondition = m.Vehicle.Condition,
        VehicleSalePrice = m.Vehicle.SalePrice,
        RequestedAmount = m.RequestedAmount,
        DownPayment = m.DownPayment,
        TermMonths = m.TermMonths,
        Channel = m.Channel,
        DealerName = m.DealerName,
        Status = m.Status,
        SubmittedAt = m.SubmittedAt.UtcDateTime,
    };

    private static void ApplyToEntity(LoanApplication m, LoanApplicationEntity entity)
    {
        entity.RequestedAmount = m.RequestedAmount;
        entity.TermMonths = m.TermMonths;
        entity.Status = m.Status;
        entity.UpdatedAt = DateTime.UtcNow;
    }

    private static LoanApplication ToModel(LoanApplicationEntity e) => new(
        ApplicationId: e.ApplicationId,
        CustomerId: e.CustomerId,
        Applicant: new Applicant(e.FirstName, e.LastName, e.DateOfBirth, e.SsnLastFour, e.Email, e.Phone, e.AddressLine1, e.City, e.State, e.ZipCode),
        Employment: new Employment(e.EmployerName, e.JobTitle, e.MonthlyIncome, e.EmploymentMonths),
        Vehicle: new Vehicle(e.VehicleYear, e.VehicleMake, e.VehicleModel, e.VehicleVin, e.VehicleMileage, e.VehicleCondition, e.VehicleSalePrice),
        RequestedAmount: e.RequestedAmount,
        DownPayment: e.DownPayment,
        TermMonths: e.TermMonths,
        Channel: e.Channel,
        DealerName: e.DealerName,
        Status: e.Status,
        SubmittedAt: e.SubmittedAt
    );
}

public class SqlDocumentRepository : IDocumentRepository
{
    private readonly OriginationDbContext _db;

    public SqlDocumentRepository(OriginationDbContext db)
    {
        _db = db;
    }

    public ApplicationDocument Add(ApplicationDocument document)
    {
        _db.ApplicationDocuments.Add(new ApplicationDocumentEntity
        {
            DocumentId = document.DocumentId,
            ApplicationId = document.ApplicationId,
            FileName = document.FileName,
            DocumentType = document.DocumentType,
            UploadedAt = document.UploadedAt.UtcDateTime,
            ExtractedDataJson = document.ExtractedDataJson,
        });
        _db.SaveChanges();
        return document;
    }

    public IReadOnlyList<ApplicationDocument> GetForApplication(string applicationId) =>
        _db.ApplicationDocuments
            .Where(d => d.ApplicationId == applicationId)
            .Select(d => new ApplicationDocument(d.DocumentId, d.ApplicationId, d.FileName, d.DocumentType, d.UploadedAt, d.ExtractedDataJson))
            .ToList();
}
