using System.Diagnostics.CodeAnalysis;
using System.IO;
using BenchmarkDotNet.Attributes;

namespace PyroclasticFlow;

/// <summary>
/// Represents a <see cref="Benchmark"/> for the <see cref="PyroclasticFlow"/> puzzle.
/// </summary>
[MemoryDiagnoser]
public class Benchmark {

    /// <summary>Runs a benchmark for the <see cref="PyroclasticFlow"/> puzzle.</summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Benchmarking static methods is not supported."
    )]
    public void Run() => PyroclasticFlow.Solve(TextWriter.Null);

}