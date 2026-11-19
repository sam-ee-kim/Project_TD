# 레벨디자인 13×9 고정 맵 보고서: Temp/LevelDesign_Stage3_after.json (LevelDesignLab.StartJob(3) 결과) →
#   Docs/LevelDesign_13x9.md · Docs/LevelDesign_13x9_Report.html · Docs/LevelDesign_13x9_combos.csv · Docs/LevelDesign_13x9_grid.csv
# 실행: python Tools/LevelDesign/make_report_13x9.py <Project_TD 폴더>
# 해석(확인한 점·다음에 정할 것)은 Tools/LevelDesign/findings_13x9.json 에 사람이 적는다.
import csv, html, json, os, sys

root = sys.argv[1]
A = json.load(open(os.path.join(root, "Temp", "LevelDesign_Stage3_after.json"), encoding="utf-8"))
notes_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "findings_13x9.json")
NOTES = json.load(open(notes_path, encoding="utf-8")) if os.path.exists(notes_path) else {"findings": [], "next": []}

TOWERS = ["basic", "flamethrower", "emp", "nano", "lightning", "cryo", "electro", "minigun", "plasma", "dust", "phosphorus"]
NAME = {"basic": "듀얼샷", "flamethrower": "화염방사", "emp": "EMP", "nano": "나노봇", "lightning": "번개", "cryo": "냉기",
        "electro": "전기", "minigun": "미니건", "plasma": "플라즈마", "dust": "분진", "phosphorus": "백린"}
CONCEPTS = A["concepts"]
CODES = [c["code"] for c in CONCEPTS]
CNAME = {c["code"]: c["name"] for c in CONCEPTS}
MAPS = A["maps"]
MULT = A["multipliers"]
TC = A["towerCount"]
SETS = ["컨셉", "비컨셉", "전체"]

def avg(xs):
    xs = list(xs)
    return sum(xs) / len(xs) if xs else 0.0

def fmt(v, d=2):
    s = f"{v:.{d}f}"
    if "." in s:
        s = s.rstrip("0").rstrip(".")
    return s if s else "0"

def esc(s):
    return html.escape(str(s))

def cmaps(code):
    return [m for m in MAPS if m["concept"] == code]

def combo(m, key):
    for c in m["combos"]:
        if c["key"] == key:
            return c
    return None

def set_score(code, i):
    return avg(m["sets"][i]["score"] for m in cmaps(code))

combo_keys = [c["key"] for c in MAPS[0]["combos"]]
combo_avg = {}
for k in combo_keys:
    for code in CODES:
        combo_avg[(k, code)] = avg(combo(m, k)["score"] for m in cmaps(code))
    combo_avg[(k, "all")] = avg(combo(m, k)["score"] for m in MAPS)

def label(k):
    return " + ".join(NAME[t] for t in k.split("+"))

def rank_of(v, vals):
    return 1 + sum(1 for x in vals if x > v)

def concept_rank(code):
    comp = CONCEPTS[CODES.index(code)]["composition"]
    k = "+".join(sorted(set(comp), key=TOWERS.index))
    v = combo_avg.get((k, code), 0)
    return k, v, rank_of(v, [combo_avg[(x, code)] for x in combo_keys])

HPS = sorted({g["hp"] for g in MAPS[0]["grid"]})
WAVES = sorted({g["waves"] for g in MAPS[0]["grid"]})
def grid_avg(code, s, hp, w):
    ms = cmaps(code) if code != "all" else MAPS
    return avg(g["score"] for m in ms for g in m["grid"] if g["set"] == s and g["hp"] == hp and g["waves"] == w)

def survivable(code, s, hp, w):
    ms = cmaps(code) if code != "all" else MAPS
    cells = [g for m in ms for g in m["grid"] if g["set"] == s and g["hp"] == hp and g["waves"] == w]
    return 100.0 * sum(1 for g in cells if g["score"] >= 1) / len(cells) if cells else 0

# ---------- CSV ----------
with open(os.path.join(root, "Docs", "LevelDesign_13x9_combos.csv"), "w", encoding="utf-8-sig", newline="") as f:
    w = csv.writer(f)
    w.writerow(["맵", "컨셉", "스타트 수", "조합", "종류 수", f"실제 배치({TC}개)", "점수(몬스터 양 배율)", "×1 준 피해", "×1 처치", "×1 등장", "×1 넥서스 피해"])
    for m in MAPS:
        for c in m["combos"]:
            w.writerow([m["id"], CNAME[m["concept"]], m["spawnCount"], label(c["key"]), len(c["key"].split("+")),
                        " ".join(NAME[t] for t in c["towers"]), c["score"], round(c["damage"], 1), c["kills"], c["spawned"], c["leak"]])
with open(os.path.join(root, "Docs", "LevelDesign_13x9_grid.csv"), "w", encoding="utf-8-sig", newline="") as f:
    w = csv.writer(f)
    w.writerow(["맵", "컨셉", "조합 방식", "체력 배율(기본 대비)", "웨이브 수", "버틴 몬스터 양 배율", "×1 등장 수", "×1 넥서스 피해"])
    for m in MAPS:
        for g in m["grid"]:
            w.writerow([m["id"], CNAME[m["concept"]], g["set"], g["hp"], g["waves"], g["score"], g["spawned"], g["leak"]])

# ---------- 그래프 ----------
def grouped_bars(title, cats, series, values, ymax, fmtv=lambda v: "×" + fmt(v), note=""):
    W, H, L, R, T, Bm = 760, 300, 52, 16, 26, 58
    n, k = len(cats), len(series)
    step = (W - L - R) / n
    bw = min(26, (step - 18) / k)
    out = [f'<figure class="chart"><figcaption>{esc(title)}</figcaption>',
           '<div class="legend">' + "".join(f'<span><i style="background:var(--s{si+1})"></i>{esc(s)}</span>' for si, s in enumerate(series)) + "</div>",
           f'<svg viewBox="0 0 {W} {H}" role="img" aria-label="{esc(title)}">']
    for g in range(5):
        v = ymax * g / 4
        y = H - Bm - (H - Bm - T) * g / 4
        out.append(f'<line class="grid" x1="{L}" x2="{W-R}" y1="{y:.1f}" y2="{y:.1f}"/><text class="ax" x="{L-6}" y="{y+4:.1f}" text-anchor="end">{esc(fmtv(v))}</text>')
    for ci, c in enumerate(cats):
        x0 = L + step * ci + (step - bw * k - 2 * (k - 1)) / 2
        for si in range(k):
            v = values[ci][si]
            h = (H - Bm - T) * min(v, ymax) / ymax
            x = x0 + si * (bw + 2)
            y = H - Bm - h
            r = min(4, h)
            path = f"M{x:.1f},{H-Bm:.1f} V{y+r:.1f} Q{x:.1f},{y:.1f} {x+r:.1f},{y:.1f} H{x+bw-r:.1f} Q{x+bw:.1f},{y:.1f} {x+bw:.1f},{y+r:.1f} V{H-Bm:.1f} Z" if h > 0.5 else ""
            out.append(f'<g class="hit"><rect x="{x-2:.1f}" y="{T}" width="{bw+4:.1f}" height="{H-Bm-T}" fill="transparent"/>'
                       f'<path d="{path}" fill="var(--s{si+1})"/><title>{esc(c)} · {esc(series[si])}: {esc(fmtv(v))}</title></g>')
        out.append(f'<text class="cat" x="{L + step*ci + step/2:.1f}" y="{H-Bm+18}" text-anchor="middle">{esc(c)}</text>')
    out.append(f'<line class="base" x1="{L}" x2="{W-R}" y1="{H-Bm}" y2="{H-Bm}"/></svg>')
    if note:
        out.append(f'<p class="note">{esc(note)}</p>')
    out.append("</figure>")
    return "\n".join(out)

def heat_table(title, rows, cols, value, fmtv, note="", vmax=None):
    vals = [value(r, c) for r in rows for c in cols]
    vmax = vmax or (max(vals) if vals and max(vals) > 0 else 1)
    out = [f'<figure class="chart"><figcaption>{esc(title)}</figcaption><div class="tw"><table class="heat"><thead><tr><th></th>'
           + "".join(f"<th>{esc(c[1])}</th>" for c in cols) + "</tr></thead><tbody>"]
    for r in rows:
        out.append(f"<tr><th>{esc(r[1])}</th>")
        for c in cols:
            v = value(r, c)
            a = 0.06 + 0.74 * min(1, v / vmax)
            out.append(f'<td style="--a:{a:.2f}" title="{esc(r[1])} · {esc(c[1])}: {esc(fmtv(v))}">{esc(fmtv(v))}</td>')
        out.append("</tr>")
    out.append("</tbody></table></div>")
    if note:
        out.append(f'<p class="note">{esc(note)}</p>')
    out.append("</figure>")
    return "\n".join(out)

cat_labels = [f"{c} {CNAME[c]}" for c in CODES]
ch_sets = grouped_bars("컨셉별 조합 점수 (버틴 몬스터 양 배율, 맵 평균)", cat_labels, SETS,
                       [[set_score(c, i) for i in range(3)] for c in CODES], max(MULT),
                       note=f"체력 = 기본 × {fmt(A['hpScale'])}, 웨이브 {A['waveCount']}개 기준. 컨셉 = 의도한 조합, 비컨셉 = 컨셉 타워를 뺀 같은 단계 타워, 전체 = 같은 단계 타워 전부에서 AI가 고름.")
trow = [(t, NAME[t]) for t in TOWERS]
ccol = [(c, f"{c} {CNAME[c]}") for c in CODES]
ch_single = heat_table(f"타워별 점수: 같은 타워 {TC}개 (컨셉별 맵 평균)", trow, ccol, lambda r, c: combo_avg[(r[0], c[0])], lambda v: "×" + fmt(v, 1), vmax=max(MULT))
ch_dmg = heat_table(f"타워별 대미지표: 같은 타워 {TC}개가 ×1 웨이브에서 준 피해 (컨셉별 맵 평균)", trow, ccol,
                    lambda r, c: avg(combo(m, r[0])["damage"] for m in cmaps(c[0])), lambda v: fmt(v, 0))
def pair(a, b):
    k = a if a == b else "+".join(sorted([a, b], key=TOWERS.index))
    return combo_avg[(k, "all")]
ch_pair = heat_table(f"2종 조합 점수 (맵 전체 평균). 대각선 = 같은 타워 {TC}개", trow, trow, lambda r, c: pair(r[0], c[0]), lambda v: "×" + fmt(v, 1), vmax=max(MULT))

hp_rows = [(h, f"체력 ×{fmt(h)}") for h in HPS]
w_cols = [(w, f"{w}웨이브") for w in WAVES]
grid_charts = []
for code in CODES + ["all"]:
    name = "맵 전체" if code == "all" else f"{code} {CNAME[code]}"
    grid_charts.append(heat_table(f"난이도 표 · {name} · 컨셉 조합: 버틴 몬스터 양 배율 (맵 평균)", hp_rows, w_cols,
                                  lambda r, c, code=code: grid_avg(code, "컨셉", r[0], c[0]), lambda v: "×" + fmt(v, 1), vmax=max(MULT),
                                  note="칸 안 숫자가 1보다 작으면 그 체력·웨이브 수에서는 기본 몬스터 양(×1)도 못 막는다는 뜻입니다."))
grid_free = heat_table("난이도 표 · 맵 전체 · 전체(AI가 고른 최선 조합)", hp_rows, w_cols, lambda r, c: grid_avg("all", "전체", r[0], c[0]),
                       lambda v: "×" + fmt(v, 1), vmax=max(MULT))
grid_ok = heat_table("버틸 수 있는 맵 비율 · 전체 조합 · 몬스터 양 ×1", hp_rows, w_cols, lambda r, c: survivable("all", "전체", r[0], c[0]),
                     lambda v: fmt(v, 0) + "%", vmax=100, note="50개 맵 중 ×1을 막아 낸 맵의 비율. 층별 목표 난이도를 고를 때 '몇 % 맵에서 막히게 할지'의 기준.")

# ---------- 규칙 ----------
RULES = [
    ("가장자리", "넥서스와 모든 스타트는 맵 가장자리에만 (꼭짓점 제외). 경로의 나머지 칸은 가장자리를 따라가지 않는다"),
    ("끝과 끝", "첫 스타트(본선)는 넥서스의 반대 변. 스타트가 1곳이면 꺾임이 없어도 가장자리 끝에서 끝까지"),
    ("넥서스 가까운 스타트", "스타트가 2곳 이상일 때, 두 번째 스타트(지선)부터만. 넥서스와 같은 변에는 둘 수 없음"),
    ("스타트 간격", "스타트끼리 맨해튼 거리 3 이상"),
    ("모양", "끊김 없음, 직진 2칸 이상 후 꺾기, 본선은 넥서스 쪽으로만 진행(역행 없음), 지선은 합류 지점 쪽으로만"),
    ("합류", "지선은 본선 직선 구간(양 끝 2칸 제외)에 수직으로 들어와 그 뒤로 본선을 따라감"),
    ("접촉 금지", "이어지지 않은 경로 칸끼리 상하좌우로 맞닿지 않음 (지름길 금지)"),
    ("최단 경로 일치", "맵 제작 씬의 경로 계산(최단 경로)이 설계한 경로와 같아야 함"),
]
rej = A.get("rejected", {})
counts = {c: len(cmaps(c)) for c in CODES}

# ---------- HTML ----------
css = """
:root{--bg:#fcfcfb;--ink:#0b0b0b;--dim:#52514e;--line:#e3e1dc;--panel:#f3f2ee;--s1:#2a78d6;--s2:#eb6834;--s3:#1baf7a;--heat:42,120,214}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#1a1a19;--ink:#f0efec;--dim:#c3c2b7;--line:#3a3935;--panel:#242422;--s1:#3987e5;--s2:#d95926;--s3:#199e70;--heat:57,135,229}}
:root[data-theme="dark"]{--bg:#1a1a19;--ink:#f0efec;--dim:#c3c2b7;--line:#3a3935;--panel:#242422;--s1:#3987e5;--s2:#d95926;--s3:#199e70;--heat:57,135,229}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.65 "Pretendard","Malgun Gothic",system-ui,sans-serif}
main{max-width:1100px;margin:0 auto;padding:28px 16px 80px}h1{font-size:26px;margin:0 0 6px}h2{font-size:20px;margin:40px 0 10px;border-top:1px solid var(--line);padding-top:18px}
.lead{color:var(--dim)}.note{color:var(--dim);font-size:13px}
.charts{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,520px),1fr));gap:18px}
figure.chart{margin:0;background:var(--panel);border-radius:10px;padding:12px 14px}figcaption{font-weight:700;margin-bottom:4px}
.legend{display:flex;gap:14px;font-size:13px;color:var(--dim);margin-bottom:4px}.legend i{display:inline-block;width:10px;height:10px;border-radius:3px;margin-right:5px;vertical-align:-1px}
svg{width:100%;height:auto;display:block}svg text{fill:var(--dim);font-size:11px;font-family:inherit}svg .cat{fill:var(--ink)}
.grid{stroke:var(--line);stroke-width:1}.base{stroke:var(--dim);stroke-width:1}.hit:hover path{opacity:.8}
.tw{overflow-x:auto}table{border-collapse:collapse;width:100%;font-size:13.5px}th,td{border-bottom:1px solid var(--line);padding:5px 7px;text-align:left;vertical-align:top}
th{color:var(--dim);font-weight:600}td.r,th.r{text-align:right}
table.heat td{text-align:right;background:rgba(var(--heat),var(--a));font-variant-numeric:tabular-nums}
pre.map{margin:0;font:12px/1.15 Consolas,"Malgun Gothic",monospace;letter-spacing:1px}
.warn{background:var(--panel);border-left:4px solid var(--s2);padding:10px 14px;border-radius:6px}
.btn{position:fixed;right:14px;top:12px;border:1px solid var(--line);background:var(--panel);color:var(--ink);border-radius:8px;padding:5px 10px;cursor:pointer}
"""
h = ['<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>13×9 레벨디자인</title>'
     f'<style>{css}</style></head><body>'
     '<button class="btn" onclick="var r=document.documentElement,d=r.dataset.theme||(matchMedia(\'(prefers-color-scheme:dark)\').matches?\'dark\':\'light\');r.dataset.theme=d==\'dark\'?\'light\':\'dark\'">밝게/어둡게</button><main>',
     "<h1>레벨디자인 · 13×9 고정 맵 (규칙 검사 통과 맵만)</h1>",
     f'<p class="lead">맵 크기는 13×9로 고정하고, 난이도는 몬스터 체력 배율 · 몬스터 양 · 웨이브 수로 조절합니다. 컨셉 5개 × 규칙을 지키는 맵 최대 10개 = {len(MAPS)}개 맵.</p>',
     '<p class="warn">이전 1단계(7×7)·2단계(9×9) 테스트 맵은 넥서스가 가운데에 있거나, 스타트가 1곳인데 넥서스 옆에 붙는 등 맵 규칙을 어긴 맵이 섞여 있었습니다. '
     '그 결과는 쓰지 않고, 맵 파일은 <code>LevelDesign_Old/</code>로 옮겼습니다. 맵 제작 씬(Test_MapBuilder)에도 같은 가장자리 규칙 검사를 넣어, 규칙을 어긴 맵은 만들어지지 않습니다.</p>',
     "<h2>맵 규칙과 검사 결과</h2><div class=\"tw\"><table><thead><tr><th>규칙</th><th>내용</th></tr></thead><tbody>"]
for a, b in RULES:
    h.append(f"<tr><td>{esc(a)}</td><td>{esc(b)}</td></tr>")
h.append("</tbody></table></div>")
h.append("<p>컨셉별 맵 수: " + " · ".join(f"{c} {esc(CNAME[c])} {counts[c]}개" for c in CODES) + ". ")
h.append(("버린 후보: " + ", ".join(f"{esc(k)} {v}개" for k, v in rej.items()) + ".") if rej else "후보는 처음부터 규칙에 맞게 만들고, 모든 맵이 검사를 통과했습니다 (버린 후보 없음).")
h.append("</p>")
h.append("<h2>테스트 방법</h2><ul>"
         f"<li>맵마다 타워 <b>{TC}개</b>를 깝니다 (비용 무시, 타워 수를 같게 두고 맵 구조 차이만 봄).</li>"
         f"<li>조합 비교: 체력 = 기본 × {fmt(A['hpScale'])}, 웨이브 {A['waveCount']}개. 몬스터 양에 배율(×{' → ×'.join(fmt(m) for m in MULT)})을 곱해 가며 넥서스 피해가 20에 닿기 전까지 버틴 가장 큰 배율을 점수로 봅니다.</li>"
         f"<li>모든 경우의 수: 타워 11종에서 1~4종을 고른 {len(combo_keys)}가지 조합 × 모든 맵.</li>"
         f"<li>난이도 표: 체력 배율 {' · '.join('×' + fmt(x) for x in HPS)} × 웨이브 {WAVES[0]}~{WAVES[-1]}개마다, 컨셉·비컨셉·전체 조합이 버틴 몬스터 양 배율. 4·5웨이브는 3웨이브 구성을 웨이브마다 30%씩 늘려 만듭니다.</li>"
         "<li>스타트가 2·3곳인 맵(D 급습)은 스타트마다 몬스터 줄이 동시에 나옵니다.</li></ul>")
h.append("<h2>그래프</h2><div class=\"charts\">" + ch_sets + ch_single + "</div><div class=\"charts\">" + ch_dmg + ch_pair + "</div>")
h.append("<h2>난이도 표 (체력 배율 × 웨이브 수 → 버틸 수 있는 몬스터 양)</h2><p class=\"lead\">층별 레벨디자인에 바로 쓰는 표입니다. 예를 들어 어떤 층에서 '체력 ×5, 3웨이브, 몬스터 양 ×2'를 주려면, 그 칸 숫자가 2 근처인 맵·조합이 '딱 막히는' 난이도입니다.</p>"
         "<div class=\"charts\">" + "\n".join(grid_charts) + grid_free + grid_ok + "</div>")
h.append("<h2>컨셉별 결과</h2><div class=\"tw\"><table><thead><tr><th>컨셉</th><th>의도한 조합</th><th class=r>컨셉</th><th class=r>비컨셉</th><th class=r>전체</th><th>모든 조합 중 의도한 조합 순위</th></tr></thead><tbody>")
for c in CONCEPTS:
    k, v, r = concept_rank(c["code"])
    h.append(f"<tr><td>{esc(c['code'])} {esc(c['name'])}</td><td>{esc(' · '.join(NAME[t] for t in c['composition']))}</td>"
             + "".join(f"<td class=r>×{fmt(set_score(c['code'], i))}</td>" for i in range(3))
             + f"<td>{esc(label(k))}: ×{fmt(v)} · {r}위 / {len(combo_keys)}</td></tr>")
h.append("</tbody></table></div>")
if NOTES["findings"]:
    h.append("<h2>확인한 점</h2><ol>" + "".join(f"<li><b>{esc(t)}</b> {esc(d)}</li>" for t, d in NOTES["findings"]) + "</ol>")
if NOTES["next"]:
    h.append("<h2>다음에 정할 것</h2><ul>" + "".join(f"<li>{esc(x)}</li>" for x in NOTES["next"]) + "</ul>")
h.append("<h2>컨셉별 최고 조합 (모든 경우의 수 중 상위 10)</h2><div class=\"charts\">")
for c in CONCEPTS:
    h.append(f"<figure class=\"chart\"><figcaption>{esc(c['code'])} {esc(c['name'])}</figcaption><table><tbody>")
    for i, k in enumerate(sorted(combo_keys, key=lambda k: -combo_avg[(k, c['code'])])[:10]):
        h.append(f"<tr><td>{i+1}</td><td>{esc(label(k))}</td><td class=r>×{fmt(combo_avg[(k, c['code'])])}</td></tr>")
    h.append("</tbody></table></figure>")
h.append("</div>")
h.append("<h2>컨셉 설명</h2><div class=\"tw\"><table><thead><tr><th>컨셉</th><th>맵 구조와 의도</th><th>웨이브</th></tr></thead><tbody>")
for c in CONCEPTS:
    h.append(f"<tr><td>{esc(c['code'])} {esc(c['name'])}</td><td>{esc(c['idea'])}</td><td>{esc(c['waves'])}</td></tr>")
h.append("</tbody></table></div>")
h.append("<h2>맵별 표 (컨셉 조합)</h2><div class=\"tw\"><table><thead><tr><th>맵</th><th>모양</th><th class=r>경로 칸</th><th class=r>스타트</th><th class=r>부지</th><th class=r>점수</th><th class=r>파워량</th><th class=r>처치</th><th>최고 조합</th></tr></thead><tbody>")
for m in MAPS:
    s = m["sets"][0]
    best = max(m["combos"], key=lambda c: c["score"])
    h.append(f"<tr><td>{m['id']}</td><td><pre class=map>{esc(chr(10).join(m['ascii']))}</pre></td><td class=r>{'/'.join(map(str, m['paths']))}</td><td class=r>{m['spawnCount']}</td>"
             f"<td class=r>{m['sites']}</td><td class=r>×{fmt(s['score'])}</td><td class=r>{fmt(s['damage'],0)}</td><td class=r>{s['kills']}/{s['spawned']}</td>"
             f"<td>{esc(label(best['key']))} ×{fmt(best['score'])}</td></tr>")
h.append("</tbody></table></div><p class=\"note\">기호: S 스타트 · N 넥서스 · ■ 경로 · □ 잔디(설치) · ♣ 나무. 원본: LevelDesign_13x9_combos.csv (모든 조합), LevelDesign_13x9_grid.csv (난이도 표).</p></main></body></html>")
open(os.path.join(root, "Docs", "LevelDesign_13x9_Report.html"), "w", encoding="utf-8").write("\n".join(h))

# ---------- Markdown ----------
md = ["# 레벨디자인 · 13×9 고정 맵 (규칙 검사 통과 맵만)", "",
      "> 그래프가 있는 보고서: `Docs/LevelDesign_13x9_Report.html` · 원본: `Docs/LevelDesign_13x9_combos.csv`, `Docs/LevelDesign_13x9_grid.csv`",
      "> 도구: `Assets/Editor/LevelDesignLab.cs` (메뉴 TowerDefence → Balance → Level Design 13x9) · 보고서: `Tools/LevelDesign/make_report_13x9.py` · 맵 파일: `LevelDesign/Stage3/*.json` (맵 제작 씬 상단 ◀ ▶)", "",
      "이전 1·2단계 테스트 맵은 넥서스가 가운데에 있거나 스타트 1곳이 넥서스 옆에 붙는 등 규칙 위반이 섞여 있어 결과를 쓰지 않습니다 (맵 파일은 `LevelDesign_Old/`).", "",
      "## 맵 규칙", "", "| 규칙 | 내용 |", "|---|---|"] + [f"| {a} | {b} |" for a, b in RULES] + [
      "", "컨셉별 맵 수: " + " · ".join(f"{c} {CNAME[c]} {counts[c]}개" for c in CODES) + ". " + (("버린 후보: " + ", ".join(f"{k} {v}개" for k, v in rej.items())) if rej else "버린 후보 없음."), "",
      "## 테스트 방법", "",
      f"- 맵마다 타워 {TC}개 (비용 무시). 조합 비교: 체력 = 기본 × {fmt(A['hpScale'])}, 웨이브 {A['waveCount']}개, 몬스터 양 배율 ×{' → ×'.join(fmt(m) for m in MULT)}.",
      f"- 모든 경우의 수: 타워 11종에서 1~4종 {len(combo_keys)}가지 조합 × 모든 맵.",
      f"- 난이도 표: 체력 배율 {' · '.join('×' + fmt(x) for x in HPS)} × 웨이브 {WAVES[0]}~{WAVES[-1]}개 → 버틴 몬스터 양 배율.", "",
      "## 컨셉별 결과", "", "| 컨셉 | 의도한 조합 | 컨셉 | 비컨셉 | 전체 | 의도한 조합 순위 |", "|---|---|---:|---:|---:|---|"]
for c in CONCEPTS:
    k, v, r = concept_rank(c["code"])
    md.append(f"| {c['code']} {c['name']} | {' · '.join(NAME[t] for t in c['composition'])} | " + " | ".join(f"×{fmt(set_score(c['code'], i))}" for i in range(3))
              + f" | {label(k)} ×{fmt(v)} · {r}위/{len(combo_keys)} |")
md += ["", "## 난이도 표 · 맵 전체 · 컨셉 조합 (버틴 몬스터 양 배율)", "", "| 체력 \\ 웨이브 | " + " | ".join(f"{w}" for w in WAVES) + " |", "|---" * (len(WAVES) + 1) + "|"]
for hp in HPS:
    md.append(f"| ×{fmt(hp)} | " + " | ".join(f"×{fmt(grid_avg('all', '컨셉', hp, w), 1)}" for w in WAVES) + " |")
md += ["", "## 난이도 표 · 맵 전체 · 전체 조합", "", "| 체력 \\ 웨이브 | " + " | ".join(f"{w}" for w in WAVES) + " |", "|---" * (len(WAVES) + 1) + "|"]
for hp in HPS:
    md.append(f"| ×{fmt(hp)} | " + " | ".join(f"×{fmt(grid_avg('all', '전체', hp, w), 1)}" for w in WAVES) + " |")
md += ["", f"## 타워별 점수 (같은 타워 {TC}개)", "", "| 타워 | " + " | ".join(f"{c}" for c in CODES) + " | 평균 |", "|---" * (len(CODES) + 2) + "|"]
for t in TOWERS:
    md.append(f"| {NAME[t]} | " + " | ".join(f"×{fmt(combo_avg[(t, c)], 1)}" for c in CODES) + f" | ×{fmt(combo_avg[(t, 'all')], 1)} |")
if NOTES["findings"]:
    md += ["", "## 확인한 점", ""] + [f"{i+1}. **{t}** {d}" for i, (t, d) in enumerate(NOTES["findings"])]
if NOTES["next"]:
    md += ["", "## 다음에 정할 것", ""] + [f"- {x}" for x in NOTES["next"]]
md += ["", "## 컨셉별 최고 조합 (상위 5)", ""]
for c in CONCEPTS:
    md.append(f"- **{c['code']} {c['name']}**: " + ", ".join(f"{label(k)} ×{fmt(combo_avg[(k, c['code'])])}" for k in sorted(combo_keys, key=lambda k: -combo_avg[(k, c['code'])])[:5]))
md += ["", "## 맵", ""]
for m in MAPS:
    s = m["sets"][0]
    md += [f"### {m['id']} · 경로 {'/'.join(map(str, m['paths']))}칸 · 스타트 {m['spawnCount']} · 컨셉 조합 ×{fmt(s['score'])}", "", "```"] + m["ascii"] + ["```", ""]
open(os.path.join(root, "Docs", "LevelDesign_13x9.md"), "w", encoding="utf-8").write("\n".join(md) + "\n")
print("ok", len(MAPS))
