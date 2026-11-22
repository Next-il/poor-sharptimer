#!/usr/bin/env python3
"""
Writes the SharpTimer Panorama layouts and the generated half of their stylesheets.

    python3 -I hud/tools/gen_layouts.py

Outputs (all under hud/panorama/):
    layout/custom_game/sharptimer_hud.xml     the HUD: timer card, speed + keys, toast, records feed
    layout/custom_game/sharptimer_menu.xml    the !hud settings menu with the mini map
    styles/custom_game/sharptimer_hud.css     everything below the GENERATED marker is rewritten
    styles/custom_game/sharptimer_menu.css    same

One table of positions feeds the screen CSS, the mini map CSS and (by hand, same names) the C# in
src/Hud/HudLayout.cs, so a position the menu offers is a position the layout can draw. Sizes are 1080p
pixels at size M; S and L are ui-scale 85% and 115%.
"""
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[1] / "panorama"
LAYOUTS = ROOT / "layout" / "custom_game"
STYLES = ROOT / "styles" / "custom_game"
MARK = "/* ===== GENERATED below by hud/tools/gen_layouts.py - edit the script, not this block ===== */"

# ---------------------------------------------------------------------------------------------- geometry
# Every spot is clear of CS2's own HUD on a 16:9 screen: radar + money (top left, to y 330), score
# (top center, to y 70), kill feed (top right, from y 74), chat (bottom left, from about y 620), HP and
# ammo (bottom corners, from y 1000) and the spectator bar (bottom center, from y 990).
POSITIONS = [
    # id    label                   h        v        x     y     ty
    ("tl", "Top left",              "left",   "top",    18,  352,   0),   # under the radar and money
    ("tc", "Top center",            "center", "top",     0,   80,   0),   # under the score
    ("tr", "Top right",             "right",  "top",    16,  158,   0),   # under the kill feed
    ("ml", "Middle left",           "left",   "center", 380,   0, -30),
    ("xa", "Above the crosshair",   "center", "bottom",  0,  586,   0),   # bottom edge at y 494
    ("xu", "Under the crosshair",   "center", "top",     0,  586,   0),   # 46px under the crosshair
    ("mr", "Middle right",          "right",  "center", 380,   0, -30),
    ("bc", "Bottom center",         "center", "bottom",  0,  118,   0),   # clears the spectator bar
    ("br", "Bottom right",          "right",  "bottom", 16,   96,   0),   # above the ammo
]
POS_IDS = [p[0] for p in POSITIONS]
SIZES = [("s", 0.85), ("m", 1.0), ("l", 1.15)]
CARD_W, CARD_H = 372, 110          # the card alone; the toast slot adds 36 above or below
CLUSTER_W, CLUSTER_H = 174, 165    # fixed width: speed box and key grid are centered in it
TOAST_H = 30
FEED_W, FEED_H = 400, 170          # five 30px rows with 4px gaps, widest expected row
GAP = 12
TOP_ANCHORED = {"tl", "tc", "tr", "xu"}   # the in-card toast goes under the card here
SEGMENTS = 16
FEED_SLOTS = 5
STEPS = 50                         # bar ladders p0..p50, 2% a step
KEYS = [("w", "W", ""), ("a", "A", ""), ("s", "S", ""), ("d", "D", ""), ("e", "E", "k-e"), ("c", "DUCK", "wide"), ("j", "JUMP", "wide")]
RANK_ICONS = ["unranked", "bronze", "silver1", "silver2", "silver3", "gold1", "gold2", "gold3", "plat1", "plat2", "plat3",
              "dia1", "dia2", "dia3", "master1", "master2", "master3", "legend1", "legend2", "legend3", "royal1", "royal2",
              "royal3", "god"]
# CounterStrikeSharp chat colour tokens a ranks.json can use, as the HUD colour of the rank name.
RANK_COLORS = {
    "default": "#c4c9cf", "white": "#e8ebee", "darkred": "#d23b3b", "green": "#4fd17f", "lightyellow": "#f3e7a2",
    "lightblue": "#77bdf2", "olive": "#b6cf3a", "lime": "#a2f05a", "red": "#ff4040", "lightpurple": "#c49bff",
    "purple": "#b46bff", "grey": "#aeb5bc", "yellow": "#e9d64e", "gold": "#f2c14e", "silver": "#c7ccd2",
    "blue": "#62c4ff", "darkblue": "#4b6fe0", "bluegrey": "#9fb4c8", "magenta": "#e05ad1", "lightred": "#ff7a7a",
    "orange": "#f39a3a",
}


def px(v):
    v = round(v, 1)
    return f"{int(v)}px" if v == int(v) else f"{v}px"


def anchor_css(h, v, x, y, ty, k=1.0, tx=0.0):
    """Alignment + margins for one anchor, scaled by k (1 on screen, the mini map factor in the menu)."""
    out = [f"horizontal-align: {h};", f"vertical-align: {v};"]
    if h == "left":
        out.append(f"margin-left: {px(x * k)};")
    elif h == "right":
        out.append(f"margin-right: {px(x * k)};")
    if v == "top":
        out.append(f"margin-top: {px(y * k)};")
    elif v == "bottom":
        out.append(f"margin-bottom: {px(y * k)};")
    if tx or ty:
        out.append(f"transform: translate3d({px(tx * k)}, {px(ty * k)}, 0px);")
    return " ".join(out)


def beside(pos, card_scale, cluster_scale, k=1.0):
    """The speed + keys cluster next to the card: on the side that faces the middle of the screen."""
    _, _, h, v, x, y, ty = next(p for p in POSITIONS if p[0] == pos)
    cw = CARD_W * card_scale
    if h == "left":
        return anchor_css("left", v, x + cw + GAP, y, ty, k)
    if h == "right":
        return anchor_css("right", v, x + cw + GAP, y, ty, k)
    return anchor_css("center", v, 0, y, ty, k, tx=cw / 2 + GAP + CLUSTER_W * cluster_scale / 2)


# ---------------------------------------------------------------------------------------------- XML helpers
def P(id_=None, cls=None, children="", hit=False, tag="Panel"):
    a = (f' id="{id_}"' if id_ else "") + (f' class="{cls}"' if cls else "") + ("" if hit else ' hittest="false"')
    return f"<{tag}{a}>{children}</{tag}>" if children else f"<{tag}{a} />"


def L(text, cls=None, id_=None):
    a = (f' id="{id_}"' if id_ else "") + (f' class="{cls}"' if cls else "")
    return f'<Label{a} text="{text}" hittest="false" />'


def B(id_, cls, children):
    return f'<Button id="{id_}" class="{cls}">{children}</Button>'


def indent(xml, n):
    """Pretty-prints a fragment, one element per line, tab-indented from depth n."""
    from xml.dom import minidom
    pretty = minidom.parseString(f"<x>{xml.replace(chr(10), '')}</x>").documentElement.toprettyxml(indent="\t")
    lines = [l for l in pretty.split("\n")[1:-2] if l.strip()]
    lines = [l[1:] if l.startswith("\t") else l for l in lines]
    return "\n".join("\t" * n + l.replace("\u2715", "&#x2715;").replace("/>", " />").replace("  />", " />") for l in lines)


# ---------------------------------------------------------------------------------------------- HUD layout
def toast(id_, extra=""):
    split = P(cls="ts-split", children="".join([
        L("{s:ts_t}", "ts-t"),
        P(cls="ts-d ts-d1", children=L("{s:ts_d1}", "ts-dv") + L("PB", "ts-ds")),
        P(cls="ts-d ts-d2", children=L("{s:ts_d2}", "ts-dv") + L("SR", "ts-ds")),
        L("{s:ts_v}", "ts-v"),
        L("{s:ts_vd}", "ts-vd"),
    ]))
    msg = P(cls="ts-msg", children=L("{s:ts_mb}", "ts-mb") + L("{s:ts_m}", "ts-m"))
    return P(id_, f"ts k-msg {extra}".strip(), L("{s:ts_k}", "ts-k") + split + msg)


def hud_xml():
    keys_rows = [
        P(cls="ks-row", children=P("k_w", "k", L("W", "k-l"))),
        P(cls="ks-row", children="".join(P(f"k_{k}", f"k {c}".strip(), L(t, "k-l")) for k, t, c in KEYS[1:5])),
        P(cls="ks-row", children="".join(P(f"k_{k}", f"k {c}".strip(), L(t, "k-l")) for k, t, c in KEYS[5:])),
    ]
    cluster = P("sc", "sc", "".join([
        P("sc_box", "sc-box", "".join([
            P(cls="sc-top", children=L("{s:sp_val}", "sc-num", "sp_num") + L("U/S", "sc-unit")),
            P("sp_bar", "sc-bar", P("sp_fill", "sc-fill p0") + P("sp_cap", "sc-cap cap-5")),
            P(cls="sc-sub", children=L("PRE", "sc-sk") + L("{s:sp_pre}", "sc-sv")
              + L("SYNC", "sc-sk sc-sync") + L("{s:sp_sync}", "sc-sv sc-sync")),
            L("{s:sc_chip}", "sc-chip"),
        ])),
        P("ks", "ks", "".join(keys_rows)),
    ]))

    segs = "".join(P(f"sg{i}", "sg") for i in range(SEGMENTS))
    card = P("tc", "tc st-start", "".join([
        P(cls="tc-eye", children="".join([
            P(cls="tc-ico"),
            L("{s:tc_lead}", "tc-lead"),
            L("{s:tc_who}", "tc-who"),
            L("{s:tc_map}", "tc-map"),
            L("{s:c_style}", "chip c-style"),
            L("{s:c_mode}", "chip c-mode"),
        ])),
        P(cls="tc-main", children="".join([
            L("{s:tc_time}", "tc-time", "tc_time"),
            P(cls="tc-side", children=P(cls="tc-stage", children=L("{s:tc_stagek}", "tc-stk") + L("{s:tc_stage}", "tc-stv"))
              + L("{s:tc_place}", "tc-place")),
            L("{s:tc_diff}", "tc-diff"),
        ])),
        P(cls="tc-cmp", children="".join([
            P(cls="cmp cmp-pb", children=L("PB", "cmp-k") + L("{s:tc_pb}", "cmp-v")),
            P(cls="cmp cmp-sr", children=L("{s:tc_srk}", "cmp-k") + L("{s:tc_sr}", "cmp-v") + L("{s:tc_srby}", "cmp-by")),
            P(cls="fill"),
            P("tc_ricon", "tc-ricon"),
            L("{s:tc_rank}", "tc-rank", "tc_rank"),
        ])),
        P(cls="tc-res", children=L("{s:tc_res}", "tc-rest") + L("{s:tc_pts}", "tc-pts")),
        P("tc_segs", "tc-segs", segs),
        P(cls="tc-prog", children=P("tc_prog", "tc-pfill p0")),
    ]))

    def feed_slot(n):
        row = P(f"fd{n}_row", "fd-row k-fin", "".join([
            P(cls="fd-ico"),
            L(f"{{s:fd{n}_n}}", "fd-n"),
            L(f"{{s:fd{n}_tag}}", "fd-tag"),
            L(f"{{s:fd{n}_t}}", "fd-t"),
            L(f"{{s:fd{n}_d}}", "fd-d"),
        ]))
        return P(f"fd{n}", "fd-slot hidden", row)

    groups = "\n".join([
        "<!-- speed + keys -->",
        P("st_cl", "st-pos p-xu", P("st_cl_sz", "st-sz sz-m", cluster)),
        "<!-- toast (splits and replies) above or under the card, then the card -->",
        P("st_cg", "st-pos p-bc", P("st_cg_sz", "st-sz sz-m",
            P("st_cg_in", "cg-in", toast("tu", "ts-up") + card + toast("td", "ts-dn hidden")))),
        "<!-- the toast on its own, when the player puts it somewhere else -->",
        P("st_tf", "st-pos p-tc", P("st_tf_sz", "st-sz sz-m", toast("tf", "ts-free"))),
        "<!-- other players' records -->",
        P("st_fd", "st-pos p-tr", P("st_fd_sz", "st-sz sz-m", P("fd", "fd", "".join(feed_slot(n) for n in range(FEED_SLOTS))))),
    ])
    groups = groups.replace("><", ">\n<")

    pos_list = " ".join(POS_IDS)
    head = f"""<root>
	<styles>
		<include src="s2r://panorama/styles/custom_game/hud_shared.vcss_c" />
		<include src="s2r://panorama/styles/custom_game/sharptimer_hud.vcss_c" />
	</styles>

	<!--
		SharpTimer HUD - generated by hud/tools/gen_layouts.py. Driven by src/Hud/TimerHud.cs.
		Replaces the center HTML: timer card, speed + keys, one toast slot for splits and replies, and a
		feed of other players' records. Every group sits wherever the player put it with !hud.

		CONTRACT
		  RootPanelId = "StHudRoot"   RevealClass = "show"   RowCount = 0
		  CaptureInput = false        HideHud = None         SharedText = false

		NOT INTERACTIVE. hittest false everywhere.

		PLACEMENT (one-of classes on the group's outer panel; size on the _sz panel)
		  st_cg st_cl st_tf st_fd   p-{{{pos_list.replace(' ', '|')}}}
		  st_cl                     nx-<cardpos>-<cardsize>-<clustersize>   next to the card
		  st_cg_sz st_cl_sz st_tf_sz st_fd_sz   sz-s | sz-m | sz-l   (ui-scale 85 / 100 / 115%)

		DIALOG VARIABLES
		  sp_val sp_pre sp_sync sc_chip
		  tc_lead tc_who tc_map c_style c_mode tc_time tc_stagek tc_stage tc_place tc_diff
		  tc_pb tc_srk tc_sr tc_srby tc_rank tc_res tc_pts
		  ts_k ts_t ts_d1 ts_d2 ts_v ts_vd ts_mb ts_m          (shared by the three toast panels)
		  fdN_n fdN_tag fdN_t fdN_d   N = 0..{FEED_SLOTS - 1}

		CLASSES THE SERVER TOGGLES
		  sc        show zone flash capped vc nospeed nobar nosync nokeys
		  sp_num    v0..v10         sp_fill  p0..p{STEPS}     sp_cap  cap-0..cap-{STEPS}
		  ks        v-row chute     k_w k_a k_s k_d k_e k_c k_j   on no
		  tc        show st-start|st-idle|st-run|st-done|st-pb|st-sr|st-cancel|st-replay
		            spec bonus rp-pb nosr nopb noseg noplace nostage nostyle nomode lead-empty who-empty
		            diff-ahead diff-behind rk-icon rk-text pts
		  tc_time   pop-a pop-b     tc_rank  rc-<chat colour>   tc_ricon  ri-<icon>
		  sg0..sg{SEGMENTS - 1}  x cur ahead behind done        tc_prog  p0..p{STEPS}
		  tu td tf  show re-a re-b k-split k-msg err hidden d1-ahead d1-behind d1-off d2-ahead d2-behind d2-off
		            vd-ahead vd-behind vd-off v-off
		  fdN hidden   fdN_row  show k-sr|k-pb|k-fin|k-stg nod
	-->
	<Panel class="hud-screen st-screen" hittest="false">
		<Panel id="StHudRoot" class="st-root" hittest="false">
"""
    tail = """
		</Panel>
	</Panel>
</root>
"""
    return head + indent(groups, 3) + tail


# ---------------------------------------------------------------------------------------------- menu layout
GROUPS = [("card", "TIMER CARD"), ("cl", "SPEED AND KEYS"), ("ts", "SPLITS"), ("fd", "RECORDS FEED")]


def opt_row(key, desc, opts, id_=None):
    buttons = "".join(B(oid, "se-opt", L(text, "se-opt-lbl")) for oid, text in opts)
    return P(cls="se-row", children=L(key, "se-k") + (L(desc, "se-d") if desc else "") + P(id_, "se-opts", buttons))


def menu_xml():
    tabs = "".join(B(f"mg_{i}", "sm-tab", L(t, "sm-tab-lbl")) for i, (_, t) in enumerate(GROUPS)) + P("sm_bar", "sm-bar at-0")
    stand_in = "".join(P(cls=f"mm-cs mm-{n}") for n in ["radar", "money", "score", "kill", "chat", "hp", "ammo", "x"])
    blocks = "".join([
        P("mb_fd", "mm-blk mm-fd p-tr sz-m", L("RECORDS", "mm-blk-l")),
        P("mb_ts", "mm-blk mm-ts wc-bc-m sz-m", L("SPLITS", "mm-blk-l")),
        P("mb_card", "mm-blk mm-card p-bc sz-m sel", L("TIMER", "mm-blk-l")),
        P("mb_cl", "mm-blk mm-cl p-xu sz-m", L("SPEED", "mm-blk-l")),
    ])
    zones = "".join(B(f"mz_{p}", f"mm-z z-{p}", P(cls="mm-z-in")) for p in POS_IDS)
    left = P(cls="sm-left", children="".join([
        P(cls="sm-lhead", children=L("WHERE IT GOES", "sm-k") + P(cls="fill") + L("{s:sm_where}", "sm-where")),
        P("mm", "mm", stand_in + blocks + zones),
        P(cls="sm-with", children=B("mz_with", "se-opt sm-with-btn", L("{s:sm_with}", "se-opt-lbl")) + L("Click a spot on the screen to move it.", "sm-hint")),
    ]))
    sizes = opt_row("SIZE", "", [("ms_s", "S"), ("ms_m", "M"), ("ms_l", "L")], "ms_opts")
    pages = "".join([
        P("mp_0", "sm-page", opt_row("TIMER HUD", "The card, speed and keys. Same as !hud on and !hud off.", [("mo_hud_on", "ON"), ("mo_hud_off", "OFF")])),
        P("mp_1", "sm-page", opt_row("SPEED", "Speed, pre and sync.", [("mo_spd_on", "ON"), ("mo_spd_off", "OFF")])
          + opt_row("KEYS", "Same as !keys.", [("mo_keys_on", "ON"), ("mo_keys_off", "OFF")])
          + opt_row("KEYS LAYOUT", "", [("mo_kl_grid", "GRID"), ("mo_kl_row", "ONE ROW")])),
        P("mp_2", "sm-page", opt_row("SPLITS", "Stage and checkpoint times. Same as !splits.", [("mo_spl_on", "ON"), ("mo_spl_off", "OFF")])),
        P("mp_3", "sm-page", opt_row("RECORDS FEED", "Other players' finishes, PBs and records.", [("mo_feed_on", "ON"), ("mo_feed_off", "OFF")])),
    ])
    right = P(cls="sm-right", children=sizes + pages)
    foot = "".join([
        B("m_reset", "sm-reset", L("RESET ALL", "sm-reset-lbl")),
        P(cls="fill"),
        L("!hud", "sm-cmd"), L("THIS MENU", "sm-foot-t"),
        L("!keys", "sm-cmd"), L("KEYS", "sm-foot-t"),
        L("!splits", "sm-cmd"), L("SPLITS", "sm-foot-t"),
    ])
    body = "\n".join([
        P(cls="hud-head", hit=True, children="".join([
            L("TIMER HUD", "hud-title sm-title"), L("{s:sm_tag}", "hud-tag sm-tag"), P(cls="fill"),
            B("sm_close", "hud-exit", L("&#x2715;", "hud-x")),
        ])),
        P(cls="sm-tabs", hit=True, children=tabs),
        P(cls="sm-body", children=left + right),
        P(cls="hud-foot sm-foot", hit=True, children=foot),
    ]).replace("><", ">\n<")
    head = f"""<root>
	<styles>
		<include src="s2r://panorama/styles/custom_game/hud_shared.vcss_c" />
		<include src="s2r://panorama/styles/custom_game/sharptimer_menu.vcss_c" />
	</styles>

	<!--
		!hud - where each part of the timer HUD goes. Generated by hud/tools/gen_layouts.py, driven by
		src/Hud/HudMenu.cs. The HUD itself moves behind the menu as the player clicks.

		CONTRACT
		  RootPanelId = "StMenuRoot"   RevealClass = "show"   CloseButtonId = "sm_close"   RowCount = 0
		  CaptureInput = true          SharedText = false

		BUTTONS (PanelAction.Button, by id)
		  mg_0..mg_3                 the part: timer card, speed and keys, splits, records feed
		  mz_{{{"|".join(POS_IDS)}}}   put it there (the nine spots on the mini map)
		  mz_with                    next to the card (speed and keys) / with the card (splits)
		  ms_s ms_m ms_l             size
		  mo_hud_on|off mo_spd_on|off mo_keys_on|off mo_kl_grid|row mo_spl_on|off mo_feed_on|off
		  m_reset                    every option back to the server default

		CLASSES THE SERVER TOGGLES
		  mg_N mp_N active    sm_bar at-0..at-3    mz_* cur    mz_with on hidden    ms_* mo_* on
		  mb_card mb_cl mb_ts mb_fd   p-* | nx-<pos>-<cs>-<ks> (mb_cl) | wc-<pos>-<cs> (mb_ts)   sz-s|m|l   sel off

		DIALOG VARIABLES
		  sm_tag sm_where sm_with
	-->
	<Panel class="hud-screen" hittest="false">
		<Panel id="StMenuRoot" class="hud-root hud-card sm-root">
"""
    tail = """
		</Panel>
	</Panel>
</root>
"""
    return head + indent(body, 3) + tail


# ---------------------------------------------------------------------------------------------- generated CSS
def hud_css():
    out = [MARK, "", "/* positions: one class per spot, on the group's outer panel */"]
    for pid, label, h, v, x, y, ty in POSITIONS:
        out.append(f".st-pos.p-{pid} {{ {anchor_css(h, v, x, y, ty)} }}   /* {label} */")
    out += ["", "/* speed + keys next to the card: nx-<card position>-<card size>-<cluster size> */"]
    for pid in POS_IDS:
        for cs, cv in SIZES:
            for ks, kv in SIZES:
                out.append(f".st-pos.nx-{pid}-{cs}-{ks} {{ {beside(pid, cv, kv)} }}")
    out += ["", "/* the feed lines up with its edge and slides in from it */"]
    for pid, _, h, v, *_ in POSITIONS:
        align = h
        if h == "right":
            start = "translate3d(420px, 0px, 0px)"
        elif h == "left":
            start = "translate3d(-420px, 0px, 0px)"
        else:
            start = "translate3d(0px, -30px, 0px)" if v == "top" else "translate3d(0px, 30px, 0px)"
        out.append(f".st-pos.p-{pid} .fd-slot {{ horizontal-align: {align}; }}")
        out.append(f".st-pos.p-{pid} .fd-row {{ transform: {start}; }}")
    out.append("/* after the starts above, and more specific: a shown row is in place */")
    out.append(".st-pos .fd-slot .fd-row.show { opacity: 1; blur: gaussian(0.0); transform: translate3d(0px, 0px, 0px); }")
    out += ["", "/* bar ladders: 2% a step. clip survives a text rewrite in the same subtree, width would not */"]
    for i in range(STEPS + 1):
        out.append(f".sc-fill.p{i}, .tc-pfill.p{i} {{ clip: rect( 0%, {i * 2}%, 100%, 0% ); }}")
    out += ["", "/* the start-speed cap marker on the 150px speed bar */"]
    for i in range(STEPS + 1):
        out.append(f".sc-cap.cap-{i} {{ transform: translate3d({px(i * 3 - 1)}, -3px, 0px); }}")
    out += ["", "/* rank name colour, from the rank's chat colour in ranks.json */"]
    for name, col in RANK_COLORS.items():
        out.append(f".tc-rank.rc-{name} {{ color: {col}; }}")
    out += ["", "/* rank icons, baked into the addon from remote_data/rank_icons (18x20) */"]
    for name in RANK_ICONS:
        out.append(f'.tc-ricon.ri-{name} {{ background-image: url("s2r://panorama/images/custom_game/sharptimer/ranks/{name}.png"); }}')
    return "\n".join(out) + "\n"


MAP_W, MAP_H = 448, 252
K = MAP_W / 1920.0
BLOCK = {"card": (CARD_W, CARD_H), "cl": (CLUSTER_W, CLUSTER_H), "ts": (400, 40), "fd": (FEED_W, FEED_H)}


def menu_css():
    out = [MARK, "", f"/* mini map: the screen at {MAP_W}x{MAP_H}, k = {K:.4f} */"]
    for pid, label, h, v, x, y, ty in POSITIONS:
        out.append(f".mm-blk.p-{pid}, .mm-z.z-{pid} {{ {anchor_css(h, v, x, y, ty, K)} }}   /* {label} */")
    out += ["", "/* block sizes */"]
    for g, (w, hgt) in BLOCK.items():
        for s, sv in SIZES:
            out.append(f".mm-{g}.sz-{s} {{ width: {px(w * sv * K)}; height: {px(max(hgt * sv * K, 8))}; }}")
    out += ["", "/* speed + keys next to the card */"]
    for pid in POS_IDS:
        for cs, cv in SIZES:
            for ks, kv in SIZES:
                out.append(f".mm-blk.nx-{pid}-{cs}-{ks} {{ {beside(pid, cv, kv, K)} }}")
    out += ["", "/* the toast with the card: above it, or under it when the card hangs from the top */"]
    th = BLOCK["ts"][1] * K
    for pid, _, h, v, x, y, ty in POSITIONS:
        for cs, cv in SIZES:
            ch = CARD_H * cv * K
            if v == "top":
                css = anchor_css(h, "top", x, 0, 0, K).replace("margin-top: 0px;", f"margin-top: {px(y * K + ch + 1)};")
            elif v == "bottom":
                css = anchor_css(h, "bottom", x, 0, 0, K).replace("margin-bottom: 0px;", f"margin-bottom: {px(y * K + ch + 1)};")
            else:
                css = anchor_css(h, "center", x, 0, 0, K) + f" transform: translate3d(0px, {px(ty * K - ch / 2 - 1 - max(th, 8) / 2)}, 0px);"
            out.append(f".mm-blk.wc-{pid}-{cs} {{ {css} }}")
    return "\n".join(out) + "\n"


def write_css(path, generated):
    text = path.read_text() if path.exists() else ""
    head = text.split(MARK)[0].rstrip() + "\n\n" if MARK in text or text else ""
    path.write_text(head + generated)


def main():
    (LAYOUTS / "sharptimer_hud.xml").write_text(hud_xml())
    (LAYOUTS / "sharptimer_menu.xml").write_text(menu_xml())
    write_css(STYLES / "sharptimer_hud.css", hud_css())
    write_css(STYLES / "sharptimer_menu.css", menu_css())
    print("wrote sharptimer_hud.xml, sharptimer_menu.xml and the generated CSS blocks")


if __name__ == "__main__":
    main()
