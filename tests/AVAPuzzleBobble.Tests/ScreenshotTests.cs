using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAPuzzleBobble.Engine;
using AVAPuzzleBobble.Views;
using Controls = AVAPuzzleBobble.Engine.Controls;

namespace AVAPuzzleBobble.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set BOBBLE_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avapuzzlebobble-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("BOBBLE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAPuzzleBobble-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 120)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Moment(int round, string name, Func<GameEngine, bool> moment)
    {
        var (w, c, g) = Open();
        g.Go(round);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 98450, 17);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 10);
        for (int i = 0; i < 60 * 200 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Aiming() => Moment(13, "aim", g => g.World.Flying == null && g.World.Clock > 1 && Math.Abs(g.World.Angle - 90) > 25);

    [AvaloniaFact]
    public void A_shot_in_flight() => Moment(19, "shot", g => g.World.Flying is { } s && s.Y < 420 && s.Y > 300);

    [AvaloniaFact]
    public void Bursting() => Moment(8, "burst", g => g.World.Bits.Any(b => b.Falls && b.T is > 0.15 and < 0.35));

    [AvaloniaFact]
    public void The_ceiling_down() => Moment(26, "ceiling", g => g.World.Drop >= 2 && g.World.Flying == null && g.World.Bits.Count == 0);

    [AvaloniaFact]
    public void Round_clear() => Moment(4, "clear", g => g.Phase == Phase.Clear && g.PhaseTime > 0.5);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(20);
        g.SetLives(1);
        g.World.Score = 23450;
        for (int i = 0; i < 60 * 120 && g.State == GameState.Playing; i++) { g.Tick(new Controls(0, i % 20 == 0)); if (i % 3 == 0) c.Advance(0.05); }
        for (int i = 0; i < 90; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(GameState.GameOver, g.State);
        Shot(w, c, "game-over");
    }
}
