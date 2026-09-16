using System.Collections.Concurrent;
using LoanPlatform.Contracts.Models;

namespace OriginationService.Repositories;

/// <summary>
/// In-memory application store — same pattern as the earlier Node.js/C#
/// demo backends in this project: simple enough to demo and test without
/// a real database, with the understanding that a production version
/// would swap this for SQL Server.
/// </summary>
public interface IApplicationRepository
{
    LoanApplication Add(LoanApplication application);
    LoanApplication? GetById(string applicationId);
    LoanApplication UpdateStatus(string applicationId, string newStatus);
    IReadOnlyList<LoanApplication> GetAll();
}

public class InMemoryApplicationRepository : IApplicationRepository
{
    private readonly ConcurrentDictionary<string, LoanApplication> _applications = new();

    public LoanApplication Add(LoanApplication application)
    {
        _applications[application.ApplicationId] = application;
        return application;
    }

    public LoanApplication? GetById(string applicationId) =>
        _applications.GetValueOrDefault(applicationId);

    public LoanApplication UpdateStatus(string applicationId, string newStatus)
    {
        var existing = _applications[applicationId];
        var updated = existing with { Status = newStatus };
        _applications[applicationId] = updated;
        return updated;
    }

    public IReadOnlyList<LoanApplication> GetAll() => _applications.Values.ToList();
}
