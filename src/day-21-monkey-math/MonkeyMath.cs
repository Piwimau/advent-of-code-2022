using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace MonkeyMath;

internal sealed partial class MonkeyMath {

    /// <summary>
    /// Represents an <see cref="INode"/> as part of a tree representing a monkey math riddle.
    /// </summary>
    private interface INode {

        /// <summary>Gets the name of this <see cref="INode"/>.</summary>
        string Name { get; }

        /// <summary>Returns the value of this <see cref="INode"/>.</summary>
        /// <returns>The value of this <see cref="INode"/>.</returns>
        long Value();

        /// <summary>Tries to find an <see cref="INode"/> that fulfills a given predicate.</summary>
        /// <param name="predicate">Predicate for the search.</param>
        /// <param name="node">
        /// An <see cref="INode"/> if the search was successful (indicated by a return value of
        /// <see langword="true"/>), otherwise the <see langword="default"/>.
        /// </param>
        /// <returns>
        /// <see langword="True"/> if an <see cref="INode"/> was found that matches the given
        /// predicate, otherwise <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="predicate"/> is <see langword="null"/>.
        /// </exception>
        bool TryFind(Func<INode, bool> predicate, [NotNullWhen(true)] out INode? node);

    }

    /// <summary>Represents a <see cref="ConstantNode"/> that encapsulates a single value.</summary>
    private sealed class ConstantNode : INode, IEquatable<ConstantNode> {

        /// <summary>Value of this <see cref="ConstantNode"/>.</summary>
        private readonly long value;

        /// <summary>Gets the name of this <see cref="ConstantNode"/>.</summary>
        public string Name { get; init; }

        /// <summary>
        /// Initializes a new <see cref="ConstantNode"/> with a given name and value.
        /// </summary>
        /// <param name="name">Name of the <see cref="ConstantNode"/>.</param>
        /// <param name="value">Positive value of the <see cref="ConstantNode"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="value"/> is negative.
        /// </exception>
        public ConstantNode(string name, long value) {
            Guard.IsNotNull(name);
            Guard.IsGreaterThanOrEqualTo(value, 0);
            Name = name;
            this.value = value;
        }

        /// <summary>Returns the value of this <see cref="ConstantNode"/>.</summary>
        /// <returns>The value of this <see cref="ConstantNode"/>.</returns>
        public long Value() => value;

        /// <inheritdoc/>
        /// <remarks>
        /// For a <see cref="ConstantNode"/>, this method simply checks if it fulfills the given
        /// predicate.
        /// </remarks>
        public bool TryFind(
            Func<INode, bool> predicate,
            [NotNullWhen(true)] out INode? node
        ) {
            Guard.IsNotNull(predicate);
            node = predicate(this) ? this : default;
            return node != default;
        }

        /// <summary>
        /// Determines if this <see cref="ConstantNode"/> is equal to a given one.
        /// </summary>
        /// <param name="other">
        /// Other <see cref="ConstantNode"/> to compare this instance to.
        /// </param>
        /// <returns>
        /// <see langword="True"/> if this <see cref="ConstantNode"/> is equal to the given one,
        /// otherwise <see langword="false"/>.
        /// </returns>
        public bool Equals(ConstantNode? other)
            => (other != null) && ((Name, value) == (other.Name, other.value));

        /// <summary>
        /// Determines if this <see cref="ConstantNode"/> is equal to a given object.
        /// </summary>
        /// <param name="obj">Object to compare this <see cref="ConstantNode"/> to.</param>
        /// <returns>
        /// <see langword="True"/> if the given object is a <see cref="ConstantNode"/> and equal to
        /// this instance, otherwise <see langword="false"/>.
        /// </returns>
        public override bool Equals([NotNullWhen(true)] object? obj)
            => Equals(obj as ConstantNode);

        /// <summary>Returns a hash code for this <see cref="ConstantNode"/>.</summary>
        /// <returns>A hash code for this <see cref="ConstantNode"/>.</returns>
        public override int GetHashCode() => HashCode.Combine(Name, value);

        /// <summary>Returns a string representation of this <see cref="ConstantNode"/>.</summary>
        /// <returns>A string representation of this <see cref="ConstantNode"/>.</returns>
        public override string ToString()
            => $"{nameof(ConstantNode)} {{ Name = {Name}, Value = {value} }}";

    }

    /// <summary>
    /// Represents an <see cref="OperationNode"/> whose value depends on an <see cref="Operation"/>
    /// performed on the values of two subnodes.
    /// </summary>
    private sealed class OperationNode : INode, IEquatable<OperationNode> {

        /// <summary>
        /// Represents an enumeration of all operations supported by an <see cref="OperationNode"/>.
        /// </summary>
        private enum Operation { Addition, Subtraction, Multiplication, Division }

        /// <summary>Name of the special human node.</summary>
        private const string HumanNodeName = "humn";

        /// <summary>Dictionary for translating symbols to operations.</summary>
        private static readonly FrozenDictionary<char, Operation> SymbolToOperation =
            FrozenDictionary.ToFrozenDictionary([
                KeyValuePair.Create('+', Operation.Addition),
                KeyValuePair.Create('-', Operation.Subtraction),
                KeyValuePair.Create('*', Operation.Multiplication),
                KeyValuePair.Create('/', Operation.Division)
            ]);

        /// <summary>Dictionary for translating operations to symbols.</summary>
        private static readonly FrozenDictionary<Operation, char> OperationToSymbol =
            FrozenDictionary.ToFrozenDictionary([
                KeyValuePair.Create(Operation.Addition, '+'),
                KeyValuePair.Create(Operation.Subtraction, '-'),
                KeyValuePair.Create(Operation.Multiplication, '*'),
                KeyValuePair.Create(Operation.Division, '/')
            ]);

        /// <summary><see cref="Operation"/> of this <see cref="OperationNode"/>.</summary>
        private readonly Operation operation;

        /// <summary>Left subnode of this <see cref="OperationNode"/>.</summary>
        private readonly INode left;

        /// <summary>Right subnode of this <see cref="OperationNode"/>.</summary>
        private readonly INode right;

        /// <summary>Gets the name of this <see cref="OperationNode"/>.</summary>
        public string Name { get; init; }

        /// <summary>Initializes a new <see cref="OperationNode"/>.</summary>
        /// <param name="name">Name of the <see cref="OperationNode"/>.</param>
        /// <param name="operation">Operation of the <see cref="OperationNode"/>.</param>
        /// <param name="left">Left subnode of the <see cref="OperationNode"/>.</param>
        /// <param name="right">Right subnode of the <see cref="OperationNode"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="name"/>, <paramref name="left"/> or <paramref name="right"/>
        /// is <see langword="null"/>.
        /// </exception>
        public OperationNode(string name, char operation, INode left, INode right) {
            Guard.IsNotNull(name);
            Guard.IsNotNull(left);
            Guard.IsNotNull(right);
            Name = name;
            this.operation = SymbolToOperation[operation];
            this.left = left;
            this.right = right;
        }

        /// <summary>
        /// Returns the value of this <see cref="OperationNode"/> by performing an
        /// <see cref="Operation"/> on the values of its two subnodes.
        /// </summary>
        /// <returns>The value of this <see cref="OperationNode"/>.</returns>
        public long Value() => operation switch {
            Operation.Addition => left.Value() + right.Value(),
            Operation.Subtraction => left.Value() - right.Value(),
            Operation.Multiplication => left.Value() * right.Value(),
            Operation.Division => left.Value() / right.Value(),
            _ => throw new InvalidOperationException("Unreachable.")
        };

        /// <inheritdoc/>
        /// <remarks>
        /// The search is done using an in-order traversal, i. e. the leftmost <see cref="INode"/>
        /// fulfilling the predicate is found and returned first.
        /// </remarks>
        public bool TryFind(
            Func<INode, bool> predicate,
            [NotNullWhen(true)] out INode? node
        ) {
            Guard.IsNotNull(predicate);
            if (left.TryFind(predicate, out node)) {
                return true;
            }
            if (predicate(this)) {
                node = this;
                return true;
            }
            return right.TryFind(predicate, out node);
        }

        /// <summary>
        /// Returns the number the human would need to yell in order to pass the root monkey's
        /// equality test.
        /// </summary>
        /// <returns>The number the human would need to yell.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when this <see cref="OperationNode"/> does not contain the human node in any of
        /// its two subtrees.
        /// </exception>
        public long HumanNumber() {
            INode? subtreeWithHumanNode;
            INode? subtreeWithoutHumanNode;
            if (left.TryFind(node => node.Name == HumanNodeName, out _)) {
                subtreeWithHumanNode = left;
                subtreeWithoutHumanNode = right;
            }
            else if (right.TryFind(node => node.Name == HumanNodeName, out _)) {
                subtreeWithHumanNode = right;
                subtreeWithoutHumanNode = left;
            }
            else {
                throw new InvalidOperationException(
                    $"Neither of the two subtrees contains a node with the name '{HumanNodeName}'."
                );
            }
            long humanNumber = subtreeWithoutHumanNode.Value();
            INode node = subtreeWithHumanNode;
            while (node.Name != HumanNodeName) {
                OperationNode operationNode = (OperationNode) node;
                bool isHumanNodeInLeftSubtree;
                if (operationNode.left.TryFind(node => node.Name == HumanNodeName, out _)) {
                    isHumanNodeInLeftSubtree = true;
                    subtreeWithHumanNode = operationNode.left;
                    subtreeWithoutHumanNode = operationNode.right;
                }
                else {
                    isHumanNodeInLeftSubtree = false;
                    subtreeWithHumanNode = operationNode.right;
                    subtreeWithoutHumanNode = operationNode.left;
                }
                long otherValue = subtreeWithoutHumanNode.Value();
                humanNumber = operationNode.operation switch {
                    Operation.Addition => humanNumber - otherValue,
                    Operation.Subtraction => isHumanNodeInLeftSubtree
                        ? humanNumber + otherValue
                        : otherValue - humanNumber,
                    Operation.Multiplication => humanNumber / otherValue,
                    Operation.Division => isHumanNodeInLeftSubtree
                        ? humanNumber * otherValue
                        : otherValue / humanNumber,
                    _ => throw new InvalidOperationException("Unreachable.")
                };
                node = subtreeWithHumanNode;
            }
            return humanNumber;
        }

        /// <summary>
        /// Determines if this <see cref="OperationNode"/> is equal to a given one.
        /// </summary>
        /// <param name="other">
        /// Other <see cref="OperationNode"/> to compare this instance to.
        /// </param>
        /// <returns>
        /// <see langword="True"/> if this <see cref="OperationNode"/> is equal to the given one,
        /// otherwise <see langword="false"/>.
        /// </returns>
        public bool Equals(OperationNode? other)
            => (other != null) && (Name == other.Name) && (operation == other.operation)
                && left.Equals(other.left) && right.Equals(other.right);

        /// <summary>
        /// Determines if this <see cref="OperationNode"/> is equal to a given object.
        /// </summary>
        /// <param name="obj">Object to compare this <see cref="OperationNode"/> to.</param>
        /// <returns>
        /// <see langword="True"/> if the given object is an <see cref="OperationNode"/> and equal
        /// to this instance, otherwise <see langword="false"/>.
        /// </returns>
        public override bool Equals([NotNullWhen(true)] object? obj)
            => Equals(obj as OperationNode);

        /// <summary>Returns a hash code for this <see cref="OperationNode"/>.</summary>
        /// <returns>A hash code for this <see cref="OperationNode"/>.</returns>
        public override int GetHashCode() => HashCode.Combine(Name, operation, left, right);

        /// <summary>Returns a string representation of this <see cref="OperationNode"/>.</summary>
        /// <returns>A string representation of this <see cref="OperationNode"/>.</returns>
        public override string ToString()
            => $"{nameof(OperationNode)} {{ Name = {Name}, Operation = {left.Name} "
                + $"{OperationToSymbol[operation]} {right.Name} }}";

    }

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    [GeneratedRegex("^(?<name>[a-z]{4}): (?<value>\\d+)$")]
    private static partial Regex ConstantNodeRegex();

    [GeneratedRegex(
        "^(?<name>[a-z]{4}): (?<leftName>[a-z]{4}) (?<operation>[+\\-*/]) (?<rightName>[a-z]{4})$"
    )]
    private static partial Regex OperationNodeRegex();

    /// <summary>Parses the nodes from a given sequence of lines.</summary>
    /// <remarks>
    /// The nodes in <paramref name="lines"/> must form a directed, acyclic graph, i. e. a tree.
    /// Each node must be either of the following:
    /// <list type="bullet">
    ///     <item>A <see cref="ConstantNode"/> with the format "&lt;Name&gt;: &lt;Value&gt;".</item>
    ///     <item>
    ///     An <see cref="OperationNode"/> with the format
    ///     "&lt;Name&gt;: &lt;LeftName&gt; &lt;Operation&gt; &lt;RightName&gt;".
    ///     </item>
    /// </list>
    /// The placeholders &lt;Name&gt;, &lt;LeftName&gt; and &lt;RightName&gt; represent sequences of
    /// four lowercase letters each. &lt;Value&gt; must be a positive integer and &lt;Operation&gt;
    /// is either '+', '-', '*' or '/'.
    /// <para/>
    /// Two special nodes (the <see cref="ConstantNode"/> "humn" and the <see cref="OperationNode"/>
    /// "root") must always be present, as the resulting tree cannot be evaluated otherwise.<br/>
    /// An example for a valid sequence of lines forming a tree might be the following:
    /// <example>
    /// <code>
    /// root: pppw + sjmn
    /// dbpl: 5
    /// cczh: sllz + lgvd
    /// zczc: 2
    /// ptdq: humn - dvpt
    /// dvpt: 3
    /// lfqf: 4
    /// humn: 5
    /// ljgn: 2
    /// sjmn: drzm * dbpl
    /// sllz: 4
    /// pppw: cczh / lfqf
    /// lgvd: ljgn * ptdq
    /// drzm: hmdt - zczc
    /// hmdt: 32
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="lines">Sequence of lines to parse the nodes from.</param>
    /// <returns>The root node of the parsed nodes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any line in <paramref name="lines"/> has an invalid format.
    /// </exception>
    private static OperationNode ParseNodes(ReadOnlySpan<string> lines) {
        Dictionary<string, INode> nodes = [];
        Queue<(string Name, char Operation, string LeftName, string RightName)> queue = [];
        foreach (string line in lines) {
            Match match = ConstantNodeRegex().Match(line);
            if (match.Success) {
                string name = match.Groups["name"].Value;
                long value = long.Parse(
                    match.Groups["value"].ValueSpan,
                    CultureInfo.InvariantCulture
                );
                nodes[name] = new ConstantNode(name, value);
                continue;
            }
            match = OperationNodeRegex().Match(line);
            if (match.Success) {
                string name = match.Groups["name"].Value;
                char operation = match.Groups["operation"].Value[0];
                string leftName = match.Groups["leftName"].Value;
                string rightName = match.Groups["rightName"].Value;
                queue.Enqueue((name, operation, leftName, rightName));
                continue;
            }
            throw new ArgumentOutOfRangeException(
                nameof(lines),
                $"The line '{line}' does not represent a valid node."
            );
        }
        while (queue.Count > 0) {
            (string name, char operation, string leftName, string rightName) = queue.Dequeue();
            if (nodes.TryGetValue(leftName, out INode? left)
                    && nodes.TryGetValue(rightName, out INode? right)) {
                nodes[name] = new OperationNode(name, operation, left, right);
            }
            else {
                queue.Enqueue((name, operation, leftName, rightName));
            }
        }
        return (OperationNode) nodes["root"];
    }

    /// <summary>Solves the <see cref="MonkeyMath"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        OperationNode root = ParseNodes([.. File.ReadLines(InputFile)]);
        textWriter.WriteLine($"The root monkey yells the number {root.Value()}.");
        textWriter.WriteLine($"The human must yell the number {root.HumanNumber()}.");
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