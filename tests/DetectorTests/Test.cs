using P02;

static class T
{
    const int W = 700, H = 460, Bpp = 4;
    static byte[] _buf = new byte[W * H * Bpp];

    static void Px(int x, int y, int b, int g, int r)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        int i = (y * W + x) * Bpp;
        _buf[i] = (byte)b; _buf[i + 1] = (byte)g; _buf[i + 2] = (byte)r; _buf[i + 3] = 255;
    }

    static void Disc(int cx, int cy, int rad, int b, int g, int r)
    {
        for (int y = cy - rad; y <= cy + rad; y++)
            for (int x = cx - rad; x <= cx + rad; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= rad * rad) Px(x, y, b, g, r);
    }

    static void Rect(int x0, int y0, int w, int h, int b, int g, int r)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++) Px(x, y, b, g, r);
    }

    static void Reset()
    {
        for (int i = 0; i < _buf.Length; i += Bpp)
        { _buf[i] = 40; _buf[i + 1] = 38; _buf[i + 2] = 36; _buf[i + 3] = 255; }
    }

    static void Main()
    {
        int fails = 0;

        // The real bottom-right corner: mana globe, plus blue skill gems and a
        // blue flask to its left. Projections merged all of this; blobs must not.
        Reset();
        Disc(520, 300, 110, 190, 90, 40);              // mana globe
        Rect(60, 330, 44, 44, 200, 120, 60);           // gem icon
        Rect(112, 330, 44, 44, 200, 120, 60);          // gem icon
        Rect(164, 330, 44, 44, 200, 120, 60);          // gem icon
        Rect(30, 250, 26, 70, 210, 110, 50);           // flask
        var got = OrbDetector.Locate(_buf, W, H, blue: true);
        fails += Check("mana globe beside gems+flask", got, 520, 300, 110);

        // Specular highlight punching a hole in the middle of the globe.
        Reset();
        Disc(520, 300, 110, 190, 90, 40);
        Rect(470, 230, 60, 18, 250, 250, 250);
        got = OrbDetector.Locate(_buf, W, H, blue: true);
        fails += Check("globe with glare streak", got, 520, 300, 110);

        // Life corner, red.
        Reset();
        Disc(150, 300, 100, 40, 40, 180);
        Rect(300, 300, 120, 20, 50, 50, 190);          // a red bar, not round
        got = OrbDetector.Locate(_buf, W, H, blue: false);
        fails += Check("life globe beside a red bar", got, 150, 300, 100);

        // Nothing but icons: must refuse rather than box the icons.
        Reset();
        Rect(60, 330, 44, 44, 200, 120, 60);
        Rect(112, 330, 44, 44, 200, 120, 60);
        got = OrbDetector.Locate(_buf, W, H, blue: true);
        if (got is null) Console.WriteLine("PASS  icons only -> refused");
        else { Console.WriteLine($"FAIL  icons only -> returned {got}"); fails++; }

        // The mana globe's centre is a pale washed-out blue: blue leads green
        // by a wide absolute margin but barely at all proportionally, which is
        // what defeated the old ratio test.
        Reset();
        Disc(520, 300, 110, 150, 70, 35);
        Disc(520, 300, 60, 210, 165, 130);
        got = OrbDetector.Locate(_buf, W, H, blue: true);
        fails += Check("globe with pale washed centre", got, 520, 300, 110);

        // Fill fraction across levels, on a real disc.
        var cfg = new WatcherConfig { Hue = "blue", ColourMargin = 30, MinValue = 50 };
        foreach (double want in new[] { 1.0, 0.75, 0.5, 0.25 })
        {
            Reset();
            int cx = 520, cy = 300, rad = 110;
            int top = (int)(cy - rad + (1 - want) * rad * 2);
            for (int y = top; y <= cy + rad; y++)
                for (int x = cx - rad; x <= cx + rad; x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= rad * rad)
                        Px(x, y, 190, 90, 40);

            var box = new byte[(rad * 2 + 1) * (rad * 2 + 1) * Bpp];
            int bw = rad * 2 + 1;
            for (int y = 0; y < bw; y++)
                for (int x = 0; x < bw; x++)
                {
                    int src = ((cy - rad + y) * W + (cx - rad + x)) * Bpp;
                    int dst = (y * bw + x) * Bpp;
                    Array.Copy(_buf, src, box, dst, Bpp);
                }
            double f = OrbDetector.Fraction(box, bw, bw, cfg);
            bool ok = Math.Abs(f - want) < 0.04;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  fill {want:P0} -> read {f:P1}");
            if (!ok) fails++;
        }

        // A box drawn by hand catches frame above the globe. Uncalibrated that
        // reads low; calibration must pull a full globe back to a true 100%.
        {
            const int pad = 24, bw2 = 220, bh2 = 270;
            var box = new byte[bw2 * bh2 * Bpp];
            for (int i = 0; i < box.Length; i += Bpp)
            { box[i] = 40; box[i + 1] = 38; box[i + 2] = 36; box[i + 3] = 255; }

            int rad = 105, cx = bw2 / 2, cy = pad + rad;
            for (int yy = 0; yy < bh2; yy++)
                for (int xx = 0; xx < bw2; xx++)
                    if ((xx - cx) * (xx - cx) + (yy - cy) * (yy - cy) <= rad * rad)
                    {
                        int i = (yy * bw2 + xx) * Bpp;
                        box[i] = 190; box[i + 1] = 90; box[i + 2] = 40;
                    }

            var c2 = new WatcherConfig { Hue = "blue", ColourMargin = 30, MinValue = 50 };
            double before = OrbDetector.Fraction(box, bw2, bh2, c2);
            bool okCal = OrbDetector.CalibrateFull(box, bw2, bh2, c2, out int fr, out int er);
            c2.FullRow = fr;
            c2.EmptyRow = er;
            double after = OrbDetector.Fraction(box, bw2, bh2, c2);

            bool low = before < 0.95;
            bool fixedUp = okCal && after > 0.99;
            Console.WriteLine((low ? "PASS" : "FAIL") + $"  padded box reads low uncalibrated -> {before:P1}");
            Console.WriteLine((fixedUp ? "PASS" : "FAIL") + $"  calibration restores full -> {after:P1} (rows {fr}..{er})");
            if (!low) fails++;
            if (!fixedUp) fails++;
        }

        // A globe is not one flat colour: it is saturated in the middle and
        // falls off towards the rim. With a single strict threshold only the
        // core passes, and that core is itself a disc that passes every shape
        // check - so auto-find returned a box a fraction of the real size.
        Reset();
        {
            int cx = 520, cy = 300, rad = 120;
            for (int y = cy - rad; y <= cy + rad; y++)
                for (int x = cx - rad; x <= cx + rad; x++)
                {
                    double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d > rad) continue;
                    // Full blue in the middle, fading towards the rim.
                    double k = 1.0 - 0.65 * (d / rad);
                    Px(x, y, (int)(200 * k), (int)(95 * k), (int)(45 * k));
                }

            var strict = OrbDetector.Locate(_buf, W, H, blue: true, margin: 48);
            var swept = OrbDetector.LocateBest(_buf, W, H, blue: true);

            int strictW = strict?.Width ?? 0;
            int sweptW = swept?.Width ?? 0;
            bool strictTooSmall = strictW < sweptW;
            bool sweptRight = swept is not null && Math.Abs(sweptW - rad * 2) <= 12;

            Console.WriteLine((strictTooSmall ? "PASS" : "FAIL")
                + $"  one strict threshold under-reads the globe -> {strictW}px "
                + $"vs {sweptW}px swept, of {rad * 2}px actual");
            Console.WriteLine((sweptRight ? "PASS" : "FAIL")
                + $"  sweeping thresholds finds the whole globe -> "
                + $"{swept?.Width ?? 0}px of {rad * 2}px");
            if (!strictTooSmall) fails++;
            if (!sweptRight) fails++;
        }

        // Refusing to act on a globe we cannot see must not also refuse to act
        // on one that is nearly empty. Both look like "almost zero" in a single
        // frame; only the history tells them apart.
        {
            var wc = new WatcherConfig { IgnoreBelow = 0.02, BlindGraceMs = 1200 };
            int bad = 0;

            void Case(string what, double frac, long now, long lastGood, bool wantRefuse)
            {
                bool refused = MonitorEngine.Unreadable(frac, now, lastGood, wc);
                bool ok = refused == wantRefuse;
                Console.WriteLine((ok ? "PASS" : "FAIL") + $"  {what} -> "
                    + (refused ? "refuse" : "fire") + $" (wanted {(wantRefuse ? "refuse" : "fire")})");
                if (!ok) bad++;
            }

            // Dying: read 60% a moment ago, 1% now. Must still fire.
            Case("1% life, healthy 200ms ago", 0.01, 10_000, 9_800, false);
            Case("0% life, healthy 900ms ago", 0.00, 10_000, 9_100, false);

            // Loading or death screen: nothing readable for a while.
            Case("0% for 1.3s", 0.00, 10_000, 8_700, true);
            Case("0% for 30s", 0.00, 40_000, 10_000, true);
            Case("0% since launch", 0.00, 5_000, long.MinValue / 2, true);

            // Ordinary low life is never refused.
            Case("15% life", 0.15, 10_000, 10_000, false);
            Case("3% life, just crossed", 0.03, 10_000, 10_000, false);

            fails += bad;
        }

        // What the OCR box actually returns, including the exact misread that
        // reported a maximum of 14,652,005 from a life of 1,465 and a shield of
        // 2,005 - and read as 0% life while the character was at full.
        {
            int bad = 0;
            void Parse(string what, string text, int wantCur, int wantMax)
            {
                bool ok = TextOcr.TryParse(text, out int cur, out int max);
                bool right = wantMax == 0 ? !ok : ok && cur == wantCur && max == wantMax;
                Console.WriteLine((right ? "PASS" : "FAIL") + $"  {what} -> "
                    + (ok ? $"{cur}/{max}" : "rejected")
                    + (wantMax == 0 ? "  (wanted rejected)" : $"  (wanted {wantCur}/{wantMax})"));
                if (!right) bad++;
            }

            // Current above maximum is legitimate here - skills push life past
            // the pool - and above full never fires either way.
            Parse("overhealed above maximum", "2,029/1,465\n2,005/2,005", 2029, 1465);
            Parse("good first line, shield below", "1,465/1,465\n2,005/2,005", 1465, 1465);
            Parse("life over shield, one line", "1,465/1,465 2,005/2,005", 1465, 1465);
            Parse("plain", "1,465/1,465", 1465, 1465);
            // Was written down as "465/1,465 is fine". It is not fine - it is a
            // 1,465 that came apart, and reading its second half as your
            // current life turns a full pool into 31% and fires. That is the
            // misread that emptied a flask belt, so the expectation is the
            // thing that changed.
            Parse("half a number is not a reading", "1 465/1,465", 0, 0);
            Parse("dot for comma", "412/1.465", 412, 1465);
            Parse("mana", "747/747", 747, 747);

            // Rejections that matter.
            // A leading digit that was not there. Overhealing is real, but not
            // to 234% - and the damage is that it clamps to a comfortable 100%
            // while the pool it claims to describe may be nearly empty.
            Parse("phantom leading digit, clamps to full", "1747/747", 0, 0);
            Parse("absurd maximum", "2,029/14,652,005", 0, 0);
            Parse("no pair at all", "Life Shield Ward", 0, 0);

            fails += bad;
        }

        // The real HUD block, all three lines in one box. Ward is a pair too,
        // and reading 90/90 as life is a misfire waiting to happen.
        {
            string hud = "Life 1,465/1,465\nShield 2,005/2,005\nWard 90/90";
            string jumbled = "Ward 90/90\nLife 1,465/1,465\nShield 2,005/2,005";
            int bad = 0;

            void Pick(string what, string text, string label, int expected,
                      int wantCur, int wantMax)
            {
                bool ok = TextOcr.TryParse(text, out int cur, out int max, label, expected);
                bool right = ok && cur == wantCur && max == wantMax;
                Console.WriteLine((right ? "PASS" : "FAIL") + $"  {what} -> "
                    + (ok ? $"{cur}/{max}" : "rejected") + $"  (wanted {wantCur}/{wantMax})");
                if (!right) bad++;
            }

            Pick("label picks life out of three lines", hud, "Life", 0, 1465, 1465);
            Pick("label works whatever the order", jumbled, "Life", 0, 1465, 1465);
            Pick("label picks shield when asked", hud, "Shield", 0, 2005, 2005);
            Pick("known maximum picks life with no label", jumbled, "", 1465, 1465, 1465);

            // Without either anchor it takes the first line, which is exactly
            // the ward misread that prompted all this.
            bool anyOk = TextOcr.TryParse(jumbled, out int c0, out int m0);
            Console.WriteLine((anyOk && c0 == 90 && m0 == 90 ? "PASS" : "FAIL")
                + $"  no anchor takes the first line -> {c0}/{m0} (this is why anchors exist)");
            if (!(anyOk && c0 == 90 && m0 == 90)) bad++;

            fails += bad;
        }

        // With a label configured but absent from what was read, nothing is
        // reported. Falling back to position is what put ward on screen as
        // life, and a refused reading is better than the wrong one.
        {
            int bad = 0;
            string noLabel = "1,465/1,465\n2,005/2,005\n90/90";

            bool gotNoLabel = TextOcr.TryParse(noLabel, out int c, out int m, "Life", 0);
            Console.WriteLine((!gotNoLabel ? "PASS" : "FAIL")
                + "  label configured but missing -> "
                + (gotNoLabel ? $"{c}/{m}" : "refused") + "  (wanted refused)");
            if (gotNoLabel) bad++;

            // The maximum still rescues it when the label is not readable.
            bool gotByMax = TextOcr.TryParse(noLabel, out int c2, out int m2, "Life", 1465);
            Console.WriteLine((gotByMax && c2 == 1465 && m2 == 1465 ? "PASS" : "FAIL")
                + "  missing label, known maximum -> "
                + (gotByMax ? $"{c2}/{m2}" : "refused") + "  (wanted 1465/1465)");
            if (!(gotByMax && c2 == 1465 && m2 == 1465)) bad++;

            fails += bad;
        }

        // A stray leading digit turns 1,465 into 11,465. Current 1,465 against
        // that reads as 13%, which is under any trigger - so it fires at full
        // health. The label finds the right line; only the maximum catches it.
        {
            int bad = 0;
            string strayDigit = "Life 1,465/11,465\nShield 2,005/2,005";

            bool taken = TextOcr.TryParse(strayDigit, out int c, out int m, "Life", 1465);
            Console.WriteLine((!taken ? "PASS" : "FAIL")
                + "  stray digit in the maximum -> "
                + (taken ? $"{c}/{m} = {100.0 * c / m:0}%" : "refused") + "  (wanted refused)");
            if (taken) bad++;

            // The same line is fine once the maximum agrees.
            bool ok = TextOcr.TryParse("Life 1,465/1,465", out int c2, out int m2, "Life", 1465);
            Console.WriteLine((ok && c2 == 1465 && m2 == 1465 ? "PASS" : "FAIL")
                + "  correct maximum still accepted -> "
                + (ok ? $"{c2}/{m2}" : "refused"));
            if (!(ok && c2 == 1465 && m2 == 1465)) bad++;

            // With no maximum stated there is nothing to check it against, so
            // it parses - the repeat rule in the reader is what guards that.
            bool loose = TextOcr.TryParse(strayDigit, out _, out int m3, "Life", 0);
            Console.WriteLine((loose && m3 == 11465 ? "PASS" : "FAIL")
                + $"  no stated maximum, nothing to compare -> {m3}");
            if (!(loose && m3 == 11465)) bad++;

            fails += bad;
        }

        // The globe pixels must never decide once memory or the numbers are
        // asked for. This has come back three times in different disguises -
        // most recently as a ding on every loading screen, where the numbers
        // vanish and the pixels read the loading screen.
        {
            int bad = 0;
            const int grace = 400;

            void Case(string what, bool better, bool exactNow, bool hadExact,
                      long since, long now, double wantFrac, bool wantHold)
            {
                var c = MonitorEngine.ChooseSource(better, exactNow,
                                                   exactFrac: 0.20, pixelFrac: 0.00,
                                                   hadExact: hadExact, lastExactFrac: 0.95,
                                                   sinceMs: since, nowMs: now, graceMs: grace);
                bool ok = Math.Abs(c.Frac - wantFrac) < 0.001 && c.Hold == wantHold;
                Console.WriteLine((ok ? "PASS" : "FAIL") + $"  {what} -> "
                    + $"{c.Frac:P0}{(c.Hold ? ", held" : "")}"
                    + $"  (wanted {wantFrac:P0}{(wantHold ? ", held" : "")})");
                if (!ok) bad++;
            }

            // Nothing better asked for: the pixels are all there is.
            Case("pixels only", false, false, false, 0, 10_000, 0.00, false);

            // An exact reading is available and wins.
            Case("numbers reading", true, true, true, 0, 10_000, 0.20, false);

            // Numbers gone for a moment: carry the last exact value, do NOT
            // fall to the pixels, which read 0% on a loading screen.
            Case("gone 200ms, carries last", true, false, true, 9_800, 10_000, 0.95, false);

            // A loading screen is seconds, not one missed frame. Acting on a
            // value frozen from before the load is what fired through them.
            Case("gone 500ms, holds", true, false, true, 9_500, 10_000, 0.95, true);
            Case("gone 3s, holds", true, false, true, 7_000, 10_000, 0.95, true);

            // Never worked: holds immediately, no grace at all.
            Case("never read, holds now", true, false, false, 10_000, 10_000, 0.00, true);

            fails += bad;
        }

        // Every key the bind box can capture must be sendable. Numpad keys
        // share scancodes with the navigation cluster and differ only by the
        // extended flag, so this guards a genuinely easy mistake.
        {
            int bad = 0, mapped = 0;
            foreach (Keys k in Enum.GetValues<Keys>())
            {
                string? name = KeySender.FromKeys(k);
                if (name is null) continue;
                mapped++;
                if (!KeySender.IsKnown(name))
                {
                    Console.WriteLine($"FAIL  {k} -> '{name}' has no scancode");
                    bad++;
                }
            }
            foreach (string want in new[] { "1", "2", "3", "4", "5", "0", "-", "=" })
                if (!KeySender.IsKnown(want))
                {
                    Console.WriteLine($"FAIL  '{want}' missing from scancode map");
                    bad++;
                }

            // Numpad is deliberately unsupported: it must be capturable by
            // nothing and sendable as nothing, or it can creep back in.
            foreach (Keys k in new[] { Keys.NumPad0, Keys.NumPad1, Keys.Add, Keys.Divide })
                if (KeySender.FromKeys(k) is not null)
                {
                    Console.WriteLine($"FAIL  {k} is still capturable");
                    bad++;
                }
            if (KeySender.IsKnown("numpad0"))
            {
                Console.WriteLine("FAIL  numpad still in the scancode map");
                bad++;
            }
            Console.WriteLine((bad == 0 ? "PASS" : "FAIL")
                              + $"  keybind map: {mapped} keys capturable, all sendable");
            fails += bad;
        }

        // A phantom digit in the CURRENT, where the maximum is still right, so
        // nothing else catches it - and it clamps to a comfortable 100% however
        // little life is really left. Seen mid-fight as "14,610/1,490".
        {
            bool bad = TextOcr.TryParse("Life 14,610/1,490", out _, out _, "Life", 1490);
            bool overheal = TextOcr.TryParse("Life 1,947/1,490", out int oc, out int om, "Life", 1490);
            Console.WriteLine((!bad ? "PASS" : "FAIL")
                              + "  current with a phantom digit is refused");
            Console.WriteLine((overheal && oc == 1947 && om == 1490 ? "PASS" : "FAIL")
                              + $"  overheal is still accepted -> {oc}/{om}");
        }

        // Levelling. The stored maximum is always a moment behind the screen,
        // and refusing every reading until somebody notices is how it came to
        // "lose it" once per level.
        {
            bool levelled = TextOcr.TryParse("Life 288/288", out int lc, out int lm,
                                             "Life", 278);
            bool phantom = TextOcr.TryParse("Life 1,465/11,465", out _, out _,
                                            "Life", 1465);
            Console.WriteLine((levelled && lc == 288 && lm == 288 ? "PASS" : "FAIL")
                              + $"  a levelled maximum is accepted -> {lc}/{lm}");
            Console.WriteLine((!phantom ? "PASS" : "FAIL")
                              + "  a maximum with a phantom digit is still refused");
        }

        // Mana emptying its flask three times a second at a box reading
        // "19/100" for a pool holding 191. The box was on some other line
        // entirely, and the reading it produced was about something else.
        {
            var onWrongLine = MonitorEngine.Judge(structured: true, memoryMax: 191, boxMax: 100);
            var noLock = MonitorEngine.Judge(structured: false, memoryMax: 191, boxMax: 100);
            var fine = MonitorEngine.Judge(structured: true, memoryMax: 191, boxMax: 191);
            var nothingToSay = MonitorEngine.Judge(structured: true, memoryMax: 0, boxMax: 100);

            Console.WriteLine((onWrongLine == MonitorEngine.Verdict.TrustMemory ? "PASS" : "FAIL")
                              + $"  a box reading 100 for a pool of 191 loses -> {onWrongLine}");
            Console.WriteLine((noLock == MonitorEngine.Verdict.HoldBoth ? "PASS" : "FAIL")
                              + $"  with no structural lock, neither fires -> {noLock}");
            Console.WriteLine((fine == MonitorEngine.Verdict.Agree ? "PASS" : "FAIL")
                              + $"  agreement is left alone -> {fine}");
            Console.WriteLine((nothingToSay == MonitorEngine.Verdict.Agree ? "PASS" : "FAIL")
                              + $"  nothing to compare is not a disagreement -> {nothingToSay}");

            // The other direction, an hour later: the box read "Life 362/362"
            // with its own label matched and memory was the stale one at 346.
            // Calling that box wrong left the numbers dark and the overlay
            // hidden behind them.
            var levelled = MonitorEngine.Judge(structured: true, memoryMax: 346, boxMax: 362);
            Console.WriteLine((levelled == MonitorEngine.Verdict.Agree ? "PASS" : "FAIL")
                              + $"  346 against 362 is one pool, four percent apart -> {levelled}");
        }

        // OCR puts specks and spaces inside words. "Life" came back as
        // "I i-fe", which failed every label test and so threw away the
        // numbers beside it - which is how a stuck maximum could never correct
        // itself.
        {
            bool speckled = TextOcr.HasLabel("I i-fe 362/362", "Life");
            bool truncated = TextOcr.HasLabel("Li 362/362", "Life");
            bool notMana = TextOcr.HasLabel("Mana 207/207", "Life");
            Console.WriteLine((speckled ? "PASS" : "FAIL")
                              + "  \"I i-fe\" is still the word Life");
            Console.WriteLine((truncated ? "PASS" : "FAIL")
                              + "  a truncated \"Li\" is still Life");
            Console.WriteLine((!notMana ? "PASS" : "FAIL")
                              + "  the mana line is not mistaken for Life");
        }

        // Short enough to paste into a chat message, and carrying nobody's keys.
        {
            var mine = new AppConfig();
            mine.Life.Key = "5";
            mine.Life.Threshold = 0.42;
            mine.ArmHotkey = "F9";
            mine.OverlayShowMana = false;

            string block = SettingsShare.Export(mine, "9.9.9");

            var theirs = new AppConfig();
            theirs.Life.Key = "0";
            theirs.ArmHotkey = "F8";
            string? why = SettingsShare.Import(block, theirs, "9.9.9");

            Console.WriteLine((block.Length <= 300 ? "PASS" : "FAIL")
                              + $"  shared settings are {block.Length} chars (want <= 300)");
            Console.WriteLine((why is null ? "PASS" : "FAIL")
                              + $"  they load back ({why ?? "ok"})");
            Console.WriteLine((theirs.Life.Key == "0" && theirs.ArmHotkey == "F8"
                               ? "PASS" : "FAIL")
                              + $"  their own keys survive -> flask {theirs.Life.Key}, arm {theirs.ArmHotkey}");
            Console.WriteLine((Math.Abs(theirs.Life.Threshold - 0.42) < 1e-9
                               && !theirs.OverlayShowMana ? "PASS" : "FAIL")
                              + $"  the behaviour travels -> fire below {theirs.Life.Threshold:P0}, mana row {theirs.OverlayShowMana}");
        }

        // The misread that emptied a flask belt at full life. OCR turned
        // "Life 1,496/1,496" into "1,49 6/1149 6s", and a perfectly plausible
        // "6/1149" was sitting in the middle of it - six out of fourteen
        // hundred, which is a last-ditch emergency.
        {
            bool wreck = TextOcr.TryParse("1,49 6/1149 6s", out _, out _, "Life", 0);
            bool wreck2 = TextOcr.TryParse("Life 1,49 6/1149 6s", out _, out _, "Life", 0);
            bool clean = TextOcr.TryParse("Life 1,496/1,496", out int cc, out int cm,
                                          "Life", 1496);
            Console.WriteLine((!wreck && !wreck2 ? "PASS" : "FAIL")
                              + "  a pair cut out of a mangled line is refused");
            Console.WriteLine((clean && cc == 1496 && cm == 1496 ? "PASS" : "FAIL")
                              + $"  the same line read properly is accepted -> {cc}/{cm}");
        }

        // A box cropped to the numbers alone, which is a sensible thing to have
        // done and which the label check used to refuse - leaving the maximum
        // stale and memory hunting a number that no longer existed.
        {
            bool tight = TextOcr.TryParse("3,096/2,078", out int tc, out int tm, "Life", 0);
            Console.WriteLine((tight && tc == 3096 && tm == 2078 ? "PASS" : "FAIL")
                              + "  numbers alone in a box assigned to Life -> "
                              + (tight ? $"{tc}/{tm}" : "refused"));

            bool wrong = TextOcr.TryParse("Shield 512/3,357", out _, out _, "Life", 0);
            Console.WriteLine((!wrong ? "PASS" : "FAIL")
                              + "  another stat's line is still refused for Life");
        }

        // OCR reads small pale text over a moving background; the label came
        // back a letter short or a letter wrong often enough that demanding it
        // exactly threw away good numbers several times a minute.
        {
            (string Text, bool Want)[] cases =
            [
                ("tife 1,947/1,490", true),
                ("Li 1,947/1,490", true),
                ("Li fiv 1,947/1,490", true),
                ("Life 1,947/1,490", true),
                ("Shield 2,011/2,011", false),
                ("Ward 90/90", false),
                ("Mana 759/759", false),
                ("Spirit 42/132", false),
            ];

            bool all = true;
            foreach (var (text, want) in cases)
            {
                bool saw = TextOcr.HasLabel(text, "Life");
                if (saw != want)
                {
                    all = false;
                    Console.WriteLine($"FAIL  \"{text}\" as Life -> {saw}, wanted {want}");
                }
            }

            if (all) Console.WriteLine("PASS  label survives a mis-read letter, "
                                       + "without matching another stat");
        }

        Console.WriteLine(fails == 0 ? "\nALL PASS" : $"\n{fails} FAILED");
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    static int Check(string name, Rectangle? got, int cx, int cy, int rad)
    {
        if (got is null) { Console.WriteLine($"FAIL  {name} -> not found"); return 1; }
        var r = got.Value;
        int gx = r.X + r.Width / 2, gy = r.Y + r.Height / 2;
        bool ok = Math.Abs(gx - cx) <= 6 && Math.Abs(gy - cy) <= 6
                  && Math.Abs(r.Width - rad * 2) <= 8 && Math.Abs(r.Height - rad * 2) <= 8;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name} -> {r}");
        return ok ? 0 : 1;


    }
}
