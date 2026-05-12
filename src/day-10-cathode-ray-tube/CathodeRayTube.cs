using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace CathodeRayTube;

internal sealed partial class CathodeRayTube {

    /// <summary>Represents an enumeration of all possible opcodes.</summary>
    private enum Opcode { Addx, Noop }

    /// <summary>
    /// Represents an <see cref="Instruction"/> consisting of an <see cref="CathodeRayTube.Opcode"/>
    /// and an optional integer operand.
    /// </summary>
    /// <param name="Opcode">
    /// <see cref="CathodeRayTube.Opcode"/> of the <see cref="Instruction"/>.
    /// </param>
    /// <param name="Operand">Optional integer operand of the <see cref="Instruction"/>.</param>
    private readonly partial record struct Instruction(Opcode Opcode, int? Operand = null) {

        [GeneratedRegex("^(?:noop|addx -?\\d+)$")]
        private static partial Regex InstructionRegex();

        /// <summary>Parses an <see cref="Instruction"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must be either of the following:
        /// <list type="bullet">
        ///     <item>
        ///     "noop" representing an <see cref="Instruction"/> that has no effect.
        ///     </item>
        ///     <item>
        ///     "addx" followed by a single space and an integer operand.
        ///     </item>
        /// </list>
        /// </remarks>
        /// <param name="s">String to parse an <see cref="Instruction"/> from.</param>
        /// <returns>An <see cref="Instruction"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> has an invalid format.
        /// </exception>
        public static Instruction Parse(ReadOnlySpan<char> s) {
            if (!InstructionRegex().IsMatch(s)) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"The string \"{s}\" does not represent a valid instruction."
                );
            }
            int spaceIndex = s.IndexOf(' ');
            Opcode opcode = (spaceIndex >= 0) ? Opcode.Addx : Opcode.Noop;
            int? operand = (opcode == Opcode.Noop)
                ? null
                : int.Parse(s[(spaceIndex + 1)..], CultureInfo.InvariantCulture);
            return new Instruction(opcode, operand);
        }

    }

    /// <summary>Width of the output screen in characters.</summary>
    private const int Width = 40;

    /// <summary>Height of the output screen in characters.</summary>
    private const int Height = 6;

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>
    /// Returns the signal strength using a given <paramref name="cycle"/> and the value of the
    /// <paramref name="x"/> register.
    /// </summary>
    /// <param name="cycle">Positive current cycle.</param>
    /// <param name="x">Value of the <paramref name="x"/> register.</param>
    /// <returns>
    /// The signal strength using the given <paramref name="cycle"/> and the value of the
    /// <paramref name="x"/> register.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="cycle"/> is negative.
    /// </exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SignalStrength(int cycle, int x) {
        Guard.IsGreaterThanOrEqualTo(cycle, 0);
        return ((cycle % Width) == 20) ? x * cycle : 0;
    }

    /// <summary>Updates the output screen by optionally displaying a pixel.</summary>
    /// <param name="output">
    /// Output screen to be updated. Note that this is conceptually a two-dimensional array of
    /// (<see cref="Width"/> * <see cref="Height"/>) pixels, but stored as a one-dimensional one for
    /// improved cache locality and performance.
    /// </param>
    /// <param name="cycle">Positive current cycle.</param>
    /// <param name="x">Value of the <paramref name="x"/> register.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="cycle"/> is negative.
    /// </exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateOutput(Span<char> output, int cycle, int x) {
        Guard.IsGreaterThanOrEqualTo(cycle, 0);
        int row = (cycle - 1) / Width;
        int column = (cycle - 1) % Width;
        // A pixel is only drawn if the value of the x register is at most one unit away from the
        // current cycle's column.
        if (Math.Abs(column - x) <= 1) {
            output[(row * Width) + column] = '#';
        }
    }

    /// <summary>Executes a sequence of instructions.</summary>
    /// <param name="instructions">Sequence of instructions to execute.</param>
    /// <returns>A tuple containing the sum of signal strengths and the output screen.</returns>
    private static (int SumOfSignalStrengths, string Output) Execute(
        ReadOnlySpan<Instruction> instructions
    ) {
        int x = 1;
        int cycle = 0;
        int sumOfSignalStrengths = 0;
        Span<char> output = stackalloc char[Width * Height];
        output.Fill('.');
        foreach (Instruction instruction in instructions) {
            cycle++;
            sumOfSignalStrengths += SignalStrength(cycle, x);
            UpdateOutput(output, cycle, x);
            if (instruction is Instruction(Opcode.Addx, int operand)) {
                cycle++;
                sumOfSignalStrengths += SignalStrength(cycle, x);
                UpdateOutput(output, cycle, x);
                x += operand;
            }
        }
        StringBuilder builder = new((Width + Environment.NewLine.Length) * Height);
        for (int i = 0; i < Height; i++) {
            builder.Append(output.Slice(i * Width, Width));
            builder.AppendLine();
        }
        return (sumOfSignalStrengths, builder.ToString());
    }

    /// <summary>Solves the <see cref="CathodeRayTube"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ReadOnlySpan<Instruction> instructions = [.. File.ReadLines(InputFile)
            .Select(line => Instruction.Parse(line))
        ];
        (int sumOfSignalStrengths, string output) = Execute(instructions);
        textWriter.WriteLine($"The sum of signal strengths is {sumOfSignalStrengths}.");
        textWriter.WriteLine(
            $"The output of the screen is:{Environment.NewLine}{Environment.NewLine}{output}"
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