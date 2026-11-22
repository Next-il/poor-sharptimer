using System.Globalization;
using CounterStrikeSharp.API.Core;
using PanoramaManager;

namespace SharpTimer.Hud;

/// <summary>
/// The names the two layouts declare (hud/tools/gen_layouts.py writes them; these must match). Positions are
/// the ten spots every part can take; the speed can also sit next to the card, the toast with the card.
/// </summary>
internal static class HudLayout
{
    public const string HudPath = "panorama/layout/custom_game/sharptimer_hud.vxml_c";
    public const string MenuPath = "panorama/layout/custom_game/sharptimer_menu.vxml_c";
    public const string HudRoot = "StHudRoot";
    public const string MenuRoot = "StMenuRoot";

    /// <summary>The spots, in the generator's order. rw is the right side above CS2's weapon list.</summary>
    public static readonly string[] Positions = ["tl", "tc", "tr", "ml", "xa", "xu", "mr", "bc", "br", "rw"];

    /// <summary>The speed next to the card.</summary>
    public const string Beside = "nx";

    /// <summary>The toast in the card's own group, above it (or under it when the card hangs from the top).</summary>
    public const string WithCard = "card";

    public static readonly string[] Sizes = ["s", "m", "l"];

    /// <summary>Positions whose group hangs from the top edge: the in-card toast goes under the card there.</summary>
    public static bool TopAnchored(string pos) => pos is "tl" or "tc" or "tr" or "xu";

    public static string Label(string pos) => pos switch
    {
        "tl" => "Top left",
        "tc" => "Top center",
        "tr" => "Top right",
        "ml" => "Middle left",
        "xa" => "Above the crosshair",
        "xu" => "Under the crosshair",
        "mr" => "Middle right",
        "bc" => "Bottom center",
        "br" => "Bottom right",
        "rw" => "Right, above the weapons",
        Beside => "Next to the card",
        WithCard => "With the card",
        _ => pos,
    };

    public static bool IsPosition(string? pos) => pos != null && Array.IndexOf(Positions, pos) >= 0;
    public static bool IsSize(string? size) => size is "s" or "m" or "l";

    public static readonly string[] PosClasses = Positions.Select(p => "p-" + p).ToArray();
    public static readonly string[] SizeClasses = Sizes.Select(s => "sz-" + s).ToArray();

    /// <summary>Every class the speed's outer panel can wear: the spots, then next to the card (nx-) and next to
    /// the compact card (nxc-), per card spot, card size and speed size.</summary>
    public static readonly string[] SpeedClasses = PosClasses
        .Concat(from x in new[] { "nx", "nxc" } from p in Positions from c in Sizes from k in Sizes select $"{x}-{p}-{c}-{k}").ToArray();

    public static string SpeedClass(HudPrefs p) => p.Speed == Beside
        ? $"{(p.Compact ? "nxc" : "nx")}-{p.Card}-{p.CardSize}-{p.SpeedSize}"
        : "p-" + p.Speed;

    /// <summary>The mini map's toast block: the spots, then with the card (wc-) and with the compact card (wcc-).</summary>
    public static readonly string[] MapToastClasses = PosClasses
        .Concat(from x in new[] { "wc", "wcc" } from p in Positions from c in Sizes select $"{x}-{p}-{c}").ToArray();

    // ------------------------------------------------------------------ style (classes on st_skin)

    public static readonly string[] Accents = ["ice", "orange", "pink", "lime", "white"];
    public static readonly string[] AccentClasses = Accents.Select(a => "ac-" + a).ToArray();
    public static readonly string[] Opacities = ["lo", "md", "hi"];
    public static readonly string[] OpacityClasses = Opacities.Select(o => "op-" + o).ToArray();
    public static readonly string[] TextClasses = Sizes.Select(s => "tx-" + s).ToArray();

    public static bool IsAccent(string? a) => a != null && Array.IndexOf(Accents, a) >= 0;
    public static bool IsOpacity(string? o) => o is "lo" or "md" or "hi";

    public const int Segments = 16;
    public const int FeedSlots = 5;
    public const int Steps = 50;

    public static readonly string[] StepClasses = Enumerable.Range(0, Steps + 1).Select(i => "p" + i).ToArray();
    public static readonly string[] CapClasses = Enumerable.Range(0, Steps + 1).Select(i => "cap-" + i).ToArray();
    public static readonly string[] SpeedColors = Enumerable.Range(0, 11).Select(i => "v" + i).ToArray();

    public static readonly string[] CardStates =
        ["st-start", "st-idle", "st-run", "st-done", "st-pb", "st-sr", "st-cancel", "st-replay"];

    public static readonly string[] FeedKinds = ["k-sr", "k-pb", "k-fin", "k-stg"];

    public static readonly string[] RankIcons =
    [
        "unranked", "bronze", "silver1", "silver2", "silver3", "gold1", "gold2", "gold3", "plat1", "plat2", "plat3",
        "dia1", "dia2", "dia3", "master1", "master2", "master3", "legend1", "legend2", "legend3", "royal1", "royal2",
        "royal3", "god",
    ];

    public static readonly string[] RankIconClasses = RankIcons.Select(i => "ri-" + i).ToArray();

    public static readonly string[] RankColors =
    [
        "default", "white", "darkred", "green", "lightyellow", "lightblue", "olive", "lime", "red", "lightpurple",
        "purple", "grey", "yellow", "gold", "silver", "blue", "darkblue", "bluegrey", "magenta", "lightred", "orange",
    ];

    public static readonly string[] RankColorClasses = RankColors.Select(c => "rc-" + c).ToArray();

    /// <summary>The bar ladder: 0..max in 50 clip steps.</summary>
    public static string Step(double value, double max) =>
        "p" + Math.Clamp((int)Math.Round(Math.Clamp(value / Math.Max(1, max), 0, 1) * Steps), 0, Steps);

    // ------------------------------------------------------------------ SharpTimer's own time formats

    /// <summary>"1:24.218" (or "1:02:03.500"), like Utils.FormatTime.</summary>
    public static string Time(int ticks)
    {
        ticks = Math.Max(0, ticks);
        var ms = (int)Math.Round((ticks % 64) * (1000.0 / 64.0));
        var totalSeconds = ticks / 64;
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        if (ms >= 1000) ms = 999;
        return minutes >= 60
            ? $"{minutes / 60}:{minutes % 60:D2}:{seconds:D2}.{ms:D3}"
            : $"{minutes}:{seconds:D2}.{ms:D3}";
    }

    /// <summary>"1:24.2": tenths, for the running clock.</summary>
    public static string TimeTenths(int ticks)
    {
        ticks = Math.Max(0, ticks);
        var tenths = (int)(ticks / 6.4);
        var minutes = tenths / 600;
        var seconds = tenths / 10 % 60;
        return minutes >= 60
            ? $"{minutes / 60}:{minutes % 60:D2}:{seconds:D2}.{tenths % 10}"
            : $"{minutes}:{seconds:D2}.{tenths % 10}";
    }

    /// <summary>"-1.204" under a minute, "+1:02.500" over, from current minus reference.</summary>
    public static string Diff(int currentTicks, int referenceTicks)
    {
        var d = currentTicks - referenceTicks;
        var sign = d < 0 ? "-" : "+";
        var abs = Math.Abs(d);
        if (abs >= 64 * 60) return sign + Time(abs);
        return sign + (abs / 64.0).ToString("0.000", CultureInfo.InvariantCulture);
    }

    public static string Number(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>"[Gold]" -> "Gold".</summary>
    public static string RankName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var t = title.Trim();
        if (t.StartsWith('[') && t.EndsWith(']') && t.Length > 2) t = t[1..^1];
        return t;
    }

    /// <summary>"https://.../rank_icons/gold3.png" -> "ri-gold3", or null when the icon is not baked into the addon.</summary>
    public static string? RankIconClass(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var name = Path.GetFileNameWithoutExtension(url.Trim().Split('?')[0]).ToLowerInvariant();
        return Array.IndexOf(RankIcons, name) >= 0 ? "ri-" + name : null;
    }

    /// <summary>"{lightyellow}" -> "rc-lightyellow"; anything unknown is the default grey.</summary>
    public static string RankColorClass(string? chatColor)
    {
        var c = (chatColor ?? "").Trim().Trim('{', '}').ToLowerInvariant();
        return Array.IndexOf(RankColors, c) >= 0 ? "rc-" + c : "rc-default";
    }
}

/// <summary>
/// What one viewer's panel holds, so a write only goes out when it changes. Text, single classes and one-of
/// class groups each have their own cache. <see cref="Reset"/> after the panel was (re)opened or rebuilt: the
/// next write of everything then goes out in full, including every "off" in a one-of group, because a round
/// restart keeps the entity's classes while a map change does not, and only a full write is right for both.
/// </summary>
internal sealed class PanelWriter
{
    private readonly Dictionary<string, string> _vars = new();
    private readonly Dictionary<(string, string), bool> _classes = new();
    private readonly Dictionary<(string, string), string?> _oneOf = new();

    public int Writes;

    public void Reset()
    {
        _vars.Clear();
        _classes.Clear();
        _oneOf.Clear();
    }

    public void Var(PanelHandle panel, CCSPlayerController player, string name, string value)
    {
        if (_vars.TryGetValue(name, out var old) && old == value) return;
        _vars[name] = value;
        panel.SetVariableFor(player, name, value);
        Writes++;
    }

    public void Cls(PanelHandle panel, CCSPlayerController player, string panelId, string className, bool on)
    {
        var key = (panelId, className);
        if (_classes.TryGetValue(key, out var old) && old == on) return;
        _classes[key] = on;
        panel.SetClassFor(player, panelId, className, on);
        Writes++;
    }

    /// <summary>One class out of a group on a panel. A panel can carry several groups (a position and a size); the
    /// group is told apart by its first option.</summary>
    public void OneOf(PanelHandle panel, CCSPlayerController player, string panelId, string? className, IReadOnlyList<string> options)
    {
        var key = (panelId, options.Count > 0 ? options[0] : "");
        if (_oneOf.TryGetValue(key, out var old))
        {
            if (old == className) return;
            if (old != null) { panel.SetClassFor(player, panelId, old, false); Writes++; }
        }
        else
        {
            foreach (var option in options)
                if (option != className) panel.SetClassFor(player, panelId, option, false);
            Writes += options.Count;
        }

        if (className != null) { panel.SetClassFor(player, panelId, className, true); Writes++; }
        _oneOf[key] = className;
    }

    /// <summary>Forgets one class so the next write goes out even if it is the same: for ping-pong restarts.</summary>
    public void Forget(string panelId, string className) => _classes.Remove((panelId, className));
}
