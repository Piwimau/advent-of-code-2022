using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace FullOfHotAir;

internal sealed partial class FullOfHotAir {

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>SNAFU numbers use base 5 instead of 10 like decimal numbers.</summary>
    private const int SnafuBase = 5;

    /// <summary>Digits of SNAFU numbers are offset by 2 compared to decimal numbers.</summary>
    private const int SnafuOffset = 2;

    [GeneratedRegex("^[=\\-012]+$")]
    private static partial Regex SnafuRegex();

    /// <summary>Converts a SNAFU number to its decimal representation.</summary>
    /// <remarks>
    /// The string <paramref name="snafu"/> must only contain valid SNAFU digits (i. e. '=', '-',
    /// '0', '1' or '2').
    /// </remarks>
    /// <param name="snafu">SNAFU number to convert to decimal.</param>
    /// <returns>The converted SNAFU number in decimal representation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Throw when <paramref name="snafu"/> does not represent a valid SNAFU number.
    /// </exception>
    private static long SnafuToDecimal(ReadOnlySpan<char> snafu) {
        if (!SnafuRegex().IsMatch(snafu)) {
            throw new ArgumentOutOfRangeException(
                nameof(snafu),
                $"The string '{snafu}' does not represent a valid SNAFU number."
            );
        }
        long number = 0;
        // Iterate in reverse order to start at the least significant digit.
        for (int i = snafu.Length - 1; i >= 0; i--) {
            int digit = snafu[i] switch {
                '=' => -2,
                '-' => -1,
                '0' => 0,
                '1' => 1,
                '2' => 2,
                _ => throw new InvalidOperationException("Unreachable.")
            };
            number += digit * ((long) Math.Pow(SnafuBase, snafu.Length - 1 - i));
        }
        return number;
    }

    /// <summary>Converts a positive decimal number to its SNAFU representation.</summary>
    /// <param name="number">Positive decimal number to convert to SNAFU.</param>
    /// <returns>The converted positive decimal number in SNAFU representation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="number"/> is negative.
    /// </exception>
    private static string DecimalToSnafu(long number) {
        Guard.IsGreaterThanOrEqualTo(number, 0);
        StringBuilder snafu = new();
        do {
            char digit = (int) (((number + SnafuOffset) % SnafuBase) - SnafuOffset) switch {
                -2 => '=',
                -1 => '-',
                0 => '0',
                1 => '1',
                2 => '2',
                _ => throw new InvalidOperationException("Unreachable.")
            };
            snafu.Insert(0, digit);
            number = (number + SnafuOffset) / SnafuBase;
        }
        while (number > 0);
        return snafu.ToString();
    }

    /// <summary>Solves the <see cref="FullOfHotAir"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        long sum = File.ReadLines(InputFile).Sum(snafu => SnafuToDecimal(snafu));
        textWriter.WriteLine(
            $"The sum of the fuel requirements in SNAFU is {DecimalToSnafu(sum)}."
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