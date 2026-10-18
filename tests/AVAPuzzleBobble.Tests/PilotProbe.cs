using AVAPuzzleBobble.Engine;

namespace AVAPuzzleBobble.Tests;

/// <summary>How the computer player gets on, round by round (set BOBBLE_PROBE to run it; BOBBLE_PROBE=lazy fires straight up).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_player_does()
    {
        if (Environment.GetEnvironmentVariable("BOBBLE_PROBE") is not { } mode) return;
        int cleared = 0;
        for (int n = 1; n <= RoundBook.Count; n++)
        {
            var w = new World();
            Sim.Start(w, RoundBook.Round(n));
            var p = new Pilot();
            int t = 0, shots = 0;
            for (; t < 60 * 300 && !w.Over && !w.Clear; t++)
            {
                var c = mode == "lazy" ? new Controls(0, t % 20 == 0) : p.Decide(w);
                bool before = w.Flying != null;
                Sim.Step(w, c);
                if (!before && w.Flying != null) shots++;
            }
            if (w.Clear) cleared++;
            output.WriteLine($"{n,2} ({w.Def.Colours} colours): {(w.Clear ? "CLEAR" : w.Over ? "lost" : "unfinished")} in {t / 60}s, {shots} shots, score {w.Score}, ceiling down {w.Drop}");
        }
        output.WriteLine($"{cleared} of {RoundBook.Count} cleared");
    }
}
