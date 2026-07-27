using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

using VerifyXunit;

using Xunit;

namespace DynamicData.Tests.RxContract;

/// <summary>
/// Locks in the terminal event behaviour of every operator overload in the library.
/// </summary>
/// <remarks>
/// <para>
/// The approved files record what each overload does today, warts and all. A good many operators currently
/// fail to deliver <c>OnCompleted</c>, or mishandle <c>OnError</c>, and those failures are recorded rather
/// than hidden so that the tests pass on a clean checkout. See the tracking issue for the catalogue.
/// </para>
/// <para>
/// Any diff against the approved files means behaviour moved. If an operator was fixed, approve the new file
/// as part of the fix. If an operator regressed, the diff is the bug report.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage]
public class RxContractFixture
{
    /// <summary>
    /// Records terminal event behaviour for operators which consume cache changesets.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public Task CacheOperators() => Verifier.Verify(ContractSweep.Report("cache", "aggregation/cache", "any")).UseDirectory("Approved");

    /// <summary>
    /// Records terminal event behaviour for operators which consume list changesets.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public Task ListOperators() => Verifier.Verify(ContractSweep.Report("list", "aggregation/list")).UseDirectory("Approved");
}
