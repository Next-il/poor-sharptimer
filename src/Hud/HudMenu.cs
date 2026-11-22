using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using PanoramaManager;

namespace SharpTimer.Hud;

/// <summary>
/// !hud: where each part of the timer HUD goes (hud/panorama/layout/custom_game/sharptimer_menu.xml). A tab per
/// part, a mini map of the screen with CS2's own HUD in grey and nine spots to click, sizes and on/off switches.
/// The HUD behind the menu moves as the player clicks, with a sample toast and feed line so every part shows.
///
/// <para>Two parts never share a spot: putting one where another sits swaps them.</para>
/// </summary>
internal sealed class HudMenu : IDisposable
{
    private const int Tabs = 4;
    private const int TabCard = 0, TabCluster = 1, TabToast = 2, TabFeed = 3;

    private static readonly string[] TabSteps = ["at-0", "at-1", "at-2", "at-3"];

    /// <summary>Where a part that has to make room goes when the spot it came from is not a spot (next to / with the card).</summary>
    private static readonly string[] Fallback = ["bc", "xu", "tc", "tr", "br", "tl", "ml", "mr", "xa"];

    private sealed class Viewer
    {
        public int Tab;
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
            _st.Utils.LogError($"[HUD] the !hud menu failed to start: {e.Message}");
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

    /// <summary>Opens the menu. False when it could not be shown, so the caller falls back to the old toggle.</summary>
    public bool Open(CCSPlayerController player, int tab = TabCard)
    {
        if (_panel == null || player is not { IsValid: true, IsBot: false }) return false;

        _panel.Open(player);
        if (!_panel.IsOpenFor(player)) return false;

        var v = _viewers[player.Slot] = new Viewer { Tab = Math.Clamp(tab, 0, Tabs - 1) };
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

        w.Var(panel, player, "sm_tag", (_st.currentMapName ?? "").ToUpperInvariant());

        for (var i = 0; i < Tabs; i++)
        {
            w.Cls(panel, player, $"mg_{i}", "active", i == v.Tab);
            w.Cls(panel, player, $"mp_{i}", "active", i == v.Tab);
        }

        w.OneOf(panel, player, "sm_bar", TabSteps[v.Tab], TabSteps);

        // the mini map: where every part sits, the selected one in blue
        var hudOff = timer?.HideTimerHud == true;
        var keysOff = timer?.HideKeys == true;
        w.OneOf(panel, player, "mb_card", "p-" + prefs.Card, HudLayout.PosClasses);
        Size(player, w, "mb_card", prefs.CardSize);
        w.Cls(panel, player, "mb_card", "off", hudOff);

        w.OneOf(panel, player, "mb_cl", prefs.Cluster == HudLayout.Beside
            ? $"nx-{prefs.Card}-{prefs.CardSize}-{prefs.ClusterSize}"
            : "p-" + prefs.Cluster, HudLayout.ClusterClasses);
        Size(player, w, "mb_cl", prefs.ClusterSize);
        w.Cls(panel, player, "mb_cl", "off", (hudOff || !prefs.Speed) && keysOff);

        w.OneOf(panel, player, "mb_ts", prefs.Toast == HudLayout.WithCard
            ? $"wc-{prefs.Card}-{prefs.CardSize}"
            : "p-" + prefs.Toast, HudLayout.MapToastClasses);
        Size(player, w, "mb_ts", prefs.Toast == HudLayout.WithCard ? prefs.CardSize : prefs.ToastSize);
        w.Cls(panel, player, "mb_ts", "off", timer?.HideChatSpeed == true);

        w.OneOf(panel, player, "mb_fd", "p-" + prefs.Feed, HudLayout.PosClasses);
        Size(player, w, "mb_fd", prefs.FeedSize);
        w.Cls(panel, player, "mb_fd", "off", !prefs.FeedOn);

        w.Cls(panel, player, "mb_card", "sel", v.Tab == TabCard);
        w.Cls(panel, player, "mb_cl", "sel", v.Tab == TabCluster);
        w.Cls(panel, player, "mb_ts", "sel", v.Tab == TabToast);
        w.Cls(panel, player, "mb_fd", "sel", v.Tab == TabFeed);

        var where = Where(prefs, v.Tab);
        foreach (var pos in HudLayout.Positions)
            w.Cls(panel, player, "mz_" + pos, "cur", pos == where);
        w.Var(panel, player, "sm_where", HudLayout.Label(where));

        // next to the card (speed and keys) / with the card (splits)
        var withTab = v.Tab is TabCluster or TabToast;
        w.Cls(panel, player, "mz_with", "hidden", !withTab);
        w.Var(panel, player, "sm_with", v.Tab == TabToast ? "WITH THE CARD" : "NEXT TO THE CARD");
        w.Cls(panel, player, "mz_with", "on", (v.Tab == TabCluster && prefs.Cluster == HudLayout.Beside)
                                              || (v.Tab == TabToast && prefs.Toast == HudLayout.WithCard));

        // size: a toast with the card takes the card's size
        var size = v.Tab switch
        {
            TabCard => prefs.CardSize,
            TabCluster => prefs.ClusterSize,
            TabToast => prefs.Toast == HudLayout.WithCard ? prefs.CardSize : prefs.ToastSize,
            _ => prefs.FeedSize,
        };
        foreach (var s in HudLayout.Sizes) w.Cls(panel, player, "ms_" + s, "on", s == size);
        w.Cls(panel, player, "ms_opts", "disabled", v.Tab == TabToast && prefs.Toast == HudLayout.WithCard);

        // switches
        w.Cls(panel, player, "mo_hud_on", "on", !hudOff);
        w.Cls(panel, player, "mo_hud_off", "on", hudOff);
        w.Cls(panel, player, "mo_spd_on", "on", prefs.Speed);
        w.Cls(panel, player, "mo_spd_off", "on", !prefs.Speed);
        w.Cls(panel, player, "mo_keys_on", "on", !keysOff);
        w.Cls(panel, player, "mo_keys_off", "on", keysOff);
        w.Cls(panel, player, "mo_kl_grid", "on", !prefs.KeysRow);
        w.Cls(panel, player, "mo_kl_row", "on", prefs.KeysRow);
        w.Cls(panel, player, "mo_spl_on", "on", timer?.HideChatSpeed != true);
        w.Cls(panel, player, "mo_spl_off", "on", timer?.HideChatSpeed == true);
        w.Cls(panel, player, "mo_feed_on", "on", prefs.FeedOn);
        w.Cls(panel, player, "mo_feed_off", "on", !prefs.FeedOn);
    }

    private void Size(CCSPlayerController player, PanelWriter w, string id, string size) =>
        w.OneOf(_panel!, player, id, "sz-" + size, HudLayout.SizeClasses);

    private static string Where(HudPrefs p, int tab) => tab switch
    {
        TabCard => p.Card,
        TabCluster => p.Cluster,
        TabToast => p.Toast,
        _ => p.Feed,
    };

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
            Draw(player, v);
            return;
        }

        var slot = player.Slot;
        var timer = _st.playerTimers.TryGetValue(slot, out var t) ? t : null;

        switch (id)
        {
            case "mo_hud_on": _st.HudSetFlag(player, hideTimerHud: false); break;
            case "mo_hud_off": _st.HudSetFlag(player, hideTimerHud: true); break;
            case "mo_keys_on": _st.HudSetFlag(player, hideKeys: false); break;
            case "mo_keys_off": _st.HudSetFlag(player, hideKeys: true); break;
            case "mo_spl_on": _st.HudSetFlag(player, hideSplits: false); break;
            case "mo_spl_off": _st.HudSetFlag(player, hideSplits: true); break;
            case "m_reset":
                Change(player, p =>
                {
                    var d = _st.HudPrefStore.Defaults;
                    p.Card = d.Card; p.CardSize = d.CardSize; p.Cluster = d.Cluster; p.ClusterSize = d.ClusterSize;
                    p.Toast = d.Toast; p.ToastSize = d.ToastSize; p.Feed = d.Feed; p.FeedSize = d.FeedSize;
                    p.Speed = d.Speed; p.KeysRow = d.KeysRow; p.FeedOn = d.FeedOn;
                });
                if (timer != null && (timer.HideTimerHud || timer.HideKeys || timer.HideChatSpeed))
                    _st.HudSetFlag(player, hideTimerHud: false, hideKeys: false, hideSplits: false);
                break;
            case "mo_spd_on": Change(player, p => p.Speed = true); break;
            case "mo_spd_off": Change(player, p => p.Speed = false); break;
            case "mo_kl_grid": Change(player, p => p.KeysRow = false); break;
            case "mo_kl_row": Change(player, p => p.KeysRow = true); break;
            case "mo_feed_on": Change(player, p => p.FeedOn = true); break;
            case "mo_feed_off": Change(player, p => p.FeedOn = false); break;
            case "mz_with":
                if (v.Tab == TabCluster) Change(player, p => p.Cluster = HudLayout.Beside);
                else if (v.Tab == TabToast) Change(player, p => p.Toast = HudLayout.WithCard);
                break;
            default:
                if (id.StartsWith("ms_") && HudLayout.IsSize(id[3..]))
                {
                    var size = id[3..];
                    if (v.Tab == TabToast && _hud.PrefsOf(player).Toast == HudLayout.WithCard) break;
                    Change(player, p =>
                    {
                        switch (v.Tab)
                        {
                            case TabCard: p.CardSize = size; break;
                            case TabCluster: p.ClusterSize = size; break;
                            case TabToast: p.ToastSize = size; break;
                            default: p.FeedSize = size; break;
                        }
                    });
                }
                else if (id.StartsWith("mz_") && HudLayout.IsPosition(id[3..]))
                {
                    var pos = id[3..];
                    Change(player, p => Place(p, v.Tab, pos));
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
    private static void Place(HudPrefs p, int tab, string pos)
    {
        var old = Where(p, tab);
        if (old == pos) return;

        Set(p, tab, pos);

        for (var other = 0; other < Tabs; other++)
        {
            if (other == tab || Where(p, other) != pos) continue;
            var to = HudLayout.IsPosition(old) ? old : Fallback.FirstOrDefault(f => Enumerable.Range(0, Tabs).All(g => Where(p, g) != f)) ?? old;
            if (HudLayout.IsPosition(to)) Set(p, other, to);
        }
    }

    private static void Set(HudPrefs p, int tab, string pos)
    {
        switch (tab)
        {
            case TabCard: p.Card = pos; break;
            case TabCluster: p.Cluster = pos; break;
            case TabToast: p.Toast = pos; break;
            default: p.Feed = pos; break;
        }
    }
}
