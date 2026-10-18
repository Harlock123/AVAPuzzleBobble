namespace AVAPuzzleBobble.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The round's number, a moment before it starts.</summary>
    Start,
    Play,
    /// <summary>The bubbles reached the line: a moment, then the round again, or the game is over.</summary>
    Lost,
    /// <summary>Every bubble gone: the time bonus, then the next round.</summary>
    Clear,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Fire, Bounce, Stick, Pop, Drop, Ceiling, Lose, Clear, Hurry, Tick,
    SayGo, SayClear,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 50;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 1.6, LostSeconds = 2.4, ClearSeconds = 3, OverSeconds = 4;
    public const int Lives0 = 3, TimeBonusSeconds = 60, TimePoints = 50;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music, _warned;
    private int _attractRound;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame(1);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Round { get; private set; } = 1;
    public int Score { get; private set; }
    public int Bonus { get; private set; }
    public bool Won { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Fire && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Round);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame(int round)
    {
        Lives = Lives0;
        Score = 0;
        Won = false;
        Load(round);
    }

    private void Load(int round)
    {
        Round = round;
        World = new World();
        Sim.Start(World, RoundBook.Round(round));
        World.Score = Score;
        SetPhase(Phase.Start);
        Message = $"ROUND {round}";
        MessageTime = StartSeconds;
        _warned = false;
        Pilot.Reset();
        Play(Sfx.SayGo);
    }

    public void StartGame(int round = 1)
    {
        NewGame(round);
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame(1 + _attractRound++ % 4 * 5);
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame(1);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                Sim.Step(w, default);
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
                Sim.Step(w, c);
                Score = w.Score;
                if (w.Flying == null && w.Idle > Sim.WarnSeconds && !_warned) { _warned = true; Play(Sfx.Hurry); }
                if (w.Flying != null) _warned = false;
                if (w.Clear)
                {
                    Bonus = Math.Max(0, TimeBonusSeconds - (int)w.Clock) * TimePoints;
                    w.Score += Bonus;
                    Score = w.Score;
                    SetPhase(Phase.Clear);
                    Music(false);
                    Play(Sfx.Clear);
                    Play(Sfx.SayClear);
                    Message = Bonus > 0 ? $"ROUND CLEAR!  BONUS {Bonus}" : "ROUND CLEAR!";
                    MessageTime = ClearSeconds;
                }
                else if (w.Over) { SetPhase(Phase.Lost); Music(false); }
                break;
            case Phase.Lost:
                Sim.Step(w, default);
                if (PhaseTime > LostSeconds)
                {
                    if (State == GameState.Attract) { ShowHighScores(-1); return; }
                    Lives--;
                    if (Lives <= 0) { End(); return; }
                    Load(Round);
                    return;
                }
                break;
            case Phase.Clear:
                Sim.Step(w, default);
                if (PhaseTime > ClearSeconds)
                {
                    if (State == GameState.Attract) { ShowHighScores(-1); return; }
                    if (Round >= RoundBook.Count)
                    {
                        Won = true;
                        Play(Sfx.Victory);
                        State = GameState.GameOver;
                        StateTime = 0;
                        return;
                    }
                    Load(Round + 1);
                    return;
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void End()
    {
        Play(Sfx.GameOver);
        Music(false);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a round, past the opening moment.</summary>
    public void Go(int round = 1)
    {
        StartGame(round);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
