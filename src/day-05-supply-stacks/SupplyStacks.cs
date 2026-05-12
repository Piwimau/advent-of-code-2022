using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace SupplyStacks;

internal sealed partial class SupplyStacks {

    /// <summary>
    /// Represents an <see cref="Instruction"/> for moving a certain number of crates from a source
    /// stack to a destination stack.
    /// </summary>
    /// <param name="Quantity">Number of crates to move.</param>
    /// <param name="Source">Index of the source stack to move the crates from.</param>
    /// <param name="Destination">Index of the destination stack to move the crates to.</param>
    private readonly partial record struct Instruction(int Quantity, int Source, int Destination) {

        [GeneratedRegex("^move \\d+ from [1-9] to [1-9]$")]
        private static partial Regex InstructionRegex();

        /// <summary>Parses an <see cref="Instruction"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must have the format "move x from y to z", where x is a
        /// positive integer and y and z must be single digits greater than zero.
        /// </remarks>
        /// <param name="s">String containing an <see cref="Instruction"/> to parse.</param>
        /// <returns>An <see cref="Instruction"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="s"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> has an invalid format.
        /// </exception>
        public static Instruction Parse(string s) {
            Guard.IsNotNull(s);
            if (!InstructionRegex().IsMatch(s)) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"The string \"{s}\" does not represent a valid instruction."
                );
            }
            ReadOnlySpan<int> numbers = [.. NumberRegex().Matches(s)
                .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
            ];
            int quantity = numbers[0];
            // Since the source and destination index are one-based in the input, they are
            // decremented here for further use in collections.
            int source = numbers[1] - 1;
            int destination = numbers[2] - 1;
            return new Instruction(quantity, source, destination);
        }

    }

    /// <summary>Maximum number of stacks allowed for rearranging crates.</summary>
    private const int MaxStacks = 9;

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    [GeneratedRegex("\\d+")]
    private static partial Regex NumberRegex();

    /// <summary>Parses all stacks from a given sequence of lines.</summary>
    /// <remarks>
    /// The given sequence of <paramref name="lines"/> must have the following format:
    /// <example>
    /// <code>
    ///     [D]    
    /// [N] [C]
    /// [Z] [M] [P]
    ///  1   2   3 
    /// </code>
    /// </example>
    /// In particular:
    /// <list type="bullet">
    ///     <item>
    ///     The last line contains an ascending (one-based) list of stack indices that is used to
    ///     determine the total number of stacks. There may be at most <see cref="MaxStacks"/>
    ///     stacks (which simultaneously corresponds to the highest allowed index).
    ///     </item>
    ///     <item>
    ///     The remaining lines contain the crates of the individual stacks. Crates consist of a
    ///     single uppercase letter enclosed in square brackets and must be center-aligned on the
    ///     corresponding stack index (leaving a gap of one space between two stacks).
    ///     </item>
    ///     <item>
    ///     Any character that does not represent a stack index or is part of a crate must be a
    ///     space. All lines must be padded to the same length using trailing spaces if necessary.
    ///     </item>
    /// </list>
    /// </remarks>
    /// <param name="lines">Lines to parse the stacks from.</param>
    /// <returns>All stacks parsed from the given sequence of lines.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Throw when <paramref name="lines"/> contains more than <see cref="MaxStacks"/> stacks.
    /// </exception>
    private static ImmutableArray<Stack<char>> ParseStacks(ReadOnlySpan<string> lines) {
        int numberOfStacks = NumberRegex().Count(lines[^1]);
        if (numberOfStacks > MaxStacks) {
            throw new ArgumentOutOfRangeException(
                nameof(lines),
                $"Number of stacks ({numberOfStacks}) must not be greater than {MaxStacks}."
            );
        }
        ImmutableArray<Stack<char>>.Builder builder = ImmutableArray.CreateBuilder<Stack<char>>(
            numberOfStacks
        );
        for (int i = 0; i < numberOfStacks; i++) {
            builder.Add(new Stack<char>());
        }
        ImmutableArray<Stack<char>> stacks = builder.MoveToImmutable();
        // We iterate in reverse to push the crates onto the stacks in the correct order. Note that
        // the last line (lines.Length - 1) can be skipped, as this line only contains the stack
        // indices, but no crates.
        for (int i = lines.Length - 2; i >= 0; i--) {
            for (int stack = 0; stack < stacks.Length; stack++) {
                int column = 1 + (stack * 4);
                if (char.IsAsciiLetterUpper(lines[i][column])) {
                    stacks[stack].Push(lines[i][column]);
                }
            }
        }
        return stacks;
    }

    /// <summary>
    /// Rearranges the crates in a given sequence of stacks using a sequence of instructions.
    /// </summary>
    /// <remarks>
    /// This method implements the CrateMover 9000, which moves crates one at a time. It does not
    /// modify the input sequence of stacks, nor their contents.
    /// </remarks>
    /// <param name="stacks">Initial sequence of stacks and their crates.</param>
    /// <param name="instructions">Sequence of instructions for rearranging the crates.</param>
    /// <returns>A rearranged sequence of stacks and their crates.</returns>
    private static ImmutableArray<Stack<char>> RearrangeWithCrateMover9000(
        ReadOnlySpan<Stack<char>> stacks,
        ReadOnlySpan<Instruction> instructions
    ) {
        ImmutableArray<Stack<char>>.Builder builder = ImmutableArray.CreateBuilder<Stack<char>>(
            stacks.Length
        );
        foreach (Stack<char> stack in stacks) {
            // Once again, we have to push the crates in reverse order for the copy to retain the
            // original order. This is because the enumerator returns the topmost element first.
            builder.Add(new Stack<char>(stack.Reverse()));
        }
        ImmutableArray<Stack<char>> result = builder.MoveToImmutable();
        foreach (Instruction instruction in instructions) {
            for (int i = 0; i < instruction.Quantity; i++) {
                result[instruction.Destination].Push(result[instruction.Source].Pop());
            }
        }
        return result;
    }

    /// <summary>
    /// Rearranges the crates in a given sequence of stacks using a sequence of instructions.
    /// </summary>
    /// <remarks>
    /// This method implements the CrateMover 9001, which moves multiple creates at the same time.
    /// It does not modify the input sequence of stacks, nor their contents.
    /// </remarks>
    /// <param name="stacks">Initial sequence of stacks and their crates.</param>
    /// <param name="instructions">Sequence of instructions for rearranging the crates.</param>
    /// <returns>A rearranged sequence of stacks and their crates.</returns>
    private static ImmutableArray<Stack<char>> RearrangeWithCrateMover9001(
        ReadOnlySpan<Stack<char>> stacks,
        ReadOnlySpan<Instruction> instructions
    ) {
        ImmutableArray<Stack<char>>.Builder builder = ImmutableArray.CreateBuilder<Stack<char>>(
            stacks.Length
        );
        foreach (Stack<char> stack in stacks) {
            // Once again, we have to push the crates in reverse order for the copy to retain the
            // original order. This is because the enumerator returns the topmost element first.
            builder.Add(new Stack<char>(stack.Reverse()));
        }
        ImmutableArray<Stack<char>> result = builder.MoveToImmutable();
        foreach (Instruction instruction in instructions) {
            Span<char> crates = new char[instruction.Quantity];
            for (int i = 0; i < instruction.Quantity; i++) {
                crates[crates.Length - 1 - i] = result[instruction.Source].Pop();
            }
            for (int i = 0; i < instruction.Quantity; i++) {
                result[instruction.Destination].Push(crates[i]);
            }
        }
        return result;
    }

    /// <summary>Returns the top crates of a given sequence of stacks joined as a string.</summary>
    /// <param name="stacks">Stacks to join the top crates of.</param>
    /// <returns>The top crates of the given sequence of stacks joined as a string.</returns>
    private static string TopCrates(ReadOnlySpan<Stack<char>> stacks) {
        Span<char> topCrates = stackalloc char[stacks.Length];
        for (int i = 0; i < stacks.Length; i++) {
            topCrates[i] = stacks[i].Peek();
        }
        return topCrates.ToString();
    }

    /// <summary>Solves the <see cref="SupplyStacks"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ImmutableArray<string> lines = [.. File.ReadLines(InputFile)];
        int emptyLineIndex = lines.IndexOf("");
        ReadOnlySpan<Stack<char>> stacks = ParseStacks(lines[..emptyLineIndex].AsSpan()).AsSpan();
        ReadOnlySpan<Instruction> instructions = [
            .. lines[(emptyLineIndex + 1)..].Select(Instruction.Parse)
        ];
        string topCratesOne = TopCrates(RearrangeWithCrateMover9000(stacks, instructions).AsSpan());
        string topCratesTwo = TopCrates(RearrangeWithCrateMover9001(stacks, instructions).AsSpan());
        textWriter.WriteLine($"Using the CrateMover 9000, the top crates are {topCratesOne}.");
        textWriter.WriteLine($"Using the CrateMover 9001, the top crates are {topCratesTwo}.");
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