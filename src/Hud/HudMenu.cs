using CounterStrikeSharp.API.Core;
using PanoramaManager;

namespace SharpTimer.Hud;

/// <summary>
/// !hud (also !settings and !hudsettings): the settings panel for the whole timer HUD
/// (hud/panorama/layout/custom_game/sharptimer_menu.xml), clickable, in the /rank Settings tab's look. Five tabs:
/// <list type="bullet">
/// <item>Layout: a mini map of the screen with CS2's own HUD in grey. Pick a part (timer card, speed, keys, splits,
/// records feed, map records), click a spot; S / M / L per part; next to the card (speed) and with the card
/// (splits).</item>
/// <item>Show / Hide: the timer HUD (!hud on|off), the card, PB and SR, rank, the split toast (!splits), speed,
/// the speed bar, sync, keys (!keys) and the records feed; then the map records: off, always, on map join or only
/// in !hud.</item>
/// <item>Style: accent, full or compact card, background, text size, timer decimals.</item>
/// <item>Chat: splits (off / HUD / HUD + chat), keys, start speed in chat, finish messages in chat.</item>
/// <item>Reset: the layout, or everything.</item>
/// </list>
/// Every click applies at once on the HUD behind the menu (which shows a sample toast and feed line while it is
/// open) and is saved per player. Two parts never share a spot: putting one where another sits swaps them.
/// </summary>
internal sealed class HudMenu : IDisposable
{
    private const int Tabs = 5;
    public const int TabLayout = 0, TabShow = 1, TabStyle = 2, TabChat = 3, TabReset = 4;
    private static readonly string[] TabSteps = ["at-0", "at-1", "at-2", "at-3", "at-4"];

    private const int PartCard = 0, PartSpeed = 1, PartKeys = 2, PartToast = 3, PartFeed = 4, PartRecords = 5;
    private static readonly string[] PartIds = ["card", "sp", "ks", "ts", "fd", "mr"];

    /// <summary>Where a part that has to make room goes when the spot it came from is not a spot (next to / with the card).</summary>
    private static readonly string[] Fallback = ["bc", "xu", "rw", "tc", "tr", "br", "tl", "ml", "mr", "xa"];

    /// <summary>The show / hide rows, by the id the layout gives them (vo_&lt;key&gt;_on|off, vr_&lt;key&gt;).</summary>
    private static readonly string[] ShowKeys = ["hud", "card", "cmp", "rank", "toast", "speed", "bar", "sync", "keys", "feed"];

    private sealed class Viewer
    {
        public int Tab;
        public int Part;
        public string Message = "";
        public readonly PanelWriter W = new();
    }

    private readonly SharpTimer _st;
    private readonly TimerHud _hud;
    private readonly Dictionary<int, Viewer> _viewers = new();
    private PanelHandle? _panel;

    public HudMenu(SharpTimer st, TimerHud hud)
    {
        _st = st;
        _hud = hud;
    }

    public void Init()
    {
        if (_panel != null) return;
        try
        {
            _panel = Panorama.Spawn(HudLayout.MenuPath, new LayoutContract
            {
                RootPanelId = HudLayout.MenuRoot,
                RevealClass = "show",
                CloseButtonId = "sm_close",
                RowCount = 0,
                CaptureInput = true,
                HideHud = HideHudFlags.None,
                SharedText = false,
            });
            _panel.OnEvent += OnEvent;
        }
        catch (Exception e)
        {
            _st.Utils.LogError($"[HUD] the !hud settings failed to start: {e}");
            _panel = null;
        }
    }

    public void Dispose()
    {
        _panel?.Dispose();
        _panel = null;
        _viewers.Clear();
    }

    public bool IsOpenFor(CCSPlayerController player) => _panel != null && _panel.IsOpenFor(player);

    /// <summary>Opens the settings. False when they could not be shown, so the caller falls back to the old toggle.</summary>
    public bool Open(CCSPlayerController player, int tab = TabLayout)
    {
        if (_panel == null || player is not { IsValid: true, IsBot: false }) return false;

        _panel.Open(player);
        if (!_panel.IsOpenFor(player)) return false;

        var v = _viewers[player.Slot] = new Viewer { Tab = Math.Clamp(tab, 0, Tabs - 1) };
        if (!_hud.PrefsOf(player).Seen) Change(player, p => p.Seen = true);
        Draw(player, v);
        _hud.SetPreview(player, true);
        return true;
    }

    public void Forget(int slot)
    {
        _panel?.Close(slot);
        _viewers.Remove(slot);
    }

    /// <summary>Redraws an open menu after !keys, !splits or !hud on|off changed something.</summary>
    public void Refresh(CCSPlayerController player)
    {
        if (_viewers.TryGetValue(player.Slot, out var v) && IsOpenFor(player)) Draw(player, v);
    }

    // ------------------------------------------------------------------ drawing

    private void Draw(CCSPlayerController player, Viewer v)
    {
        var panel = _panel!;
        var w = v.W;
        var prefs = _hud.PrefsOf(player);
        var timer = _st.playerTimers.TryGetValue(player.Slot, out var t) ? t : null;
        var hudOff = timer?.HideTimerHud == true;
        var keysOff = timer?.HideKeys == true;
        var splitsOff = timer?.HideChatSpeed == true;

        w.Var(panel, player, "sm_tag", (_st.currentMapName ?? "").ToUpperInvariant());
        for (var i = 0; i < Tabs; i++)
        {
            w.Cls(panel, player, $"mg_{i}", "active", i == v.Tab);
            w.Cls(panel, player, $"mp_{i}", "active", i == v.Tab);
        }

        w.OneOf(panel, player, "sm_bar", TabSteps[v.Tab], TabSteps);

        DrawLayout(player, v, prefs, hudOff, keysOff, splitsOff);
        DrawShow(player, v, prefs, hudOff, keysOff, splitsOff);
        DrawStyle(player, v, prefs);
        DrawChat(player, v, prefs, keysOff, splitsOff);
        w.Var(panel, player, "rs_msg", v.Message);
    }

    private void DrawLayout(CCSPlayerController player, Viewer v, HudPrefs prefs, bool hudOff, bool keysOff, bool splitsOff)
    {
        var panel = _panel!;
        var w = v.W;

        // the mini map: where every part sits, the one being moved in blue, hidden ones faint
        w.OneOf(panel, player, "mb_card", "p-" + prefs.Card, HudLayout.PosClasses);
        Size(player, w, "mb_card", prefs.CardSize);
        w.Cls(panel, player, "mb_card", "cc", prefs.Compact);
        w.Cls(panel, player, "mb_card", "off", hudOff || !prefs.CardOn);

        w.OneOf(panel, player, "mb_sp", HudLayout.SpeedClass(prefs), HudLayout.SpeedClasses);
        Size(player, w, "mb_sp", prefs.SpeedSize);
        w.Cls(panel, player, "mb_sp", "off", hudOff || !prefs.SpeedOn);

        w.OneOf(panel, player, "mb_ks", "p-" + prefs.Keys, HudLayout.PosClasses);
        Size(player, w, "mb_ks", prefs.KeysSize);
        w.Cls(panel, player, "mb_ks", "off", keysOff);

        w.OneOf(panel, player, "mb_ts", prefs.Toast == HudLayout.WithCard
            ? $"{(prefs.Compact ? "wcc" : "wc")}-{prefs.Card}-{prefs.CardSize}"
            : "p-" + prefs.Toast, HudLayout.MapToastClasses);
        Size(player, w, "mb_ts", prefs.Toast == HudLayout.WithCard ? prefs.CardSize : prefs.ToastSize);
        w.Cls(panel, player, "mb_ts", "off", splitsOff);

        w.OneOf(panel, player, "mb_fd", "p-" + prefs.Feed, HudLayout.PosClasses);
        Size(player, w, "mb_fd", prefs.FeedSize);
        w.Cls(panel, player, "mb_fd", "off", !prefs.FeedOn);

        w.OneOf(panel, player, "mb_mr", "p-" + prefs.Records, HudLayout.PosClasses);
        Size(player, w, "mb_mr", prefs.RecordsSize);
        w.Cls(panel, player, "mb_mr", "off", !prefs.RecordsOn);

        bool[] hidden = [hudOff || !prefs.CardOn, hudOff || !prefs.SpeedOn, keysOff, splitsOff, !prefs.FeedOn, !prefs.RecordsOn];
        for (var i = 0; i < PartIds.Length; i++)
        {
            var id = PartIds[i];
            w.Cls(panel, player, "mb_" + id, "sel", i == v.Part);
            w.Cls(panel, player, "lp_" + id, "on", i == v.Part);
            w.Cls(panel, player, "lp_" + id, "off", hidden[i]);
            w.Var(panel, player, $"lp_{id}_w", hidden[i] ? "Hidden" : HudLayout.Label(Where(prefs, i)));
        }

        var where = Where(prefs, v.Part);
        foreach (var pos in HudLayout.Positions)
            w.Cls(panel, player, "mz_" + pos, "cur", pos == where);
        w.Var(panel, player, "sm_where", HudLayout.Label(where));

        // next to the card (speed) / with the card (splits)
        w.Cls(panel, player, "mz_with", "hidden", v.Part is not (PartSpeed or PartToast));
        w.Var(panel, player, "sm_with", v.Part == PartToast ? "WITH THE CARD" : "NEXT TO THE CARD");
        w.Cls(panel, player, "mz_with", "on", (v.Part == PartSpeed && prefs.Speed == HudLayout.Beside)
                                              || (v.Part == PartToast && prefs.Toast == HudLayout.WithCard));

        // size: a toast with the card takes the card's size
        var size = v.Part switch
        {
            PartCard => prefs.CardSize,
            PartSpeed => prefs.SpeedSize,
            PartKeys => prefs.KeysSize,
            PartToast => prefs.Toast == HudLayout.WithCard ? prefs.CardSize : prefs.ToastSize,
            PartRecords => prefs.RecordsSize,
            _ => prefs.FeedSize,
        };
        foreach (var s in HudLayout.Sizes) w.Cls(panel, player, "ms_" + s, "on", s == size);
        w.Cls(panel, player, "ms_opts", "disabled", v.Part == PartToast && prefs.Toast == HudLayout.WithCard);

        w.Cls(panel, player, "lo_kl", "hidden", v.Part != PartKeys);
        w.Cls(panel, player, "mo_kl_grid", "on", !prefs.KeysRow);
        w.Cls(panel, player, "mo_kl_row", "on", prefs.KeysRow);
    }

    private void DrawShow(CCSPlayerController player, Viewer v, HudPrefs p, bool hudOff, bool keysOff, bool splitsOff)
    {
        var panel = _panel!;
        var w = v.W;
        foreach (var key in ShowKeys)
        {
            var on = key switch
            {
                "hud" => !hudOff,
                "card" => p.CardOn,
                "cmp" => p.Compare,
                "rank" => p.Rank,
                "toast" => !splitsOff,
                "speed" => p.SpeedOn,
                "bar" => p.SpeedBar,
                "sync" => p.Sync,
                "keys" => !keysOff,
                _ => p.FeedOn,
            };
            w.Cls(panel, player, $"vo_{key}_on", "on", on);
            w.Cls(panel, player, $"vo_{key}_off", "on", !on);

            // a row whose part a switch above it already hides: still clickable, drawn faint
            var muted = key switch
            {
                "card" or "speed" or "toast" => hudOff,
                "cmp" or "rank" => hudOff || !p.CardOn,
                "bar" or "sync" => hudOff || !p.SpeedOn,
                _ => false,
            };
            w.Cls(panel, player, "vr_" + key, "disabled", muted);
        }

        // map records: off, or when they show
        w.Cls(panel, player, "vo_mr_off", "on", !p.RecordsOn);
        foreach (var when in HudLayout.RecordsShows)
            w.Cls(panel, player, "vo_mr_" + when, "on", p.RecordsOn && p.RecordsShow == when);
    }

    private void DrawStyle(CCSPlayerController player, Viewer v, HudPrefs p)
    {
        var panel = _panel!;
        var w = v.W;
        foreach (var a in HudLayout.Accents) w.Cls(panel, player, "ss_ac_" + a, "on", p.Accent == a);
        w.Cls(panel, player, "ss_cc_full", "on", !p.Compact);
        w.Cls(panel, player, "ss_cc_compact", "on", p.Compact);
        foreach (var o in HudLayout.Opacities) w.Cls(panel, player, "ss_op_" + o, "on", p.Opacity == o);
        foreach (var s in HudLayout.Sizes) w.Cls(panel, player, "ss_tx_" + s, "on", p.Text == s);
        for (var d = 1; d <= 3; d++) w.Cls(panel, player, $"ss_td_{d}", "on", p.Decimals == d);
    }

    private void DrawChat(CCSPlayerController player, Viewer v, HudPrefs p, bool keysOff, bool splitsOff)
    {
        var panel = _panel!;
        var w = v.W;
        w.Cls(panel, player, "ch_spl_off", "on", splitsOff);
        w.Cls(panel, player, "ch_spl_hud", "on", !splitsOff && !p.SplitsChat);
        w.Cls(panel, player, "ch_spl_chat", "on", !splitsOff && p.SplitsChat);
        w.Cls(panel, player, "ch_keys_on", "on", !keysOff);
        w.Cls(panel, player, "ch_keys_off", "on", keysOff);
        w.Cls(panel, player, "ch_ss_on", "on", p.SpeedChat);
        w.Cls(panel, player, "ch_ss_off", "on", !p.SpeedChat);
        w.Cls(panel, player, "ch_fin_on", "on", p.FinishChat);
        w.Cls(panel, player, "ch_fin_off", "on", !p.FinishChat);
    }

    private void Size(CCSPlayerController player, PanelWriter w, string id, string size) =>
        w.OneOf(_panel!, player, id, "sz-" + size, HudLayout.SizeClasses);

    private static string Where(HudPrefs p, int part) => part switch
    {
        PartCard => p.Card,
        PartSpeed => p.Speed,
        PartKeys => p.Keys,
        PartToast => p.Toast,
        PartRecords => p.Records,
        _ => p.Feed,
    };

    private static void Set(HudPrefs p, int part, string pos)
    {
        switch (part)
        {
            case PartCard: p.Card = pos; break;
            case PartSpeed: p.Speed = pos; break;
            case PartKeys: p.Keys = pos; break;
            case PartToast: p.Toast = pos; break;
            case PartRecords: p.Records = pos; break;
            default: p.Feed = pos; break;
        }
    }

    // ------------------------------------------------------------------ input

    private void OnEvent(PanelEvent e)
    {
        if (e.Player is not { IsValid: true } player) return;

        switch (e.Action)
        {
            case PanelAction.Close:
                _viewers.Remove(player.Slot);
                _hud.SetPreview(player, false);
                return;

            case PanelAction.Restored:
                if (!_viewers.TryGetValue(player.Slot, out var restored))
                {
                    _panel?.Close(player);
                    return;
                }

                restored.W.Reset();
                Draw(player, restored);
                return;

            case PanelAction.Button:
                if (_viewers.TryGetValue(player.Slot, out var v)) Click(player, v, e.ElementId);
                return;
        }
    }

    private void Click(CCSPlayerController player, Viewer v, string id)
    {
        if (id.StartsWith("mg_") && int.TryParse(id.AsSpan(3), out var tab) && tab is >= 0 and < Tabs)
        {
            v.Tab = tab;
            if (tab != TabReset) v.Message = "";
            Draw(player, v);
            return;
        }

        if (id.StartsWith("lp_") && Array.IndexOf(PartIds, id[3..]) is var part and >= 0)
        {
            v.Part = part;
            Draw(player, v);
            return;
        }

        if (id.StartsWith("vo_mr_"))
        {
            var when = id[6..];
            if (when == "off") Change(player, p => p.RecordsOn = false);
            else if (HudLayout.RecordsShow(when) is { } w)
                Change(player, p =>
                {
                    p.RecordsOn = true;
                    p.RecordsShow = w;
                });
            else return;

            Draw(player, v);
            return;
        }

        if (id.StartsWith("vo_") && (id.EndsWith("_on") || id.EndsWith("_off")))
        {
            var on = id.EndsWith("_on");
            var key = id[3..id.LastIndexOf('_')];
            switch (key)
            {
                case "hud": _st.HudSetFlag(player, hideTimerHud: !on); break;
                case "keys": _st.HudSetFlag(player, hideKeys: !on); break;
                case "toast": _st.HudSetFlag(player, hideSplits: !on); break;
                case "card": Change(player, p => p.CardOn = on); break;
                case "cmp": Change(player, p => p.Compare = on); break;
                case "rank": Change(player, p => p.Rank = on); break;
                case "speed": Change(player, p => p.SpeedOn = on); break;
                case "bar": Change(player, p => p.SpeedBar = on); break;
                case "sync": Change(player, p => p.Sync = on); break;
                case "feed": Change(player, p => p.FeedOn = on); break;
                default: return;
            }

            Draw(player, v);
            return;
        }

        if (id.StartsWith("ss_"))
        {
            var value = id[(id.LastIndexOf('_') + 1)..];
            if (id.StartsWith("ss_ac_") && HudLayout.IsAccent(value)) Change(player, p => p.Accent = value);
            else if (id == "ss_cc_full") Change(player, p => p.Compact = false);
            else if (id == "ss_cc_compact") Change(player, p => p.Compact = true);
            else if (id.StartsWith("ss_op_") && HudLayout.IsOpacity(value)) Change(player, p => p.Opacity = value);
            else if (id.StartsWith("ss_tx_") && HudLayout.IsSize(value)) Change(player, p => p.Text = value);
            else if (id.StartsWith("ss_td_") && HudLayout.IsDecimals(value)) Change(player, p => p.Decimals = value[0] - '0');
            else return;
            Draw(player, v);
            return;
        }

        switch (id)
        {
            case "ch_spl_off": _st.HudSetFlag(player, hideSplits: true); break;
            case "ch_spl_hud":
                Change(player, p => p.SplitsChat = false);
                _st.HudSetFlag(player, hideSplits: false);
                break;
            case "ch_spl_chat":
                Change(player, p => p.SplitsChat = true);
                _st.HudSetFlag(player, hideSplits: false);
                break;
            case "ch_keys_on": _st.HudSetFlag(player, hideKeys: false); break;
            case "ch_keys_off": _st.HudSetFlag(player, hideKeys: true); break;
            case "ch_ss_on": Change(player, p => p.SpeedChat = true); break;
            case "ch_ss_off": Change(player, p => p.SpeedChat = false); break;
            case "ch_fin_on": Change(player, p => p.FinishChat = true); break;
            case "ch_fin_off": Change(player, p => p.FinishChat = false); break;

            case "rs_layout":
                Change(player, p => p.ResetLayout(_st.HudPrefStore.Defaults));
                v.Message = "Done: every part is back at its default spot and size.";
                _hud.Reply(player, "SETTINGS", "Layout reset", "· every part is back in its spot");
                break;
            case "rs_all":
                Change(player, p =>
                {
                    var fresh = _st.HudPrefStore.Defaults;
                    var seen = p.Seen;
                    foreach (var f in typeof(HudPrefs).GetFields()) f.SetValue(p, f.GetValue(fresh));
                    p.Seen = seen;
                });
                if (_st.playerTimers.TryGetValue(player.Slot, out var timer) && (timer.HideTimerHud || timer.HideKeys || timer.HideChatSpeed))
                    _st.HudSetFlag(player, hideTimerHud: false, hideKeys: false, hideSplits: false);
                v.Message = "Done: everything is back to the server defaults.";
                _hud.Reply(player, "SETTINGS", "Reset", "· everything is back to the defaults");
                break;

            case "mz_with":
                if (v.Part == PartSpeed) Change(player, p => p.Speed = HudLayout.Beside);
                else if (v.Part == PartToast) Change(player, p => p.Toast = HudLayout.WithCard);
                break;
            case "mo_kl_grid": Change(player, p => p.KeysRow = false); break;
            case "mo_kl_row": Change(player, p => p.KeysRow = true); break;

            default:
                if (id.StartsWith("ms_") && HudLayout.IsSize(id[3..]))
                {
                    var size = id[3..];
                    if (v.Part == PartToast && _hud.PrefsOf(player).Toast == HudLayout.WithCard) break;
                    Change(player, p =>
                    {
                        switch (v.Part)
                        {
                            case PartCard: p.CardSize = size; break;
                            case PartSpeed: p.SpeedSize = size; break;
                            case PartKeys: p.KeysSize = size; break;
                            case PartToast: p.ToastSize = size; break;
                            case PartRecords: p.RecordsSize = size; break;
                            default: p.FeedSize = size; break;
                        }
                    });
                }
                else if (id.StartsWith("mz_") && HudLayout.IsPosition(id[3..]))
                {
                    var pos = id[3..];
                    Change(player, p => Place(p, v.Part, pos));
                }
                else
                {
                    return;
                }

                break;
        }

        Draw(player, v);
    }

    private void Change(CCSPlayerController player, Action<HudPrefs> change)
    {
        var prefs = _st.HudPrefStore.Update(player.SteamID, change);
        _hud.PrefsChanged(player, prefs);
    }

    /// <summary>Puts one part on a spot; the part already there takes the moved part's old spot.</summary>
    private static void Place(HudPrefs p, int part, string pos)
    {
        var old = Where(p, part);
        if (old == pos) return;

        Set(p, part, pos);

        for (var other = 0; other < PartIds.Length; other++)
        {
            if (other == part || Where(p, other) != pos) continue;
            var to = HudLayout.IsPosition(old)
                ? old
                : Fallback.FirstOrDefault(f => Enumerable.Range(0, PartIds.Length).All(g => Where(p, g) != f)) ?? old;
            if (HudLayout.IsPosition(to)) Set(p, other, to);
        }
    }
}
