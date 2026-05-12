using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace GrovePositioningSystem;

internal sealed class GrovePositioningSystem {

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Decryption key used for part two of the puzzle.</summary>
    private const int DecryptionKey = 811589153;

    /// <summary>
    /// Calculates the sum of the grove coordinates using a given sequence of encrypted numbers and
    /// the positive number of mixing passes to perform.
    /// </summary>
    /// <param name="numbers">
    /// Initial sequence of encrypted numbers out of which exactly one must be a zero.
    /// </param>
    /// <param name="mixingPasses">Positive number of mixing passes to perform.</param>
    /// <returns>The sum of the grove coordinates.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="mixingPasses"/> is negative or not exactly one out of
    /// <paramref name="numbers"/> is a zero.
    /// </exception>
    private static long SumOfGroveCoordinates(ImmutableArray<long> numbers, int mixingPasses) {
        Guard.IsGreaterThanOrEqualTo(mixingPasses, 0);
        if (numbers.Count(number => number == 0L) != 1) {
            throw new ArgumentOutOfRangeException(
                nameof(numbers),
                "Exactly one out of the encrypted numbers must be a zero."
            );
        }
        Span<int> indices = [.. numbers.Select((_, index) => index)];
        for (int mixingPass = 0; mixingPass < mixingPasses; mixingPass++) {
            for (int originalIndex = 0; originalIndex < numbers.Length; originalIndex++) {
                int currentIndex = indices.IndexOf(originalIndex);
                int newIndex = (int) ((currentIndex + numbers[originalIndex]) % (numbers.Length - 1));
                if (newIndex < 0) {
                    newIndex += numbers.Length - 1;
                }
                if (newIndex > currentIndex) {
                    indices[(currentIndex + 1)..(newIndex + 1)].CopyTo(indices[currentIndex..]);
                }
                else if (newIndex < currentIndex) {
                    indices[newIndex..currentIndex].CopyTo(indices[(newIndex + 1)..]);
                }
                indices[newIndex] = originalIndex;
            }
        }
        int indexOfZero = indices.IndexOf(numbers.IndexOf(0L));
        return numbers[indices[(indexOfZero + 1000) % numbers.Length]]
            + numbers[indices[(indexOfZero + 2000) % numbers.Length]]
            + numbers[indices[(indexOfZero + 3000) % numbers.Length]];
    }

    /// <summary>Solves the <see cref="GrovePositioningSystem"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ImmutableArray<long> numbers = [.. File.ReadLines(InputFile).Select(long.Parse)];
        ImmutableArray<long> decryptedNumbers = [
            .. numbers.Select(number => number * DecryptionKey)
        ];
        long sumOnePass = SumOfGroveCoordinates(numbers, 1);
        long sumTenPasses = SumOfGroveCoordinates(decryptedNumbers, 10);
        textWriter.WriteLine(
            $"The sum of the grove coordinates using 1 mixing pass is {sumOnePass}."
        );
        textWriter.WriteLine(
            $"The sum of the grove coordinates using 10 mixing passes is {sumTenPasses}."
        );
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