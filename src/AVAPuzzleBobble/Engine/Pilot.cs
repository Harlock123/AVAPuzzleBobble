namespace AVAPuzzleBobble.Engine;

/// <summary>
/// The computer player, for the demonstration and the tests. For each bubble it tries every angle the launcher can
/// turn to (straight shots and banks off the walls), works out where each would stick, and scores what it would leave:
/// bubbles burst and dropped are best; failing that, a bubble beside its own colour, set high rather than low, and
/// never one that would reach the line. Then it turns the launcher there and fires.
/// </summary>
public sealed class Pilot
{
    private double? _target;
    private byte _for;
    private int _shotsFor = -1;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _target = null;
        _shotsFor = -1;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        if (w.Flying != null || w.Over || w.Clear) return default;
        int key = w.Shots + w.Drop * 100 + w.Count * 10000;
        if (_target == null || _for != w.Current || _shotsFor != key)
        {
            _for = w.Current;
            _shotsFor = key;
            _target = Choose(w);
        }
        double diff = _target.Value - w.Angle;
        double step = Sim.AimSpeed * Field.Step;
        if (Math.Abs(diff) < step / 2)
        {
            Doing = "firing";
            _target = null;
            return new Controls(Fire: !w.FirePrev);
        }
        Doing = "aiming";
        return new Controls(diff > 0 ? -1 : 1);
    }

    /// <summary>The best angle, among those the launcher can reach from where it is in whole steps.</summary>
    public static double Choose(World w)
    {
        double step = Sim.AimSpeed * Field.Step, best = double.NegativeInfinity, pick = w.Angle;
        int lo = (int)Math.Ceiling((Sim.MinAngle - w.Angle) / step), hi = (int)Math.Floor((Sim.MaxAngle - w.Angle) / step);
        for (int k = lo; k <= hi; k++)
        {
            double a = w.Angle + k * step;
            double v = Value(w, a) - Math.Abs(k) * 0.01;
            if (v > best) { best = v; pick = a; }
        }
        return pick;
    }

    public static double Value(World w, double angle)
    {
        var (c, r) = Sim.Trace(w, angle);
        var g = (byte[,])w.Grid.Clone();
        g[c, r] = w.Current;
        var group = RoundBook.Group(g, c, r);
        double v;
        int depth = r + w.Drop;
        if (group.Count >= 3)
        {
            foreach (var (gc, gr) in group) g[gc, gr] = 0;
            int dropped = RoundBook.Floating(g).Count;
            v = 100 + group.Count * 10 + dropped * 25;
        }
        else
        {
            // A bubble next to its own colour builds toward a burst; one next to the next bubble's colour sets it up.
            int same = 0, next = 0;
            foreach (var n in Field.Neighbours(c, r))
                if (Field.Inside(n.Col, n.Row))
                {
                    if (g[n.Col, n.Row] == w.Current) same++;
                    if (g[n.Col, n.Row] == w.Next) next++;
                }
            v = same * 12 + (group.Count == 2 ? 15 : 0) - depth * 4 - next * 2;
            if (depth >= Field.DeathRow - 1) v -= 1000;
        }
        return v;
    }
}
