namespace AVAPuzzleBobble.Engine;

/// <summary>The rules, one 60th of a second at a time.</summary>
public static class Sim
{
    public const double AimSpeed = 75, MinAngle = 10, MaxAngle = 170, ShotSpeed = 960;
    public const int SubSteps = 6, PopPoints = 10, DropPoints = 20;
    /// <summary>After this long without a shot, a warning; after <see cref="HurrySeconds"/>, it fires itself.</summary>
    public const double WarnSeconds = 5, HurrySeconds = 8;

    public static int ShotsPerDrop(int round) => Math.Max(6, 9 - round / 8);

    public static void Start(World w, RoundDef def)
    {
        w.Def = def;
        w.Grid = (byte[,])def.Grid.Clone();
        w.Drop = w.Shots = 0;
        w.Angle = 90;
        w.Flying = null;
        w.Over = w.Clear = false;
        w.Idle = w.Clock = 0;
        w.Dice = new Dice(def.Number * 7 + 3);
        w.Current = Pick(w);
        w.Next = Pick(w);
        w.Bits.Clear();
    }

    /// <summary>A colour for the launcher: one still on the board.</summary>
    private static byte Pick(World w)
    {
        Span<bool> present = stackalloc bool[8];
        int n = 0;
        foreach (var b in w.Grid) if (b != 0 && !present[b]) { present[b] = true; n++; }
        if (n == 0) return (byte)(1 + w.Dice.Next(w.Def.Colours));
        int k = w.Dice.Next(n);
        for (byte c = 1; c < 8; c++)
            if (present[c] && k-- == 0) return c;
        return 1;
    }

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        w.Clock += dt;
        for (int i = w.Bits.Count - 1; i >= 0; i--)
        {
            var b = w.Bits[i];
            b.T += dt;
            if (b.Falls) b.Vy += 1400 * dt;
            if (b.T > (b.Falls ? 1.4 : 0.35)) w.Bits.RemoveAt(i);
        }
        if (w.Over || w.Clear) return;
        w.Angle = Math.Clamp(w.Angle - c.Dx * AimSpeed * dt, MinAngle, MaxAngle);
        bool fire = c.Fire && !w.FirePrev;
        w.FirePrev = c.Fire;
        if (w.Flying == null)
        {
            w.Idle += dt;
            if (fire || w.Idle >= HurrySeconds) Fire(w);
        }
        if (w.Flying is { } s) Fly(w, s);
    }

    public static void Fire(World w)
    {
        double a = w.Angle * Math.PI / 180;
        w.Flying = new Shot { X = Field.LaunchX, Y = Field.LaunchY, Vx = Math.Cos(a) * ShotSpeed, Vy = -Math.Sin(a) * ShotSpeed, Colour = w.Current };
        w.Current = w.Next;
        w.Next = Pick(w);
        w.Idle = 0;
        w.Heard.Add(Sfx.Fire);
    }

    /// <summary>The shot's flight: off the side walls, until it touches the ceiling or a bubble, where it sticks.</summary>
    private static void Fly(World w, Shot s)
    {
        double h = Field.Step / SubSteps;
        for (int i = 0; i < SubSteps; i++)
        {
            s.X += s.Vx * h;
            s.Y += s.Vy * h;
            if (s.X < Field.Left + Field.R) { s.X = 2 * (Field.Left + Field.R) - s.X; s.Vx = -s.Vx; w.Heard.Add(Sfx.Bounce); }
            if (s.X > Field.Right - Field.R) { s.X = 2 * (Field.Right - Field.R) - s.X; s.Vx = -s.Vx; w.Heard.Add(Sfx.Bounce); }
            if (Touching(w, s.X, s.Y))
            {
                Stick(w, s);
                return;
            }
        }
    }

    public static double Ceiling(World w) => Field.Top + w.Drop * Field.RowH;

    private static bool Touching(World w, double x, double y)
    {
        if (y - Field.R <= Ceiling(w)) return true;
        double reach = (2 * Field.R - 8) * (2 * Field.R - 8);
        for (int r = 0; r < Field.Rows; r++)
        {
            double cy = Field.Y(r, w.Drop);
            if (Math.Abs(cy - y) > 2 * Field.R) continue;
            for (int c = 0; c < Field.Width_(r); c++)
            {
                if (w.Grid[c, r] == 0) continue;
                double dx = Field.X(c, r) - x, dy = cy - y;
                if (dx * dx + dy * dy < reach) return true;
            }
        }
        return false;
    }

    /// <summary>The empty place nearest the shot that's against the ceiling or another bubble.</summary>
    public static (int Col, int Row) Snap(World w, double x, double y)
    {
        double best = double.MaxValue;
        (int, int) pick = (0, 0);
        for (int r = 0; r < Field.Rows; r++)
            for (int c = 0; c < Field.Width_(r); c++)
            {
                if (w.Grid[c, r] != 0) continue;
                bool held = r == 0 || Field.Neighbours(c, r).Any(n => Field.Inside(n.Col, n.Row) && w.Grid[n.Col, n.Row] != 0);
                if (!held) continue;
                double dx = Field.X(c, r) - x, dy = Field.Y(r, w.Drop) - y, d = dx * dx + dy * dy;
                if (d < best) { best = d; pick = (c, r); }
            }
        return pick;
    }

    private static void Stick(World w, Shot s)
    {
        w.Flying = null;
        var (c, r) = Snap(w, s.X, s.Y);
        w.Grid[c, r] = s.Colour;
        Settle(w, c, r);
    }

    /// <summary>
    /// A bubble has stuck at (c, r): three or more of a colour together burst, and whatever's left hanging falls. Then
    /// maybe the ceiling comes down, and the round's won or lost.
    /// </summary>
    public static void Settle(World w, int c, int r)
    {
        var group = RoundBook.Group(w.Grid, c, r);
        if (group.Count >= 3)
        {
            foreach (var (gc, gr) in group)
            {
                w.Bits.Add(new Bit(Field.X(gc, gr), Field.Y(gr, w.Drop), w.Grid[gc, gr], false));
                w.Grid[gc, gr] = 0;
            }
            w.Score += group.Count * PopPoints;
            w.Heard.Add(Sfx.Pop);
            var loose = RoundBook.Floating(w.Grid);
            foreach (var (lc, lr) in loose)
            {
                w.Bits.Add(new Bit(Field.X(lc, lr), Field.Y(lr, w.Drop), w.Grid[lc, lr], true));
                w.Grid[lc, lr] = 0;
            }
            if (loose.Count > 0)
            {
                w.Score += DropPoints * (1 << Math.Min(loose.Count, 10));
                w.Heard.Add(Sfx.Drop);
            }
        }
        else w.Heard.Add(Sfx.Stick);
        if (w.Count == 0)
        {
            w.Clear = true;
            return;
        }
        if (++w.Shots >= ShotsPerDrop(w.Def.Number))
        {
            w.Shots = 0;
            w.Drop++;
            // What's bursting or falling comes down with the rest.
            for (int i = 0; i < w.Bits.Count; i++) w.Bits[i] = w.Bits[i] with { Y = w.Bits[i].Y + Field.RowH };
            w.Heard.Add(Sfx.Ceiling);
        }
        for (int rr = 0; rr < Field.Rows; rr++)
            for (int cc = 0; cc < Field.Width_(rr); cc++)
                if (w.Grid[cc, rr] != 0 && rr + w.Drop >= Field.DeathRow) { w.Over = true; w.Heard.Add(Sfx.Lose); return; }
        // Make sure the launcher's bubbles are colours still on the board.
        if (!Present(w, w.Current)) w.Current = Pick(w);
        if (!Present(w, w.Next)) w.Next = Pick(w);
    }

    private static bool Present(World w, byte colour)
    {
        foreach (var b in w.Grid) if (b == colour) return true;
        return false;
    }

    /// <summary>Where a shot at an angle would stick, worked out without firing it.</summary>
    public static (int Col, int Row) Trace(World w, double angle)
    {
        double a = angle * Math.PI / 180, x = Field.LaunchX, y = Field.LaunchY, vx = Math.Cos(a) * ShotSpeed, vy = -Math.Sin(a) * ShotSpeed;
        double h = Field.Step / SubSteps;
        for (int i = 0; i < 4000; i++)
        {
            x += vx * h;
            y += vy * h;
            if (x < Field.Left + Field.R) { x = 2 * (Field.Left + Field.R) - x; vx = -vx; }
            if (x > Field.Right - Field.R) { x = 2 * (Field.Right - Field.R) - x; vx = -vx; }
            if (Touching(w, x, y)) return Snap(w, x, y);
        }
        return Snap(w, x, y);
    }
}
