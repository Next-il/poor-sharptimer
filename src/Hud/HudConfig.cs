using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTimer.Hud;

/// <summary>
/// The Panorama HUD's server settings: cfg/SharpTimer/hud.json, read on load and on every map start when it
/// exists. Every key is optional and the code defaults below are the whole default config; the file is never
/// written. cfg/SharpTimer/example.hud.json lists every key with its default.
///
/// <para>The open questions from the design are switches here, each defaulting to what SharpTimer does today
/// (finishes announced to everyone, the live place, rank icons, the count only past 16 checkpoints, no world
/// record). The SR chat ad already has its own convar, sharptimer_ad_sr_enabled.</para>
/// </summary>
public sealed class HudConfig
{
    /// <summary>False: SharpTimer's center HTML, exactly as before. True: the Panorama HUD, with the center HTML
    /// kept for any player the panel cannot reach.</summary>
    public bool Enabled { get; set; } = true;

    // ------------------------------------------------------------------ update rates (per second)
    /// <summary>The running clock for players on 1 decimal. On 2 or 3 decimals it is written every tick (only that
    /// label, only when its text changed).</summary>
    public int TimerRate { get; set; } = 10;
    public int SpeedRate { get; set; } = 16;
    public int SyncRate { get; set; } = 4;
    public int PlaceRate { get; set; } = 2;

    // ------------------------------------------------------------------ open questions, today's behaviour by default

    /// <summary>Plain finishes (no PB) go to everyone's records feed, like today's chat lines. False: only PBs and records.</summary>
    public bool FeedFinishes { get; set; } = true;

    /// <summary>The live "#3" on the card while running, against the cached top 100.</summary>
    public bool LivePlace { get; set; } = true;

    /// <summary>"icon" (today: the rank PNG), "text" (the rank name in its colour) or "both".</summary>
    public string RankDisplay { get; set; } = "text";

    /// <summary>More checkpoints or stages than the bar's 16 segments: "count" (no bar, only "12/24"), "group"
    /// (each segment covers several, coloured by the last split in it) or "scroll" (the 16 around the current one).</summary>
    public string SplitBarOverflow { get; set; } = "count";

    /// <summary>Show the world record (with a global API key) as a third time on the card.</summary>
    public bool ShowWorldRecord { get; set; }

    // ------------------------------------------------------------------ chat lines the HUD replaces
    // The first three are the defaults of the player's own Chat tab in !hud; each player can change theirs.

    /// <summary>Keep the 3 split lines per stage or checkpoint in chat too.</summary>
    public bool SplitsInChat { get; set; }

    /// <summary>Keep the 3-5 finish lines ("set a new Map PB!", rank, time, style, mode, points) in chat too.</summary>
    public bool RecordsInChat { get; set; }

    /// <summary>Keep "Start speed: 352" in chat too.</summary>
    public bool StartSpeedInChat { get; set; }

    /// <summary>Keep the !style and !mode replies in chat too.</summary>
    public bool RepliesInChat { get; set; }

    // ------------------------------------------------------------------ timing
    public float ResultSeconds { get; set; } = 6f;
    public float ToastSeconds { get; set; } = 3f;
    public float FeedSeconds { get; set; } = 4.5f;
    public float FeedRecordSeconds { get; set; } = 6.5f;
    public float StartChipSeconds { get; set; } = 2f;

    /// <summary>The speed bar's full scale, u/s.</summary>
    public int SpeedBarMax { get; set; } = 3500;

    // ------------------------------------------------------------------ map records (the map's top times)
    /// <summary>Rows on screen (1 to 10).</summary>
    public int MapRecordsRows { get; set; } = 3;
    /// <summary>Rows while !hud is open (1 to 10).</summary>
    public int MapRecordsMenuRows { get; set; } = 10;
    /// <summary>"On map join": how long the list stays after joining or a map start.</summary>
    public float MapRecordsJoinSeconds { get; set; } = 10f;

    /// <summary>Until a player has opened !settings once: "!SETTINGS" on the card in the start zone, and one chat
    /// line a little after they join.</summary>
    public bool SettingsHint { get; set; } = true;

    /// <summary>What a player who never opened !hud gets.</summary>
    public HudDefaults Defaults { get; set; } = new();

    [JsonIgnore] public string Source { get; private set; } = "code defaults";

    public static HudConfig Load(string path, Action<string> logError)
    {
        try
        {
            if (!File.Exists(path)) return new HudConfig();

            var config = JsonSerializer.Deserialize<HudConfig>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) ?? new HudConfig();

            config.Source = path;
            config.Clamp();
            return config;
        }
        catch (Exception e)
        {
            logError($"[HUD] {path} could not be read, using the defaults: {e.Message}");
            return new HudConfig();
        }
    }

    private void Clamp()
    {
        TimerRate = Math.Clamp(TimerRate, 1, 64);
        SpeedRate = Math.Clamp(SpeedRate, 1, 64);
        SyncRate = Math.Clamp(SyncRate, 1, 64);
        PlaceRate = Math.Clamp(PlaceRate, 1, 64);
        RankDisplay = RankDisplay?.Trim().ToLowerInvariant() is "text" or "both" or "icon" ? RankDisplay.Trim().ToLowerInvariant() : "text";
        SplitBarOverflow = SplitBarOverflow?.Trim().ToLowerInvariant() is "group" or "scroll" ? SplitBarOverflow.Trim().ToLowerInvariant() : "count";
        SpeedBarMax = Math.Max(100, SpeedBarMax);
        MapRecordsRows = Math.Clamp(MapRecordsRows, 1, HudLayout.MapRecordRows);
        MapRecordsMenuRows = Math.Clamp(MapRecordsMenuRows, 1, HudLayout.MapRecordRows);
        MapRecordsJoinSeconds = Math.Clamp(MapRecordsJoinSeconds, 1f, 120f);
        Defaults ??= new HudDefaults();
    }
}

/// <summary>What a player who never changed an option gets, per option. Spots: tl tc tr ml xa xu mr bc br rw
/// (rw = the right side above the weapon list). Sizes: s m l. The chat options' defaults are SplitsInChat,
/// StartSpeedInChat and RecordsInChat above.</summary>
public sealed class HudDefaults
{
    // ---- layout
    public string Card { get; set; } = "bc";
    public string CardSize { get; set; } = "m";
    /// <summary>The speed readout: a spot, or "nx" for next to the card. Named Cluster from when speed and keys
    /// were one part, so a hud.json from then keeps working.</summary>
    public string Cluster { get; set; } = "xu";
    public string ClusterSize { get; set; } = "m";
    /// <summary>The keys: a spot. Right side above the weapon list by default (owner, 11-23).</summary>
    public string Keys { get; set; } = "rw";
    public string KeysSize { get; set; } = "m";
    /// <summary>"grid" or "row".</summary>
    public string KeysLayout { get; set; } = "grid";
    /// <summary>A spot, or "card" for with the card.</summary>
    public string Toast { get; set; } = "card";
    public string ToastSize { get; set; } = "m";
    public string Feed { get; set; } = "tr";
    public string FeedSize { get; set; } = "m";
    /// <summary>The map's top times. Top left, under the radar and money, by default: the feed has the top right.</summary>
    public string MapRecords { get; set; } = "tl";
    public string MapRecordsSize { get; set; } = "m";

    // ---- show / hide (the timer HUD, keys and splits switches stay !hud on|off, !keys and !splits)
    public bool CardOn { get; set; } = true;
    public bool Speed { get; set; } = true;
    public bool SpeedBar { get; set; } = true;
    public bool Sync { get; set; } = true;
    /// <summary>PB and SR on the card.</summary>
    public bool Compare { get; set; } = true;
    public bool Rank { get; set; } = true;
    public bool FeedOn { get; set; } = true;
    public bool MapRecordsOn { get; set; } = true;
    /// <summary>When the map records show: "always", "join" (for MapRecordsJoinSeconds after joining or a map
    /// start) or "hud" (only while !hud is open). They always show while !hud is open, with MapRecordsMenuRows.</summary>
    public string MapRecordsShow { get; set; } = "always";

    // ---- style
    /// <summary>ice, orange, pink, lime or white.</summary>
    public string Accent { get; set; } = "ice";
    public bool CompactCard { get; set; }
    /// <summary>Background: "lo" (light), "md" (today) or "hi" (solid).</summary>
    public string Opacity { get; set; } = "md";
    /// <summary>s, m or l, on top of each part's size.</summary>
    public string TextSize { get; set; } = "m";
    /// <summary>Digits after the second on the running clock: 1, 2 or 3 (m:ss.fff, SharpTimer's own format).</summary>
    public int TimerDecimals { get; set; } = 3;
}
