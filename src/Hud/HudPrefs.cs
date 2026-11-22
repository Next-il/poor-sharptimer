using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace SharpTimer.Hud;

/// <summary>
/// One player's timer HUD settings, everything the !hud settings panel changes. Three switches stay where they
/// always were, in PlayerStats: HideTimerHud (!hud on|off), HideKeys (!keys) and HideChatSpeed (!splits), so the
/// old commands keep working and old rows keep their meaning. Everything else is here.
///
/// <para>Saved as "key=value;..." holding only what differs from the server defaults, so a default changed later
/// in hud.json still reaches everyone who never touched that option. Keys: card cs (timer card spot, size),
/// cl cls (speed: spot or nx, size; "cl" is the old speed + keys cluster, so a saved cluster spot is now the speed
/// spot), ky kys (keys), kr (keys in one row), ts tss (toast: spot or card, size), fd fds (records feed),
/// cd sp sb sy pc rk fo (card, speed, speed bar, sync, PB and SR, rank, feed: 0 hides), ac (accent), cc
/// (compact card), op (background lo|md|hi), tx (text size), td (running clock decimals 1|2|3), spc ssc fc (splits, start speed and finish
/// messages in chat), hs (has opened the settings once). Unknown keys and bad values are ignored.</para>
/// </summary>
public sealed class HudPrefs
{
    // ---- layout
    public string Card = "bc";
    public string CardSize = "m";
    /// <summary>The speed readout's spot, or <see cref="HudLayout.Beside"/>.</summary>
    public string Speed = "xu";
    public string SpeedSize = "m";
    public string Keys = "rw";
    public string KeysSize = "m";
    public bool KeysRow;
    public string Toast = HudLayout.WithCard;
    public string ToastSize = "m";
    public string Feed = "tr";
    public string FeedSize = "m";

    // ---- show / hide
    public bool CardOn = true;
    public bool SpeedOn = true;
    public bool SpeedBar = true;
    public bool Sync = true;
    public bool Compare = true;
    public bool Rank = true;
    public bool FeedOn = true;

    // ---- style
    public string Accent = "ice";
    public bool Compact;
    public string Opacity = "md";
    public string Text = "m";
    /// <summary>Digits after the second on the running clock, 1 to 3.</summary>
    public int Decimals = 3;

    // ---- chat
    public bool SplitsChat;
    public bool SpeedChat;
    public bool FinishChat;

    /// <summary>Has opened the settings at least once: no more hints.</summary>
    public bool Seen;

    public HudPrefs Clone() => (HudPrefs)MemberwiseClone();

    public static HudPrefs From(HudConfig config)
    {
        var d = config.Defaults ?? new HudDefaults();
        var p = new HudPrefs();
        p.Apply("card", d.Card);
        p.Apply("cs", d.CardSize);
        p.Apply("cl", d.Cluster);
        p.Apply("cls", d.ClusterSize);
        p.Apply("ky", d.Keys);
        p.Apply("kys", d.KeysSize);
        p.Apply("ts", d.Toast);
        p.Apply("tss", d.ToastSize);
        p.Apply("fd", d.Feed);
        p.Apply("fds", d.FeedSize);
        p.KeysRow = string.Equals(d.KeysLayout, "row", StringComparison.OrdinalIgnoreCase);
        p.CardOn = d.CardOn;
        p.SpeedOn = d.Speed;
        p.SpeedBar = d.SpeedBar;
        p.Sync = d.Sync;
        p.Compare = d.Compare;
        p.Rank = d.Rank;
        p.FeedOn = d.FeedOn;
        p.Apply("ac", d.Accent);
        p.Compact = d.CompactCard;
        p.Apply("op", d.Opacity);
        p.Apply("tx", d.TextSize);
        p.Apply("td", d.TimerDecimals.ToString());
        p.SplitsChat = config.SplitsInChat;
        p.SpeedChat = config.StartSpeedInChat;
        p.FinishChat = config.RecordsInChat;
        // Two parts on one spot from a hand-edited hud.json: the keys make room.
        if (p.Keys == p.Card || p.Keys == p.Speed || p.Keys == p.Toast || p.Keys == p.Feed)
            p.Keys = HudLayout.Positions.FirstOrDefault(s => s != p.Card && s != p.Speed && s != p.Toast && s != p.Feed) ?? p.Keys;
        return p;
    }

    /// <summary>Puts the layout (spots, sizes, keys layout) back to <paramref name="d"/>.</summary>
    public void ResetLayout(HudPrefs d)
    {
        Card = d.Card; CardSize = d.CardSize; Speed = d.Speed; SpeedSize = d.SpeedSize; Keys = d.Keys; KeysSize = d.KeysSize;
        KeysRow = d.KeysRow; Toast = d.Toast; ToastSize = d.ToastSize; Feed = d.Feed; FeedSize = d.FeedSize;
    }

    private static string B(bool v) => v ? "1" : "0";

    public string Encode(HudPrefs d)
    {
        var parts = new List<string>();
        void Add(string key, string value, string def) { if (value != def) parts.Add($"{key}={value}"); }
        Add("card", Card, d.Card);
        Add("cs", CardSize, d.CardSize);
        Add("cl", Speed, d.Speed);
        Add("cls", SpeedSize, d.SpeedSize);
        Add("ky", Keys, d.Keys);
        Add("kys", KeysSize, d.KeysSize);
        Add("kr", B(KeysRow), B(d.KeysRow));
        Add("ts", Toast, d.Toast);
        Add("tss", ToastSize, d.ToastSize);
        Add("fd", Feed, d.Feed);
        Add("fds", FeedSize, d.FeedSize);
        Add("cd", B(CardOn), B(d.CardOn));
        Add("sp", B(SpeedOn), B(d.SpeedOn));
        Add("sb", B(SpeedBar), B(d.SpeedBar));
        Add("sy", B(Sync), B(d.Sync));
        Add("pc", B(Compare), B(d.Compare));
        Add("rk", B(Rank), B(d.Rank));
        Add("fo", B(FeedOn), B(d.FeedOn));
        Add("ac", Accent, d.Accent);
        Add("cc", B(Compact), B(d.Compact));
        Add("op", Opacity, d.Opacity);
        Add("tx", Text, d.Text);
        Add("td", Decimals.ToString(), d.Decimals.ToString());
        Add("spc", B(SplitsChat), B(d.SplitsChat));
        Add("ssc", B(SpeedChat), B(d.SpeedChat));
        Add("fc", B(FinishChat), B(d.FinishChat));
        Add("hs", B(Seen), B(d.Seen));
        return string.Join(';', parts);
    }

    public static HudPrefs Decode(string? text, HudPrefs defaults)
    {
        var p = defaults.Clone();
        if (string.IsNullOrWhiteSpace(text)) return p;

        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) p.Apply(part[..eq].Trim().ToLowerInvariant(), part[(eq + 1)..].Trim().ToLowerInvariant());
        }

        // A row saved before keys had a spot of their own: keys go to their default unless a part already sits there.
        if (p.Keys == p.Card || p.Keys == p.Speed || p.Keys == p.Toast || p.Keys == p.Feed)
            p.Keys = HudLayout.Positions.FirstOrDefault(s => s != p.Card && s != p.Speed && s != p.Toast && s != p.Feed) ?? p.Keys;
        return p;
    }

    /// <summary>Takes one key=value; anything it does not recognise, or a value out of range, is ignored.</summary>
    public void Apply(string key, string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        switch (key)
        {
            case "card": if (HudLayout.IsPosition(v)) Card = v; break;
            case "cl": if (HudLayout.IsPosition(v) || v == HudLayout.Beside) Speed = v; break;
            case "ky": if (HudLayout.IsPosition(v)) Keys = v; break;
            case "ts": if (HudLayout.IsPosition(v) || v == HudLayout.WithCard) Toast = v; break;
            case "fd": if (HudLayout.IsPosition(v)) Feed = v; break;
            case "cs": if (HudLayout.IsSize(v)) CardSize = v; break;
            case "cls": if (HudLayout.IsSize(v)) SpeedSize = v; break;
            case "kys": if (HudLayout.IsSize(v)) KeysSize = v; break;
            case "tss": if (HudLayout.IsSize(v)) ToastSize = v; break;
            case "fds": if (HudLayout.IsSize(v)) FeedSize = v; break;
            case "kr": KeysRow = v == "1"; break;
            case "cd": CardOn = v != "0"; break;
            case "sp": SpeedOn = v != "0"; break;
            case "sb": SpeedBar = v != "0"; break;
            case "sy": Sync = v != "0"; break;
            case "pc": Compare = v != "0"; break;
            case "rk": Rank = v != "0"; break;
            case "fo": FeedOn = v != "0"; break;
            case "ac": if (HudLayout.IsAccent(v)) Accent = v; break;
            case "cc": Compact = v == "1"; break;
            case "op": if (HudLayout.IsOpacity(v)) Opacity = v; break;
            case "tx": if (HudLayout.IsSize(v)) Text = v; break;
            case "td": if (HudLayout.IsDecimals(v)) Decimals = v[0] - '0'; break;
            case "spc": SplitsChat = v == "1"; break;
            case "ssc": SpeedChat = v == "1"; break;
            case "fc": FinishChat = v == "1"; break;
            case "hs": Seen = v == "1"; break;
        }
    }
}

/// <summary>
/// HUD settings per SteamID64, in one small table shared by every server on the same database (no table prefix:
/// a player's HUD is the same on every timer server). SharpTimer creates its own tables on start, so this one is
/// created the same way (CREATE TABLE IF NOT EXISTS); sharptimer-hud-settings.sql in the repo root is the same
/// statement for running by hand. Without a database (JSON mode) the settings last until the player leaves.
/// </summary>
internal sealed class HudPrefsStore
{
    public const string Table = "PlayerHudSettings";

    private readonly SharpTimer _plugin;
    private readonly ConcurrentDictionary<ulong, HudPrefs> _prefs = new();
    private readonly ConcurrentDictionary<ulong, int> _saveSeq = new();
    private volatile bool _ready;
    private volatile bool _failed;

    public HudPrefsStore(SharpTimer plugin) => _plugin = plugin;

    public HudPrefs Defaults => HudPrefs.From(_plugin.HudSettings);

    public HudPrefs Get(ulong steamId) => _prefs.TryGetValue(steamId, out var p) ? p : Defaults;

    public void Forget(ulong steamId) => _prefs.TryRemove(steamId, out _);

    private bool UseDb => _plugin.enableDb && !_failed;

    /// <summary>Loads (off the main thread) and calls back on the main thread with what it found.</summary>
    public void Load(ulong steamId, Action<HudPrefs> loaded)
    {
        var defaults = Defaults;
        if (!UseDb)
        {
            _prefs.TryAdd(steamId, defaults);
            loaded(Get(steamId));
            return;
        }

        Task.Run(async () =>
        {
            var prefs = defaults;
            try
            {
                await EnsureTable();
                using var connection = await _plugin.OpenConnectionAsync();
                using var cmd = Command(connection, _plugin.dbType switch
                {
                    DatabaseType.PostgreSQL => $@"SELECT ""Settings"" FROM ""{Table}"" WHERE ""SteamID"" = @SteamID",
                    _ => $"SELECT Settings FROM {Table} WHERE SteamID = @SteamID",
                });
                cmd.AddParameterWithValue("@SteamID", steamId.ToString());
                var value = await cmd.ExecuteScalarAsync();
                if (value is string text) prefs = HudPrefs.Decode(text, defaults);
            }
            catch (Exception e)
            {
                Fail("load", e);
            }

            CounterStrikeSharp.API.Server.NextFrame(() =>
            {
                _prefs[steamId] = prefs;
                loaded(prefs);
            });
        });
    }

    /// <summary>Applies a change at once and saves it a moment later, so a burst of clicks is one write.</summary>
    public HudPrefs Update(ulong steamId, Action<HudPrefs> change)
    {
        var prefs = Get(steamId).Clone();
        change(prefs);
        _prefs[steamId] = prefs;

        if (UseDb)
        {
            var seq = _saveSeq.AddOrUpdate(steamId, 1, (_, s) => s + 1);
            var text = prefs.Encode(Defaults);
            Task.Run(async () =>
            {
                await Task.Delay(1500);
                if (_saveSeq.TryGetValue(steamId, out var now) && now != seq) return;
                await Save(steamId, text);
            });
        }

        return prefs;
    }

    private async Task Save(ulong steamId, string text)
    {
        try
        {
            await EnsureTable();
            using var connection = await _plugin.OpenConnectionAsync();
            using var cmd = Command(connection, _plugin.dbType switch
            {
                DatabaseType.MySQL => $@"INSERT INTO {Table} (SteamID, Settings, UpdatedAt) VALUES (@SteamID, @Settings, @UpdatedAt)
                                         ON DUPLICATE KEY UPDATE Settings = VALUES(Settings), UpdatedAt = VALUES(UpdatedAt)",
                DatabaseType.PostgreSQL => $@"INSERT INTO ""{Table}"" (""SteamID"", ""Settings"", ""UpdatedAt"") VALUES (@SteamID, @Settings, @UpdatedAt)
                                              ON CONFLICT (""SteamID"") DO UPDATE SET ""Settings"" = EXCLUDED.""Settings"", ""UpdatedAt"" = EXCLUDED.""UpdatedAt""",
                _ => $@"INSERT INTO {Table} (SteamID, Settings, UpdatedAt) VALUES (@SteamID, @Settings, @UpdatedAt)
                        ON CONFLICT(SteamID) DO UPDATE SET Settings = excluded.Settings, UpdatedAt = excluded.UpdatedAt",
            });
            cmd.AddParameterWithValue("@SteamID", steamId.ToString());
            cmd.AddParameterWithValue("@Settings", text);
            cmd.AddParameterWithValue("@UpdatedAt", (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            Fail("save", e);
        }
    }

    /// <summary>The same statement as sharptimer-hud-settings.sql, once per plugin load.</summary>
    private async Task EnsureTable()
    {
        if (_ready) return;
        _ready = true;
        try
        {
            await CreateTable();
        }
        catch (Exception e)
        {
            // A user without CREATE gets refused even when the table is there; the SELECT / INSERT decide.
            var message = e.Message;
            CounterStrikeSharp.API.Server.NextFrame(() => _plugin.Utils.LogDebug($"[HUD] CREATE TABLE {Table} refused: {message}"));
        }
    }

    private async Task CreateTable()
    {
        using var connection = await _plugin.OpenConnectionAsync();
        using var cmd = Command(connection, _plugin.dbType switch
        {
            DatabaseType.MySQL => $@"CREATE TABLE IF NOT EXISTS {Table} (
                                        SteamID VARCHAR(20) NOT NULL,
                                        Settings VARCHAR(255) NOT NULL DEFAULT '',
                                        UpdatedAt INT NOT NULL DEFAULT 0,
                                        PRIMARY KEY (SteamID)
                                    )",
            DatabaseType.PostgreSQL => $@"CREATE TABLE IF NOT EXISTS ""{Table}"" (
                                        ""SteamID"" VARCHAR(20) NOT NULL PRIMARY KEY,
                                        ""Settings"" VARCHAR(255) NOT NULL DEFAULT '',
                                        ""UpdatedAt"" INT NOT NULL DEFAULT 0
                                    )",
            _ => $@"CREATE TABLE IF NOT EXISTS {Table} (
                        SteamID TEXT NOT NULL PRIMARY KEY,
                        Settings TEXT NOT NULL DEFAULT '',
                        UpdatedAt INTEGER NOT NULL DEFAULT 0
                    )",
        });
        await cmd.ExecuteNonQueryAsync();
    }

    private DbCommand Command(IDbConnection connection, string sql) => _plugin.dbType switch
    {
        DatabaseType.MySQL => new MySqlCommand(sql, (MySqlConnection)connection),
        DatabaseType.PostgreSQL => new NpgsqlCommand(sql, (NpgsqlConnection)connection),
        _ => new SqliteCommand(sql, (SqliteConnection)connection),
    };

    private void Fail(string what, Exception e)
    {
        // One failure turns saving off for the rest of the map load, so a missing table or a refused CREATE logs once
        // instead of on every click; everyone keeps working with defaults and in-memory changes.
        if (_failed) return;
        _failed = true;
        CounterStrikeSharp.API.Server.NextFrame(() =>
            _plugin.Utils.LogError($"[HUD] settings {what} failed, using defaults without saving until the next restart " +
                                   $"(run sharptimer-hud-settings.sql if the table is missing): {e.Message}"));
    }
}
