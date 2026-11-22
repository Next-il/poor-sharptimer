-- SharpTimer Panorama HUD: where each player wants each part of the HUD (!hud).
--
-- SharpTimer creates this table itself on the first !hud load or save (CREATE TABLE IF NOT EXISTS, like its
-- other tables), so running this by hand is only needed when the database user cannot create tables. No table
-- prefix on purpose: a player's HUD is the same on every timer server that shares the database.
--
-- Settings holds only what differs from the server defaults in cfg/SharpTimer/hud.json, for example
--   card=tl;cs=l;cl=nx;fd=br;sp=0
-- (card / cl / ts / fd = timer card, speed + keys, toast, records feed position; cs / cls / tss / fds = their
-- size s|m|l; sp = speed on, kr = keys in one row, fo = feed on). The timer HUD, keys and splits on/off stay in
-- PlayerStats (HideTimerHud, HideKeys, HideChatSpeed) as before.

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
