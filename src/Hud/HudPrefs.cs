using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace SharpTimer.Hud;

/// <summary>
/// Where a player wants each part of the HUD. The timer HUD on/off, keys on/off and splits on/off stay where
/// they always were (PlayerStats.HideTimerHud / HideKeys / HideChatSpeed, so !hud on|off, !keys and !splits
/// keep working and old rows keep their meaning). Everything new is here.
/// </summary>
public sealed class HudPrefs
{
    public string Card = "bc";
    public string CardSize = "m";
    public string Cluster = "xu";
    public string ClusterSize = "m";
    public string Toast = HudLayout.WithCard;
    public string ToastSize = "m";
    public string Feed = "tr";
    public string FeedSize = "m";
    public bool Speed = true;
    public bool KeysRow;
    public bool FeedOn = true;

    public HudPrefs Clone() => (HudPrefs)MemberwiseClone();

    public static HudPrefs From(HudDefaults d)
    {
        var p = new HudPrefs();
        p.Apply("card", d.Card);
        p.Apply("cs", d.CardSize);
        p.Apply("cl", d.Cluster);
        p.Apply("cls", d.ClusterSize);
        p.Apply("ts", d.Toast);
        p.Apply("tss", d.ToastSize);
        p.Apply("fd", d.Feed);
        p.Apply("fds", d.FeedSize);
        p.Speed = d.Speed;
        p.KeysRow = string.Equals(d.KeysLayout, "row", StringComparison.OrdinalIgnoreCase);
        p.FeedOn = d.FeedOn;
        return p;
    }

    /// <summary>"card=bc;cs=m;cl=xu;...": only what differs from the server defaults, so a default changed
    /// later in hud.json still reaches everyone who never touched that option.</summary>
    public string Encode(HudPrefs defaults)
    {
        var parts = new List<string>();
        void Add(string key, string value, string def) { if (value != def) parts.Add($"{key}={value}"); }
        Add("card", Card, defaults.Card);
        Add("cs", CardSize, defaults.CardSize);
        Add("cl", Cluster, defaults.Cluster);
        Add("cls", ClusterSize, defaults.ClusterSize);
        Add("ts", Toast, defaults.Toast);
        Add("tss", ToastSize, defaults.ToastSize);
        Add("fd", Feed, defaults.Feed);
        Add("fds", FeedSize, defaults.FeedSize);
        Add("sp", Speed ? "1" : "0", defaults.Speed ? "1" : "0");
        Add("kr", KeysRow ? "1" : "0", defaults.KeysRow ? "1" : "0");
        Add("fo", FeedOn ? "1" : "0", defaults.FeedOn ? "1" : "0");
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

        return p;
    }

    /// <summary>Takes one key=value; anything it does not recognise, or a value out of range, is ignored.</summary>
    public void Apply(string key, string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        switch (key)
        {
            case "card": if (HudLayout.IsPosition(v)) Card = v; break;
            case "cl": if (HudLayout.IsPosition(v) || v == HudLayout.Beside) Cluster = v; break;
            case "ts": if (HudLayout.IsPosition(v) || v == HudLayout.WithCard) Toast = v; break;
            case "fd": if (HudLayout.IsPosition(v)) Feed = v; break;
            case "cs": if (HudLayout.IsSize(v)) CardSize = v; break;
            case "cls": if (HudLayout.IsSize(v)) ClusterSize = v; break;
            case "tss": if (HudLayout.IsSize(v)) ToastSize = v; break;
            case "fds": if (HudLayout.IsSize(v)) FeedSize = v; break;
            case "sp": Speed = v != "0"; break;
            case "kr": KeysRow = v == "1"; break;
            case "fo": FeedOn = v != "0"; break;
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

    public HudPrefs Defaults => HudPrefs.From(_plugin.HudSettings.Defaults);

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
