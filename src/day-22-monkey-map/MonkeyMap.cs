using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace MonkeyMap;

internal sealed partial class MonkeyMap {

    /// <summary>
    /// Represents an enumeration of all supported strategies for wrapping coordinates on the
    /// <see cref="Map"/>.
    /// </summary>
    private enum Wrapping { Plane, Cube }

    /// <summary>Represents a <see cref="Map"/> of tiles used to find a password.</summary>
    private sealed class Map {

        /// <summary>
        /// Represents an enumeration of all tiles found on the <see cref="Map"/>.
        /// </summary>
        private enum Tile { None, Empty, Wall }

        /// <summary>
        /// Represents an enumeration of all directions used for moving around the
        /// <see cref="Map"/>.
        /// </summary>
        private enum Direction { Right, Down, Left, Up }

        /// <summary>Represents a two-dimensional <see cref="Position"/>.</summary>
        /// <param name="X">X-coordinate of the <see cref="Position"/>.</param>
        /// <param name="Y">Y-coordinate of the <see cref="Position"/>.</param>
        private readonly record struct Position(int X, int Y);

        /// <summary>Width of the cube formed by folding this <see cref="Map"/>.</summary>
        private const int CubeWidth = 50;

        /// <summary>Width of this <see cref="Map"/>.</summary>
        private const int Width = 3 * CubeWidth;

        /// <summary>Height of this <see cref="Map"/>.</summary>
        private const int Height = 4 * CubeWidth;

        /// <summary>Tiles of this <see cref="Map"/>.</summary>
        /// <remarks>
        /// Note that this is actually a two-dimensional array, which is stored as a one-dimensional
        /// one for improved cache locality and performance.
        /// </remarks>
        private readonly ImmutableArray<Tile> tiles;

        /// <summary>Initializes a new <see cref="Map"/> with a given array of tiles.</summary>
        /// <param name="tiles">Tiles of the <see cref="Map"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="tiles"/> does not have the expected
        /// (<see cref="Width"/> * <see cref="Height"/>) elements.
        /// </exception>
        private Map(ImmutableArray<Tile> tiles) {
            Guard.IsEqualTo(tiles.Length, Width * Height);
            this.tiles = tiles;
        }

        /// <summary>Parses a <see cref="Map"/> from a given sequence of lines.</summary>
        /// <remarks>
        /// The sequence of <paramref name="lines"/> must form an unfolded cube with the following
        /// shape:
        /// <example>
        /// <code>
        ///     ---------
        ///     | A | B |
        ///     ---------
        ///     | C |
        /// ---------
        /// | D | E |
        /// ---------
        /// | F |
        /// -----
        /// </code>
        /// </example>
        /// The following additional requirements must be met in order to create a valid
        /// <see cref="Map"/>:
        /// <list type="bullet">
        ///     <item>
        ///     The six faces A - F must be squares with the same width/height, which only consist
        ///     of empty tiles ('.') or walls ('#').
        ///     </item>
        ///     <item>
        ///     The lines before the faces A and C must be padded using spaces for reasons of
        ///     alignment. Padding behind the faces C, E and F is optional.
        ///     </item>
        ///     <item>
        ///     Each face is expected to have a width of <see cref="CubeWidth"/>, making the
        ///     <see cref="Map"/> a total of <see cref="Width"/> units wide and <see cref="Height"/>
        ///     units tall.
        ///     </item>
        /// </list>
        /// An example for a sequence of lines might be the following:
        /// <example>
        /// <code>
        ///     ...#...#
        ///     .#......
        ///     ..#...#.
        ///     ........
        ///     .#..
        ///     ....
        ///     ....
        ///     ..#.
        /// ...#....
        /// #.......
        /// ...#.#..
        /// ..##...#
        /// ....
        /// .#..
        /// ....
        /// ..#.
        /// </code>
        /// </example>
        /// Note however that this example does not form a valid <see cref="Map"/>, as the faces are
        /// not <see cref="CubeWidth"/> units wide/tall each.
        /// </remarks>
        /// <param name="lines">Sequence of lines to parse the <see cref="Map"/> from.</param>
        /// <returns>A <see cref="Map"/> parsed from the given sequence of lines.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="lines"/> contains an invalid character or the resulting
        /// <see cref="Map"/> does not have the expected <see cref="Width"/> and
        /// <see cref="Height"/>.
        /// </exception>
        public static Map Parse(ReadOnlySpan<string> lines) {
            // We store the (width * height) tiles of the two-dimensional map in an one-dimensional
            // array for reasons of improved cache locality and performance.
            ImmutableArray<Tile>.Builder tiles = ImmutableArray.CreateBuilder<Tile>();
            tiles.Count = Width * Height;
            for (int y = 0; y < Height; y++) {
                for (int x = 0; x < Width; x++) {
                    // Padding after the cube faces is optional, so we just continue when reaching
                    // the end of the current line.
                    if (x >= lines[y].Length) {
                        continue;
                    }
                    tiles[(y * Width) + x] = lines[y][x] switch {
                        ' ' => Tile.None,
                        '.' => Tile.Empty,
                        '#' => Tile.Wall,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(lines),
                            $"The character '{lines[y][x]}' does not represent a valid tile."
                        )
                    };
                }
            }
            return new Map(tiles.MoveToImmutable());
        }

        /// <summary>
        /// Determines the new position and direction by starting at the current
        /// <see cref="Position"/> and moving one step in a given <see cref="Direction"/>.
        /// </summary>
        /// <remarks>
        /// The coordinates are wrapped according to a given <see cref="Wrapping"/> strategy to
        /// ensure they stay within this <see cref="Map"/>:
        /// <list type="bullet">
        ///     <item>
        ///     With <see cref="Wrapping.Plane"/>, the <see cref="Map"/> is treated as a
        ///     two-dimensional plane and coordinates wrap around to the opposite side if an edge
        ///     is crossed. In this case, the <see cref="Direction"/> remains unchanged.
        ///     </item>
        ///     <item>
        ///     With <see cref="Wrapping.Cube"/>, the <see cref="Map"/> is folded and treated as a
        ///     three-dimensional cube. This causes coordinates to wrap around to adjacent faces,
        ///     which in turn might result in a new <see cref="Direction"/> being followed.
        ///     </item>
        /// </list>
        /// </remarks>
        /// <param name="position">Current <see cref="Position"/> in the <see cref="Map"/>.</param>
        /// <param name="direction"><see cref="Direction"/> to move to.</param>
        /// <param name="wrapping"><see cref="Wrapping"/> strategy to apply.</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (Position Position, Direction Direction) Move(
            Position position,
            Direction direction,
            Wrapping wrapping
        ) {
            if (wrapping == Wrapping.Plane) {
                do {
                    position = direction switch {
                        Direction.Right => position with { X = (position.X + 1) % Width },
                        Direction.Down => position with { Y = (position.Y + 1) % Height },
                        Direction.Left => position with {
                            X = (position.X + Width - 1) % Width
                        },
                        Direction.Up => position with {
                            Y = (position.Y + Height - 1) % Height
                        },
                        _ => throw new InvalidOperationException("Unreachable.")
                    };
                }
                while (tiles[(position.Y * Width) + position.X] == Tile.None);
            }
            else {
                switch (direction) {
                    case Direction.Right:
                        if ((position.X + 1) >= Width) {
                            position = position with {
                                X = (2 * CubeWidth) - 1,
                                Y = Width - 1 - position.Y
                            };
                            direction = Direction.Left;
                        }
                        else if (tiles[(position.Y * Width) + position.X + 1] != Tile.None) {
                            position = position with { X = position.X + 1 };
                        }
                        else if (position.Y < (2 * CubeWidth)) {
                            position = position with {
                                X = position.Y + CubeWidth,
                                Y = CubeWidth - 1
                            };
                            direction = Direction.Up;
                        }
                        else if (position.Y < Width) {
                            position = position with { X = Width - 1, Y = Width - 1 - position.Y };
                            direction = Direction.Left;
                        }
                        else {
                            position = position with {
                                X = position.Y - (2 * CubeWidth),
                                Y = Width - 1
                            };
                            direction = Direction.Up;
                        }
                        break;
                    case Direction.Down:
                        if ((position.Y + 1) >= Height) {
                            position = position with { X = position.X + (2 * CubeWidth), Y = 0 };
                        }
                        else if (tiles[((position.Y + 1) * Width) + position.X] != Tile.None) {
                            position = position with { Y = position.Y + 1 };
                        }
                        else if (position.X < (2 * CubeWidth)) {
                            position = position with {
                                X = CubeWidth - 1,
                                Y = position.X + (2 * CubeWidth)
                            };
                            direction = Direction.Left;
                        }
                        else {
                            position = position with {
                                X = (2 * CubeWidth) - 1,
                                Y = position.X - CubeWidth
                            };
                            direction = Direction.Left;
                        }
                        break;
                    case Direction.Left:
                        if ((position.X - 1) < 0) {
                            if (position.Y < Width) {
                                position = position with {
                                    X = CubeWidth,
                                    Y = Width - 1 - position.Y
                                };
                                direction = Direction.Right;
                            }
                            else {
                                position = position with {
                                    X = position.Y - (2 * CubeWidth),
                                    Y = 0
                                };
                                direction = Direction.Down;
                            }
                        }
                        else if (tiles[(position.Y * Width) + position.X - 1] != Tile.None) {
                            position = position with { X = position.X - 1 };
                        }
                        else if (position.Y < CubeWidth) {
                            position = position with { X = 0, Y = Width - 1 - position.Y };
                            direction = Direction.Right;
                        }
                        else {
                            position = position with {
                                X = position.Y - CubeWidth,
                                Y = 2 * CubeWidth
                            };
                            direction = Direction.Down;
                        }
                        break;
                    case Direction.Up:
                        if ((position.Y - 1) < 0) {
                            if (position.X < (2 * CubeWidth)) {
                                position = position with {
                                    X = 0,
                                    Y = position.X + (2 * CubeWidth)
                                };
                                direction = Direction.Right;
                            }
                            else {
                                position = position with {
                                    X = position.X - (2 * CubeWidth),
                                    Y = Height - 1
                                };
                            }
                        }
                        else if (tiles[((position.Y - 1) * Width) + position.X] != Tile.None) {
                            position = position with { Y = position.Y - 1 };
                        }
                        else {
                            position = position with { X = CubeWidth, Y = position.X + CubeWidth };
                            direction = Direction.Right;
                        }
                        break;
                    default:
                        throw new InvalidOperationException("Unreachable.");
                }
            }
            return (position, direction);
        }

        /// <summary>
        /// Returns the final password found by tracing a path on this <see cref="Map"/>.
        /// </summary>
        /// <param name="instructions">Sequence of instructions for tracing the path.</param>
        /// <param name="wrapping"><see cref="Wrapping"/> strategy to apply.</param>
        /// <returns>The final password found by tracing a path on this <see cref="Map"/>.</returns>
        public int Password(ReadOnlySpan<IInstruction> instructions, Wrapping wrapping) {
            // We start tracing the path at the first empty tile in the topmost row of the map and
            // face right initially.
            Position position = new(0, 0);
            while (tiles[(position.Y * Width) + position.X] != Tile.Empty) {
                position = position with { X = position.X + 1 };
            }
            Direction direction = Direction.Right;
            foreach (IInstruction instruction in instructions) {
                switch (instruction) {
                    case MoveForward moveForward:
                        for (int i = 0; i < moveForward.Distance; i++) {
                            (Position newPosition, Direction newDirection) = Move(
                                position,
                                direction,
                                wrapping
                            );
                            if (tiles[(newPosition.Y * Width) + newPosition.X] == Tile.Wall) {
                                break;
                            }
                            position = newPosition;
                            direction = newDirection;
                        }
                        break;
                    case TurnLeft:
                        direction = direction switch {
                            Direction.Right => Direction.Up,
                            Direction.Down => Direction.Right,
                            Direction.Left => Direction.Down,
                            Direction.Up => Direction.Left,
                            _ => throw new InvalidOperationException("Unreachable.")
                        };
                        break;
                    case TurnRight:
                        direction = direction switch {
                            Direction.Right => Direction.Down,
                            Direction.Down => Direction.Left,
                            Direction.Left => Direction.Up,
                            Direction.Up => Direction.Right,
                            _ => throw new InvalidOperationException("Unreachable.")
                        };
                        break;
                    default:
                        throw new InvalidOperationException("Unreachable.");
                }
            }
            return (1000 * (position.Y + 1)) + (4 * (position.X + 1)) + ((int) direction);
        }

    }

    /// <summary>
    /// Represents an <see cref="IInstruction"/> for tracing a path around the <see cref="Map"/>.
    /// </summary>
    private interface IInstruction { }

    /// <summary>
    /// Represents an <see cref="IInstruction"/> that indicates to move forward in the current
    /// <see cref="Map.Direction"/> for a specified distance.
    /// </summary>
    /// <param name="Distance">Distance to move in the current <see cref="Map.Direction"/>.</param>
    private readonly record struct MoveForward(int Distance) : IInstruction;

    /// <summary>Represents an <see cref="IInstruction"/> that indicates to turn left.</summary>
    private readonly record struct TurnLeft() : IInstruction;

    /// <summary>Represents an <see cref="IInstruction"/> that indicates to turn right.</summary>
    private readonly record struct TurnRight() : IInstruction;

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    [GeneratedRegex("\\d+|[LR]")]
    private static partial Regex InstructionRegex();

    /// <summary>Solves the <see cref="MonkeyMap"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ReadOnlySpan<string> lines = [.. File.ReadLines(InputFile)];
        Map map = Map.Parse(lines[..^2]);
        ReadOnlySpan<IInstruction> instructions = [
            .. InstructionRegex()
                .Matches(lines[^1])
                .Select<Match, IInstruction>(match => match.ValueSpan switch {
                    "L" => new TurnLeft(),
                    "R" => new TurnRight(),
                    _ => new MoveForward(int.Parse(match.ValueSpan, CultureInfo.InvariantCulture))
                })
        ];
        int planePassword = map.Password(instructions, Wrapping.Plane);
        int cubePassword = map.Password(instructions, Wrapping.Cube);
        textWriter.WriteLine($"Using the plane wrapping, the password is {planePassword}.");
        textWriter.WriteLine($"Using the cube wrapping, the password is {cubePassword}.");
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