using System.Runtime.CompilerServices;

// One test at a time: several checks read whole queues, counts or exposure totals, which a test
// creating claims in parallel would change underneath them.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ChubbClaims.SmokeTests.Infrastructure;

/// <summary>A fact that needs the running stack; skipped (with the reason) when the API isn't reachable.</summary>
public sealed class SmokeFactAttribute : FactAttribute
{
    public SmokeFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = Api.NotRunningMessage;
        SkipType = typeof(Api);
        SkipUnless = nameof(Api.IsRunning);
    }
}

/// <inheritdoc cref="SmokeFactAttribute"/>
public sealed class SmokeTheoryAttribute : TheoryAttribute
{
    public SmokeTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = Api.NotRunningMessage;
        SkipType = typeof(Api);
        SkipUnless = nameof(Api.IsRunning);
    }
}
