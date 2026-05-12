using System.Diagnostics.CodeAnalysis;
using System.IO;
using BenchmarkDotNet.Attributes;

namespace HillClimbingAlgorithm;

/// <summary>
/// Represents a <see cref="Benchmark"/> for the <see cref="HillClimbingAlgorithm"/> puzzle.
/// </summary>
[MemoryDiagnoser]
public class Benchmark {

    /// <summary>Runs a benchmark for the <see cref="HillClimbingAlgorithm"/> puzzle.</summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Benchmarking static methods is not supported."
    )]
    public void Run() => HillClimbingAlgorithm.Solve(TextWriter.Null);

}