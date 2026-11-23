#!/usr/bin/env python3
"""
Writes the SharpTimer Panorama layouts and the generated half of their stylesheets.

    python3 -I hud/tools/gen_layouts.py

Outputs (all under hud/panorama/):
    layout/custom_game/sharptimer_hud.xml     the HUD: timer card, speed, keys, toast, records feed, map records
    layout/custom_game/sharptimer_menu.xml    !hud: the settings panel (layout, show / hide, style, chat, reset)
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
# ammo (bottom corners, from y 1000), the weapon list (right, stacked up from the ammo: about y 880 with a
# knife and a pistol, y 750 with a full loadout) and the spectator bar (bottom center, from y 990).
POSITIONS = [
    # id    label                        h        v        x     y     ty
    ("tl", "Top left",                   "left",   "top",    18,  352,   0),   # under the radar and money
    ("tc", "Top center",                 "center", "top",     0,   80,   0),   # under the score
    ("tr", "Top right",                  "right",  "top",    16,  158,   0),   # under the kill feed
    ("ml", "Middle left",                "left",   "center", 380,   0, -30),
    ("xa", "Above the crosshair",        "center", "bottom",  0,  586,   0),   # bottom edge at y 494
    ("xu", "Under the crosshair",        "center", "top",     0,  586,   0),   # 46px under the crosshair
    ("mr", "Middle right",               "right",  "center", 380,   0, -30),
    ("bc", "Bottom center",              "center", "bottom",  0,  118,   0),   # clears the spectator bar
    ("br", "Bottom right",               "right",  "bottom", 16,   96,   0),   # above the ammo
    ("rw", "Right, above the weapons",   "right",  "bottom", 16,  340,   0),   # bottom edge at y 740
]
POS_IDS = [p[0] for p in POSITIONS]
SIZES = [("s", 0.85), ("m", 1.0), ("l", 1.15)]
CARD_W, CARD_H = 372, 110          # the card alone; the toast slot adds 36 above or below
CARD_W_C, CARD_H_C = 300, 92       # the compact card
SPEED_W, SPEED_H = 174, 74         # fixed width: the speed box is centered in it
KEYS_W, KEYS_H = 102, 96           # W / A S D / DUCK JUMP, 30px keys
TOAST_H = 30
FEED_W, FEED_H = 400, 170          # five 30px rows with 4px gaps, widest expected row
# Map records: the map's top times. 19px rows with 2px gaps under an 18px head, 6px padding; the player's own
# row (when they are not in it) under the list. 3 rows on screen by default, 10 while !hud is open: at top left
# that ends at y 616, clear of the chat.
MR_W, MR_ROW, MR_ROWS = 236, 19, 10
MR_H = 6 + 18 + 3 * (MR_ROW + 2) + (MR_ROW + 5) + 6    # 3 rows and the own row, the mini map's block
GAP = 12
TOP_ANCHORED = {"tl", "tc", "tr", "xu"}   # the in-card toast goes under the card here
SEGMENTS = 16
FEED_SLOTS = 5
STEPS = 50                         # bar ladders p0..p50, 2% a step
KEYS = [("w", "W", ""), ("a", "A", ""), ("s", "S", ""), ("d", "D", ""), ("e", "E", "k-e"), ("c", "DUCK", "wide"), ("j", "JUMP", "wide")]
# CounterStrikeSharp chat colour tokens a ranks.json can use, as the HUD colour of the rank name.
RANK_COLORS = {
    "default": "#c4c9cf", "white": "#e8ebee", "darkred": "#d23b3b", "green": "#4fd17f", "lightyellow": "#f3e7a2",
    "lightblue": "#77bdf2", "olive": "#b6cf3a", "lime": "#a2f05a", "red": "#ff4040", "lightpurple": "#c49bff",
    "purple": "#b46bff", "grey": "#aeb5bc", "yellow": "#e9d64e", "gold": "#f2c14e", "silver": "#c7ccd2",
    "blue": "#62c4ff", "darkblue": "#4b6fe0", "bluegrey": "#9fb4c8", "magenta": "#e05ad1", "lightred": "#ff7a7a",
    "orange": "#f39a3a",
}

# ---------------------------------------------------------------------------------------------- style options
# The accent: the running card's edge, icon and lead, the style chip, the toast's key, the start chip and the
# start-speed marker. Results keep their own colours (PB gold, SR violet, ahead green, behind red).
ACCENTS = [("ice", "ICE", "#62c4ff"), ("orange", "ORANGE", "#f7783c"), ("pink", "PINK", "#ff6fae"),
           ("lime", "LIME", "#a2e05a"), ("white", "WHITE", "#e8ebee")]
# Background alpha of every HUD panel: light / normal (today) / solid.
OPACITY = {
    "lo": {"tc": ("80", "8c"), "sc": "66", "k": "70", "ts": "99", "fd": "8c", "mr": "8c"},
    "md": {"tc": ("c6", "d0"), "sc": "9e", "k": "a6", "ts": "cc", "fd": "c4", "mr": "c4"},
    "hi": {"tc": ("ee", "f2"), "sc": "e0", "k": "e0", "ts": "f0", "fd": "ee", "mr": "ee"},
}
OPACITY_NAMES = [("lo", "LIGHT"), ("md", "NORMAL"), ("hi", "SOLID")]
TEXT_SCALE = {"s": 0.88, "l": 1.15}
# Every label's font size: (selector, full card, compact card). None = the compact card does not change it.
TEXT = [
    (".tc-time", 40, 32), (".tc-lead", 9.5, 9), (".tc-who", 9.5, 9), (".tc-map", 9.5, None), (".chip", 8.5, 8),
    (".tc-stv", 19, 16), (".tc-stk", 8.5, 8), (".tc-place", 10.5, 10), (".tc-diff", 21, 17),
    (".cmp-k", 8.5, 8), (".cmp-v", 10.5, 10), (".cmp-by", 12.5, 11.5), (".tc-rank", 12.5, 11.5),
    (".tc-rest", 12, 11), (".tc-pts", 12, 11),
    (".sc-num", 29, None), (".sc-unit", 9, None), (".sc-sk", 9, None), (".sc-sv", 9, None), (".sc-chip", 9.5, None),
    (".k-l", 14, None), (".k.wide .k-l", 10, None), (".ks.v-row .k-l", 12, None), (".ks.v-row .k.wide .k-l", 8.5, None),
    (".ts-k", 9.5, None), (".ts-t", 15, None), (".ts-dv", 12, None), (".ts-ds", 8, None), (".ts-v", 10.5, None),
    (".ts-vd", 10.5, None), (".ts-mb", 15, None), (".ts-m", 15, None),
    (".fd-n", 16, None), (".fd-tag", 8.5, None), (".fd-t", 13, None), (".fd-d", 10.5, None),
    (".mr-k", 9, None), (".mr-sub", 8.5, None), (".mr-p", 10, None), (".mr-n", 14, None), (".mr-t", 11.5, None),
    (".mr-empty", 12, None),
]


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


def beside(pos, card_w, speed_scale, k=1.0):
    """The speed readout next to the card: on the side that faces the middle of the screen."""
    _, _, h, v, x, y, ty = next(p for p in POSITIONS if p[0] == pos)
    if h == "left":
        return anchor_css("left", v, x + card_w + GAP, y, ty, k)
    if h == "right":
        return anchor_css("right", v, x + card_w + GAP, y, ty, k)
    return anchor_css("center", v, 0, y, ty, k, tx=card_w / 2 + GAP + SPEED_W * speed_scale / 2)


def beside_classes():
    """(class, css args) for every speed-next-to-the-card spot: nx- with the full card, nxc- with the compact one."""
    for prefix, w in (("nx", CARD_W), ("nxc", CARD_W_C)):
        for pid in POS_IDS:
            for cs, cv in SIZES:
                for ss, sv in SIZES:
                    yield f"{prefix}-{pid}-{cs}-{ss}", (pid, w * cv, sv)


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
    speed = P("sc", "sc", P("sc_box", "sc-box", "".join([
        P(cls="sc-top", children=L("{s:sp_val}", "sc-num", "sp_num") + L("U/S", "sc-unit")),
        P("sp_bar", "sc-bar", P("sp_fill", "sc-fill p0") + P("sp_cap", "sc-cap cap-5")),
        P(cls="sc-sub", children=L("PRE", "sc-sk") + L("{s:sp_pre}", "sc-sv")
          + L("SYNC", "sc-sk sc-sync") + L("{s:sp_sync}", "sc-sv sc-sync")),
        L("{s:sc_chip}", "sc-chip"),
    ])))
    keys = P("ks", "ks", "".join([
        P(cls="ks-row", children=P("k_w", "k", L("W", "k-l"))),
        P(cls="ks-row", children="".join(P(f"k_{k}", f"k {c}".strip(), L(t, "k-l")) for k, t, c in KEYS[1:5])),
        P(cls="ks-row", children="".join(P(f"k_{k}", f"k {c}".strip(), L(t, "k-l")) for k, t, c in KEYS[5:])),
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
            L("!HUD SETTINGS", "chip c-hint"),
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

    def mr_row(id_, cls):
        return P(id_, cls, L(f"{{s:{id_}_p}}", "mr-p") + L(f"{{s:{id_}_n}}", "mr-n") + L(f"{{s:{id_}_t}}", "mr-t"))

    records = P("mr", "mr", "".join([
        P(cls="mr-head", children=P(cls="mr-ico") + L("MAP RECORDS", "mr-k") + P(cls="fill") + L("{s:mr_sub}", "mr-sub")),
        "".join(mr_row(f"mr{n}", "mr-row hidden") for n in range(MR_ROWS)),
        L("{s:mr_empty}", "mr-empty"),
        mr_row("mr_me", "mr-row mr-me hidden"),
    ]))

    groups = "\n".join([
        "<!-- speed -->",
        P("st_sp", "st-pos p-xu", P("st_sp_sz", "st-sz sz-m", speed)),
        "<!-- keys -->",
        P("st_ks", "st-pos p-rw", P("st_ks_sz", "st-sz sz-m", keys)),
        "<!-- toast (splits and replies) above or under the card, then the card -->",
        P("st_cg", "st-pos p-bc", P("st_cg_sz", "st-sz sz-m",
            P("st_cg_in", "cg-in", toast("tu", "ts-up") + card + toast("td", "ts-dn hidden")))),
        "<!-- the toast on its own, when the player puts it somewhere else -->",
        P("st_tf", "st-pos p-tc", P("st_tf_sz", "st-sz sz-m", toast("tf", "ts-free"))),
        "<!-- other players' records -->",
        P("st_fd", "st-pos p-tr", P("st_fd_sz", "st-sz sz-m", P("fd", "fd", "".join(feed_slot(n) for n in range(FEED_SLOTS))))),
        "<!-- the map's top times -->",
        P("st_mr", "st-pos p-tl", P("st_mr_sz", "st-sz sz-m", records)),
    ])
    skin = P("st_skin", "st-skin ac-ice op-md tx-m", groups.replace("><", ">\n<"))

    pos_list = "|".join(POS_IDS)
    accents = "|".join(a for a, _, _ in ACCENTS)
    head = f"""<root>
	<styles>
		<include src="s2r://panorama/styles/custom_game/hud_shared.vcss_c" />
		<include src="s2r://panorama/styles/custom_game/sharptimer_hud.vcss_c" />
	</styles>

	<!--
		SharpTimer HUD - generated by hud/tools/gen_layouts.py. Driven by src/Hud/TimerHud.cs.
		Replaces the center HTML: timer card, speed, keys, one toast slot for splits and replies, a feed
		of other players' records and the map's top times. Every part sits wherever the player put it with !hud.

		CONTRACT
		  RootPanelId = "StHudRoot"   RevealClass = "show"   RowCount = 0
		  CaptureInput = false        HideHud = None         SharedText = false

		NOT INTERACTIVE. hittest false everywhere.

		STYLE (one-of classes on st_skin, which holds every part)
		  ac-{{{accents}}}   op-lo|op-md|op-hi   tx-s|tx-m|tx-l   cc (compact card)

		PLACEMENT (one-of classes on the part's outer panel; size on the _sz panel)
		  st_cg st_sp st_ks st_tf st_fd st_mr   p-{{{pos_list}}}
		  st_sp                     nx-<cardpos>-<cardsize>-<speedsize> (nxc- with the compact card)   next to the card
		  st_cg_sz st_sp_sz st_ks_sz st_tf_sz st_fd_sz st_mr_sz   sz-s | sz-m | sz-l   (ui-scale 85 / 100 / 115%)

		DIALOG VARIABLES
		  sp_val sp_pre sp_sync sc_chip
		  tc_lead tc_who tc_map c_style c_mode tc_time tc_stagek tc_stage tc_place tc_diff
		  tc_pb tc_srk tc_sr tc_srby tc_rank tc_res tc_pts
		  ts_k ts_t ts_d1 ts_d2 ts_v ts_vd ts_mb ts_m          (shared by the three toast panels)
		  fdN_n fdN_tag fdN_t fdN_d   N = 0..{FEED_SLOTS - 1}
		  mr_sub mr_empty mrN_p mrN_n mrN_t mr_me_p mr_me_n mr_me_t   N = 0..{MR_ROWS - 1}

		CLASSES THE SERVER TOGGLES
		  sc        show zone flash capped vc nobar nosync
		  sp_num    v0..v10         sp_fill  p0..p{STEPS}     sp_cap  cap-0..cap-{STEPS}
		  ks        show v-row chute     k_w k_a k_s k_d k_e k_c k_j   on no
		  tc        show st-start|st-idle|st-run|st-done|st-pb|st-sr|st-cancel|st-replay
		            spec bonus rp-pb nosr nopb nocmp noseg noplace nostage nostyle nomode lead-empty who-empty
		            diff-ahead diff-behind rk-icon rk-text pts hint
		  tc_time   pop-a pop-b     tc_rank  rc-<chat colour>   tc_ricon  ri-<icon>
		  sg0..sg{SEGMENTS - 1}  x cur ahead behind done        tc_prog  p0..p{STEPS}
		  tu td tf  show re-a re-b k-split k-msg err hidden d1-ahead d1-behind d1-off d2-ahead d2-behind d2-off
		            vd-ahead vd-behind vd-off v-off
		  fdN hidden   fdN_row  show k-sr|k-pb|k-fin|k-stg nod
		  mr        show empty      mrN  hidden top me      mr_me  hidden
	-->
	<Panel class="hud-screen st-screen" hittest="false">
		<Panel id="StHudRoot" class="st-root" hittest="false">
"""
    tail = """
		</Panel>
	</Panel>
</root>
"""
    return head + indent(skin, 3) + tail


# ---------------------------------------------------------------------------------------------- menu layout
TABS = ["LAYOUT", "SHOW / HIDE", "STYLE", "CHAT", "RESET"]
PARTS = [("card", "TIMER CARD", "TIMER"), ("sp", "SPEED", "SPEED"), ("ks", "KEYS", "KEYS"), ("ts", "SPLITS AND REPLIES", "SPLITS"),
         ("fd", "RECORDS FEED", "RECORDS"), ("mr", "MAP RECORDS", "MAP TOP")]
# Map records' own row on the show / hide tab, under the two columns: off, or when it shows.
MR_SHOW = [("vo_mr_off", "OFF"), ("vo_mr_a", "ALWAYS"), ("vo_mr_j", "ON MAP JOIN"), ("vo_mr_h", "ONLY IN !HUD")]
# Show / hide: (key, name, what it is), two columns.
SHOW = [
    [("hud", "TIMER HUD", "Card and speed. Same as !hud on / off."),
     ("card", "TIMER CARD", "Time, stage, place and the split bar."),
     ("cmp", "PB AND SR", "Your best and the server record."),
     ("rank", "RANK", "Your rank name on the card."),
     ("toast", "SPLIT TOAST", "Stage and checkpoint times. Same as !splits.")],
    [("speed", "SPEED", "Your speed in u/s."),
     ("bar", "SPEED BAR", "The bar under the number."),
     ("sync", "SYNC", "Strafe sync, next to pre."),
     ("keys", "KEYS", "The keys you press. Same as !keys."),
     ("feed", "RECORDS FEED", "Other players' finishes, PBs and records.")],
]


def opt_row(key, desc, opts, id_=None, row_id=None, cls="se-row"):
    buttons = "".join(B(oid, "se-opt", L(text, "se-opt-lbl")) for oid, text in opts)
    return P(row_id, cls, L(key, "se-k") + (L(desc, "se-d") if desc else "") + P(id_, "se-opts", buttons))


def onoff(prefix, on="ON", off="OFF"):
    return [(f"{prefix}_on", on), (f"{prefix}_off", off)]


def menu_xml():
    tabs = "".join(B(f"mg_{i}", "sm-tab", L(t, "sm-tab-lbl")) for i, t in enumerate(TABS)) + P("sm_bar", "sm-bar at-0")

    # ---------------------------------------------------------------- 0: layout
    stand_in = "".join(P(cls=f"mm-cs mm-{n}") for n in ["radar", "money", "score", "kill", "chat", "hp", "ammo", "weap", "x"])
    start = {"card": " p-bc sel", "sp": " p-xu", "ks": " p-rw", "ts": " wc-bc-m", "fd": " p-tr", "mr": " p-tl"}
    blocks = "".join(P(f"mb_{g}", f"mm-blk mm-{g} sz-m" + start[g], L(short, "mm-blk-l"))
                     for g, _, short in [PARTS[5], PARTS[4], PARTS[3], PARTS[0], PARTS[1], PARTS[2]])
    zones = "".join(B(f"mz_{p}", f"mm-z z-{p}", P(cls="mm-z-in")) for p in POS_IDS)
    left = P(cls="sm-left", children="".join([
        P(cls="sm-lhead", children=L("WHERE IT GOES", "sm-k") + P(cls="fill") + L("{s:sm_where}", "sm-where")),
        P("mm", "mm", stand_in + blocks + zones),
        P(cls="sm-with", children=B("mz_with", "se-opt sm-with-btn", L("{s:sm_with}", "se-opt-lbl"))
          + L("Pick a part, then click a spot on the screen.", "sm-hint")),
    ]))
    parts = "".join(B(f"lp_{g}", "lp", L(name, "lp-n") + L(f"{{s:lp_{g}_w}}", "lp-w")) for g, name, _ in PARTS)
    right = P(cls="sm-right", children="".join([
        L("PART", "sm-k lp-head"),
        parts,
        opt_row("SIZE", "", [("ms_s", "S"), ("ms_m", "M"), ("ms_l", "L")], "ms_opts", cls="se-row lo-row"),
        opt_row("KEYS LAYOUT", "", [("mo_kl_grid", "GRID"), ("mo_kl_row", "ONE ROW")], row_id="lo_kl", cls="se-row lo-row hidden"),
    ]))
    page_layout = P("mp_0", "sm-page pg-cols", left + right)

    # ---------------------------------------------------------------- 1: show / hide
    def vh_row(key, name, desc):
        txt = P(cls="vh-txt", children=L(name, "se-k") + L(desc, "se-d"))
        opts = P(cls="se-opts vh-opts", children="".join(B(oid, "se-opt", L(t, "se-opt-lbl")) for oid, t in onoff(f"vo_{key}")))
        return P(f"vr_{key}", "vh-row", txt + opts)
    cols = "".join(P(cls=f"vh-col vh-col{i}", children="".join(vh_row(*r) for r in col)) for i, col in enumerate(SHOW))
    mr_txt = P(cls="vh-txt", children=L("MAP RECORDS", "se-k") + L("The map's top 3, and your place. The top 10 while this menu is open.", "se-d"))
    mr_opts = P(cls="se-opts vh-opts", children="".join(B(oid, "se-opt", L(t, "se-opt-lbl")) for oid, t in MR_SHOW))
    page_show = P("mp_1", "sm-page pg-reset", P(cls="vh-cols", children=cols) + P("vr_mr", "vh-row vh-wide", mr_txt + mr_opts))

    # ---------------------------------------------------------------- 2: style
    accent_btns = "".join(B(f"ss_ac_{a}", "se-opt sw-opt", P(cls=f"sw sw-{a}") + L(label, "se-opt-lbl")) for a, label, _ in ACCENTS)
    accent = P(cls="se-row", children=L("ACCENT", "se-k") + L("The running card, the toast and the start chip.", "se-d")
               + P(cls="se-opts", children=accent_btns))
    style_l = P(cls="st-col st-col0", children=
                opt_row("TIMER CARD", "Compact drops the map name and shrinks the clock.", [("ss_cc_full", "FULL"), ("ss_cc_compact", "COMPACT")])
                + opt_row("TEXT SIZE", "On top of each part's size.", [("ss_tx_s", "S"), ("ss_tx_m", "M"), ("ss_tx_l", "L")]))
    style_r = P(cls="st-col st-col1", children=
                opt_row("BACKGROUND", "How much of the game shows through.", [(f"ss_op_{k}", n) for k, n in OPACITY_NAMES])
                + opt_row("TIMER DECIMALS", "Digits after the second while the clock runs.", [("ss_td_1", "1"), ("ss_td_2", "2"), ("ss_td_3", "3")]))
    page_style = P("mp_2", "sm-page pg-reset", accent + P(cls="st-cols", children=style_l + style_r)
                   + L("Changes show on the HUD behind this menu.", "rs-note st-note"))

    # ---------------------------------------------------------------- 3: chat
    chat_l = P(cls="st-col st-col0", children=
               opt_row("SPLITS", "Stage and checkpoint times. !splits turns them off and on.",
                       [("ch_spl_off", "OFF"), ("ch_spl_hud", "HUD"), ("ch_spl_chat", "HUD + CHAT")])
               + opt_row("KEYS", "The keys you press, on screen. Same as !keys.", onoff("ch_keys")))
    chat_r = P(cls="st-col st-col1", children=
               opt_row("SPEED IN CHAT", "Start speed: 352 when you leave the zone. The HUD shows it under the speed either way.",
                       [("ch_ss_off", "OFF"), ("ch_ss_on", "ON")])
               + opt_row("FINISH MESSAGES", "Finishes, PBs and records in chat, yours and others'. The HUD shows them either way.",
                         [("ch_fin_off", "OFF"), ("ch_fin_on", "ON")]))
    page_chat = P("mp_3", "sm-page pg-cols", chat_l + chat_r)

    # ---------------------------------------------------------------- 4: reset
    page_reset = P("mp_4", "sm-page pg-reset", "".join([
        opt_row("RESET THE LAYOUT", "Every part back to its default spot and size.", [("rs_layout", "RESET LAYOUT")]),
        opt_row("RESET EVERYTHING", "Layout, show and hide, style and chat, with !hud, !keys and !splits back on.", [("rs_all", "RESET EVERYTHING")]),
        L("{s:rs_msg}", "rs-msg"),
        L("Your settings are saved and follow you to every timer server.", "rs-note"),
    ]))

    pages = page_layout + page_show + page_style + page_chat + page_reset
    foot = "".join([
        L("!hud", "sm-cmd"), L("!settings", "sm-cmd sm-cmd2"), L("THIS MENU", "sm-foot-t"),
        L("!hud on|off", "sm-cmd"), L("TIMER HUD", "sm-foot-t"),
        L("!keys", "sm-cmd"), L("KEYS", "sm-foot-t"),
        L("!splits", "sm-cmd"), L("SPLITS", "sm-foot-t"),
    ])
    body = "\n".join([
        P(cls="hud-head", hit=True, children="".join([
            L("TIMER SETTINGS", "hud-title sm-title"), L("{s:sm_tag}", "hud-tag sm-tag"), P(cls="fill"),
            B("sm_close", "hud-exit", L("&#x2715;", "hud-x")),
        ])),
        P(cls="sm-tabs", hit=True, children=tabs),
        P(cls="sm-body", children=pages),
        P(cls="hud-foot sm-foot", hit=True, children=foot),
    ]).replace("><", ">\n<")

    show_ids = " ".join(k for col in SHOW for k, _, _ in col)
    head = f"""<root>
	<styles>
		<include src="s2r://panorama/styles/custom_game/hud_shared.vcss_c" />
		<include src="s2r://panorama/styles/custom_game/sharptimer_menu.vcss_c" />
	</styles>

	<!--
		!hud (also !settings and !hudsettings) - the settings panel for the whole timer HUD. Generated by
		hud/tools/gen_layouts.py, driven by src/Hud/HudMenu.cs. The HUD behind the menu follows every click.

		CONTRACT
		  RootPanelId = "StMenuRoot"   RevealClass = "show"   CloseButtonId = "sm_close"   RowCount = 0
		  CaptureInput = true          SharedText = false

		BUTTONS (PanelAction.Button, by id)
		  mg_0..mg_4                 tabs: layout, show / hide, style, chat, reset
		  lp_card lp_sp lp_ks lp_ts lp_fd lp_mr   the part the layout tab moves
		  mz_{{{"|".join(POS_IDS)}}}   put it there (the spots on the mini map)
		  mz_with                    next to the card (speed) / with the card (splits)
		  ms_s ms_m ms_l             size        mo_kl_grid mo_kl_row   keys layout
		  vo_<k>_on vo_<k>_off       show / hide, k = {show_ids}
		  vo_mr_off vo_mr_a vo_mr_j vo_mr_h   map records: off, always, on map join, only in !hud
		  ss_ac_<accent> ss_cc_full|compact ss_op_lo|md|hi ss_tx_s|m|l ss_td_1|2|3
		  ch_spl_off|hud|chat ch_keys_on|off ch_ss_on|off ch_fin_on|off
		  rs_layout rs_all

		CLASSES THE SERVER TOGGLES
		  mg_N mp_N active    sm_bar at-0..at-4    mz_* cur    mz_with on hidden    lp_* on off
		  ms_* mo_* vo_* ss_* ch_* on    ms_opts disabled    lo_kl hidden    vr_<k> vr_mr disabled
		  mb_card mb_sp mb_ks mb_ts mb_fd mb_mr   p-* | nx-* nxc-* (mb_sp) | wc-* wcc-* (mb_ts)   sz-s|m|l   sel off   cc (mb_card)

		DIALOG VARIABLES
		  sm_tag sm_where sm_with lp_card_w lp_sp_w lp_ks_w lp_ts_w lp_fd_w lp_mr_w rs_msg
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
    out = [MARK, "", "/* positions: one class per spot, on the part's outer panel */"]
    for pid, label, h, v, x, y, ty in POSITIONS:
        out.append(f".st-pos.p-{pid} {{ {anchor_css(h, v, x, y, ty)} }}   /* {label} */")
    out += ["", "/* speed next to the card: nx-<card position>-<card size>-<speed size>, nxc- next to the compact card */"]
    for cls, (pid, cw, sv) in beside_classes():
        out.append(f".st-pos.{cls} {{ {beside(pid, cw, sv)} }}")
    out += ["", "/* the feed lines up with its edge and slides in from it */"]
    for pid, _, h, v, *_ in POSITIONS:
        if h == "right":
            start = "translate3d(420px, 0px, 0px)"
        elif h == "left":
            start = "translate3d(-420px, 0px, 0px)"
        else:
            start = "translate3d(0px, -30px, 0px)" if v == "top" else "translate3d(0px, 30px, 0px)"
        out.append(f".st-pos.p-{pid} .fd-slot {{ horizontal-align: {h}; }}")
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
    # No rank icon images (owner, 11-22): Workshop Tools has no standalone compiler for PNGs, so the rank
    # shows as its coloured name (RankDisplay "text"). The .tc-ricon panel stays for a later vector badge set.

    out += ["", "/* ---- style: accent (ac-*). Only the running and replay card, so result colours stay their own */"]
    for a, _, col in ACCENTS:
        s = f".st-skin.ac-{a}"
        out.append(f"{s} .tc.st-run, {s} .tc.st-replay {{ border-top: 2px solid {col}; }}")
        out.append(f"{s} .tc.st-run .tc-ico, {s} .tc.st-replay .tc-ico {{ wash-color: {col}; }}")
        out.append(f"{s} .tc.st-run .tc-lead, {s} .tc.st-replay .tc-lead {{ color: {col}; }}")
        out.append(f"{s} .chip.c-style {{ color: {col}; background-color: {col}1f; }}")
        out.append(f"{s} .ts-k, {s} .ts-mb {{ color: {col}; }}")
        out.append(f"{s} .sc-chip, {s} .sc-cap, {s} .tc-pfill {{ background-color: {col}; }}")
        out.append(f"{s} .mr-row.me .mr-n, {s} .mr-row.me .mr-p, {s} .mr-me .mr-n, {s} .mr-me .mr-p, {s} .mr-sub {{ color: {col}; }}")
    out.append("/* errors and the capped start keep their colour whatever the accent */")
    out.append(".st-skin .ts.err .ts-k, .st-skin .ts.err .ts-mb { color: #f26450; }")
    out.append(".st-skin .sc.flash.capped .sc-chip { background-color: #f2c14e; color: #1a1405; }")

    out += ["", "/* ---- style: background (op-*), every panel's fill */"]
    for k, o in OPACITY.items():
        s = f".st-skin.op-{k}"
        a, b = o["tc"]
        out.append(f"{s} .tc {{ background-color: gradient(linear, 0% 0%, 0% 100%, from(#12171e{a}), to(#0b0f14{b})); }}")
        out.append(f"{s} .sc-box {{ background-color: #0b0f14{o['sc']}; }}")
        out.append(f"{s} .k {{ background-color: #0b0f14{o['k']}; }}")
        out.append(f"{s} .ts {{ background-color: #0b0f14{o['ts']}; }}")
        out.append(f"{s} .fd-row {{ background-color: #0b0f14{o['fd']}; }}")
        out.append(f"{s} .mr {{ background-color: #0b0f14{o['mr']}; }}")
    out.append("/* pressed and blocked keys and the error toast keep their fill */")
    out.append(".st-skin .k.no { background-color: #0b0f1459; }")
    out.append(".st-skin .k.on { background-color: #f2f4f6; }")
    out.append(".st-skin .k.no.on { background-color: #f26450; }")
    out.append(".st-skin .ts.err { background-color: #2a0f0ccc; }")

    out += ["", "/* ---- style: the compact card (cc) */",
            f".st-skin.cc .tc {{ width: {CARD_W_C}px; padding: 6px 12px 8px 12px; }}",
            ".st-skin.cc .tc-map { visibility: collapse; }",
            ".st-skin.cc .tc-main { margin-top: 3px; }",
            ".st-skin.cc .tc-cmp { margin-top: 6px; }",
            ".st-skin.cc .tc-res { margin-top: 4px; }",
            ".st-skin.cc .tc-rest { max-width: 270px; }",
            ".st-skin.cc .tc-segs { height: 3px; margin-top: 7px; }",
            ".st-skin.cc .tc-prog { margin-top: 7px; }",
            ".st-skin.cc .cmp-by { max-width: 84px; }"]

    out += ["", "/* ---- style: text size (tx-s / tx-l; tx-m is the sizes above) */"]
    for t, k in TEXT_SCALE.items():
        for sel, full, compact in TEXT:
            out.append(f".st-skin.tx-{t} {sel} {{ font-size: {px(full * k)}; }}")
            if compact is not None:
                out.append(f".st-skin.cc.tx-{t} {sel} {{ font-size: {px(compact * k)}; }}")
        out.append(f".st-skin.tx-{t} .sc-num {{ width: {px(76 * k)}; }}")
    out.append("/* the compact card at text size M */")
    for sel, full, compact in TEXT:
        if compact is not None:
            out.append(f".st-skin.cc.tx-m {sel} {{ font-size: {px(compact)}; }}")
    return "\n".join(out) + "\n"


MAP_W, MAP_H = 448, 252
K = MAP_W / 1920.0
BLOCK = {"card": (CARD_W, CARD_H), "sp": (SPEED_W, SPEED_H), "ks": (KEYS_W, KEYS_H), "ts": (400, 40), "fd": (FEED_W, FEED_H),
         "mr": (MR_W, MR_H)}


def menu_css():
    out = [MARK, "", f"/* mini map: the screen at {MAP_W}x{MAP_H}, k = {K:.4f} */"]
    for pid, label, h, v, x, y, ty in POSITIONS:
        out.append(f".mm-blk.p-{pid}, .mm-z.z-{pid} {{ {anchor_css(h, v, x, y, ty, K)} }}   /* {label} */")
    out += ["", "/* block sizes; the compact card (cc) on its own rows */"]
    for g, (w, hgt) in BLOCK.items():
        for s, sv in SIZES:
            out.append(f".mm-{g}.sz-{s} {{ width: {px(w * sv * K)}; height: {px(max(hgt * sv * K, 8))}; }}")
    for s, sv in SIZES:
        out.append(f".mm-card.cc.sz-{s} {{ width: {px(CARD_W_C * sv * K)}; height: {px(max(CARD_H_C * sv * K, 8))}; }}")
    out += ["", "/* speed next to the card */"]
    for cls, (pid, cw, sv) in beside_classes():
        out.append(f".mm-blk.{cls} {{ {beside(pid, cw, sv, K)} }}")
    out += ["", "/* the toast with the card: above it, or under it when the card hangs from the top (wcc-: the compact card) */"]
    th = BLOCK["ts"][1] * K
    for prefix, card_h in (("wc", CARD_H), ("wcc", CARD_H_C)):
        for pid, _, h, v, x, y, ty in POSITIONS:
            for cs, cv in SIZES:
                ch = card_h * cv * K
                if v == "top":
                    css = anchor_css(h, "top", x, 0, 0, K).replace("margin-top: 0px;", f"margin-top: {px(y * K + ch + 1)};")
                elif v == "bottom":
                    css = anchor_css(h, "bottom", x, 0, 0, K).replace("margin-bottom: 0px;", f"margin-bottom: {px(y * K + ch + 1)};")
                else:
                    css = anchor_css(h, "center", x, 0, 0, K) + f" transform: translate3d(0px, {px(ty * K - ch / 2 - 1 - max(th, 8) / 2)}, 0px);"
                out.append(f".mm-blk.{prefix}-{pid}-{cs} {{ {css} }}")
    out += ["", "/* accent swatches */"]
    for a, _, col in ACCENTS:
        out.append(f".sw.sw-{a} {{ background-color: {col}; }}")
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
