using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace RucksackReorganization;

internal sealed class RucksackReorganization {

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Returns a priority for a given item.</summary>
    /// <remarks>Only lowercase or uppercase letters count as valid items.</remarks>
    /// <param name="item">Item to get a priority for.</param>
    /// <returns>A priority for the given <paramref name="item"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="item"/> is not a lowercase or uppercase letter.
    /// </exception>
    private static int Priority(char item) => item switch {
        (>= 'a') and (<= 'z') => item - 'a' + 1,
        (>= 'A') and (<= 'Z') => item - 'A' + 27,
        _ => throw new ArgumentOutOfRangeException(
            nameof(item),
            $"'{item}' does not represent a valid item."
        )
    };

    /// <summary>Solves the <see cref="RucksackReorganization"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ImmutableArray<string> lines = [.. File.ReadLines(InputFile)];
        int singleSum = lines
            .Sum(
                line => line
                    .Take(line.Length / 2)
                    .Intersect(line.Skip(line.Length / 2))
                    .Sum(Priority)
            );
        int groupSum = lines
            .Chunk(3)
            .Sum(group => group[0].Intersect(group[1]).Intersect(group[2]).Sum(Priority));
        textWriter.WriteLine($"The sum of priorities for individual rucksacks is {singleSum}.");
        textWriter.WriteLine($"The sum of priorities for groups of three is {groupSum}.");
    }

    private static void Main(string[] args) {
        if (args.Contains("--benchmark")) {
            BenchmarkRunner.Run<Benchmark>();
        }
        else {
            Solve(Console.Out);
        }
    }

}