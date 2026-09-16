namespace LoanPlatform.Common.Vendors;

/// <summary>
/// A stable string hash, used anywhere this codebase needs "the same
/// input always produces the same simulated output" — deterministic mock
/// credit scores, identity verification results, tradelines, etc.
///
/// Real bug this exists to fix: several classes here originally used
/// string.GetHashCode() for this. .NET randomizes string hash codes
/// per-process by default (a security mitigation against hash-flooding
/// DoS attacks) — meaning the *same* customer ID produced a *different*
/// simulated credit score on every service restart, silently breaking
/// the "consistent demo data" comments already in this codebase. Caught
/// by actually restarting the services between test runs and noticing
/// the same customer ID's score change — not something a single test run
/// would ever reveal.
///
/// FNV-1a is a simple, well-known non-cryptographic hash with no such
/// randomization — same input, same output, every time, in every process.
/// </summary>
public static class StableHash
{
    public static int Compute(string input)
    {
        unchecked
        {
            const uint fnvPrime = 16777619;
            uint hash = 2166136261;
            foreach (var c in input)
            {
                hash ^= c;
                hash *= fnvPrime;
            }
            return (int)(hash & 0x7FFFFFFF); // mask off the sign bit — callers expect a non-negative int
        }
    }
}
