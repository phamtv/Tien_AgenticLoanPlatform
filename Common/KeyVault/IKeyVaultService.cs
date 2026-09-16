namespace LoanPlatform.Common.KeyVault;

/// <summary>
/// Same key vault abstraction pattern as the earlier backend-dotnet demo:
/// env provider for local dev (fully working), Azure provider for
/// production (documented, unverified here — see AzureKeyVaultService.reference.cs.txt).
/// </summary>
public interface IKeyVaultService
{
    Task<string> GetSecretAsync(string name);
}
