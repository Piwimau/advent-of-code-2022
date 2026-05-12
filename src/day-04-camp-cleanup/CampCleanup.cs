using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace CampCleanup;

internal sealed partial class CampCleanup {

    /// <summary>Represents a <see cref="Section"/> with a start and end.</summary>
    /// <param name="Start">Start of the <see cref="Section"/>.</param>
    /// <param name="End">End of the <see cref="Section"/> (inclusive).</param>
    private readonly partial record struct Section(int Start, int End) {

        [GeneratedRegex("^\\d+-\\d+$")]
        private static partial Regex SectionRegex();

        /// <summary>Parses a <see cref="Section"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must contain two positive integers separated by a '-',
        /// as in "28-42". The left integer counts as the start of the <see cref="Section"/> and
        /// must be less than or equal to the right integer.
        /// </remarks>
        /// <param name="s">String to parse a <see cref="Section"/> from.</param>
        /// <returns>A <see cref="Section"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> has an invalid format or the start is greater than the
        /// end.
        /// </exception>
        public static Section Parse(ReadOnlySpan<char> s) {
            if (!SectionRegex().IsMatch(s)) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"The string \"{s}\" does not represent a valid section."
                );
            }
            int dashIndex = s.IndexOf('-');
            int start = int.Parse(s[..dashIndex], CultureInfo.InvariantCulture);
            int end = int.Parse(s[(dashIndex + 1)..], CultureInfo.InvariantCulture);
            if (start > end) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"Start ({start}) must be less than or equal to end ({end})."
                );
            }
            return new Section(start, end);
        }

        /// <summary>Determines if this <see cref="Section"/> fully contains a given one.</summary>
        /// <param name="other">Other <see cref="Section"/> for the check.</param>
        /// <returns>
        /// <see langword="True"/> if this <see cref="Section"/> fully contains the given one,
        /// otherwise <see langword="false"/>.
        /// </returns>
        public bool FullyContains(Section other) => (other.Start >= Start) && (other.End <= End);

        /// <summary>Determines if this <see cref="Section"/> overlaps with a given one.</summary>
        /// <param name="other">Other <see cref="Section"/> to check.</param>
        /// <returns>
        /// <see langword="True"/> if this <see cref="Section"/> overlaps with the given one,
        /// otherwise <see langword="false"/>.
        /// </returns>
        public bool OverlapsWith(Section other) => (other.Start <= End) && (other.End >= Start);

    }

    /// <summary>Represents an <see cref="Assignment"/> containing two sections.</summary>
    /// <param name="First">First <see cref="Section"/> of the <see cref="Assignment"/>.</param>
    /// <param name="Second">Second <see cref="Section"/> of the <see cref="Assignment"/>.</param>
    private readonly partial record struct Assignment(Section First, Section Second) {

        [GeneratedRegex("^\\d+-\\d+,\\d+-\\d+$")]
        private static partial Regex AssignmentRegex();

        /// <summary>Parses an <see cref="Assignment"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must contain two sections (see
        /// <see cref="Section.Parse(ReadOnlySpan{char})"/> for more information) separated by a
        /// ','. An valid example might be the string "2-3,4-5".
        /// </remarks>
        /// <param name="s">String to parse an <see cref="Assignment"/> from.</param>
        /// <returns>An <see cref="Assignment"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> has an invalid format.
        /// </exception>
        public static Assignment Parse(ReadOnlySpan<char> s) {
            if (!AssignmentRegex().IsMatch(s)) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"The string \"{s}\" does not represent a valid assignment."
                );
            }
            int commaIndex = s.IndexOf(',');
            Section first = Section.Parse(s[..commaIndex]);
            Section second = Section.Parse(s[(commaIndex + 1)..]);
            return new Assignment(first, second);
        }

        /// <summary>
        /// Determines if one of the sections of this <see cref="Assignment"/> fully contains the
        /// other.
        /// </summary>
        /// <returns>
        /// <see langword="True"/> if one of the sections of this <see cref="Assignment"/> fully
        /// contains the other, otherwise <see langword="false"/>.
        /// </returns>
        public bool HasFullyContainedSection()
            => First.FullyContains(Second) || Second.FullyContains(First);

        /// <summary>
        /// Determines if one of the sections of this <see cref="Assignment"/> overlaps with the
        /// other.
        /// </summary>
        /// <returns>
        /// <see langword="True"/> if one of the sections of this <see cref="Assignment"/> overlaps
        /// with the other, otherwise <see langword="false"/>.
        /// </returns>
        public bool HasOverlappingSection()
            => First.OverlapsWith(Second) || Second.OverlapsWith(First);

    }

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Solves the <see cref="CampCleanup"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ImmutableArray<Assignment> assignments = [
            .. File.ReadLines(InputFile).Select(line => Assignment.Parse(line))
        ];
        int fullyContained = assignments.Count(assignment => assignment.HasFullyContainedSection());
        int overlapping = assignments.Count(assignment => assignment.HasOverlappingSection());
        textWriter.WriteLine($"In {fullyContained} lines, a section fully contains the other.");
        textWriter.WriteLine($"In {overlapping} lines, a section overlaps with the other.");
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