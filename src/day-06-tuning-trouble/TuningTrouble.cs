using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace TuningTrouble;

internal sealed class TuningTrouble {

    /// <summary>
    /// Minimum number of distinct characters required before a start-of-packet marker.
    /// </summary>
    private const int MinDistinctCharactersStartOfPacket = 4;

    /// <summary>
    /// Minimum number of distinct characters required before a start-of-message marker.
    /// </summary>
    private const int MinDistinctCharactersStartOfMessage = 14;

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>
    /// Tries to find the index of the first marker after a series of at least
    /// <paramref name="minDistinctCharacters"/> distinct characters in a given buffer.
    /// </summary>
    /// <param name="buffer">Buffer of characters for the search.</param>
    /// <param name="minDistinctCharacters">
    /// Positive number of distinct characters required before the marker.
    /// </param>
    /// <param name="firstMarkerIndex">
    /// Index of the first marker in the buffer if one was found (indicated by a return value of
    /// <see langword="true"/>), otherwise zero.
    /// </param>
    /// <returns>
    /// <see langword="True"/> if a marker was found, otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="minDistinctCharacters"/> is negative.
    /// </exception>
    private static bool TryFindFirstMarker(
        ReadOnlySpan<char> buffer,
        int minDistinctCharacters,
        out int firstMarkerIndex
    ) {
        Guard.IsGreaterThanOrEqualTo(minDistinctCharacters, 0);
        // The first marker may appear after at least the number of required distinct characters
        // have been read, which is why we start the search here. Note that if the remaining buffer
        // is shorter than the number of required distinct characters, there cannot possibly be
        // a marker and we return null in this case.
        for (int i = minDistinctCharacters; i < buffer.Length; i++) {
            // The straight-forward, naive solution would be to put all seen characters in a
            // HashSet<char> and check its length against the required number of distinct
            // characters. However, since we are only dealing with lowercase letters here, we may
            // as well opt for a faster and more advanced mechanism. Specifically, we can use a uint
            // which contains just enough bits (32) to store which of the 26 characters ('a' to 'z')
            // we have already seen. This not only reduces the memory footprint, but also lowers the
            // runtime by several orders of magnitude.
            uint seenCharacters = 0;
            for (int j = i - minDistinctCharacters; j < i; j++) {
                int characterIndex = buffer[j] - 'a';
                bool characterAlreadySeen = (seenCharacters & (1U << characterIndex)) != 0;
                if (characterAlreadySeen) {
                    break;
                }
                seenCharacters |= 1U << characterIndex;
            }
            if (uint.PopCount(seenCharacters) == minDistinctCharacters) {
                firstMarkerIndex = i;
                return true;
            }
        }
        firstMarkerIndex = default;
        return false;
    }

    /// <summary>Solves the <see cref="TuningTrouble"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ReadOnlySpan<char> buffer = File.ReadAllText(InputFile);
        TryFindFirstMarker(buffer, MinDistinctCharactersStartOfPacket, out int startOfPacket);
        TryFindFirstMarker(buffer, MinDistinctCharactersStartOfMessage, out int startOfMessage);
        textWriter.WriteLine(
            $"The first start-of-packet marker is detected after {startOfPacket} characters."
        );
        textWriter.WriteLine(
            $"The first start-of-message marker is detected after {startOfMessage} characters."
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