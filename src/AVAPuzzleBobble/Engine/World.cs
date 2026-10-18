namespace AVAPuzzleBobble.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The controls: turn the launcher left or right, and fire.</summary>
public readonly record struct Controls(int Dx = 0, bool Fire = false);

/// <summary>A bubble on its way up.</summary>
public sealed class Shot
{
    public double X, Y, Vx, Vy;
    public byte Colour;
    public Shot Copy() => (Shot)MemberwiseClone();
}

/// <summary>A bubble on its way out: bursting, or falling off the board.</summary>
public sealed record Bit(double X, double Y, byte Colour, bool Falls)
{
    public double T, Vy;
}

/// <summary>
/// Everything in the round that moves or changes, kept together so it can be copied whole: the bubbles, how far the
/// ceiling has come down, the launcher and its bubbles, the shot in flight, the score.
/// </summary>
public sealed class World
{
    public RoundDef Def = null!;
    public byte[,] Grid = new byte[Field.Cols, Field.Rows];
    /// <summary>How many rows the ceiling has come down, and the shots since it last did.</summary>
    public int Drop, Shots;
    /// <summary>The launcher's angle in degrees: 90 is straight up.</summary>
    public double Angle = 90;
    public byte Current, Next;
    public Shot? Flying;
    public int Score;
    /// <summary>Seconds since the last shot (too long and it fires by itself).</summary>
    public double Idle, Clock;
    public bool FirePrev;
    public bool Over, Clear;
    public Dice Dice = new(1);
    public List<Bit> Bits = [];
    public List<Sfx> Heard = [];

    public World Copy() => new()
    {
        Def = Def, Grid = (byte[,])Grid.Clone(), Drop = Drop, Shots = Shots, Angle = Angle, Current = Current, Next = Next, Flying = Flying?.Copy(),
        Score = Score, Idle = Idle, Clock = Clock, FirePrev = FirePrev, Over = Over, Clear = Clear, Dice = Dice,
    };

    public int Count
    {
        get
        {
            int n = 0;
            foreach (var b in Grid) if (b != 0) n++;
            return n;
        }
    }
}
