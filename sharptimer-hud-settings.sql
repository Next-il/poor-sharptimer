-- SharpTimer Panorama HUD: each player's timer HUD settings (!hud, !settings, !hudsettings).
--
-- SharpTimer creates this table itself on the first !hud load or save (CREATE TABLE IF NOT EXISTS, like its
-- other tables), so running this by hand is only needed when the database user cannot create tables. No table
-- prefix on purpose: a player's HUD is the same on every timer server that shares the database.
--
-- Settings holds only what differs from the server defaults in cfg/SharpTimer/hud.json, for example
--   card=tl;cs=l;cl=nx;ky=br;ac=orange;cc=1;fc=1
-- Layout: card / cl / ky / ts / fd = timer card, speed, keys, toast, records feed spot (cl also nx = next to the
-- card, ts also card = with it); cs / cls / kys / tss / fds = their size s|m|l; kr = keys in one row.
-- Show / hide (0 hides): cd card, sp speed, sb speed bar, sy sync, pc PB and SR, rk rank, fo records feed.
-- Style: ac accent (ice|orange|pink|lime|white), cc compact card, op background (lo|md|hi), tx text size (s|m|l).
-- Chat (1 keeps the line in chat): spc splits, ssc start speed, fc finish messages. hs = has opened !hud.
-- Until 11-23 speed and keys were one part: a saved cl is now the speed's spot and the keys take their new
-- default (rw, right side above the weapons). Older rows load as they are; nothing needs migrating by hand.
-- The timer HUD, keys and splits on/off stay in PlayerStats (HideTimerHud, HideKeys, HideChatSpeed) as before.

-- MySQL / MariaDB
CREATE TABLE IF NOT EXISTS PlayerHudSettings (
    SteamID VARCHAR(20) NOT NULL,
    Settings VARCHAR(255) NOT NULL DEFAULT '',
    UpdatedAt INT NOT NULL DEFAULT 0,
    PRIMARY KEY (SteamID)
);

-- PostgreSQL
-- CREATE TABLE IF NOT EXISTS "PlayerHudSettings" (
--     "SteamID" VARCHAR(20) NOT NULL PRIMARY KEY,
--     "Settings" VARCHAR(255) NOT NULL DEFAULT '',
--     "UpdatedAt" INT NOT NULL DEFAULT 0
-- );

-- SQLite
-- CREATE TABLE IF NOT EXISTS PlayerHudSettings (
--     SteamID TEXT NOT NULL PRIMARY KEY,
--     Settings TEXT NOT NULL DEFAULT '',
--     UpdatedAt INTEGER NOT NULL DEFAULT 0
-- );
