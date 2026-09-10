namespace P02;

/// <summary>
/// What is wrong right now, in the order it needs fixing.
///
/// Everything this reports was already knowable - it was spread over a status
/// bar, three coloured labels, a log file and a tooltip, in the app's words
/// rather than the player's. Working out which of them mattered took somebody
/// who had read the source.
///
/// So: one list, worst first, each line saying what is wrong and the single
/// thing that fixes it. A check that cannot suggest an action is not worth
/// making.
/// </summary>
internal static class SelfCheck
{
    internal readonly record struct Finding(int Rank, string What, string Fix)
    {
        /// <summary>0 stops it working, 1 makes it worse, 2 is worth knowing.</summary>
        public bool Stops => Rank == 0;
    }

    public static List<Finding> Run(AppConfig cfg, MonitorEngine engine, string version)
    {
        var found = new List<Finding>();

        // Whether anything exact is actually reading right now. Several of the
        // checks below are about the numbers, and the numbers are only the
        // thing keeping you alive while memory is not.
        bool memoryReading = cfg.UseMemory && engine.MemoryLocked;

        void Say(int rank, string what, string fix) => found.Add(new Finding(rank, what, fix));

        if (!cfg.Life.Enabled && !cfg.Mana.Enabled)
            Say(0, "Nothing is being watched.",
                "Tick \"Watch my life\" on the Life panel.");

        // With the game shut, most of what follows cannot be judged at all -
        // nothing can be read from a window that is not there - so it says the
        // one true thing and stops.
        if (Native.FindWindowRect(cfg.WindowMatch) is null)
        {
            Say(1, $"No window titled \"{cfg.WindowMatch}\" is open.",
                "Start the game. Nothing can be read or sent until it is running.");
            return found;
        }

        if (!engine.TextAvailable)
            Say(0, "Windows cannot read text on this machine, so the numbers are out.",
                "Turn on \"Read game memory\", which does not need it. "
                + $"({engine.TextUnavailable})");

        foreach (var (name, w) in new[] { ("Life", cfg.Life), ("Mana", cfg.Mana) })
        {
            if (!w.Enabled) continue;

            bool numbers = w.UseText && w.TextRegion.IsValid;
            if (!numbers && !memoryReading)
                Say(0, $"{name} has nothing exact to read.",
                    "Press \"Set it up for me\" with the game on screen.");

            // Not before it has had a chance. Reading takes a second or two to
            // start, and asking at launch always answered "nothing is coming
            // back" - which is how a working setup was accused of being broken
            // every single time it opened.
            // Not a fault that stops anything while memory is reading the pool
            // exactly. It said "this is not protecting you yet" on a machine
            // whose own panel read "memory, life 1,340/1,504" - which is not
            // merely wrong, it is the opposite of what was happening, and it
            // teaches people to ignore the one warning that matters.
            if (numbers && engine.TextAvailable && engine.UptimeMs > 6000
                && !engine.NumbersReading(name))
                Say(memoryReading ? 2 : 0,
                    memoryReading
                        ? $"{name} is being read from memory; its numbers are not coming back."
                        : $"{name}'s numbers are set up but nothing is coming back from them.",
                    memoryReading
                        ? "Nothing is wrong with the reading. The numbers are only a "
                          + "cross-check while memory has your character."
                        : "Press \"Set it up for me\" while standing somewhere safe with the "
                          + "numbers on screen. They are not drawn in menus or on the death "
                          + "screen.");

            if (w.Key.Trim().Length == 0)
                Say(0, $"{name} has no key set, so it has nothing to press.",
                    $"Click the Key box on the {name} panel and press your flask key.");

            if (w.BurstCount > 1)
                Say(2, $"{name} sends {w.BurstCount} presses per trigger.",
                    "A flask recovers over time and the second press is mostly wasted. "
                    + "One is almost always right.");

            if (w.HoldMs > 150)
                Say(2, $"{name} holds each key for {w.HoldMs} ms.",
                    "70 ms spans a frame at any sane rate. Longer only delays the "
                    + "emergency press.");

            if (w.UberBelow >= w.Threshold)
                Say(1, $"{name}'s emergency threshold ({w.UberBelow:P0}) is not below its "
                     + $"trigger ({w.Threshold:P0}).",
                    "The net should sit well under the trigger - around a third of it - "
                    + "or it is just a second trigger spending charges.");

            if (w.CooldownMs < w.BurstCount * (w.HoldMs + 40))
                Say(2, $"{name}'s cooldown is shorter than one burst takes to send.",
                    "Requests below that are skipped, so the cooldown is not doing what "
                    + "it says.");
        }

        if (cfg.UseMemory && !engine.MemoryLocked && engine.MemoryStatus.Length > 0)
            Say(1, $"Memory: {engine.MemoryStatus}",
                "Stand at full life and mana and press Re-scan.");
        else if (cfg.UseMemory && !engine.MemoryLocked)
            Say(1, "Memory is switched on but has not found your character.",
                "Stand at full life and mana and press Re-scan. A full pool is the one "
                + "hint that needs nothing read off the screen.");

        if (!cfg.UseMemory)
            Say(2, "Memory is off, so everything is read from the screen.",
                "Memory refreshes every 15 ms where reading the screen manages 60-900, "
                + "and it cannot misread a digit.");

        if (!cfg.NumbersOnly && !memoryReading)
            Say(1, "The globe colours are allowed to decide.",
                "Tick \"Numbers only\". The globe cannot tell life from energy shield "
                + "and reads a poisoned globe as empty; nearly every misfire came from it.");

        if (cfg.SoundOnFire && cfg.SoundGapMs > 8000)
            Say(2, $"The ding is limited to one every {cfg.SoundGapMs / 1000} seconds.",
                "That is long enough to miss that it fired at all.");

        if (!cfg.CheckUpdatesOnStart)
            Say(2, "It will not look for updates on its own.",
                "Being behind is what caused most of the deaths this has ever had. "
                + "Tick \"Check at launch\".");

        return found.OrderBy(f => f.Rank).ToList();
    }

    /// <summary>The findings as something a person can read, worst first.</summary>
    public static string Describe(List<Finding> found)
    {
        if (found.Count == 0)
            return "Nothing looks wrong. It is watching, it has something exact to read, "
                 + "and it has a key to press.";

        var text = new System.Text.StringBuilder();
        bool anyStop = found.Any(f => f.Stops);

        text.AppendLine(anyStop
            ? "This is not protecting you yet:"
            : "It is working. These are worth knowing:");
        text.AppendLine();

        foreach (var f in found)
        {
            text.AppendLine((f.Stops ? "STOPS IT WORKING - " : "") + f.What);
            text.AppendLine("    " + f.Fix);
            text.AppendLine();
        }

        return text.ToString().TrimEnd();
    }
}
