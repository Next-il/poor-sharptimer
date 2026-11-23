using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using FixVectorLeak;
using PanoramaManager;

namespace SharpTimer.Hud;

internal enum ResultKind { None, Done, Pb, Sr, Cancel }

internal enum SplitMark { Done, Ahead, Behind }

/// <summary>One toast: a split (stage or checkpoint time against the PB and SR) or a reply to a command.</summary>
internal sealed record HudToast(
    bool Split,
    string Key,
    string Time = "",
    string PbDiff = "", int PbSign = 0,
    string SrDiff = "", int SrSign = 0,
    string Speed = "", string SpeedDiff = "", int SpeedSign = 0,
    string Bold = "", string Message = "", bool Error = false);

internal enum FeedKind { Sr, Pb, Finish, Stage }

/// <summary>One line of the records feed: somebody else's finish, PB or record.</summary>
internal sealed record FeedLine(FeedKind Kind, string Name, string Tag, string Time, string Diff);

/// <summary>
/// The SharpTimer HUD on Panorama (hud/panorama/layout/custom_game/sharptimer_hud.xml): the timer card, the speed,
/// the keys, one toast slot for splits and replies, a feed of other players' records and the map's top times
/// (map records). It replaces the center
/// HTML that was rebuilt and sent to every player 64 times a second. Each part goes where the player put it in
/// !hud, in the player's accent, background and text size.
///
/// <para><b>Who draws what.</b> A runner's state (the result of their last finish, their splits, their toast,
/// the start-speed chip) lives in a <see cref="Runner"/> per runner slot. Each viewer's panel shows one runner:
/// themselves while alive, the player they watch while spectating. What a viewer's panel holds is cached in its
/// <see cref="PanelWriter"/>, so only a change is ever written.</para>
///
/// <para><b>Rates.</b> The clock is written 10 times a second, speed 16 (and only when the number changed),
/// sync and pre 4, the live place 2, keys whenever one changes. Everything else is recomputed 8 times a second
/// and written only when it differs. Nothing writes the whole HUD per tick.</para>
///
/// <para><b>Fallback.</b> <see cref="Drive"/> returns false for a player the panel cannot reach, and the caller
/// prints the old center HTML for them.</para>
/// </summary>
internal sealed class TimerHud : IDisposable
{
    private const float FeedEntrySeconds = 0.06f;
    private const float FeedExitSeconds = 0.34f;
    private const int StaticRate = 8;
    private const int JumpHoldTicks = 3;

    private sealed class Runner
    {
        public ResultKind Result;
        public float ResultUntil;
        public int FinalTicks;
        public string DiffText = "";
        public bool DiffAhead;
        public string ResText = "";
        public string PtsText = "";

        public readonly Dictionary<int, SplitMark> Splits = new();

        public HudToast? Toast;
        public float ToastUntil;
        public int ToastSeq;

        public string ChipText = "";
        public bool ChipCapped;
        public float ChipUntil;

        public int PopSeq;

        public string SrByAtStart = "";
    }

    private sealed class FeedEntry
    {
        public long Id;
        public required FeedLine Line;
        public float ShowAt;
        public float LeaveAt;
        public float CollapseAt = -1;
        public bool Shown;
        public bool Leaving;
        public bool Sample;
    }

    private sealed class Viewer
    {
        public readonly PanelWriter W = new();
        public HudPrefs Prefs = new();
        public bool PrefsLoaded;
        public int Source = -1;
        public long TimerSlot = -1, SpeedSlot = -1, SyncSlot = -1, PlaceSlot = -1, ProgSlot = -1, StaticSlot = -1;
        public int JumpHold;
        public int PopSeq;
        public bool PopA;
        public int ToastSeq = -1;
        public bool ReA;
        public string? ToastPanel;
        public bool ToastShown;
        public readonly FeedEntry?[] Feed = new FeedEntry?[HudLayout.FeedSlots];
        public int FeedNext;
        public long FeedSeq;
        public bool Preview;
        public bool Blank;
        /// <summary>Map records: drawn at the static rate on their own slot, since they also show while blank.</summary>
        public long RecordsSlot = -1;
        /// <summary>"On map join": the window starts at the first draw after joining or a map start.</summary>
        public bool RecordsJoin = true;
        public float RecordsUntil;
        public float RetryAt;
        /// <summary>The panel was reset: every feed slot is written empty on the next draw.</summary>
        public bool FeedDirty = true;
    }

    private readonly SharpTimer _st;
    private readonly Dictionary<int, Viewer> _viewers = new();
    private readonly Dictionary<int, Runner> _runners = new();
    private PanelHandle? _panel;

    public TimerHud(SharpTimer st) => _st = st;

    public bool Enabled => _panel != null;
    private HudConfig Cfg => _st.HudSettings;

    public void Init()
    {
        if (_panel != null) return;
        try
        {
            _panel = Panorama.Spawn(HudLayout.HudPath, new LayoutContract
            {
                // Unique server-wide: dialog variables are keyed by the root panel's id.
                RootPanelId = HudLayout.HudRoot,
                RevealClass = "show",
                RowCount = 0,
                CaptureInput = false,
                HideHud = HideHudFlags.None,
                SharedText = false,
            });
            _panel.OnEvent += OnPanelEvent;
        }
        catch (Exception e)
        {
            _st.Utils.LogError($"[HUD] the Panorama HUD failed to start, every player gets the center HTML: {e}");
            _panel = null;
        }
    }

    public void Dispose()
    {
        _panel?.Dispose();
        _panel = null;
        _viewers.Clear();
        _runners.Clear();
    }

    private void OnPanelEvent(PanelEvent e)
    {
        if (e.Action != PanelAction.Restored || e.Player is not { IsValid: true } player) return;
        if (!_viewers.TryGetValue(player.Slot, out var v)) return;
        Invalidate(v);
    }

    /// <summary>Everything this viewer's panel holds is unknown now: the next draw writes it all.</summary>
    private static void Invalidate(Viewer v)
    {
        v.W.Reset();
        v.Source = -1;
        v.TimerSlot = v.SpeedSlot = v.SyncSlot = v.PlaceSlot = v.ProgSlot = v.StaticSlot = v.RecordsSlot = -1;
        v.ToastPanel = null;
        v.ToastShown = false;
        v.ToastSeq = -1;
        Array.Clear(v.Feed);
        v.FeedNext = 0;
        v.FeedDirty = true;
    }

    // ================================================================== viewers

    private static bool IsHuman(CCSPlayerController? p) =>
        p is { IsValid: true, IsBot: false, IsHLTV: false } && p.Connected == PlayerConnectedState.Connected;

    /// <summary>Opens the player's HUD session and loads their placement. Safe to repeat.</summary>
    public void Attach(CCSPlayerController player)
    {
        if (_panel == null || !IsHuman(player)) return;

        if (!_viewers.TryGetValue(player.Slot, out var v))
        {
            v = new Viewer();
            _viewers[player.Slot] = v;
        }

        if (!_panel.IsOpenFor(player))
        {
            // A player the panel cannot reach is tried again every few seconds, not every tick.
            if (Server.CurrentTime < v.RetryAt) return;
            v.RetryAt = Server.CurrentTime + 3f;
            _panel.Open(player);
            Invalidate(v);
        }

        if (!v.PrefsLoaded)
        {
            v.PrefsLoaded = true;
            v.Prefs = _st.HudPrefStore.Get(player.SteamID);
            var slot = player.Slot;
            var steamId = player.SteamID;
            _st.HudPrefStore.Load(steamId, prefs =>
            {
                var p = Utilities.GetPlayerFromSlot(slot);
                if (p is not { IsValid: true } || p.SteamID != steamId || !_viewers.TryGetValue(slot, out var viewer)) return;
                viewer.Prefs = prefs;
                viewer.StaticSlot = viewer.RecordsSlot = -1;
                if (!prefs.Seen && Cfg.SettingsHint) Hint(slot, steamId);
            });
        }
    }

    /// <summary>One chat line, a little after joining, for a player who has never opened the settings.</summary>
    private void Hint(int slot, ulong steamId)
    {
        _st.AddTimer(20f, () =>
        {
            var p = Utilities.GetPlayerFromSlot(slot);
            if (p is not { IsValid: true } || p.SteamID != steamId || !Reaches(p) || PrefsOf(p).Seen) return;
            _st.Utils.PrintToChat(p, $"Type {_st.primaryChatColor}!hud{CounterStrikeSharp.API.Modules.Utils.ChatColors.Default} " +
                                     "to move, hide or restyle any part of the timer HUD.");
        });
    }

    public void Forget(int slot)
    {
        _viewers.Remove(slot);
        _runners.Remove(slot);
        _panel?.Close(slot);
    }

    /// <summary>New map: server time starts over, so every stored time is meaningless.</summary>
    public void MapStart()
    {
        _runners.Clear();
        foreach (var v in _viewers.Values)
        {
            Invalidate(v);
            v.RetryAt = 0;
            v.RecordsJoin = true;
        }
    }

    public bool Reaches(CCSPlayerController? player) =>
        _panel != null && player is { IsValid: true } && _panel.IsOpenFor(player);

    public HudPrefs PrefsOf(CCSPlayerController player) =>
        _viewers.TryGetValue(player.Slot, out var v) ? v.Prefs : _st.HudPrefStore.Get(player.SteamID);

    /// <summary>The player changed something in !hud (or with !keys / !splits): redraw now.</summary>
    public void PrefsChanged(CCSPlayerController player, HudPrefs? prefs = null)
    {
        if (!_viewers.TryGetValue(player.Slot, out var v)) return;
        if (prefs != null) v.Prefs = prefs;
        v.StaticSlot = v.RecordsSlot = -1;
        if (v.Preview) SyncSample(player, v);
    }

    /// <summary>The sample feed line follows the records feed switch while the settings are open.</summary>
    private void SyncSample(CCSPlayerController player, Viewer v)
    {
        var shown = v.Feed.Any(e => e is { Sample: true, Leaving: false });
        var want = v.Preview && v.Prefs.FeedOn;
        if (want && !shown)
        {
            Push(player, v, new FeedLine(FeedKind.Pb, player.PlayerName, "PB", "1:24.218", "-0.877"), sample: true);
        }
        else if (!want && shown)
        {
            for (var i = 0; i < v.Feed.Length; i++)
                if (v.Feed[i] is { Sample: true }) Leave(player, v, i);
        }
    }

    /// <summary>While !hud is open: a sample toast and feed line, so every part shows where it goes.</summary>
    public void SetPreview(CCSPlayerController player, bool on)
    {
        if (!_viewers.TryGetValue(player.Slot, out var v) || v.Preview == on) return;
        v.Preview = on;
        v.StaticSlot = v.RecordsSlot = -1;
        SyncSample(player, v);
    }

    private Runner RunnerOf(int slot)
    {
        if (!_runners.TryGetValue(slot, out var r))
        {
            r = new Runner();
            _runners[slot] = r;
        }

        return r;
    }

    // ================================================================== events from the timer

    /// <summary>The timer started: a fresh card, the pop, and the start-speed chip.</summary>
    public void TimerStarted(CCSPlayerController player, int startSpeed, bool capped)
    {
        var r = RunnerOf(player.Slot);
        r.Result = ResultKind.None;
        r.Splits.Clear();
        r.PopSeq++;
        r.ChipText = capped ? $"START {startSpeed} · CAPPED" : $"START {startSpeed}";
        r.ChipCapped = capped;
        r.ChipUntil = Server.CurrentTime + Cfg.StartChipSeconds;
        r.SrByAtStart = _st.HudServerRecord(player)?.PlayerName ?? "";
        if (r.Toast is { Split: true }) r.ToastUntil = 0;
    }

    public void ClearChip(CCSPlayerController player) => RunnerOf(player.Slot).ChipUntil = 0;

    /// <summary>The last stage has no trigger of its own: the finish marks it.</summary>
    public void MarkSegment(CCSPlayerController player, int segment)
    {
        var r = RunnerOf(player.Slot);
        if (segment >= 0) r.Splits.TryAdd(segment, SplitMark.Done);
    }

    /// <summary>A stage or checkpoint: the toast above the card, and the segment's colour on the bar.</summary>
    public void Split(CCSPlayerController player, int segment, SplitMark mark, HudToast toast)
    {
        var r = RunnerOf(player.Slot);
        if (segment >= 0) r.Splits[segment] = mark;
        ShowToast(r, toast, Cfg.ToastSeconds);
    }

    /// <summary>A command reply or a note: the toast, for the player and whoever watches them.</summary>
    public void Reply(CCSPlayerController player, string key, string bold, string message, bool error = false, float seconds = 0)
    {
        ShowToast(RunnerOf(player.Slot), new HudToast(false, key, Bold: bold, Message: message, Error: error),
            seconds > 0 ? seconds : Cfg.ToastSeconds);
    }

    private static void ShowToast(Runner r, HudToast toast, float seconds)
    {
        r.Toast = toast;
        r.ToastUntil = Server.CurrentTime + seconds;
        r.ToastSeq++;
    }

    /// <summary>The end zone: the final time at once. <see cref="FinishResult"/> fills in PB or SR when the database answers.</summary>
    public void Finished(CCSPlayerController player, int ticks, int pbTicks)
    {
        var r = RunnerOf(player.Slot);
        r.Result = ResultKind.Done;
        r.FinalTicks = ticks;
        r.ResultUntil = Server.CurrentTime + Cfg.ResultSeconds;
        r.DiffText = pbTicks > 0 ? HudLayout.Diff(ticks, pbTicks) : "";
        r.DiffAhead = pbTicks > 0 && ticks < pbTicks;
        r.ResText = "";
        r.PtsText = "";
        r.PopSeq++;
    }

    public void FinishResult(CCSPlayerController player, ResultKind kind, int newTicks, int oldTicks, int prevSr,
        string ranking, int timesFinished, int bonus)
    {
        var r = RunnerOf(player.Slot);
        var now = Server.CurrentTime;
        var firstWrite = r.Result == ResultKind.None || now >= r.ResultUntil;
        r.Result = kind;
        r.FinalTicks = newTicks;
        r.ResultUntil = Math.Max(r.ResultUntil, now + Cfg.ResultSeconds * 0.6f);
        if (firstWrite) r.ResultUntil = now + Cfg.ResultSeconds;

        var reference = kind == ResultKind.Sr && prevSr > 0 ? prevSr : oldTicks;
        r.DiffText = reference > 0 ? HudLayout.Diff(newTicks, reference) : "";
        r.DiffAhead = reference > 0 && newTicks < reference;

        var (place, total) = ParseRanking(ranking);
        var parts = new List<string>();
        if (bonus != 0) parts.Add($"Bonus {bonus}");
        if (place > 0) parts.Add(total > 0 ? $"#{place} of {HudLayout.Number(total)}" : $"#{place}");
        if (kind == ResultKind.Sr && r.SrByAtStart.Length > 0 && r.SrByAtStart != player.PlayerName)
            parts.Add($"beat {r.SrByAtStart}");
        else if (timesFinished > 0)
            parts.Add($"finish {HudLayout.Number(timesFinished)}");
        r.ResText = string.Join(" · ", parts);
    }

    public void Points(CCSPlayerController player, int gained)
    {
        if (gained <= 0) return;
        RunnerOf(player.Slot).PtsText = $"· +{HudLayout.Number(gained)} points";
    }

    public void Cancelled(CCSPlayerController player, int ticks, string text)
    {
        var r = RunnerOf(player.Slot);
        r.Result = ResultKind.Cancel;
        r.FinalTicks = ticks;
        r.ResText = text;
        r.PtsText = "";
        r.DiffText = "";
        r.ResultUntil = Server.CurrentTime + 2.6f;
    }

    /// <summary>A record somebody else set: one line in the feed of every player who has it on.</summary>
    public void Feed(FeedLine line, int exceptSlot)
    {
        foreach (var (slot, v) in _viewers)
        {
            if (slot == exceptSlot || !v.Prefs.FeedOn) continue;
            var p = Utilities.GetPlayerFromSlot(slot);
            if (p is not { IsValid: true } || !Reaches(p)) continue;
            Push(p, v, line);
        }
    }

    private static (int Place, int Total) ParseRanking(string ranking)
    {
        var parts = (ranking ?? "").Split('/');
        int.TryParse(parts.ElementAtOrDefault(0)?.Trim(), out var place);
        int.TryParse(parts.ElementAtOrDefault(1)?.Trim(), out var total);
        return (place, total);
    }

    // ================================================================== the feed

    private void Push(CCSPlayerController player, Viewer v, FeedLine line, bool sample = false)
    {
        var now = Server.CurrentTime;
        var live = Enumerable.Range(0, v.Feed.Length)
            .Where(i => v.Feed[i] is { Leaving: false })
            .OrderBy(i => v.Feed[i]!.Id)
            .ToList();
        if (live.Count >= HudLayout.FeedSlots - 1) Leave(player, v, live[0]);

        if (v.Feed.All(s => s == null)) v.FeedNext = 0;
        int? slot = null;
        for (var i = 0; i < v.Feed.Length && slot == null; i++)
        {
            var n = (v.FeedNext + i) % v.Feed.Length;
            if (v.Feed[n] == null) slot = n;
        }

        if (slot is not { } s) return;

        var seconds = sample ? 3600f : line.Kind == FeedKind.Sr ? Cfg.FeedRecordSeconds : Cfg.FeedSeconds;
        v.Feed[s] = new FeedEntry
        {
            Id = ++v.FeedSeq, Line = line, ShowAt = now + FeedEntrySeconds, LeaveAt = now + seconds, Sample = sample,
        };
        v.FeedNext = s + 1;

        var row = $"fd{s}_row";
        v.W.Var(_panel!, player, $"fd{s}_n", line.Name);
        v.W.Var(_panel!, player, $"fd{s}_tag", line.Tag);
        v.W.Var(_panel!, player, $"fd{s}_t", line.Time);
        v.W.Var(_panel!, player, $"fd{s}_d", line.Diff);
        v.W.OneOf(_panel!, player, row, line.Kind switch
        {
            FeedKind.Sr => "k-sr",
            FeedKind.Pb => "k-pb",
            FeedKind.Stage => "k-stg",
            _ => "k-fin",
        }, HudLayout.FeedKinds);
        v.W.Cls(_panel!, player, row, "nod", line.Diff.Length == 0);
        v.W.Cls(_panel!, player, row, "show", false);
        // Into layout first, revealed a beat later: the slide needs the row drawn at its start first.
        v.W.Cls(_panel!, player, $"fd{s}", "hidden", false);
    }

    private void Leave(CCSPlayerController player, Viewer v, int n)
    {
        if (v.Feed[n] is not { Leaving: false } entry) return;
        entry.Leaving = true;
        entry.CollapseAt = Server.CurrentTime + FeedExitSeconds;
        v.W.Cls(_panel!, player, $"fd{n}_row", "show", false);
    }

    /// <summary>The feed's timed steps, for every viewer, 8 times a second.</summary>
    public void Tick()
    {
        if (_panel == null || Server.TickCount % (64 / StaticRate) != 0) return;
        var now = Server.CurrentTime;

        foreach (var (slot, v) in _viewers)
        {
            if (v.Feed.All(e => e == null)) continue;
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not { IsValid: true } || !Reaches(player)) continue;

            for (var n = 0; n < v.Feed.Length; n++)
            {
                if (v.Feed[n] is not { } e) continue;

                if (!e.Shown && !e.Leaving && now >= e.ShowAt)
                {
                    e.Shown = true;
                    v.W.Cls(_panel, player, $"fd{n}_row", "show", true);
                }

                if (!e.Leaving && now >= e.LeaveAt) Leave(player, v, n);

                if (e.Leaving && now >= e.CollapseAt)
                {
                    v.Feed[n] = null;
                    v.W.Cls(_panel, player, $"fd{n}", "hidden", true);
                }
            }
        }
    }

    // ================================================================== drawing

    /// <summary>A spectator: draws the player they watch, or nothing. False when the panel does not reach them.</summary>
    public bool DriveSpectator(CCSPlayerController viewer, int tick)
    {
        if (_panel == null) return false;
        if (!Reaches(viewer)) Attach(viewer);
        if (!Reaches(viewer)) return false;

        var target = _st.HudSpecTarget(viewer);
        if (target != null && _st.playerTimers.TryGetValue(target.Slot, out var timer) && _st.IsAllowedPlayer(target))
            return Drive(viewer, target, timer, tick);

        Blank(viewer);
        // The map records are the viewer's own, not the watched runner's: they stay while watching nobody.
        if (_viewers.TryGetValue(viewer.Slot, out var v) && Due(ref v.RecordsSlot, tick, StaticRate))
            DrawRecords(viewer, v, Server.CurrentTime);
        return true;
    }

    /// <summary>Nothing to show (spectating nobody): the card, the speed and the keys leave, the feed stays.</summary>
    private void Blank(CCSPlayerController viewer)
    {
        if (!_viewers.TryGetValue(viewer.Slot, out var v)) return;
        if (v.Blank && v.Source == -1) return;
        v.Blank = true;
        v.Source = -1;
        v.W.Cls(_panel!, viewer, "tc", "show", false);
        v.W.Cls(_panel!, viewer, "sc", "show", false);
        v.W.Cls(_panel!, viewer, "ks", "show", false);
        HideToast(viewer, v);
    }

    /// <summary>Draws <paramref name="runner"/>'s state on <paramref name="viewer"/>'s panel at the configured rates.</summary>
    public bool Drive(CCSPlayerController viewer, CCSPlayerController runner, PlayerTimerInfo timer, int tick)
    {
        if (_panel == null) return false;
        if (!_viewers.TryGetValue(viewer.Slot, out var v) || !_panel.IsOpenFor(viewer))
        {
            Attach(viewer);
            if (!_viewers.TryGetValue(viewer.Slot, out v) || !_panel.IsOpenFor(viewer)) return false;
        }

        if (v.Source != runner.Slot)
        {
            // A new runner on this panel: every value that belongs to the runner goes out again.
            v.Source = runner.Slot;
            v.TimerSlot = v.SpeedSlot = v.SyncSlot = v.PlaceSlot = v.ProgSlot = v.StaticSlot = -1;
            v.PopSeq = RunnerOf(runner.Slot).PopSeq;
            v.ToastSeq = -1;
        }

        v.Blank = false;
        var r = RunnerOf(runner.Slot);
        var mine = _st.playerTimers.TryGetValue(viewer.Slot, out var own) ? own : timer;
        var now = Server.CurrentTime;

        // !hud off (HideTimerHud) hides the card and the speed, as it always did; each also has its own switch.
        var hudOn = !mine.HideTimerHud && _st.hudOverlayEnabled;
        var showCard = hudOn && v.Prefs.CardOn;
        var showSpeed = hudOn && _st.VelocityHudEnabled && v.Prefs.SpeedOn;
        var showKeys = !mine.HideKeys && _st.keysOverlayEnabled;

        if (Due(ref v.StaticSlot, tick, StaticRate))
            DrawStatic(viewer, v, runner, timer, r, mine, hudOn, showCard, showSpeed, showKeys, now);

        if (Due(ref v.RecordsSlot, tick, StaticRate))
            DrawRecords(viewer, v, now);

        if (showCard) DrawClock(viewer, v, runner, timer, r, tick);
        if (showSpeed) DrawSpeed(viewer, v, runner, timer, tick);
        if (showKeys) DrawKeys(viewer, v, runner, timer);

        return true;
    }

    private static bool Due(ref long slot, int tick, int rate)
    {
        var now = (long)tick * rate / 64;
        if (slot == now) return false;
        slot = now;
        return true;
    }

    private void DrawClock(CCSPlayerController viewer, Viewer v, CCSPlayerController runner, PlayerTimerInfo t, Runner r, int tick)
    {
        var replay = t.IsReplaying;
        var running = t.IsTimerRunning || t.IsBonusTimerRunning;

        // 2 or 3 decimals: every tick, so the digits count instead of jumping. Only this label goes out that often,
        // and only when its text changed. 1 decimal: the configured rate (10 a second) is enough.
        var decimals = v.Prefs.Decimals;
        if ((running || replay) && Due(ref v.TimerSlot, tick, decimals >= 2 ? 64 : Cfg.TimerRate))
        {
            var ticks = replay ? _st.HudReplayFrame(runner.Slot) : t.IsBonusTimerRunning ? t.BonusTimerTicks : t.TimerTicks;
            v.W.Var(_panel!, viewer, "tc_time", HudLayout.TimeRunning(ticks, decimals));
        }

        if (running && Cfg.LivePlace && !t.IsBonusTimerRunning && Due(ref v.PlaceSlot, tick, Cfg.PlaceRate))
        {
            var place = _st.GetPlayerPlacement(runner);
            v.W.Var(_panel!, viewer, "tc_place", place);
        }

        if (replay && Due(ref v.ProgSlot, tick, 2))
        {
            var total = Math.Max(1, _st.HudReplayLength(runner.Slot));
            v.W.OneOf(_panel!, viewer, "tc_prog", HudLayout.Step(_st.HudReplayFrame(runner.Slot), total), HudLayout.StepClasses);
        }

        if (v.PopSeq != r.PopSeq)
        {
            v.PopSeq = r.PopSeq;
            v.PopA = !v.PopA;
            v.W.Cls(_panel!, viewer, "tc_time", v.PopA ? "pop-b" : "pop-a", false);
            v.W.Cls(_panel!, viewer, "tc_time", v.PopA ? "pop-a" : "pop-b", true);
        }
    }

    private void DrawSpeed(CCSPlayerController viewer, Viewer v, CCSPlayerController runner, PlayerTimerInfo t, int tick)
    {
        if (Due(ref v.SpeedSlot, tick, Cfg.SpeedRate))
        {
            var pawn = runner.PlayerPawn?.Value;
            if (pawn != null)
            {
                var vel = pawn.AbsVelocity.ToVector_t();
                var speed = (int)Math.Round(_st.use2DSpeed ? vel.Length2D() : vel.Length());
                var text = speed.ToString();
                v.W.Var(_panel!, viewer, "sp_val", text);
                v.W.OneOf(_panel!, viewer, "sp_fill", HudLayout.Step(speed, Cfg.SpeedBarMax), HudLayout.StepClasses);
                if (_st.useDynamicColor)
                {
                    int[] thresholds = [349, 699, 1049, 1399, 1749, 2099, 2449, 2799, 3149, 3499];
                    var c = Array.FindIndex(thresholds, x => speed < x);
                    v.W.OneOf(_panel!, viewer, "sp_num", HudLayout.SpeedColors[c < 0 ? 10 : c], HudLayout.SpeedColors);
                }
            }
        }

        if (Due(ref v.SyncSlot, tick, Cfg.SyncRate))
        {
            var pre = (int)Math.Round(_st.Utils.ParseVector_t(t.PreSpeed ?? "0 0 0").Length2D());
            v.W.Var(_panel!, viewer, "sp_pre", pre.ToString());
            v.W.Var(_panel!, viewer, "sp_sync", t.Sync.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%");
        }
    }

    private void DrawKeys(CCSPlayerController viewer, Viewer v, CCSPlayerController runner, PlayerTimerInfo t)
    {
        PlayerButtons buttons;
        var jump = false;
        if (t.IsReplaying)
        {
            buttons = _st.HudReplayButtons(runner.Slot);
        }
        else
        {
            buttons = runner.Buttons;
            jump = t.MovementService?.LegacyJump.OldJumpPressed == true;
        }

        jump |= (buttons & PlayerButtons.Jump) != 0;
        // A scroll jump lasts one tick: keep Jump lit for three so it shows.
        if (jump) v.JumpHold = JumpHoldTicks;
        else if (v.JumpHold > 0) v.JumpHold--;

        Key(viewer, v, "k_w", (buttons & PlayerButtons.Forward) != 0);
        Key(viewer, v, "k_a", (buttons & PlayerButtons.Moveleft) != 0);
        Key(viewer, v, "k_s", (buttons & PlayerButtons.Back) != 0);
        Key(viewer, v, "k_d", (buttons & PlayerButtons.Moveright) != 0);
        Key(viewer, v, "k_e", (buttons & PlayerButtons.Use) != 0);
        Key(viewer, v, "k_c", (buttons & PlayerButtons.Duck) != 0);
        Key(viewer, v, "k_j", v.JumpHold > 0);
    }

    private void Key(CCSPlayerController viewer, Viewer v, string id, bool on) => v.W.Cls(_panel!, viewer, id, "on", on);

    /// <summary>The keys each style takes away, struck out on the overlay.</summary>
    private static string Blocked(int style) => style switch
    {
        2 => "ad",
        3 => "asd",
        6 => "wsd",
        7 => "was",
        8 => "wad",
        _ => "",
    };

    private void DrawStatic(CCSPlayerController viewer, Viewer v, CCSPlayerController runner, PlayerTimerInfo t, Runner r,
        PlayerTimerInfo mine, bool hudOn, bool showCard, bool showSpeed, bool showKeys, float now)
    {
        var panel = _panel!;
        var w = v.W;
        var prefs = v.Prefs;

        // ------------------------------------------------------------ style
        w.OneOf(panel, viewer, "st_skin", "ac-" + prefs.Accent, HudLayout.AccentClasses);
        w.OneOf(panel, viewer, "st_skin", "op-" + prefs.Opacity, HudLayout.OpacityClasses);
        w.OneOf(panel, viewer, "st_skin", "tx-" + prefs.Text, HudLayout.TextClasses);
        w.Cls(panel, viewer, "st_skin", "cc", prefs.Compact);

        // ------------------------------------------------------------ placement
        w.OneOf(panel, viewer, "st_cg", "p-" + prefs.Card, HudLayout.PosClasses);
        w.OneOf(panel, viewer, "st_cg_sz", "sz-" + prefs.CardSize, HudLayout.SizeClasses);
        w.OneOf(panel, viewer, "st_sp", HudLayout.SpeedClass(prefs), HudLayout.SpeedClasses);
        w.OneOf(panel, viewer, "st_sp_sz", "sz-" + prefs.SpeedSize, HudLayout.SizeClasses);
        w.OneOf(panel, viewer, "st_ks", "p-" + prefs.Keys, HudLayout.PosClasses);
        w.OneOf(panel, viewer, "st_ks_sz", "sz-" + prefs.KeysSize, HudLayout.SizeClasses);
        var toastFree = prefs.Toast != HudLayout.WithCard;
        w.OneOf(panel, viewer, "st_tf", "p-" + (toastFree ? prefs.Toast : "tc"), HudLayout.PosClasses);
        w.OneOf(panel, viewer, "st_tf_sz", "sz-" + prefs.ToastSize, HudLayout.SizeClasses);
        w.OneOf(panel, viewer, "st_fd", "p-" + prefs.Feed, HudLayout.PosClasses);
        w.OneOf(panel, viewer, "st_fd_sz", "sz-" + prefs.FeedSize, HudLayout.SizeClasses);
        // st_mr's spot and size: DrawRecords.
        if (v.FeedDirty)
        {
            v.FeedDirty = false;
            for (var n = 0; n < HudLayout.FeedSlots; n++)
            {
                if (v.Feed[n] != null) continue;
                w.Cls(panel, viewer, $"fd{n}_row", "show", false);
                w.Cls(panel, viewer, $"fd{n}", "hidden", true);
            }
        }

        DrawCard(viewer, v, runner, t, r, showCard, now);
        DrawSpeedPanel(viewer, v, t, r, showSpeed, now);
        DrawKeysPanel(viewer, v, t, showKeys);
        DrawToast(viewer, v, r, mine, hudOn, now);
    }

    private void DrawCard(CCSPlayerController viewer, Viewer v, CCSPlayerController runner, PlayerTimerInfo t, Runner r, bool show, float now)
    {
        var panel = _panel!;
        var w = v.W;
        w.Cls(panel, viewer, "tc", "show", show);
        if (!show) return;

        var replay = t.IsReplaying;
        var running = t.IsTimerRunning || t.IsBonusTimerRunning;
        var bonus = t.IsBonusTimerRunning ? t.BonusStage : t.CurrentZoneInfo.InBonusStartZone ? t.CurrentZoneInfo.CurrentBonusNumber : 0;
        // The result stays up for ResultSeconds, or until the player is back in the start zone (!r).
        var result = !running && !replay && r.Result != ResultKind.None && now < r.ResultUntil && !t.inStartzone
            ? r.Result
            : ResultKind.None;
        var spectating = viewer.Slot != runner.Slot;

        var state = replay ? "st-replay"
            : running ? "st-run"
            : result switch
            {
                ResultKind.Done => "st-done",
                ResultKind.Pb => "st-pb",
                ResultKind.Sr => "st-sr",
                ResultKind.Cancel => "st-cancel",
                _ => t.inStartzone || t.CurrentZoneInfo.InBonusStartZone ? "st-start" : "st-idle",
            };
        w.OneOf(panel, viewer, "tc", state, HudLayout.CardStates);

        // ------------------------------------------------------------ eyebrow: lead, who, map, chips
        var replayKind = _st.HudReplayKind(runner.Slot);
        var lead = state switch
        {
            "st-replay" => replayKind switch
            {
                "pb" => "Replay · PB",
                "wr" => "Replay · world record",
                "sr" => "Replay · server record",
                var k when k.StartsWith("top") => "Replay · #" + k[3..],
                _ => "Replay",
            },
            "st-run" => bonus != 0 ? $"Bonus {bonus}" : "",
            "st-done" => "Finished",
            "st-pb" => "New personal best",
            "st-sr" => "New server record",
            "st-cancel" => "Run cancelled",
            "st-start" => bonus != 0 ? $"Bonus {bonus} start" : "Start zone",
            _ => "Not running",
        };
        var who = "";
        if (spectating && !runner.IsBot)
        {
            who = runner.PlayerName + (state is "st-run" or "st-start" or "st-idle" || lead.Length == 0 ? "" : " · " + lead);
            lead = "Watching";
        }

        w.Var(panel, viewer, "tc_lead", lead);
        w.Var(panel, viewer, "tc_who", who);
        w.Cls(panel, viewer, "tc", "lead-empty", lead.Length == 0);
        w.Cls(panel, viewer, "tc", "who-empty", who.Length == 0);
        w.Cls(panel, viewer, "tc", "spec", spectating && !replay);
        w.Cls(panel, viewer, "tc", "bonus", bonus != 0);
        w.Var(panel, viewer, "tc_map", _st.HudMapLine());

        var style = t.currentStyle;
        var styleOn = _st.enableStyles && style != 0;
        w.Cls(panel, viewer, "tc", "nostyle", !styleOn);
        if (styleOn) w.Var(panel, viewer, "c_style", _st.GetNamedStyle(style));
        var modeOn = !string.IsNullOrEmpty(t.Mode) && t.Mode != _st.HudDefaultModeName;
        w.Cls(panel, viewer, "tc", "nomode", !modeOn);
        if (modeOn) w.Var(panel, viewer, "c_mode", t.Mode);

        // ------------------------------------------------------------ the clock when it is not running
        if (!running && !replay)
            w.Var(panel, viewer, "tc_time", result is ResultKind.Done or ResultKind.Pb or ResultKind.Sr or ResultKind.Cancel
                ? HudLayout.Time(r.FinalTicks)
                : HudLayout.TimeZero(v.Prefs.Decimals));
        if (result != ResultKind.None)
        {
            w.Var(panel, viewer, "tc_diff", r.DiffText);
            w.Cls(panel, viewer, "tc", "diff-ahead", r.DiffText.Length > 0 && r.DiffAhead);
            w.Cls(panel, viewer, "tc", "diff-behind", r.DiffText.Length > 0 && !r.DiffAhead);
            w.Var(panel, viewer, "tc_res", r.ResText);
            w.Var(panel, viewer, "tc_pts", r.PtsText);
            w.Cls(panel, viewer, "tc", "pts", r.PtsText.Length > 0);
        }

        // ------------------------------------------------------------ stage / checkpoint, live place
        var (kind, count, current, shown) = _st.HudProgress(t);
        w.Cls(panel, viewer, "tc", "nostage", kind == null || bonus != 0);
        if (kind != null)
        {
            w.Var(panel, viewer, "tc_stagek", kind);
            w.Var(panel, viewer, "tc_stage", $"{Math.Clamp(shown, 0, count)}/{count}");
        }

        var placeOn = Cfg.LivePlace && running && bonus == 0;
        w.Cls(panel, viewer, "tc", "noplace", !placeOn);

        // ------------------------------------------------------------ PB, SR, rank
        string pb;
        if (replay && replayKind == "pb")
            pb = _st.HudReplayTime(runner.Slot);
        else if (bonus != 0)
            pb = t.CachedBonusInfo.TryGetValue(bonus, out var b) && b.PbTicks > 0 ? HudLayout.Time(b.PbTicks) : "";
        else
            pb = (t.CachedPB ?? "").Trim();
        var prefs = v.Prefs;
        var pbOn = pb.Length > 0 && prefs.Compare;
        w.Cls(panel, viewer, "tc", "nopb", !pbOn);
        w.Cls(panel, viewer, "tc", "rp-pb", replay && replayKind == "pb");
        if (pb.Length > 0) w.Var(panel, viewer, "tc_pb", pb);

        string srKey = "SR", sr = "", srBy = "";
        if (replay && replayKind != "pb")
        {
            (srBy, sr) = _st.HudReplayHolder(runner.Slot);
            if (replayKind == "wr") srKey = "WR";
            else if (replayKind.StartsWith("top")) srKey = "#" + replayKind[3..];
        }
        else if (bonus == 0)
        {
            if (Cfg.ShowWorldRecord && _st.HudWorldRecord(t.Mode) is { } wr)
            {
                srKey = "WR";
                sr = wr.Time;
                srBy = wr.Name;
            }
            else if (_st.HudServerRecord(runner) is { } rec)
            {
                sr = HudLayout.Time(rec.TimerTicks);
                srBy = rec.PlayerName ?? "";
            }
        }

        var srOn = sr.Length > 0 && prefs.Compare;
        w.Cls(panel, viewer, "tc", "nosr", !srOn);
        if (sr.Length > 0)
        {
            w.Var(panel, viewer, "tc_srk", srKey);
            w.Var(panel, viewer, "tc_sr", sr);
            w.Var(panel, viewer, "tc_srby", srBy);
        }

        var (rankName, rankColor, rankIcon) = _st.HudRank(t);
        // Today's HUD shows the rank icon (sharptimer_enable_rankicons_hud). A rank whose icon is not baked into the
        // addon falls back to its name.
        var wantIcon = Cfg.RankDisplay is "icon" or "both" && _st.RankIconsEnabled;
        var useIcon = prefs.Rank && wantIcon && rankIcon != null && rankName.Length > 0;
        var useText = prefs.Rank && rankName.Length > 0 && (Cfg.RankDisplay is "text" or "both" || (wantIcon && rankIcon == null));
        w.Cls(panel, viewer, "tc", "rk-icon", useIcon);
        w.Cls(panel, viewer, "tc", "rk-text", useText);
        if (useIcon) w.OneOf(panel, viewer, "tc_ricon", rankIcon, HudLayout.RankIconClasses);
        if (useText)
        {
            w.Var(panel, viewer, "tc_rank", rankName);
            w.OneOf(panel, viewer, "tc_rank", rankColor, HudLayout.RankColorClasses);
        }

        // PB, SR and rank all off: the row goes, with its margin.
        w.Cls(panel, viewer, "tc", "nocmp", !pbOn && !srOn && !useIcon && !useText);
        // "!HUD SETTINGS" in the start zone until the player has opened the settings once.
        w.Cls(panel, viewer, "tc", "hint", Cfg.SettingsHint && !prefs.Seen && !spectating);

        // ------------------------------------------------------------ the split bar
        DrawSegments(viewer, v, t, r, kind, count, current, running, bonus != 0 || replay);
    }

    private void DrawSegments(CCSPlayerController viewer, Viewer v, PlayerTimerInfo t, Runner r, string? kind, int count,
        int current, bool running, bool off)
    {
        var panel = _panel!;
        var w = v.W;
        var n = HudLayout.Segments;
        var overflow = count > n ? Cfg.SplitBarOverflow : "fit";
        var hide = off || kind == null || count <= 0 || overflow == "count";
        w.Cls(panel, viewer, "tc", "noseg", hide);
        if (hide) return;

        var group = overflow == "group" ? (int)Math.Ceiling(count / (double)n) : 1;
        var shown = overflow == "group" ? (int)Math.Ceiling(count / (double)group) : Math.Min(count, n);
        var start = overflow == "scroll" ? Math.Clamp(current - n / 2, 0, count - n) : 0;

        for (var j = 0; j < n; j++)
        {
            var id = $"sg{j}";
            if (j >= shown)
            {
                w.Cls(panel, viewer, id, "x", true);
                continue;
            }

            w.Cls(panel, viewer, id, "x", false);
            var from = start + j * group;
            var to = Math.Min(count, from + group);
            SplitMark? mark = null;
            for (var i = to - 1; i >= from && mark == null; i--)
                if (r.Splits.TryGetValue(i, out var m)) mark = m;
            var isCurrent = running && current >= from && current < to;

            w.Cls(panel, viewer, id, "ahead", mark == SplitMark.Ahead);
            w.Cls(panel, viewer, id, "behind", mark == SplitMark.Behind);
            w.Cls(panel, viewer, id, "done", mark == SplitMark.Done);
            w.Cls(panel, viewer, id, "cur", isCurrent && mark == null);
        }
    }

    private void DrawSpeedPanel(CCSPlayerController viewer, Viewer v, PlayerTimerInfo t, Runner r, bool show, float now)
    {
        var panel = _panel!;
        var w = v.W;
        w.Cls(panel, viewer, "sc", "show", show);
        w.Cls(panel, viewer, "sc", "nobar", !v.Prefs.SpeedBar);
        w.Cls(panel, viewer, "sc", "nosync", !_st.StrafeHudEnabled || t.IsReplaying || !v.Prefs.Sync);
        w.Cls(panel, viewer, "sc", "vc", _st.useDynamicColor);

        var chip = now < r.ChipUntil && (t.IsTimerRunning || t.IsBonusTimerRunning);
        w.Cls(panel, viewer, "sc", "flash", chip);
        w.Cls(panel, viewer, "sc", "capped", chip && r.ChipCapped);
        if (chip) w.Var(panel, viewer, "sc_chip", r.ChipText);
        var zone = _st.maxStartingSpeedEnabled && (t.inStartzone || (chip && r.ChipCapped));
        w.Cls(panel, viewer, "sc", "zone", zone);
        if (zone) w.OneOf(panel, viewer, "sp_cap", "cap-" + HudLayout.Step(_st.maxStartingSpeed, Cfg.SpeedBarMax)[1..], HudLayout.CapClasses);
    }

    private void DrawKeysPanel(CCSPlayerController viewer, Viewer v, PlayerTimerInfo t, bool show)
    {
        var panel = _panel!;
        var w = v.W;
        w.Cls(panel, viewer, "ks", "show", show);
        w.Cls(panel, viewer, "ks", "v-row", v.Prefs.KeysRow);
        var running = t.IsTimerRunning || t.IsBonusTimerRunning || t.IsReplaying;
        var blocked = Blocked(t.currentStyle);
        foreach (var k in "wasd")
            w.Cls(panel, viewer, "k_" + k, "no", running && blocked.Contains(k));
        w.Cls(panel, viewer, "ks", "chute", t.currentStyle == 11);
    }

    private void DrawToast(CCSPlayerController viewer, Viewer v, Runner r, PlayerTimerInfo mine, bool hudOn, float now)
    {
        var panel = _panel!;
        var w = v.W;
        var prefs = v.Prefs;
        var withCard = prefs.Toast == HudLayout.WithCard;
        var target = withCard ? (HudLayout.TopAnchored(prefs.Card) ? "td" : "tu") : "tf";

        // The in-card slots hold their space only when the toast lives there, so the card does not jump.
        w.Cls(panel, viewer, "tu", "hidden", !(withCard && target == "tu"));
        w.Cls(panel, viewer, "td", "hidden", !(withCard && target == "td"));

        if (v.ToastPanel != null && v.ToastPanel != target)
        {
            w.Cls(panel, viewer, v.ToastPanel, "show", false);
            v.ToastShown = false;
            v.ToastSeq = -1;
        }

        v.ToastPanel = target;

        HudToast? toast = null;
        // Splits: off with !splits (the split toast) or !hud off. Replies always.
        if (r.Toast != null && now < r.ToastUntil && (!r.Toast.Split || (!mine.HideChatSpeed && hudOn)))
            toast = r.Toast;
        else if (v.Preview)
            toast = new HudToast(false, "SPLITS", Bold: "Stage 3  1:02.114", Message: "splits and replies show here");

        if (toast == null)
        {
            HideToast(viewer, v);
            return;
        }

        w.Var(panel, viewer, "ts_k", toast.Key);
        w.Cls(panel, viewer, target, "k-split", toast.Split);
        w.Cls(panel, viewer, target, "k-msg", !toast.Split);
        w.Cls(panel, viewer, target, "err", toast.Error);
        if (toast.Split)
        {
            w.Var(panel, viewer, "ts_t", toast.Time);
            Chip(viewer, v, target, "d1", "ts_d1", toast.PbDiff, toast.PbSign);
            Chip(viewer, v, target, "d2", "ts_d2", toast.SrDiff, toast.SrSign);
            w.Cls(panel, viewer, target, "v-off", toast.Speed.Length == 0);
            w.Var(panel, viewer, "ts_v", toast.Speed);
            Chip(viewer, v, target, "vd", "ts_vd", toast.SpeedDiff, toast.SpeedSign);
        }
        else
        {
            w.Var(panel, viewer, "ts_mb", toast.Bold);
            w.Var(panel, viewer, "ts_m", toast.Message);
        }

        var seq = v.Preview && toast != r.Toast ? -2 : r.ToastSeq;
        if (v.ToastShown && seq != v.ToastSeq)
        {
            // Already up: a new toast replaces the text and bumps (two class names, so the restart always arrives).
            v.ReA = !v.ReA;
            w.Cls(panel, viewer, target, v.ReA ? "re-b" : "re-a", false);
            w.Cls(panel, viewer, target, v.ReA ? "re-a" : "re-b", true);
        }

        v.ToastSeq = seq;
        w.Cls(panel, viewer, target, "show", true);
        v.ToastShown = true;
    }

    /// <summary>The map's top times for this viewer: the main course in their mode, normal style, from the cache
    /// SharpTimer keeps per mode (refreshed on a map start and after a new time; never a query from here). 3 rows,
    /// 10 while !hud is open, and the viewer's own place under the list when they are further down.</summary>
    private void DrawRecords(CCSPlayerController viewer, Viewer v, float now)
    {
        var panel = _panel!;
        var w = v.W;
        var prefs = v.Prefs;

        // Placement here too: a spectator watching nobody gets no DrawStatic.
        w.OneOf(panel, viewer, "st_mr", "p-" + prefs.Records, HudLayout.PosClasses);
        w.OneOf(panel, viewer, "st_mr_sz", "sz-" + prefs.RecordsSize, HudLayout.SizeClasses);

        var own = _st.playerTimers.TryGetValue(viewer.Slot, out var t) ? t : null;
        var mode = _st.HudModeOf(own);
        // Asked even while hidden: the same cache gives the card its SR and the live place.
        var top = _st.HudMapTop(mode);

        if (v.RecordsJoin)
        {
            v.RecordsJoin = false;
            v.RecordsUntil = now + Cfg.MapRecordsJoinSeconds;
        }

        var show = prefs.RecordsOn && (v.Preview || prefs.RecordsShow switch
        {
            "j" => now < v.RecordsUntil,
            "h" => false,
            _ => true,
        });
        w.Cls(panel, viewer, "mr", "show", show);
        if (!show) return;

        var list = top ?? (IReadOnlyList<PlayerRecord>)Array.Empty<PlayerRecord>();
        var rows = Math.Min(v.Preview ? Cfg.MapRecordsMenuRows : Cfg.MapRecordsRows, list.Count);
        var steamId = viewer.SteamID.ToString();
        var mine = -1;
        for (var i = 0; i < list.Count; i++)
            if (list[i].SteamID == steamId) { mine = i; break; }

        w.Var(panel, viewer, "mr_sub", mode != _st.HudDefaultModeName ? mode : "");
        w.Cls(panel, viewer, "mr", "empty", list.Count == 0);
        if (list.Count == 0) w.Var(panel, viewer, "mr_empty", top == null ? "Loading the times" : "No times yet. Be the first");

        for (var j = 0; j < HudLayout.MapRecordRows; j++)
        {
            var id = "mr" + j;
            w.Cls(panel, viewer, id, "hidden", j >= rows);
            if (j >= rows) continue;
            var rec = list[j];
            w.Var(panel, viewer, id + "_p", (j + 1).ToString());
            w.Var(panel, viewer, id + "_n", rec.PlayerName ?? "");
            w.Var(panel, viewer, id + "_t", HudLayout.Time(rec.TimerTicks));
            w.Cls(panel, viewer, id, "top", j == 0);
            w.Cls(panel, viewer, id, "me", j == mine);
        }

        // The viewer's own row: further down the cached list, or past it with their PB and SharpTimer's placement.
        string place = "", time = "";
        if (mine >= rows)
        {
            place = (mine + 1).ToString();
            time = HudLayout.Time(list[mine].TimerTicks);
        }
        else if (mine < 0 && top != null && own is { CachedPBTicks: > 0 })
        {
            var (p, _) = ParseRanking(own.CachedMapPlacement ?? "");
            place = p > 0 ? p.ToString() : "";
            time = HudLayout.Time(own.CachedPBTicks);
        }

        var meOn = time.Length > 0;
        w.Cls(panel, viewer, "mr_me", "hidden", !meOn);
        if (!meOn) return;
        w.Var(panel, viewer, "mr_me_p", place);
        w.Var(panel, viewer, "mr_me_n", viewer.PlayerName);
        w.Var(panel, viewer, "mr_me_t", time);
    }

    private void Chip(CCSPlayerController viewer, Viewer v, string panelId, string prefix, string variable, string text, int sign)
    {
        var panel = _panel!;
        var off = text.Length == 0;
        v.W.Cls(panel, viewer, panelId, prefix + "-off", off);
        v.W.Cls(panel, viewer, panelId, prefix + "-ahead", !off && sign < 0);
        v.W.Cls(panel, viewer, panelId, prefix + "-behind", !off && sign > 0);
        if (!off) v.W.Var(panel, viewer, variable, text);
    }

    private void HideToast(CCSPlayerController viewer, Viewer v)
    {
        if (v.ToastPanel == null || !v.ToastShown) return;
        v.W.Cls(_panel!, viewer, v.ToastPanel, "show", false);
        v.ToastShown = false;
    }
}
