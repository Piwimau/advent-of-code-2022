using System.Diagnostics.CodeAnalysis;
using System.IO;
using BenchmarkDotNet.Attributes;

namespace MonkeyMap;

/// <summary>Represents a <see cref="Benchmark"/> for the <see cref="MonkeyMap"/> puzzle.</summary>
[MemoryDiagnoser]
public class Benchmark {

    /// <summary>Runs a benchmark for the <see cref="MonkeyMap"/> puzzle.</summary>
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Benchmarking static methods is not supported."
    )]
    public void Run() => MonkeyMap.Solve(TextWriter.Null);

}