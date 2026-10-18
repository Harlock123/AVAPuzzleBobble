using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPuzzleBobble.Engine;

namespace AVAPuzzleBobble.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAPuzzleBobble/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double R = Field.R;

    /// <summary>The sky behind each set of five rounds.</summary>
    private static readonly (uint Top, uint Low)[] Skies =
    [
        (0xFF7AB8F0, 0xFFE0F0FF), (0xFFF0A0C0, 0xFFFFE8F0), (0xFF90D890, 0xFFF0FFE8), (0xFFC0A0F0, 0xFFF4ECFF), (0xFFF0C080, 0xFFFFF4E0), (0xFF80D0D0, 0xFFE8FFFF),
    ];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawSky(dc, 2); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawSky(dc, 3); DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawSky(dc, (g.Round - 1) / 5);
                DrawBoard(dc, g.World);
                DrawSides(dc, g.World);
                if (g.Message is { } msg && g.Phase is Phase.Start or Phase.Clear)
                {
                    dc.FillRectangle(Brush(0xC0203060), new Rect(Field.Left, 330, Field.Right - Field.Left, 70), 10);
                    Text(dc, msg, new Point(Field.LaunchX, 365), msg.Length > 14 ? 34 : 54, 0xFFFFFFFF, center: true);
                }
                if (g.Phase == Phase.Lost)
                {
                    dc.FillRectangle(Brush(0xC0600020), new Rect(Field.Left, 330, Field.Right - Field.Left, 70), 10);
                    Text(dc, "OH NO!", new Point(Field.LaunchX, 365), 54, 0xFFFFFFFF, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 16), 22, 0xFF203060, center: true, shadow: false);
    }

    private static uint Blend(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private void DrawSky(DrawingContext dc, int k)
    {
        var (top, low) = Skies[k % Skies.Length];
        for (int i = 0; i < 24; i++)
            dc.FillRectangle(Brush(Blend(top, low, i / 23.0)), new Rect(0, i * 32, Field.Width, 33));
        // Bubbles drifting up the sides.
        for (int i = 0; i < 14; i++)
        {
            double x = i % 2 == 0 ? 40 + Hash(i, 1, k) % 180 : 800 + Hash(i, 1, k) % 190;
            double y = 800 - (_clock * (20 + Hash(i, 2, k) % 30) + Hash(i, 3, k) % 800) % 860;
            double r = 6 + Hash(i, 4, k) % 14;
            dc.DrawEllipse(Brush(0x30FFFFFF), Pen(0x80FFFFFF, 1.5), new Point(x, y), r, r);
        }
    }

    /// <summary>A glossy bubble.</summary>
    private static void Bubble(DrawingContext dc, double x, double y, byte colour, double scale = 1, double alpha = 1)
    {
        uint c = RoundBook.Colours[colour];
        double r = (R - 1) * scale;
        dc.DrawEllipse(Brush(Alpha(c, alpha)), Pen(Alpha(Blend(c, 0xFF000000, 0.45), alpha), 2), new Point(x, y), r, r);
        dc.DrawEllipse(Brush(Alpha(Blend(c, 0xFFFFFFFF, 0.25), alpha)), null, new Point(x - r * 0.12, y - r * 0.12), r * 0.7, r * 0.7);
        dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.8 * alpha)), null, new Point(x - r * 0.38, y - r * 0.4), r * 0.22, r * 0.16);
    }

    private void DrawBoard(DrawingContext dc, World w)
    {
        double left = Field.Left, right = Field.Right, top = Field.Top;
        // The cabinet: a pale glass well with a frame.
        dc.FillRectangle(Brush(0x60FFFFFF), new Rect(left, top, right - left, Field.LaunchY + R + 20 - top));
        dc.DrawRectangle(null, Pen(0xFF5A4A8A, 10), new Rect(left - 5, top - 5, right - left + 10, Field.LaunchY + R + 30 - top), 8);
        // The ceiling plate, lowered by however many rows, on its chains.
        double ceil = Sim.Ceiling(w);
        if (w.Drop > 0)
        {
            dc.FillRectangle(Brush(0xFF8A7AB0), new Rect(left, top, right - left, ceil - top));
            for (double x = left + 40; x < right; x += 120)
                for (double y = top + 6; y < ceil - 6; y += 14) dc.DrawEllipse(null, Pen(0xFF5A4A80, 3), new Point(x, y), 4, 7);
        }
        dc.FillRectangle(Brush(0xFF5A4A8A), new Rect(left, ceil - 8, right - left, 8));
        // The line not to reach.
        double line = Field.Y(Field.DeathRow - w.Drop, w.Drop) - R;
        for (double x = left + 8; x < right; x += 28) dc.DrawLine(Pen(0xC0E04040, 3), new Point(x, line), new Point(x + 14, line));
        for (int r = 0; r < Field.Rows; r++)
            for (int c = 0; c < Field.Width_(r); c++)
                if (w.Grid[c, r] != 0)
                {
                    // Bubbles about to reach the line tremble.
                    double shake = r + w.Drop >= Field.DeathRow - 2 && (int)(_clock * 20) % 2 == 0 ? 1.5 : 0;
                    Bubble(dc, Field.X(c, r) + shake, Field.Y(r, w.Drop), w.Grid[c, r]);
                }
        foreach (var b in w.Bits)
        {
            if (b.Falls) Bubble(dc, b.X, b.Y + b.Vy * b.T * 0.5 + 300 * b.T * b.T, b.Colour, 1, Math.Max(0, 1 - b.T / 1.4));
            else Bubble(dc, b.X, b.Y, b.Colour, 1 + b.T * 2, Math.Max(0, 1 - b.T / 0.35));
        }
        if (w.Flying is { } s) Bubble(dc, s.X, s.Y, s.Colour);
        DrawLauncher(dc, w);
    }

    private void DrawLauncher(DrawingContext dc, World w)
    {
        double x = Field.LaunchX, y = Field.LaunchY, a = w.Angle * Math.PI / 180;
        // The base: a cog.
        dc.FillEllipse(x, y + 30, 46, 0xFF8A7AB0);
        for (int k = 0; k < 10; k++)
        {
            double t = k * Math.PI / 5 + w.Angle * 0.05;
            dc.FillEllipse(x + Math.Cos(t) * 46, y + 30 + Math.Sin(t) * 46, 8, 0xFF6A5A90);
        }
        // The pointer, then the bubble on it.
        double len = 110;
        var tip = new Point(x + Math.Cos(a) * len, y - Math.Sin(a) * len);
        dc.DrawLine(Pen(0xFF404060, 12), new Point(x, y), tip);
        dc.DrawLine(Pen(0xFFE0E0F0, 6), new Point(x, y), tip);
        Poly(dc, 0xFFE0E0F0, 0xFF404060, 2, new Point(tip.X + Math.Cos(a) * 22, tip.Y - Math.Sin(a) * 22),
            new Point(tip.X + Math.Cos(a + 2.3) * 16, tip.Y - Math.Sin(a + 2.3) * 16), new Point(tip.X + Math.Cos(a - 2.3) * 16, tip.Y - Math.Sin(a - 2.3) * 16));
        if (w.Flying == null && w.Current != 0) Bubble(dc, x, y, w.Current);
        // The next one, waiting beside it.
        if (w.Next != 0)
        {
            Bubble(dc, x - 110, y + 18, w.Next, 0.75);
            Text(dc, "NEXT", new Point(x - 110, y + 54), 18, 0xFF203060, center: true, shadow: false);
        }
        // The little cranker turning it.
        double cx = x + 100, cy = y + 40, bob = Math.Sin(_clock * 6) * 2;
        dc.DrawEllipse(Brush(0xFF60C060), Pen(0xFF307030, 2), new Point(cx, cy - 18 + bob), 22, 20);
        dc.FillEllipse(cx - 7, cy - 24 + bob, 5, 0xFFFFFFFF);
        dc.FillEllipse(cx + 7, cy - 24 + bob, 5, 0xFFFFFFFF);
        dc.FillEllipse(cx - 6, cy - 23 + bob, 2.2, 0xFF000000);
        dc.FillEllipse(cx + 8, cy - 23 + bob, 2.2, 0xFF000000);
        dc.DrawLine(Pen(0xFF307030, 3), new Point(cx - 18, cy - 12 + bob), new Point(cx - 40, cy - 20 + Math.Sin(w.Angle * 0.2) * 6));
        // Hurry!
        if (w.Flying == null && w.Idle > Sim.WarnSeconds && (int)(_clock * 4) % 2 == 0)
            Text(dc, $"HURRY! {Math.Max(0, (int)Math.Ceiling(Sim.HurrySeconds - w.Idle))}", new Point(x, y - 150), 40, 0xFFE02040, center: true);
    }

    private void DrawSides(DrawingContext dc, World w)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(128, 80), 28, 0xFF203060, center: true, shadow: false);
        Text(dc, $"{g.Score}", new Point(128, 116), 40, 0xFFFFFFFF, center: true);
        Text(dc, "HIGH", new Point(128, 180), 24, 0xFF203060, center: true, shadow: false);
        Text(dc, $"{Math.Max(g.HighScores.TopScore, g.Score)}", new Point(128, 210), 32, 0xFFFFE040, center: true);
        Text(dc, "ROUND", new Point(896, 80), 28, 0xFF203060, center: true, shadow: false);
        Text(dc, $"{g.Round}", new Point(896, 122), 56, 0xFFFFFFFF, center: true);
        Text(dc, "LIVES", new Point(896, 200), 24, 0xFF203060, center: true, shadow: false);
        for (int i = 0; i < Math.Min(g.Lives, 6); i++) Bubble(dc, 860 + (i % 3) * 36, 236 + i / 3 * 36, 4, 0.45);
        Text(dc, "SHOTS TO", new Point(896, 320), 22, 0xFF203060, center: true, shadow: false);
        Text(dc, "THE DROP", new Point(896, 344), 22, 0xFF203060, center: true, shadow: false);
        Text(dc, $"{Sim.ShotsPerDrop(g.Round) - w.Shots}", new Point(896, 384), 44, 0xFFE02040, center: true);
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawSky(dc, 0);
        // A pile of bubbles along the bottom.
        for (int k = 0; k < 16; k++) Bubble(dc, 32 + k * 64, 728 - (k % 2) * 20, (byte)(1 + k % 6));
        string title = "AVA PUZZLE BOBBLE";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 28 + 28 + i * 56, ty = 120 + Math.Sin(_clock * 2.4 + i * 0.45) * 6;
            if (title[i] != ' ') Bubble(dc, tx, ty, (byte)(1 + i % 6), 0.95);
            Text(dc, title[i].ToString(), new Point(tx, ty), 64, 0xFFFFFFFF, center: true);
        }
        Text(dc, "AFTER TAITO'S 1994 GAME", new Point(cx, 196), 28, 0xFF203060, center: true, shadow: false);
        dc.FillRectangle(Brush(0xC0203060), new Rect(cx - 400, 230, 800, 160), 14);
        Text(dc, "AIM THE LAUNCHER AND FIRE BUBBLES INTO THE PILE. THREE OF A COLOUR", new Point(cx, 256), 24, White, center: true);
        Text(dc, "TOGETHER BURST, AND ANYTHING LEFT HANGING FALLS. BANK SHOTS OFF THE", new Point(cx, 284), 24, White, center: true);
        Text(dc, "WALLS. THE CEILING COMES DOWN EVERY FEW SHOTS: CLEAR THE ROUND BEFORE", new Point(cx, 312), 24, White, center: true);
        Text(dc, "THE BUBBLES REACH THE LINE. THIRTY ROUNDS, THREE COLOURS UP TO SIX.", new Point(cx, 340), 24, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 450), 46, 0xFFFFE040, center: true);
        Text(dc, "LEFT/RIGHT: AIM   UP OR SPACE: FIRE   P: PAUSE   M: SOUND", new Point(cx, 560), 24, 0xFF203060, center: true, shadow: false);
        Text(dc, "MUSIC: THE BLUE DANUBE (STRAUSS, 1866) AND A TUNE OF ITS OWN", new Point(cx, 596), 20, 0xFF203060, center: true, shadow: false);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawSky(dc, g.Won ? 2 : 1);
        dc.FillRectangle(Brush(0xC0203060), new Rect(cx - 360, 150, 720, 230), 14);
        Text(dc, g.Won ? "ALL THIRTY ROUNDS!" : "GAME OVER", new Point(cx, 220), g.Won ? 72 : 100, (int)(_clock * 4) % 2 == 0 ? 0xFFFFE040 : White, center: true);
        Text(dc, $"SCORE {g.Score}", new Point(cx, 300), 46, White, center: true);
        Text(dc, $"ROUND {g.Round}", new Point(cx, 350), 30, Cyan, center: true);
        for (int k = 0; k < 8; k++) Bubble(dc, cx - 224 + k * 64, 500 + Math.Sin(_clock * 3 + k) * 10, (byte)(1 + k % 6));
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0203060), new Rect(cx - 330, 70, 660, 640), 14);
        Text(dc, "THE BEST BUBBLE BURSTERS", new Point(cx, 120), 52, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   ROUND", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0203060), new Rect(cx - 330, 140, 660, 460), 14);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
