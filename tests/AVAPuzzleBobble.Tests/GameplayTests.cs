using Avalonia.Input;
using AVAPuzzleBobble.Engine;

namespace AVAPuzzleBobble.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avapuzzlebobble-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    /// <summary>A round with the bubbles given (as (col, row, colour)), and the launcher holding the colour given.</summary>
    private static World With(byte current, params (int C, int R, byte Colour)[] bubbles)
    {
        var def = new RoundDef { Number = 1, Colours = 4 };
        foreach (var (c, r, col) in bubbles) def.Grid[c, r] = col;
        var w = new World();
        Sim.Start(w, def);
        w.Current = current;
        return w;
    }

    private static void FlyOut(World w)
    {
        for (int i = 0; i < 300 && w.Flying != null; i++) Sim.Step(w, default);
    }

    // ───────────────────────────── the board ─────────────────────────────

    [Fact]
    public void Rows_alternate_eight_and_seven_and_neighbours_fit_the_honeycomb()
    {
        Assert.Equal(8, Field.Width_(0));
        Assert.Equal(7, Field.Width_(1));
        foreach (var (c, r) in Field.Neighbours(3, 2).Where(n => Field.Inside(n.Col, n.Row)))
        {
            double dx = Field.X(c, r) - Field.X(3, 2), dy = Field.Y(r, 0) - Field.Y(2, 0);
            Assert.Equal(2 * Field.R, Math.Sqrt(dx * dx + dy * dy), 3);
        }
    }

    [Fact]
    public void Every_round_is_mirrored_hangs_from_the_ceiling_and_grows_harder()
    {
        for (int n = 1; n <= RoundBook.Count; n++)
        {
            var d = RoundBook.Round(n);
            Assert.Empty(RoundBook.Floating(d.Grid));
            for (int r = 0; r < Field.Rows; r++)
                for (int c = 0; c < Field.Width_(r); c++)
                    Assert.Equal(d.Grid[c, r], d.Grid[Field.Width_(r) - 1 - c, r]);
            Assert.InRange(d.Colours, 3, 6);
        }
        Assert.True(RoundBook.Round(30).Colours > RoundBook.Round(1).Colours);
    }

    // ───────────────────────────── shooting ─────────────────────────────

    [Fact]
    public void Left_and_right_turn_the_launcher_within_its_limits()
    {
        var w = With(1, (0, 0, 2));
        Sim.Step(w, new Controls(1));
        Assert.True(w.Angle < 90);
        for (int i = 0; i < 300; i++) Sim.Step(w, new Controls(-1));
        Assert.Equal(Sim.MaxAngle, w.Angle);
    }

    [Fact]
    public void A_shot_straight_up_sticks_to_the_ceiling()
    {
        var w = With(1, (0, 0, 2));
        Sim.Step(w, new Controls(Fire: true));
        Assert.NotNull(w.Flying);
        FlyOut(w);
        Assert.Equal(1, w.Grid[3, 0] + w.Grid[4, 0]);
    }

    [Fact]
    public void A_shot_banks_off_the_wall()
    {
        var w = With(1, (0, 0, 2));
        w.Angle = 30;
        Sim.Step(w, new Controls(Fire: true));
        bool bounced = false;
        double vx = w.Flying!.Vx;
        for (int i = 0; i < 300 && w.Flying != null; i++) { Sim.Step(w, default); if (w.Flying != null && Math.Sign(w.Flying.Vx) != Math.Sign(vx)) bounced = true; }
        Assert.True(bounced);
        Assert.Contains(Sfx.Bounce, w.Heard);
    }

    [Fact]
    public void Three_of_a_colour_burst_and_what_hangs_from_them_falls()
    {
        // Two reds on the ceiling, and a third stuck under them with a blue hanging from it.
        var w = With(1, (3, 0, 1), (4, 0, 1), (3, 2, 2), (0, 0, 3));
        w.Grid[3, 1] = 1;
        Sim.Settle(w, 3, 1);
        Assert.Equal(0, w.Grid[3, 0] + w.Grid[4, 0] + w.Grid[3, 1]);
        Assert.Equal(0, w.Grid[3, 2]);
        Assert.Equal(3, w.Grid[0, 0]);
        Assert.Equal(3 * Sim.PopPoints + Sim.DropPoints * 2, w.Score);
        Assert.Contains(Sfx.Pop, w.Heard);
        Assert.Contains(Sfx.Drop, w.Heard);
    }

    [Fact]
    public void Two_of_a_colour_just_stick()
    {
        var w = With(1, (4, 0, 1), (0, 0, 2));
        Sim.Step(w, new Controls(Fire: true));
        FlyOut(w);
        Assert.Equal(3, w.Count);
        Assert.Equal(0, w.Score);
    }

    [Fact]
    public void The_launcher_only_holds_colours_still_on_the_board()
    {
        var w = With(1, (0, 0, 3), (1, 0, 3));
        for (int i = 0; i < 10; i++)
        {
            Sim.Step(w, new Controls(Fire: i % 2 == 0));
            Assert.Equal(3, w.Next);
        }
    }

    [Fact]
    public void The_ceiling_comes_down_every_few_shots_and_the_line_reached_loses_the_round()
    {
        var w = With(1, (0, 0, 2), (7, 0, 3));
        int per = Sim.ShotsPerDrop(1);
        for (int k = 0; k < per; k++)
        {
            w.Angle = 90 + (k % 2 == 0 ? 30 : -30);
            w.Current = (byte)(k % 2 == 0 ? 4 : 1);
            Sim.Step(w, new Controls(Fire: true));
            Sim.Step(w, default);
            FlyOut(w);
        }
        Assert.Equal(1, w.Drop);
        w.Drop = Field.DeathRow - 1;
        w.Grid[0, 1] = 2;
        Sim.Settle(w, 0, 1);
        Assert.True(w.Over);
    }

    [Fact]
    public void Too_long_without_a_shot_and_it_fires_by_itself()
    {
        var w = With(1, (0, 0, 2));
        for (int i = 0; i < 60 * Sim.HurrySeconds + 2; i++) Sim.Step(w, default);
        Assert.True(w.Flying != null || w.Count == 2);
    }

    [Fact]
    public void Clearing_every_bubble_wins_the_round()
    {
        var w = With(1, (3, 0, 1), (4, 0, 1));
        Sim.Step(w, new Controls(Fire: true));
        FlyOut(w);
        Assert.True(w.Clear);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void A_clear_round_scores_the_time_bonus_and_goes_on()
    {
        var g = Engine();
        g.Go(1);
        g.World.Grid = new byte[Field.Cols, Field.Rows];
        g.World.Grid[3, 0] = g.World.Grid[4, 0] = 1;
        g.World.Current = 1;
        g.Tick(new Controls(Fire: true));
        g.Run(60);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.True(g.Bonus > 0);
        g.Run((int)(60 * GameEngine.ClearSeconds) + 5);
        Assert.Equal(2, g.Round);
    }

    [Fact]
    public void Losing_a_round_costs_a_life_and_the_last_ends_the_game()
    {
        var g = Engine();
        g.Go(1);
        for (int i = 0; i < GameEngine.Lives0; i++)
        {
            g.Run((int)(60 * GameEngine.StartSeconds) + 5);
            g.World.Over = true;
            g.Run((int)(60 * GameEngine.LostSeconds) + 5);
        }
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Left_alone_the_title_shows_the_computer_playing_then_the_high_scores()
    {
        var g = Engine();
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        for (int i = 0; i < 60 * (GameEngine.AttractSeconds + 5) && g.State == GameState.Attract; i++) g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void Pause_stops_everything()
    {
        var g = Engine();
        g.Go();
        g.Run(30);
        double t = g.World.Clock;
        g.Pause();
        g.Run(200);
        Assert.Equal(t, g.World.Clock);
        g.Resume();
        g.Run(10);
        Assert.True(g.World.Clock > t);
    }

    [Fact]
    public void A_copy_of_the_world_goes_its_own_way()
    {
        var w = With(1, (3, 0, 1), (4, 0, 1));
        var c = w.Copy();
        Sim.Step(c, new Controls(Fire: true));
        FlyOut(c);
        Assert.Equal(2, w.Count);
        Assert.Equal(0, c.Count);
    }

    [Fact]
    public void The_keys_reach_the_controls()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Fire);
        input.OnKeyUp(Key.Space);
        Assert.Equal(default, input.Player);
        input.Dispose();

        var g = Engine();
        g.Go();
        g.Run(10, new Controls(1));
        Assert.True(g.World.Angle < 90);
    }

    // ───────────────────────────── the computer player ─────────────────────────────

    [Fact]
    public void The_computer_clears_every_round_and_firing_straight_up_does_not()
    {
        for (int n = 1; n <= RoundBook.Count; n++)
        {
            var w = new World();
            Sim.Start(w, RoundBook.Round(n));
            var p = new Pilot();
            for (int t = 0; t < 60 * 120 && !w.Clear && !w.Over; t++) Sim.Step(w, p.Decide(w));
            Assert.True(w.Clear, $"round {n}");
        }
        int lazy = 0;
        for (int n = 10; n <= RoundBook.Count; n++)
        {
            var w = new World();
            Sim.Start(w, RoundBook.Round(n));
            for (int t = 0; t < 60 * 120 && !w.Clear && !w.Over; t++) Sim.Step(w, new Controls(0, t % 20 == 0));
            if (w.Clear) lazy++;
        }
        Assert.True(lazy <= 2, $"{lazy} cleared just firing straight up");
    }
}
