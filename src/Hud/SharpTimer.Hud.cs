using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using PanoramaManager;
using SharpTimer.Hud;

namespace SharpTimer;

/// <summary>
/// The Panorama HUD's hooks into SharpTimer: start-up, what the HUD needs to read from the timer, and the chat
/// lines it replaces. Everything here is a no-op while cfg/SharpTimer/hud.json says "Enabled": false, so the
/// center HTML and every chat line are exactly as before.
/// </summary>
public partial class SharpTimer
{
    public HudConfig HudSettings { get; private set; } = new();
    internal HudPrefsStore HudPrefStore = null!;
    internal TimerHud? TimerHud;
    internal HudMenu? HudMenuPanel;

    private string HudConfigPath => Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "hud.json");

    /// <summary>Runs one HUD step. The HUD is called from inside the timer's tick and its trigger handlers, so an
    /// exception here must never reach them: it is logged (the stack once per throw point, then at most one line
    /// every 10 seconds) and the timer carries on.</summary>
    private void HudSafe(string what, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            HudError(what, e);
        }
    }

    /// <summary>A HUD answer the timer's own code waits on (does this line still go to chat?). An exception gives
    /// what SharpTimer did before the HUD, and is logged like <see cref="HudSafe"/>.</summary>
    private T HudAsk<T>(string what, Func<T> ask, T fallback)
    {
        try
        {
            return ask();
        }
        catch (Exception e)
        {
            HudError(what, e);
            return fallback;
        }
    }

    private void HudError(string what, Exception e) => Utils.LogException($"[HUD] {what}", e);

    /// <summary>PlayerOnTick: the Panorama HUD for a living player. False: print the center HTML.</summary>
    private bool HudDrive(CCSPlayerController player, PlayerTimerInfo timer, int tick)
    {
        if (TimerHud == null || player.IsBot) return false;
        try
        {
            return TimerHud.Drive(player, player, timer, tick);
        }
        catch (Exception e)
        {
            HudError("draw", e);
            return false;
        }
    }

    /// <summary>PlayerOnTick: the Panorama HUD for a spectator or a dead player. False: print the center HTML.</summary>
    private bool HudDriveSpectator(CCSPlayerController player, int tick)
    {
        if (TimerHud == null || player.IsBot) return false;
        try
        {
            return TimerHud.DriveSpectator(player, tick);
        }
        catch (Exception e)
        {
            HudError("draw spectator", e);
            return false;
        }
    }

    private void HudTick()
    {
        if (TimerHud != null) HudSafe("feed", TimerHud.Tick);
    }

    private void HudLoad(bool hotReload)
    {
        HudSettings = HudConfig.Load(HudConfigPath, Utils.LogError);
        HudPrefStore = new HudPrefsStore(this);
        if (!HudSettings.Enabled)
        {
            Utils.LogDebug("[HUD] Panorama HUD off (hud.json Enabled = false), using the center HTML");
            return;
        }

        try
        {
            Panorama.Init(this);
            TimerHud = new TimerHud(this);
            TimerHud.Init();
            if (!TimerHud.Enabled)
            {
                TimerHud = null;
                return;
            }

            HudMenuPanel = new HudMenu(this, TimerHud);
            HudMenuPanel.Init();
        }
        catch (Exception e)
        {
            Utils.LogError($"[HUD] Panorama failed to start, using the center HTML: {e}");
            TimerHud = null;
            HudMenuPanel = null;
            return;
        }

        if (hotReload)
            foreach (var p in Utilities.GetPlayers())
                TimerHud.Attach(p);
    }

    private void HudUnload()
    {
        HudMenuPanel?.Dispose();
        TimerHud?.Dispose();
        HudMenuPanel = null;
        TimerHud = null;
        try { Panorama.Shutdown(); } catch { /* already down */ }
    }

    /// <summary>Map start: hud.json again (Enabled itself only changes on a plugin load) and fresh timers.</summary>
    private void HudMapStart()
    {
        var fresh = HudConfig.Load(HudConfigPath, Utils.LogError);
        if (fresh.Enabled != HudSettings.Enabled)
            Utils.LogError("[HUD] hud.json Enabled changed: it takes effect on the next plugin load");
        fresh.Enabled = HudSettings.Enabled;
        HudSettings = fresh;
        HudTopReset();
        if (TimerHud != null) HudSafe("map start", TimerHud.MapStart);
    }

    private void HudPlayerConnected(CCSPlayerController player)
    {
        if (TimerHud != null) HudSafe("connect", () => TimerHud.Attach(player));
    }

    private void HudPlayerLeft(CCSPlayerController player) => HudSafe("disconnect", () =>
    {
        TimerHud?.Forget(player.Slot);
        HudMenuPanel?.Forget(player.Slot);
        HudPrefStore?.Forget(player.SteamID);
    });

    // ------------------------------------------------------------------ chat the HUD replaces

    // These answer the timer's own code from inside its triggers, splits and finish lines (a split's NextFrame
    // counts the stage after asking them), so none of them may throw: an exception falls back to the chat line.

    internal bool HudReaches(CCSPlayerController? player) =>
        TimerHud is { } hud && player != null && HudAsk("reach", () => hud.Reaches(player), false);

    /// <summary>The panel reaches the player and they have not hidden the timer HUD (!hud off): the card and the
    /// toast show what the chat line would have said.</summary>
    internal bool HudShowsCard(CCSPlayerController? player) =>
        player != null && HudReaches(player) && !(playerTimers.TryGetValue(player.Slot, out var t) && t.HideTimerHud);

    /// <summary>Whether this player still gets a chat line about their own run that the HUD shows: always without
    /// the HUD, when the panel cannot reach them or they hid the timer HUD, otherwise only when they keep that line
    /// in chat (their Chat tab in !hud, defaulting to hud.json).</summary>
    internal bool HudWantsChat(CCSPlayerController? player, bool keep) => keep || !HudShowsCard(player);

    /// <summary>The player's HUD settings, or the server defaults before they load (or without the HUD).</summary>
    internal HudPrefs HudPrefFor(CCSPlayerController player) => HudAsk("prefs",
        () => TimerHud?.PrefsOf(player) ?? HudPrefStore?.Get(player.SteamID) ?? HudPrefs.From(HudSettings),
        HudPrefs.From(HudSettings));

    /// <summary>The 3 split lines per stage or checkpoint, for the runner.</summary>
    internal bool HudSplitsInChat(CCSPlayerController player) =>
        HudAsk("splits chat", () => HudWantsChat(player, HudPrefFor(player).SplitsChat), true);

    /// <summary>"Start speed: 352", for the runner. With the HUD it follows the Chat tab only; without it, !splits
    /// as it always did.</summary>
    internal bool HudStartSpeedInChat(CCSPlayerController player)
    {
        var splitsOn = !(playerTimers.TryGetValue(player.Slot, out var t) && t.HideChatSpeed);
        return HudAsk("start speed chat", () => HudShowsCard(player) ? HudPrefFor(player).SpeedChat : splitsOn, splitsOn);
    }

    /// <summary>The finish, record and points lines, for one player who reads them.</summary>
    internal bool HudFinishInChat(CCSPlayerController player) =>
        HudAsk("finish chat", () => HudWantsChat(player, HudPrefFor(player).FinishChat), true);

    /// <summary>Utils.PrintToChatAll for a finish or record line: the runner sees it on the card, everyone else in
    /// the records feed, so it goes to whoever the HUD does not cover and to whoever keeps finish messages in chat.</summary>
    internal void HudPrintToChatAll(string message, CCSPlayerController? runner = null)
    {
        if (TimerHud == null)
        {
            Utils.PrintToChatAll(message);
            return;
        }

        foreach (var p in Utilities.GetPlayers())
        {
            if (p is not { IsValid: true, IsBot: false }) continue;
            var print = HudAsk("finish line", () =>
            {
                var covered = runner != null && p.Slot == runner.Slot ? HudShowsCard(p) : HudReaches(p);
                return !covered || HudPrefFor(p).FinishChat;
            }, true);
            if (print) p.PrintToChat($" {Localizer["prefix"]} {message}");
        }
    }

    /// <summary>Utils.PrintToSpec for a line the HUD shows: spectators see the runner's toast instead, unless they
    /// keep that line in chat (<paramref name="keep"/> reads their settings).</summary>
    internal void HudPrintToSpec(CCSPlayerController target, string message, Func<HudPrefs, bool> keep)
    {
        if (TimerHud == null)
        {
            Utils.PrintToSpec(target, message);
            return;
        }

        if (target == null || !target.IsValid) return;
        foreach (var spectator in Utilities.GetPlayers())
        {
            if (spectator is not { IsValid: true } || spectator.TeamNum != (byte)CsTeam.Spectator) continue;
            if (HudAsk("spectator line", () => HudShowsCard(spectator) && !keep(HudPrefFor(spectator)), false)) continue;
            if (spectator.Pawn.Value?.ObserverServices?.ObserverTarget.Index == target.Pawn.Index)
                spectator.PrintToChat($"{Localizer["prefix"]} *{target.PlayerName}* - {message}");
        }
    }

    /// <summary>The split lines PrintToSpec copies to spectators: those the HUD reaches see the runner's toast.</summary>
    internal void HudSplitToSpec(CCSPlayerController target, string message) =>
        HudPrintToSpec(target, message, p => p.SplitsChat);

    // ------------------------------------------------------------------ events into the HUD

    private void HudTimerStarted(CCSPlayerController player)
    {
        if (TimerHud == null) return;
        HudSafe("timer start", () =>
        {
            var speed = int.TryParse(GetCurrentPlayerSpeed(player), out var s) ? s : 0;
            var capped = maxStartingSpeedEnabled && speed > maxStartingSpeed;
            TimerHud.TimerStarted(player, capped ? maxStartingSpeed : speed, capped);
            if (!printStartSpeedEnabled) TimerHud.ClearChip(player);
        });
    }

    private void HudFinished(CCSPlayerController player, int ticks, bool bonus)
    {
        if (TimerHud == null || !playerTimers.TryGetValue(player.Slot, out var t)) return;
        HudSafe("finish", () =>
        {
            var pb = bonus
                ? (t.CachedBonusInfo.TryGetValue(t.BonusStage, out var b) ? b.PbTicks : 0)
                : t.CachedPBTicks;
            var (kind, count, _, _) = HudProgress(t);
            if (!bonus && kind == "STAGE" && count > 0) TimerHud.MarkSegment(player, count - 1);
            TimerHud.Finished(player, ticks, pb);
        });

        // With a database the save itself refreshes the map records; the JSON store is read again a moment later.
        if (!enableDb && !bonus && t.currentStyle == 0)
        {
            var mode = HudModeOf(t);
            AddTimer(2f, () => HudSafe("map records", () => HudTopChanged(mode)));
        }
    }

    private void HudCancelled(CCSPlayerController player, string reason)
    {
        if (TimerHud != null && playerTimers.TryGetValue(player.Slot, out var t))
            HudSafe("cancel", () => TimerHud.Cancelled(player, t.TimerTicks, reason));
    }

    private void HudPoints(CCSPlayerController? player, int gained)
    {
        if (TimerHud != null && player != null && IsAllowedPlayer(player))
            HudSafe("points", () => TimerHud.Points(player, gained));
    }

    /// <summary>A stage or checkpoint split, from HandlePlayerStageTimes / HandlePlayerCheckpointTimes.</summary>
    private void HudSplit(CCSPlayerController player, string label, int segment, int ticks, int pbTicks, int srTicks,
        string speed, string pbSpeed, string srSpeed, bool showSr)
    {
        if (TimerHud == null) return;
        HudSafe("split", () => HudSplitCore(player, label, segment, ticks, pbTicks, srTicks, speed, pbSpeed, showSr));
    }

    private void HudSplitCore(CCSPlayerController player, string label, int segment, int ticks, int pbTicks, int srTicks,
        string speed, string pbSpeed, bool showSr)
    {
        var hud = TimerHud!;

        var mark = pbTicks > 0 ? (ticks < pbTicks ? SplitMark.Ahead : SplitMark.Behind) : SplitMark.Done;
        var pbDiff = pbTicks > 0 ? HudLayout.Diff(ticks, pbTicks) : "";
        var srOn = showSr && srTicks > 0 && srTicks != pbTicks;
        var srDiff = srOn ? HudLayout.Diff(ticks, srTicks) : "";

        var speedText = "";
        var speedDiff = "";
        var speedSign = 0;
        if (int.TryParse(speed, out var v) && v >= 100)
        {
            speedText = $"{v} u/s";
            if (int.TryParse(pbSpeed, out var pv) && pv > 0)
            {
                var d = v - pv;
                speedDiff = d >= 0 ? $"+{d}" : d.ToString();
                speedSign = d >= 0 ? -1 : 1;
            }
        }

        hud.Split(player, segment, mark, new HudToast(true, label, HudLayout.Time(ticks),
            pbDiff, ticks < pbTicks ? -1 : 1,
            srDiff, ticks < srTicks ? -1 : 1,
            speedText, speedDiff, speedSign));
    }

    /// <summary>A finish has been saved: the card's result, and one feed line for everyone else.</summary>
    private void HudFinishResult(CCSPlayerController player, string playerName, bool newSr, bool beatPb, int newTicks,
        int oldTicks, int prevSr, string ranking, int timesFinished, int bonus)
    {
        if (TimerHud == null) return;
        HudSafe("finish result", () => HudFinishResultCore(player, playerName, newSr, beatPb, newTicks, oldTicks, prevSr, ranking, timesFinished, bonus));
    }

    private void HudFinishResultCore(CCSPlayerController player, string playerName, bool newSr, bool beatPb, int newTicks,
        int oldTicks, int prevSr, string ranking, int timesFinished, int bonus)
    {
        var hud = TimerHud!;
        var kind = newSr ? ResultKind.Sr : beatPb ? ResultKind.Pb : ResultKind.Done;
        hud.FinishResult(player, kind, newTicks, oldTicks, prevSr, ranking, timesFinished, bonus);

        if (kind == ResultKind.Done && !HudSettings.FeedFinishes) return;
        var reference = newSr && prevSr > 0 ? prevSr : oldTicks;
        var diff = kind != ResultKind.Done && reference > 0 && newTicks < reference ? HudLayout.Diff(newTicks, reference) : "";
        var b = bonus != 0 ? $"B{bonus} " : "";
        var tag = kind switch { ResultKind.Sr => b + "NEW SR", ResultKind.Pb => b + "PB", _ => bonus != 0 ? $"BONUS {bonus}" : "FINISH" };
        hud.Feed(new FeedLine(kind switch { ResultKind.Sr => FeedKind.Sr, ResultKind.Pb => FeedKind.Pb, _ => FeedKind.Finish },
            playerName, tag, HudLayout.Time(newTicks), diff), player.Slot);
    }

    private void HudStageRecord(CCSPlayerController player, string playerName, int stage, int newTicks, int prevSr, bool checkpoint)
    {
        if (TimerHud == null) return;
        HudSafe("stage record", () => HudStageRecordCore(player, playerName, stage, newTicks, prevSr, checkpoint));
    }

    private void HudStageRecordCore(CCSPlayerController player, string playerName, int stage, int newTicks, int prevSr, bool checkpoint)
    {
        var hud = TimerHud!;
        var diff = prevSr > 0 ? HudLayout.Diff(newTicks, prevSr) : "";
        hud.Split(player, -1, SplitMark.Done, new HudToast(true, (checkpoint ? "CP " : "Stage ") + stage,
            Time: HudLayout.Time(newTicks), SrDiff: diff, SrSign: -1,
            Speed: checkpoint ? "NEW CHECKPOINT RECORD" : "NEW STAGE RECORD"));
        hud.Feed(new FeedLine(FeedKind.Stage, playerName, (checkpoint ? "CP SR " : "STAGE SR ") + stage,
            HudLayout.Time(newTicks), diff), player.Slot);
    }

    /// <summary>A reply to a command the HUD shows as the toast. True when the chat line should still be printed.</summary>
    internal bool HudReply(CCSPlayerController? player, string key, string bold, string message, bool error = false)
    {
        if (player == null || TimerHud == null || !TimerHud.Reaches(player)) return true;
        try
        {
            TimerHud.Reply(player, key, bold, message, error);
        }
        catch (Exception e)
        {
            HudError("reply", e);
            return true;
        }

        return HudSettings.RepliesInChat;
    }

    /// <summary>"Style set to: Sideways", as the toast: the style and what it means while the timer runs.</summary>
    private void StyleReply(CCSPlayerController player, int style)
    {
        var note = style switch
        {
            0 => "every key",
            1 => "less gravity",
            2 => "W and S only while the timer runs",
            3 => "W only while the timer runs",
            4 => "speed capped at 400",
            5 => "more gravity",
            6 => "A only while the timer runs",
            7 => "D only while the timer runs",
            8 => "S only while the timer runs",
            9 => "two keys at once while the timer runs",
            10 => "faster in the air",
            11 => "hold E to glide",
            12 => "tool assisted",
            _ => "",
        };
        if (HudReply(player, "STYLE", GetNamedStyle(style), note.Length > 0 ? "\u00b7 " + note : ""))
            Utils.PrintToChat(player, Localizer["style_set", GetNamedStyle(style)]);
    }

    private bool HudOpenMenu(CCSPlayerController player, int tab = HudMenu.TabLayout)
    {
        try
        {
            return HudMenuPanel!.Open(player, tab);
        }
        catch (Exception e)
        {
            HudError("menu", e);
            return false;
        }
    }

    /// <summary>The flags SharpTimer already keeps in PlayerStats, set from the menu.</summary>
    internal void HudSetFlag(CCSPlayerController player, bool? hideTimerHud = null, bool? hideKeys = null, bool? hideSplits = null)
    {
        if (!playerTimers.TryGetValue(player.Slot, out var t)) return;
        if (hideTimerHud is { } h) t.HideTimerHud = h;
        if (hideKeys is { } k) t.HideKeys = k;
        if (hideSplits is { } s) t.HideChatSpeed = s;

        HudFlagsChanged(player);
        if (enableDb)
        {
            var steamId = player.SteamID.ToString();
            var name = player.PlayerName;
            var slot = player.Slot;
            var valid = IsAllowedPlayer(player);
            _ = Task.Run(async () => await SetPlayerStats(player, steamId, name, slot, valid));
        }
    }

    /// <summary>!hud on|off, !keys or !splits changed a flag: the HUD and an open menu follow.</summary>
    internal void HudFlagsChanged(CCSPlayerController player) => HudSafe("flags", () =>
    {
        TimerHud?.PrefsChanged(player);
        HudMenuPanel?.Refresh(player);
    });

    // ------------------------------------------------------------------ what the HUD reads from the timer

    internal CCSPlayerController? HudSpecTarget(CCSPlayerController spectator)
    {
        var index = spectator.Pawn.Value?.ObserverServices?.ObserverTarget.Index;
        if (index == null) return null;
        return specTargets.TryGetValue(index.Value, out var target) && target is { IsValid: true } ? target : null;
    }

    internal int HudReplayFrame(int slot) => playerReplays.TryGetValue(slot, out var r) ? Math.Max(0, r.CurrentPlaybackFrame) : 0;

    internal int HudReplayLength(int slot) => playerReplays.TryGetValue(slot, out var r) ? r.replayFrames.Count : 0;

    internal PlayerButtons HudReplayButtons(int slot)
    {
        if (!playerReplays.TryGetValue(slot, out var r) || r.replayFrames.Count == 0) return 0;
        var i = Math.Clamp(r.CurrentPlaybackFrame - 1, 0, r.replayFrames.Count - 1);
        return r.replayFrames[i].Buttons ?? 0;
    }

    internal string HudReplayKind(int slot) =>
        playerTimers.TryGetValue(slot, out var t) && !string.IsNullOrEmpty(t.HudReplayKind) ? t.HudReplayKind : "sr";

    /// <summary>ReplayHUDString is "name | 1:19.902".</summary>
    internal (string Name, string Time) HudReplayHolder(int slot)
    {
        var text = playerTimers.TryGetValue(slot, out var t) ? t.ReplayHUDString ?? "" : "";
        var bar = text.LastIndexOf('|');
        return bar < 0 ? ("", text.Trim()) : (text[..bar].Trim(), text[(bar + 1)..].Trim());
    }

    internal string HudReplayTime(int slot) => HudReplayHolder(slot).Time;

    internal PlayerRecord? HudServerRecord(CCSPlayerController player)
    {
        if (!playerTimers.TryGetValue(player.Slot, out var t)) return null;
        var top = HudMapTop(HudModeOf(t));
        return top is { Count: > 0 } ? top[0] : null;
    }

    // ------------------------------------------------------------------ the map's top times (map records)
    // SharpTimer's per-mode record caches (SortedCached<Mode>Records) are declared but were never filled again
    // after upstream fdca8d1 moved to on-demand queries, so the live place always said #1. The HUD fills them:
    // the current map's top 100 on the main course, normal style, with the same query !top uses
    // (GetSortedRecordsFromDatabase). One mode at a time, the first time something asks for it, again after a new
    // PB or first time in it, and from scratch on a map start. Never per tick: an ask while loaded is a lookup.
    // Main thread only.

    private const int HudTopLimit = 100;
    private readonly Dictionary<string, List<PlayerRecord>> _hudTop = new();
    private readonly HashSet<string> _hudTopLoading = new();
    private readonly HashSet<string> _hudTopStale = new();
    private readonly Dictionary<string, float> _hudTopRetryAt = new();
    private int _hudTopGen;

    internal string HudModeOf(PlayerTimerInfo? t) => string.IsNullOrEmpty(t?.Mode) ? HudDefaultModeName : t.Mode;

    /// <summary>The cached top of this map in <paramref name="mode"/>, fastest first. Null while it loads (the
    /// first ask starts the load).</summary>
    internal IReadOnlyList<PlayerRecord>? HudMapTop(string mode)
    {
        if (_hudTop.TryGetValue(mode, out var list)) return list;
        if (!_hudTopRetryAt.TryGetValue(mode, out var at) || Server.CurrentTime >= at) HudLoadTop(mode);
        return null;
    }

    /// <summary>A new time on the main course in <paramref name="mode"/> was saved: load the top again. The old
    /// list stays up until the new one is in.</summary>
    internal void HudTopChanged(string mode)
    {
        // Nobody asked for this mode yet: the first ask loads it fresh.
        if (!_hudTop.ContainsKey(mode) && !_hudTopLoading.Contains(mode)) return;
        _hudTopStale.Add(mode);
        HudLoadTop(mode);
    }

    private void HudLoadTop(string mode)
    {
        // One query per mode at a time; a time saved meanwhile marks it stale and it loads again after.
        if (!_hudTopLoading.Add(mode)) return;
        _hudTopStale.Remove(mode);

        var gen = _hudTopGen;
        var map = Server.MapName;
        var useDb = enableDb;
        var jsonMode = mode == HudDefaultModeName;
        Task.Run(async () =>
        {
            Dictionary<int, PlayerRecord>? records = null;
            Exception? error = null;
            try
            {
                if (useDb)
                    records = await GetSortedRecordsFromDatabase(HudTopLimit, 0, map, 0, mode);
                else if (jsonMode)
                    records = (await Utils.GetSortedRecords(0, $"{map}.json"))
                        .Select((kv, i) => (i, new PlayerRecord { SteamID = kv.Key, PlayerName = kv.Value.PlayerName, TimerTicks = kv.Value.TimerTicks }))
                        .ToDictionary(x => x.i, x => x.Item2);
                else
                    records = []; // the JSON store has no modes
            }
            catch (Exception e)
            {
                error = e;
            }

            Server.NextFrame(() =>
            {
                _hudTopLoading.Remove(mode);
                if (gen != _hudTopGen) return; // the map changed meanwhile
                if (records == null)
                {
                    if (error != null) HudError("map records", error);
                    _hudTopRetryAt[mode] = Server.CurrentTime + 30f;
                    return;
                }

                var list = records.Values.Where(r => r.TimerTicks > 0).OrderBy(r => r.TimerTicks).ToList();
                _hudTop[mode] = list;
                var sorted = list.Select((r, i) => (i, r)).ToDictionary(x => x.i, x => x.r);
                switch (mode)
                {
                    case "Standard": SortedCachedStandardRecords = sorted; break;
                    case "85t": SortedCached85tRecords = sorted; break;
                    case "102t": SortedCached102tRecords = sorted; break;
                    case "128t": SortedCached128tRecords = sorted; break;
                    case "Source": SortedCachedSourceRecords = sorted; break;
                    case "Bhop": SortedCachedBhopRecords = sorted; break;
                    case "Custom": SortedCachedCustomRecords = sorted; break;
                }

                if (_hudTopStale.Contains(mode)) HudLoadTop(mode);
            });
        });
    }

    /// <summary>Map start: every cached top belonged to the last map.</summary>
    private void HudTopReset()
    {
        _hudTopGen++;
        _hudTop.Clear();
        _hudTopStale.Clear();
        _hudTopRetryAt.Clear();
        SortedCachedStandardRecords = [];
        SortedCached85tRecords = [];
        SortedCached102tRecords = [];
        SortedCached128tRecords = [];
        SortedCachedSourceRecords = [];
        SortedCachedBhopRecords = [];
        SortedCachedCustomRecords = [];
    }

    /// <summary>The world record from the global API cache. This fork forces the API key blank, so this is null
    /// until a key exists.</summary>
    internal (string Time, string Name)? HudWorldRecord(string mode)
    {
        if (string.IsNullOrEmpty(apiKey)) return null;
        var cache = mode switch
        {
            "85t" => recordCache.Cached85tWorldRecords,
            "102t" => recordCache.Cached102tWorldRecords,
            "128t" => recordCache.Cached128tWorldRecords,
            "Source" => recordCache.CachedSourceWorldRecords,
            "Bhop" => recordCache.CachedBhopWorldRecords,
            _ => recordCache.CachedStandardWorldRecords,
        };
        var wr = cache?.Values.FirstOrDefault();
        return wr == null ? null : (Utils.FormatDecimalTime(wr.time), wr.player_name);
    }

    internal string HudDefaultModeName => GetModeName(defaultMode);

    /// <summary>"surf_kitsune · T1 · Staged".</summary>
    internal string HudMapLine()
    {
        var parts = new List<string> { currentMapName ?? Server.MapName };
        if (MapTierHudEnabled && currentMapTier != null) parts.Add($"T{currentMapTier}");
        if (MapTypeHudEnabled && !string.IsNullOrEmpty(currentMapType)) parts.Add(currentMapType);
        return string.Join(" · ", parts);
    }

    /// <summary>Stage or checkpoint progress: the label, how many, the 0-based segment the runner is on, and the
    /// number the card shows.</summary>
    internal (string? Kind, int Count, int Current, int Shown) HudProgress(PlayerTimerInfo t)
    {
        if (useStageTriggers && stageTriggerCount > 0)
            return ("STAGE", stageTriggerCount, Math.Max(0, t.CurrentMapStage - 1), Math.Max(1, t.CurrentMapStage));
        if (useCheckpointTriggers && cpTriggerCount > 0)
            return ("CP", cpTriggerCount, t.CurrentMapCheckpoint, t.CurrentMapCheckpoint);
        return (null, 0, 0, 0);
    }

    /// <summary>The rank name, its colour class and its icon class (null when the icon is not in the addon).</summary>
    internal (string Name, string Color, string? Icon) HudRank(PlayerTimerInfo t)
    {
        var title = t.CachedRank ?? "";
        if (title.Length == 0) return ("", "rc-default", null);

        var color = UnrankedColor;
        if (!title.Contains(UnrankedTitle))
        {
            var rank = rankDataList.FirstOrDefault(r => !string.IsNullOrEmpty(r.Title) && title.Contains(r.Title));
            if (rank != null) color = rank.Color;
        }

        return (HudLayout.RankName(title), HudLayout.RankColorClass(color), HudLayout.RankIconClass(t.RankHUDIcon));
    }
}
