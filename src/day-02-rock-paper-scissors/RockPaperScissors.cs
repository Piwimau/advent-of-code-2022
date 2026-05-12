using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace RockPaperScissors;

internal sealed partial class RockPaperScissors {

    /// <summary>
    /// Represents an enumeration of all possible shapes used in a game of Rock Paper Scissors.
    /// </summary>
    private enum Shape { Rock, Paper, Scissors }

    /// <summary>
    /// Represents an enumeration of all possible outcomes for a game of Rock Paper Scissors.
    /// </summary>
    private enum Outcome { Lose, Draw, Win }

    /// <summary>
    /// Represents a single <see cref="Round"/> of a Rock Paper Scissors tournament.
    /// </summary>
    /// <param name="Opponent"><see cref="Shape"/> the opponent chose.</param>
    /// <param name="Player"><see cref="Shape"/> the player chose.</param>
    private readonly partial record struct Round(Shape Opponent, Shape Player) {

        [GeneratedRegex("^[ABC] [XYZ]$")]
        private static partial Regex RoundRegex();

        /// <summary>Parses a <see cref="Round"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must match the format described by
        /// <see cref="RoundRegex"/>. In particular, it must consist of three characters, the first
        /// of which must be either 'A', 'B' or 'C'. It is followed by a space and another
        /// character, which must be either 'X', 'Y' or 'Z'.
        /// </remarks>
        /// <param name="s">String to parse a <see cref="Round"/> from.</param>
        /// <returns>A <see cref="Round"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> has an invalid format.
        /// </exception>
        public static Round Parse(ReadOnlySpan<char> s) {
            if (!RoundRegex().IsMatch(s)) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"The string \"{s}\" does not represent a valid round."
                );
            }
            Shape opponent = ParseShape(s[0]);
            Shape player = ParseShape(s[2]);
            return new Round(opponent, player);
        }

    }

    /// <summary>
    /// Represents a <see cref="Strategy"/> for playing a round of a Rock Paper Scissors tournament.
    /// </summary>
    /// <param name="Opponent"><see cref="Shape"/> the opponent chose.</param>
    /// <param name="AimedOutcome"><see cref="Outcome"/> the <see cref="Strategy"/> aims at.</param>
    private readonly partial record struct Strategy(Shape Opponent, Outcome AimedOutcome) {

        [GeneratedRegex("^[ABC] [XYZ]$")]
        private static partial Regex StrategyRegex();

        /// <summary>Parses a <see cref="Strategy"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must match the format described by
        /// <see cref="StrategyRegex"/>. In particular, it must consist of three characters,
        /// the first of which must be either 'A', 'B' or 'C'. It is followed by a space and another
        /// character, which must be either 'X', 'Y' or 'Z'.
        /// </remarks>
        /// <param name="s">String to parse a <see cref="Strategy"/> from.</param>
        /// <returns>A <see cref="Strategy"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> has an invalid format.
        /// </exception>
        public static Strategy Parse(ReadOnlySpan<char> s) {
            if (!StrategyRegex().IsMatch(s)) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"The string \"{s}\" does not represent a valid strategy."
                );
            }
            Shape opponent = ParseShape(s[0]);
            Outcome aimedOutcome = ParseOutcome(s[2]);
            return new Strategy(opponent, aimedOutcome);
        }

        /// <summary>
        /// Converts this <see cref="Strategy"/> to a <see cref="Round"/> by selecting a recommended
        /// <see cref="Shape"/> to play.
        /// </summary>
        /// <returns>
        /// The resulting <see cref="Round"/> if this <see cref="Strategy"/> is applied.
        /// </returns>
        public Round ToRound() {
            Shape player = AimedOutcome switch {
                Outcome.Lose => Opponent switch {
                    Shape.Rock => Shape.Scissors,
                    Shape.Paper => Shape.Rock,
                    Shape.Scissors => Shape.Paper,
                    _ => throw new InvalidOperationException("Unreachable.")
                },
                Outcome.Draw => Opponent,
                Outcome.Win => Opponent switch {
                    Shape.Rock => Shape.Paper,
                    Shape.Paper => Shape.Scissors,
                    Shape.Scissors => Shape.Rock,
                    _ => throw new InvalidOperationException("Unreachable.")
                },
                _ => throw new InvalidOperationException("Unreachable.")
            };
            return new Round(Opponent, player);
        }

    }

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Parses a <see cref="Shape"/> from a given character.</summary>
    /// <remarks>
    /// The character <paramref name="c"/> must be one of the following:
    /// <list type="bullet">
    ///     <item>'A' or 'X' - <see cref="Shape.Rock"/></item>
    ///     <item>'B' or 'Y' - <see cref="Shape.Paper"/></item>
    ///     <item>''C' or 'Z' - <see cref="Shape.Scissors"/></item>
    /// </list>
    /// </remarks>
    /// <param name="c">Character to parse a <see cref="Shape"/> from.</param>
    /// <returns>A <see cref="Shape"/> parsed from the given character.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="c"/> is an invalid character.
    /// </exception>
    private static Shape ParseShape(char c) => c switch {
        'A' or 'X' => Shape.Rock,
        'B' or 'Y' => Shape.Paper,
        'C' or 'Z' => Shape.Scissors,
        _ => throw new ArgumentOutOfRangeException(
            nameof(c),
            $"The character '{c}' does not represent a valid shape."
        )
    };

    /// <summary>Parses an <see cref="Outcome"/> from a given character.</summary>
    /// <remarks>
    /// The character <paramref name="c"/> must be one of the following:
    /// <list type="bullet">
    ///     <item>'X' - <see cref="Outcome.Lose"/></item>
    ///     <item>'Y' - <see cref="Outcome.Draw"/></item>
    ///     <item>'Z' - <see cref="Outcome.Win"/></item>
    /// </list>
    /// </remarks>
    /// <param name="c">Character to parse an <see cref="Outcome"/> from.</param>
    /// <returns>An <see cref="Outcome"/> parsed from the given character.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="c"/> is not one of the valid characters.
    /// </exception>
    private static Outcome ParseOutcome(char c) => c switch {
        'X' => Outcome.Lose,
        'Y' => Outcome.Draw,
        'Z' => Outcome.Win,
        _ => throw new ArgumentOutOfRangeException(
            nameof(c),
            $"The character '{c}' does not represent a valid outcome."
        )
    };

    /// <summary>
    /// Returns the total score the player achieved in a given sequence of rounds.
    /// </summary>
    /// <param name="rounds">Sequence of rounds to play.</param>
    /// <returns>The total score the player achieved in the given sequence of rounds.</returns>
    private static int TotalScore(ReadOnlySpan<Round> rounds) {
        int totalScore = 0;
        foreach ((Shape opponent, Shape player) in rounds) {
            totalScore += ((int) player) + 1;
            switch ((opponent, player)) {
                case (Shape.Rock, Shape.Rock):
                case (Shape.Paper, Shape.Paper):
                case (Shape.Scissors, Shape.Scissors):
                    totalScore += 3;
                    break;
                case (Shape.Rock, Shape.Paper):
                case (Shape.Paper, Shape.Scissors):
                case (Shape.Scissors, Shape.Rock):
                    totalScore += 6;
                    break;
            }
        }
        return totalScore;
    }

    /// <summary>Solves the <see cref="RockPaperScissors"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ImmutableArray<string> lines = [.. File.ReadLines(InputFile)];
        ImmutableArray<Round> initialRounds = [.. lines.Select(line => Round.Parse(line))];
        ImmutableArray<Round> strategyRounds = [
            .. lines.Select(line => Strategy.Parse(line).ToRound())
        ];
        int initialScore = TotalScore(initialRounds.AsSpan());
        int strategyScore = TotalScore(strategyRounds.AsSpan());
        textWriter.WriteLine($"The initial total score is {initialScore}.");
        textWriter.WriteLine($"The total score using the strategies is {strategyScore}.");
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