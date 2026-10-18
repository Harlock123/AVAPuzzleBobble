namespace AVAPuzzleBobble.Engine;

/// <summary>
/// The screen and the board: bubbles in a honeycomb eight across (seven on the rows set in), in a cabinet in the middle
/// of the screen, with the launcher at the bottom.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int Cols = 8, Rows = 13;
    public const double R = 32;
    /// <summary>The height of a row of the honeycomb.</summary>
    public static readonly double RowH = R * Math.Sqrt(3);
    public const double Left = 256, Right = Left + Cols * 2 * R, Top = 48;
    /// <summary>Where the launcher's bubble sits.</summary>
    public const double LaunchX = (Left + Right) / 2, LaunchY = 690;
    /// <summary>The line no bubble may reach: the row it's in, counted with the ceiling's drop.</summary>
    public const int DeathRow = 11;

    /// <summary>Rows set in by half a bubble have one fewer.</summary>
    public static int Width_(int row) => row % 2 == 0 ? Cols : Cols - 1;

    public static bool Inside(int col, int row) => row >= 0 && row < Rows && col >= 0 && col < Width_(row);

    public static double X(int col, int row) => Left + R + col * 2 * R + (row % 2 == 1 ? R : 0);
    public static double Y(int row, int drop) => Top + drop * RowH + R + row * RowH;

    public static IEnumerable<(int Col, int Row)> Neighbours(int c, int r)
    {
        yield return (c - 1, r);
        yield return (c + 1, r);
        int a = r % 2 == 0 ? c - 1 : c, b = a + 1;
        yield return (a, r - 1);
        yield return (b, r - 1);
        yield return (a, r + 1);
        yield return (b, r + 1);
    }
}

/// <summary>A round: the bubbles it starts with (0 is none, 1 to 6 the colours).</summary>
public sealed class RoundDef
{
    public int Number;
    public byte[,] Grid = new byte[Field.Cols, Field.Rows];
    public int Colours;
}

/// <summary>
/// The thirty rounds, built from seeds: four to eight rows of bubbles, mirrored left to right the way the cabinet's
/// designs were, in three colours at first and up to six later.
/// </summary>
public static class RoundBook
{
    public const int Count = 30;
    public static readonly uint[] Colours = [0, 0xFFF03838, 0xFF3878F0, 0xFFF0D030, 0xFF40D050, 0xFFB050E0, 0xFFF08A20];

    private static readonly RoundDef?[] Built = new RoundDef?[Count];
    private static readonly Lock Gate = new();

    public static RoundDef Round(int n)
    {
        lock (Gate) return Built[n - 1] ??= Build(n, 1994 + n * 53);
    }

    public static RoundDef Build(int n, int seed)
    {
        var dice = new Dice(seed);
        int colours = Math.Min(6, 3 + (n - 1) / 6);
        int rows = Math.Min(7, 4 + (n - 1) / 6 + dice.Next(2));
        var def = new RoundDef { Number = n, Colours = colours };
        // Runs of a colour, mirrored: two or three alike side by side, so there's something to aim for.
        for (int r = 0; r < rows; r++)
        {
            int w = Field.Width_(r);
            byte run = (byte)(1 + dice.Next(colours));
            for (int c = 0; c < (w + 1) / 2; c++)
            {
                if (dice.Next(3) == 0) run = (byte)(1 + dice.Next(colours));
                // Now and then a gap (never on the top row).
                byte b = r > 0 && dice.Next(10) == 0 ? (byte)0 : run;
                def.Grid[c, r] = b;
                def.Grid[w - 1 - c, r] = b;
            }
        }
        // Nothing left hanging from the start.
        foreach (var (c, r) in Floating(def.Grid)) def.Grid[c, r] = 0;
        return def;
    }

    /// <summary>The bubbles not joined to the ceiling, through any chain of neighbours.</summary>
    public static List<(int Col, int Row)> Floating(byte[,] g)
    {
        var held = new HashSet<(int, int)>();
        var q = new Queue<(int, int)>();
        for (int c = 0; c < Field.Cols; c++)
            if (g[c, 0] != 0) { held.Add((c, 0)); q.Enqueue((c, 0)); }
        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            foreach (var n in Field.Neighbours(c, r))
                if (Field.Inside(n.Col, n.Row) && g[n.Col, n.Row] != 0 && held.Add(n)) q.Enqueue(n);
        }
        var loose = new List<(int, int)>();
        for (int r = 0; r < Field.Rows; r++)
            for (int c = 0; c < Field.Width_(r); c++)
                if (g[c, r] != 0 && !held.Contains((c, r))) loose.Add((c, r));
        return loose;
    }

    /// <summary>The bubbles of one colour joined to the one given.</summary>
    public static List<(int Col, int Row)> Group(byte[,] g, int c0, int r0)
    {
        byte colour = g[c0, r0];
        var seen = new HashSet<(int, int)> { (c0, r0) };
        var q = new Queue<(int, int)>(seen);
        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            foreach (var n in Field.Neighbours(c, r))
                if (Field.Inside(n.Col, n.Row) && g[n.Col, n.Row] == colour && seen.Add(n)) q.Enqueue(n);
        }
        return [.. seen];
    }
}
