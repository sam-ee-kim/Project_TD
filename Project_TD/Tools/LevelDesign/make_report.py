# 레벨디자인 단계 보고서: Temp/LevelDesign_Stage{n}_before.json · _after.json (LevelDesignLab 결과) →
#   Docs/LevelDesign_Stage{n}.md (표) · Docs/LevelDesign_Stage{n}_Report.html (그래프 + 표) · Docs/LevelDesign_Stage{n}_combos.csv (모든 조합)
# 실행: python Tools/LevelDesign/make_report.py <Project_TD 폴더> <단계 번호>
# 실행: python Tools/LevelDesign/make_report.py <Project_TD 폴더>
import csv, html, json, os, sys
from collections import defaultdict

root = sys.argv[1]
STAGE = int(sys.argv[2]) if len(sys.argv) > 2 else 1
TAG = f"Stage{STAGE}"
B = json.load(open(os.path.join(root, "Temp", f"LevelDesign_{TAG}_before.json"), encoding="utf-8"))
A = json.load(open(os.path.join(root, "Temp", f"LevelDesign_{TAG}_after.json"), encoding="utf-8"))
SIZE = A.get("size", 7)
STITLE = f"{STAGE}단계 ({SIZE}×{SIZE})"

TOWERS = ["basic", "flamethrower", "emp", "nano", "lightning", "cryo", "electro", "minigun", "plasma", "dust", "phosphorus"]
NAME = {"basic": "듀얼샷", "flamethrower": "화염방사", "emp": "EMP", "nano": "나노봇", "lightning": "번개", "cryo": "냉기",
        "electro": "전기", "minigun": "미니건", "plasma": "플라즈마", "dust": "분진", "phosphorus": "백린"}
TIER = {"basic": 1, "flamethrower": 1, "emp": 1, "nano": 1, "lightning": 1, "cryo": 2, "electro": 2, "minigun": 2, "plasma": 2, "dust": 2, "phosphorus": 2}
CONCEPTS = A["concepts"]
CODES = [c["code"] for c in CONCEPTS]
CNAME = {c["code"]: c["name"] for c in CONCEPTS}
MULT = A["multipliers"]

CHANGES_2 = [
    ("EMP", "파동 피해", "1", "2", "단독으로도 몬스터를 조금 잡게. 기계 몬스터 2배 상성은 그대로"),
    ("나노봇", "나노필드 취약", "없음", "필드 안 몬스터가 받는 모든 타워 피해 +20%", "CC와 화력 타워를 섞는 조합 유도"),
    ("그 밖", "-", "-", "1차 조정 값 그대로", "플라즈마·화염 계열은 바꾸지 않음 (결정대로)"),
]
CHANGES_1 = [
    ("듀얼샷", "1발 피해", "1", "1.5", "단일 대상 +50%"),
    ("번개", "1발 피해", "4", "6", "단일 대상 +50%"),
    ("냉기", "1발 피해 · 얼음 발동", "3 · 3회", "4.5 · 2회", "단일 대상 +50%, 정지 2번 맞으면"),
    ("전기", "1발 피해 · 감전 발동", "4 · 3회", "6 · 2회", "단일 대상 +50%, 정지 2번 맞으면"),
    ("미니건", "1발 피해", "1", "1.5", "단일 대상 +50%"),
    ("미니건", "예열 · 과열", "예열 1.5초 · 과열 없음", "예열 2초 → 최고 연사 5초 → 과열 3초 휴식", "휴식 뒤 예열부터 (한 주기 10초)"),
    ("미니건", "집중 사격", "없음", "같은 대상을 계속 쏘면 1초마다 한 발 +0.2", "대상이 죽거나 바뀌면 0부터, 예열은 유지"),
    ("화염방사·분진·백린", "공격 범위", "사거리 칸(인식 범위) 안의 부채꼴", "발사구에서 헤드 방향으로 나가는 부채꼴 전체", "사거리 표시는 인식 범위, 피해는 실제 불길 모양"),
    ("화염방사·분진·백린", "최대 대상", "제한 없음", "한 번에 5마리 (가까운 순)", "피해 배율 증가 한도"),
    ("분진", "폭발", "맞은 몬스터만", "주변 1칸, 최대 5마리", "5스택 발동 피해가 번진다"),
    ("백린", "지속 피해", "맞은 몬스터만", "주변 0.5칸, 최대 5마리", "불이 옆 몬스터로 번진다"),
    ("플라즈마", "-", "-", "변경 없음", "고정 일직선이라 그대로"),
]
CHANGES = CHANGES_2 if STAGE >= 2 else CHANGES_1
BEFORE_NOTE = "1차 조정 뒤 수치 (1단계 보고서의 조정 후)" if STAGE >= 2 else "처음 수치"


def by_map(data):
    return {m["id"]: m for m in data["maps"]}

BM, AM = by_map(B), by_map(A)
IDS = [m["id"] for m in A["maps"]]

def combo(m, key):
    for c in m["combos"]:
        if c["key"] == key:
            return c
    return None

def single(m, t):
    return combo(m, t)

def avg(xs):
    xs = list(xs)
    return sum(xs) / len(xs) if xs else 0.0

def concept_maps(data, code):
    return [m for m in data["maps"] if m["concept"] == code]

def fmt(v, d=2):
    s = f"{v:.{d}f}"
    if "." in s:
        s = s.rstrip("0").rstrip(".")  # 소수점 아래 0만 지운다 (100 → "1" 이 되지 않게)
    return s if s else "0"

def pct(a, b):
    return 100.0 * a / b if b else 0.0

# ---------- 집계 ----------
set_names = ["컨셉", "비컨셉", "전체"]
def set_score(data, code, i):
    return avg(m["sets"][i]["score"] for m in concept_maps(data, code))

single_score = {}
single_dmg = {}
for tag, data in (("before", B), ("after", A)):
    for t in TOWERS:
        for code in CODES:
            ms = concept_maps(data, code)
            single_score[(tag, t, code)] = avg(single(m, t)["score"] for m in ms)
            single_dmg[(tag, t, code)] = avg(single(m, t)["damage"] for m in ms)

# 모든 조합: 조합 키 → 컨셉별 평균 점수 (조정 후)
combo_keys = [c["key"] for c in A["maps"][0]["combos"]]
combo_avg = {}
for k in combo_keys:
    for code in CODES:
        combo_avg[(k, code)] = avg(combo(m, k)["score"] for m in concept_maps(A, code))
    combo_avg[(k, "all")] = avg(combo(m, k)["score"] for m in A["maps"])

def rank_of(value, values):
    return 1 + sum(1 for v in values if v > value)

# ---------- CSV (모든 경우의 수) ----------
with open(os.path.join(root, "Docs", f"LevelDesign_{TAG}_combos.csv"), "w", encoding="utf-8-sig", newline="") as f:
    w = csv.writer(f)
    w.writerow(["맵", "컨셉", "조합", "종류 수", "실제 배치(4개)", "점수(배율)", "×1 준 피해", "×1 처치", "×1 등장", "×1 넥서스 피해"])
    for m in A["maps"]:
        for c in m["combos"]:
            w.writerow([m["id"], CNAME[m["concept"]], " + ".join(NAME[t] for t in c["key"].split("+")), len(c["key"].split("+")),
                        " ".join(NAME[t] for t in c["towers"]), c["score"], round(c["damage"], 1), c["kills"], c["spawned"], c["leak"]])


# ---------- 확인한 점 (사람이 적은 해석, 숫자는 이번 결과 기준) ----------
FINDINGS_1 = [
    ("범위 틱 타워는 크게 내려갔습니다.", "같은 타워 " + str(A["towerCount"]) + "개 평균: 화염방사 ×12.1 → ×2.6, 분진 ×19.5 → ×6.2, 백린 ×25.2 → ×9. 한 번에 5마리 제한이 몰려오는 물량 웨이브에서 크게 작용했습니다."),
    ("플라즈마는 바꾸지 않아서 혼자 압도적입니다.", "같은 타워 " + str(A["towerCount"]) + "개 평균 ×29로, 다음인 백린(×9)의 3배가 넘습니다. 561가지 조합 중 상위 6개가 모두 플라즈마가 들어간 조합입니다. 회랑 맵이 아니어도 ×25 이상이라, 맵 구조가 고점을 만드는 게 아니라 타워 자체가 강합니다."),
    ("단일 대상 타워는 올랐지만 여전히 낮습니다.", "듀얼샷 ×0.1 → ×0.7, 번개 ×0.6 → ×1.2, 냉기 ×1.3 → ×1.8, 전기 ×1.6 → ×2.3. 경로가 긴 E 정예 맵에서만 전기 ×4.7, 냉기 ×3.2로 역할이 보입니다."),
    ("미니건은 의도한 방향으로 움직였습니다.", "과열 때문에 평균은 ×4.3 → ×3.6으로 내려갔지만, E 정예 맵에서는 ×5.6으로 단일 대상 타워 중 최고입니다. 물량전 맵(A·C)에서 가장 많이 떨어져 '정예 특화, 다수전 약점'이 수치에 드러납니다."),
    ("CC 타워(나노봇·EMP)를 섞으면 오히려 점수가 내려갑니다.", "화염방사 4개 ×2.6 → 화염 + 나노 ×1.7, 화염 + EMP ×2.0, 화염 + EMP + 나노 ×1.5. 미니건 4개 ×3.6 → 미니건 + 냉기 ×3.1. 감속·정지가 화력 1칸을 내준 만큼을 메우지 못합니다. 플라즈마를 빼면 CC 조합 최고는 EMP + 백린 ×7.3입니다."),
    ("그래서 의도한 컨셉 조합의 순위가 낮습니다.", "A 플라즈마 회랑만 561개 중 1위이고, B·C(화염 + CC)는 450~510위, D 급습은 442위, E 정예는 278위입니다. 화염 + CC로 고점을 보게 하려면 CC 쪽 수치가 더 필요합니다."),
    ("EMP는 혼자서는 거의 0입니다.", "1.6초마다 피해 1이라 체력 10(기본 4 × 2.5) 몬스터를 거의 잡지 못합니다. 기계 몬스터 2배 상성은 정예 맵의 우란탱크에서만 의미가 있습니다."),
]
NEXT_1 = [
    "플라즈마: 그대로 둘지, 피해를 낮출지(예: 1 → 0.5), 최대 대상 수를 둘지",
    "CC 강화: 나노봇 감속(필드 반경 1 → 1.5칸, 지속 3 → 5초), EMP에 짧은 정지(예: 0.5초) 등",
    "화염 계열: 하향 폭이 큰 편이라 틱 피해나 최대 대상 수(5 → 8)를 조금 되돌릴지",
    "이 수치로 2단계(9×9)로 넘어갈지, 한 번 더 조정하고 1단계를 다시 돌릴지",
]
_stage_notes = os.path.join(os.path.dirname(os.path.abspath(__file__)), f"findings_stage{STAGE}.json")
if STAGE >= 2 and os.path.exists(_stage_notes):
    _n = json.load(open(_stage_notes, encoding="utf-8"))
    FINDINGS, NEXT = [tuple(x) for x in _n["findings"]], _n["next"]
elif STAGE >= 2:
    FINDINGS, NEXT = [], []
else:
    FINDINGS, NEXT = FINDINGS_1, NEXT_1

# ---------- 그래프 (SVG) ----------
def esc(s):
    return html.escape(str(s))

def grouped_bars(title, cats, series, values, ymax=None, height=300, fmtv=lambda v: "×" + fmt(v), note=""):
    # values[ci][si]
    W, H, L, R, T, Bm = 760, height, 52, 16, 26, 58
    n, k = len(cats), len(series)
    vmax = ymax or max(1e-9, max(max(r) for r in values))
    step = (W - L - R) / n
    bw = min(26, (step - 18) / k)
    out = [f'<figure class="chart"><figcaption>{esc(title)}</figcaption>',
           '<div class="legend">' + "".join(f'<span><i style="background:var(--s{si+1})"></i>{esc(s)}</span>' for si, s in enumerate(series)) + "</div>",
           f'<svg viewBox="0 0 {W} {H}" role="img" aria-label="{esc(title)}">']
    for g in range(5):
        v = vmax * g / 4
        y = H - Bm - (H - Bm - T) * g / 4
        out.append(f'<line class="grid" x1="{L}" x2="{W-R}" y1="{y:.1f}" y2="{y:.1f}"/><text class="ax" x="{L-6}" y="{y+4:.1f}" text-anchor="end">{esc(fmtv(v))}</text>')
    for ci, c in enumerate(cats):
        x0 = L + step * ci + (step - bw * k - 2 * (k - 1)) / 2
        for si in range(k):
            v = values[ci][si]
            h = (H - Bm - T) * min(v, vmax) / vmax
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

def dumbbell(title, labels, before, after, fmtv, ymax, note=""):
    W, H, L, R, T, Bm = 760, 260, 52, 16, 26, 64
    n = len(labels)
    step = (W - L - R) / n
    out = [f'<figure class="chart"><figcaption>{esc(title)}</figcaption>',
           '<div class="legend"><span><i style="background:var(--s1)"></i>조정 전</span><span><i style="background:var(--s2)"></i>조정 후</span></div>',
           f'<svg viewBox="0 0 {W} {H}" role="img" aria-label="{esc(title)}">']
    for g in range(5):
        v = ymax * g / 4
        y = H - Bm - (H - Bm - T) * g / 4
        out.append(f'<line class="grid" x1="{L}" x2="{W-R}" y1="{y:.1f}" y2="{y:.1f}"/><text class="ax" x="{L-6}" y="{y+4:.1f}" text-anchor="end">{esc(fmtv(v))}</text>')
    yof = lambda v: H - Bm - (H - Bm - T) * min(v, ymax) / ymax
    for i, lab in enumerate(labels):
        x = L + step * i + step / 2
        yb, ya = yof(before[i]), yof(after[i])
        out.append(f'<g class="hit"><rect x="{x-step/2:.1f}" y="{T}" width="{step:.1f}" height="{H-Bm-T}" fill="transparent"/>'
                   f'<line x1="{x:.1f}" x2="{x:.1f}" y1="{yb:.1f}" y2="{ya:.1f}" class="link"/>'
                   f'<circle cx="{x:.1f}" cy="{yb:.1f}" r="4" fill="var(--s1)" class="ring"/><circle cx="{x:.1f}" cy="{ya:.1f}" r="4" fill="var(--s2)" class="ring"/>'
                   f'<title>{esc(lab)}: 조정 전 {esc(fmtv(before[i]))} → 조정 후 {esc(fmtv(after[i]))}</title></g>')
        if i % 10 == 0:
            out.append(f'<text class="cat" x="{x:.1f}" y="{H-Bm+18}" text-anchor="start">{esc(lab[:6])}</text>')
    # 컨셉 구분선
    for ci in range(1, len(CODES)):
        x = L + step * ci * 10
        out.append(f'<line class="sep" x1="{x:.1f}" x2="{x:.1f}" y1="{T}" y2="{H-Bm+30}"/>')
    for ci, code in enumerate(CODES):
        out.append(f'<text class="cat" x="{L + step*(ci*10+5):.1f}" y="{H-Bm+40}" text-anchor="middle">{esc(code)} {esc(CNAME[code])}</text>')
    out.append(f'<line class="base" x1="{L}" x2="{W-R}" y1="{H-Bm}" y2="{H-Bm}"/></svg>')
    if note:
        out.append(f'<p class="note">{esc(note)}</p>')
    out.append("</figure>")
    return "\n".join(out)

def heat_table(title, rows, cols, value, fmtv, note=""):
    vals = [value(r, c) for r in rows for c in cols]
    vmax = max(vals) if vals else 1
    out = [f'<figure class="chart"><figcaption>{esc(title)}</figcaption><div class="tw"><table class="heat"><thead><tr><th></th>'
           + "".join(f"<th>{esc(c[1])}</th>" for c in cols) + "</tr></thead><tbody>"]
    for r in rows:
        out.append(f"<tr><th>{esc(r[1])}</th>")
        for c in cols:
            v = value(r, c)
            a = 0.08 + 0.72 * (v / vmax if vmax else 0)
            out.append(f'<td style="--a:{a:.2f}" title="{esc(r[1])} · {esc(c[1])}: {esc(fmtv(v))}">{esc(fmtv(v))}</td>')
        out.append("</tr>")
    out.append("</tbody></table></div>")
    if note:
        out.append(f'<p class="note">{esc(note)}</p>')
    out.append("</figure>")
    return "\n".join(out)

charts = []
cat_labels = [f"{c} {CNAME[c]}" for c in CODES]
charts.append(grouped_bars("컨셉 조합 점수 (버틴 몬스터 수 배율, 맵 10개 평균)", cat_labels, ["조정 전", "조정 후"],
                           [[set_score(B, c, 0), set_score(A, c, 0)] for c in CODES], ymax=max(MULT)))
charts.append(grouped_bars("비컨셉 조합 점수 (같은 단계 타워로 바꿔 끼움)", cat_labels, ["조정 전", "조정 후"],
                           [[set_score(B, c, 1), set_score(A, c, 1)] for c in CODES], ymax=max(MULT)))
tower_labels = [NAME[t] for t in TOWERS]
charts.append(grouped_bars("타워별 점수 (같은 타워 " + str(A["towerCount"]) + "개, 맵 50개 평균)", tower_labels, ["조정 전", "조정 후"],
                           [[avg(single_score[("before", t, c)] for c in CODES), avg(single_score[("after", t, c)] for c in CODES)] for t in TOWERS], ymax=max(MULT)))

def concept_power(data, mid):
    return data and data[mid]["sets"][0]["damage"]
power_b = [BM[i]["sets"][0]["damage"] for i in IDS]
power_a = [AM[i]["sets"][0]["damage"] for i in IDS]
charts.append(dumbbell("맵별 타워 파워량: 컨셉 조합이 ×1 웨이브에서 준 피해 총량", IDS, power_b, power_a, lambda v: fmt(v, 0), max(power_b + power_a) * 1.05,
                       "몬스터가 실제로 잃은 체력의 합입니다 (남은 체력보다 큰 피해는 남은 만큼만). 맵의 몬스터를 모두 잡으면 그 맵 몬스터 체력 총량에서 멈춥니다."))
kill_b = [pct(BM[i]["sets"][0]["kills"], BM[i]["sets"][0]["spawned"]) for i in IDS]
kill_a = [pct(AM[i]["sets"][0]["kills"], AM[i]["sets"][0]["spawned"]) for i in IDS]
charts.append(dumbbell("맵별 몬스터 소모량: 컨셉 조합이 ×1 웨이브에서 처치한 비율", IDS, kill_b, kill_a, lambda v: fmt(v, 0) + "%", 100))

dmg_rows = [(t, NAME[t]) for t in TOWERS]
dmg_cols = [(c, f"{c} {CNAME[c]}") for c in CODES]
charts.append(heat_table("타워별 대미지표 (조정 후): 같은 타워 " + str(A["towerCount"]) + "개가 ×1 웨이브에서 준 피해 (컨셉별 맵 10개 평균)", dmg_rows, dmg_cols,
                         lambda r, c: single_dmg[("after", r[0], c[0])], lambda v: fmt(v, 0)))
charts.append(heat_table("타워별 대미지표 (조정 전)", dmg_rows, dmg_cols, lambda r, c: single_dmg[("before", r[0], c[0])], lambda v: fmt(v, 0)))

# 2종 조합 행렬 (조정 후, 맵 50개 평균)
def pair_score(a, b):
    if a == b:
        return combo_avg[(a, "all")]
    k = "+".join(sorted([a, b], key=TOWERS.index))
    return combo_avg[(k, "all")]
charts.append(heat_table("2종 조합 점수 (조정 후, 맵 50개 평균). 대각선 = 같은 타워 " + str(A["towerCount"]) + "개", dmg_rows, [(t, NAME[t]) for t in TOWERS],
                         lambda r, c: pair_score(r[0], c[0]), lambda v: "×" + fmt(v, 1)))

# ---------- 표 (HTML·MD 공용 데이터) ----------
def top_combos(code, n=10):
    ks = sorted(combo_keys, key=lambda k: -combo_avg[(k, code)])
    return ks[:n]

def combo_label(k):
    return " + ".join(NAME[t] for t in k.split("+"))

def concept_rank(code):
    comp = A["concepts"][CODES.index(code)]["composition"]
    kinds = sorted(set(comp), key=TOWERS.index)
    k = "+".join(kinds)
    vals = [combo_avg[(x, code)] for x in combo_keys]
    return k, combo_avg.get((k, code), 0), rank_of(combo_avg.get((k, code), 0), vals)

# ---------- HTML ----------
css = """
:root{--bg:#fcfcfb;--ink:#0b0b0b;--dim:#52514e;--line:#e3e1dc;--panel:#f3f2ee;--s1:#2a78d6;--s2:#eb6834;--heat:42,120,214}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#1a1a19;--ink:#f0efec;--dim:#c3c2b7;--line:#3a3935;--panel:#242422;--s1:#3987e5;--s2:#d95926;--heat:57,135,229}}
:root[data-theme="dark"]{--bg:#1a1a19;--ink:#f0efec;--dim:#c3c2b7;--line:#3a3935;--panel:#242422;--s1:#3987e5;--s2:#d95926;--heat:57,135,229}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.65 "Pretendard","Malgun Gothic",system-ui,sans-serif}
main{max-width:1100px;margin:0 auto;padding:28px 16px 80px}h1{font-size:26px;margin:0 0 6px}h2{font-size:20px;margin:40px 0 10px;border-top:1px solid var(--line);padding-top:18px}
.lead{color:var(--dim)}.note{color:var(--dim);font-size:13px}
.charts{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,520px),1fr));gap:18px}
figure.chart{margin:0;background:var(--panel);border-radius:10px;padding:12px 14px}figcaption{font-weight:700;margin-bottom:4px}
.legend{display:flex;gap:14px;font-size:13px;color:var(--dim);margin-bottom:4px}.legend i{display:inline-block;width:10px;height:10px;border-radius:3px;margin-right:5px;vertical-align:-1px}
svg{width:100%;height:auto;display:block}svg text{fill:var(--dim);font-size:11px;font-family:inherit}svg .cat{fill:var(--ink)}
.grid{stroke:var(--line);stroke-width:1}.base{stroke:var(--dim);stroke-width:1}.sep{stroke:var(--line);stroke-dasharray:3 3}.link{stroke:var(--dim);stroke-width:1.5;opacity:.5}
.ring{stroke:var(--panel);stroke-width:2}.hit:hover path,.hit:hover circle{opacity:.8}
.tw{overflow-x:auto}table{border-collapse:collapse;width:100%;font-size:13.5px}th,td{border-bottom:1px solid var(--line);padding:5px 7px;text-align:left;vertical-align:top}
th{color:var(--dim);font-weight:600}td.r,th.r{text-align:right}
table.heat td{text-align:right;background:rgba(var(--heat),var(--a));font-variant-numeric:tabular-nums}
pre.map{margin:0;font:12px/1.15 Consolas,"Malgun Gothic",monospace;letter-spacing:1px}
.btn{position:fixed;right:14px;top:12px;border:1px solid var(--line);background:var(--panel);color:var(--ink);border-radius:8px;padding:5px 10px;cursor:pointer}
"""
h = [f'<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>레벨디자인 {STAGE}단계 보고서</title><style>{css}</style></head><body>'
     '<button class="btn" onclick="var r=document.documentElement,d=r.dataset.theme||(matchMedia(\'(prefers-color-scheme:dark)\').matches?\'dark\':\'light\');r.dataset.theme=d==\'dark\'?\'light\':\'dark\'">밝게/어둡게</button><main>',
     f"<h1>레벨디자인 {STITLE} · 타워 수치 조정 전·후 비교</h1>",
     f'<p class="lead">조정 전 = {BEFORE_NOTE}.</p>',
     f'<p class="lead">컨셉 맵 50종 × 타워 조합. 타워 {A["towerCount"]}개, 몬스터 체력 = 기본 × {fmt(A["hpScale"])}, 몬스터 수 배율 ×{" → ×".join(fmt(m) for m in MULT)}. 점수 = 넥서스 피해가 20에 닿기 전까지 버틴 가장 큰 배율.</p>',
     "<h2>수치 조정 내역</h2><div class=\"tw\"><table><thead><tr><th>타워</th><th>항목</th><th>조정 전</th><th>조정 후</th><th>메모</th></tr></thead><tbody>"]
for t, item, b, a, memo in CHANGES:
    h.append(f"<tr><td>{esc(t)}</td><td>{esc(item)}</td><td>{esc(b)}</td><td>{esc(a)}</td><td>{esc(memo)}</td></tr>")
h.append("</tbody></table></div><h2>그래프</h2><div class=\"charts\">" + "\n".join(charts[:3]) + "</div>" + "\n".join(charts[3:5]) + "<div class=\"charts\">" + "\n".join(charts[5:]) + "</div>")

h.append("<h2>컨셉별 결과 (조정 전 → 조정 후)</h2><div class=\"tw\"><table><thead><tr><th>컨셉</th><th>의도한 조합</th><th class=r>컨셉</th><th class=r>비컨셉</th><th class=r>전체</th><th>모든 조합 중 의도한 조합 순위</th></tr></thead><tbody>")
for c in CONCEPTS:
    k, v, r = concept_rank(c["code"])
    h.append(f"<tr><td>{esc(c['code'])} {esc(c['name'])}</td><td>{esc(' · '.join(NAME[t] for t in c['composition']))}</td>"
             + "".join(f"<td class=r>×{fmt(set_score(B, c['code'], i))} → <b>×{fmt(set_score(A, c['code'], i))}</b></td>" for i in range(3))
             + f"<td>{esc(combo_label(k))}: ×{fmt(v)} · {r}위 / {len(combo_keys)}</td></tr>")
h.append("</tbody></table></div><p class=\"note\">순위는 조합 키(타워 종류) 기준입니다. 같은 종류 조합이라도 남은 칸은 AI가 채워 의도한 개수와 다를 수 있습니다.</p>")
h.append("<h2>확인한 점</h2><ol>" + "".join(f"<li><b>{esc(t)}</b> {esc(d)}</li>" for t, d in FINDINGS) + "</ol><h2>다음에 정할 것</h2><ul>" + "".join(f"<li>{esc(x)}</li>" for x in NEXT) + "</ul>")

h.append("<h2>컨셉별 최고 조합 (조정 후, 모든 경우의 수 중 상위 10)</h2><div class=\"charts\">")
for c in CONCEPTS:
    h.append(f"<figure class=\"chart\"><figcaption>{esc(c['code'])} {esc(c['name'])}</figcaption><table><tbody>")
    for i, k in enumerate(top_combos(c["code"])):
        h.append(f"<tr><td>{i+1}</td><td>{esc(combo_label(k))}</td><td class=r>×{fmt(combo_avg[(k, c['code'])])}</td></tr>")
    h.append("</tbody></table></figure>")
h.append("</div>")

h.append("<h2>맵별 표 (조정 후, 컨셉 조합)</h2><div class=\"tw\"><table><thead><tr><th>맵</th><th>모양</th><th class=r>경로</th><th class=r>부지</th><th class=r>점수 전→후</th><th class=r>파워량 전→후</th><th class=r>처치 전→후</th><th>최고 조합</th></tr></thead><tbody>")
for i in IDS:
    a, b = AM[i], BM[i]
    sa, sb = a["sets"][0], b["sets"][0]
    best = max(a["combos"], key=lambda c: c["score"])
    h.append(f"<tr><td>{i}</td><td><pre class=map>{esc(chr(10).join(a['ascii']))}</pre></td><td class=r>{'/'.join(map(str, a['paths']))}</td><td class=r>{a['sites']}</td>"
             f"<td class=r>×{fmt(sb['score'])} → ×{fmt(sa['score'])}</td><td class=r>{fmt(sb['damage'],0)} → {fmt(sa['damage'],0)}</td>"
             f"<td class=r>{sb['kills']}/{sb['spawned']} → {sa['kills']}/{sa['spawned']}</td><td>{esc(combo_label(best['key']))} ×{fmt(best['score'])}</td></tr>")
h.append("</tbody></table></div><p class=\"note\">기호: S 스타트 · N 넥서스 · ■ 경로 · □ 잔디(설치) · ♣ 나무. 모든 맵 × 모든 조합 원본은 LevelDesign_" + TAG + "_combos.csv.</p></main></body></html>")
open(os.path.join(root, "Docs", f"LevelDesign_{TAG}_Report.html"), "w", encoding="utf-8").write("\n".join(h))

# ---------- Markdown ----------
md = [f"# 레벨디자인 {STITLE} · 컨셉 맵 50종 테스트", "",
      f"> 그래프가 있는 보고서: `Docs/LevelDesign_{TAG}_Report.html` · 모든 맵 × 모든 조합 원본: `Docs/LevelDesign_{TAG}_combos.csv`",
      f"> 도구: `Assets/Editor/LevelDesignLab.cs` (메뉴 TowerDefence → Balance → Level Design Stage {STAGE}) · 보고서: `Tools/LevelDesign/make_report.py` · 맵 파일: `LevelDesign/{TAG}/*.json` (맵 제작 씬 상단 ◀ ▶)",
      f"> 조정 전 = {BEFORE_NOTE}", "",
      "## 테스트 방법", "",
      f"- 맵마다 타워 **{A['towerCount']}개**를 깝니다 (비용 무시: 1단계 타워 비용이 임시로 모두 50이라 예산으로 맞추면 비용 차이가 구조 차이를 덮습니다).",
      "  - **컨셉**: 의도한 조합 그대로 / **비컨셉**: 컨셉 조합의 칸마다 같은 단계의 컨셉 밖 타워로 바꿔 끼움 (AI) / **전체**: 칸마다 같은 단계의 모든 타워 중 AI",
      f"  - **모든 경우의 수**: 타워 11종에서 1~4종을 고른 {len(combo_keys)}가지 조합 (1종 = 같은 타워 " + str(A["towerCount"]) + "개, 2종 = 보통 2 + 2). 조합의 타워를 1개씩 먼저 놓고 남은 칸은 그 조합 안에서 AI가 채웁니다.",
      "  - 조정 전 수치는 1종·2종 조합과 컨셉·비컨셉·전체만 돌렸습니다 (비교용).",
      f"- 웨이브 3개를 실제 게임 코드로 돌립니다. 몬스터 체력 = 기본 × {fmt(A['hpScale'])}. 몬스터 수에 배율 ×{' → ×'.join(fmt(m) for m in MULT)}를 곱해(간격 ÷ √배율) 넥서스 피해 20 전까지 버틴 가장 큰 배율이 **점수**입니다.",
      "- **타워 파워량** = ×1 웨이브에서 몬스터가 실제로 잃은 체력 합 · **몬스터 소모량** = ×1 웨이브 처치 수 / 등장 수", "",
      "## 수치 조정 내역", "", "| 타워 | 항목 | 조정 전 | 조정 후 | 메모 |", "|---|---|---|---|---|"]
for t, item, b, a, memo in CHANGES:
    md.append(f"| {t} | {item} | {b} | {a} | {memo} |")
md += ["", "## 컨셉별 결과 (조정 전 → 조정 후)", "", "| 컨셉 | 의도한 조합 | 컨셉 | 비컨셉 | 전체 | 의도한 조합의 순위 (모든 조합 중) |", "|---|---|---:|---:|---:|---|"]
for c in CONCEPTS:
    k, v, r = concept_rank(c["code"])
    md.append(f"| {c['code']} {c['name']} | {' · '.join(NAME[t] for t in c['composition'])} | "
              + " | ".join(f"×{fmt(set_score(B, c['code'], i))} → **×{fmt(set_score(A, c['code'], i))}**" for i in range(3))
              + f" | {combo_label(k)} ×{fmt(v)} · {r}위 / {len(combo_keys)} |")
md += ["", "## 확인한 점", ""] + [f"{i+1}. **{t}** {d}" for i, (t, d) in enumerate(FINDINGS)] + ["", "### 다음에 정할 것", ""] + [f"- {x}" for x in NEXT]
md += ["", "## 타워별 점수 (같은 타워 " + str(A["towerCount"]) + "개, 컨셉별 맵 10개 평균, 조정 전 → 조정 후)", "",
       "| 타워 | 단계 | " + " | ".join(f"{c} {CNAME[c]}" for c in CODES) + " | 평균 |", "|---|---:|" + "---:|" * (len(CODES) + 1)]
for t in TOWERS:
    cells = [f"×{fmt(single_score[('before', t, c)], 1)} → ×{fmt(single_score[('after', t, c)], 1)}" for c in CODES]
    md.append(f"| {NAME[t]} | {TIER[t]} | " + " | ".join(cells) + f" | ×{fmt(avg(single_score[('before', t, c)] for c in CODES), 1)} → **×{fmt(avg(single_score[('after', t, c)] for c in CODES), 1)}** |")
md += ["", "## 타워별 대미지표 (같은 타워 " + str(A["towerCount"]) + "개가 ×1 웨이브에서 준 피해, 컨셉별 맵 10개 평균, 조정 전 → 조정 후)", "",
       "| 타워 | " + " | ".join(f"{c} {CNAME[c]}" for c in CODES) + " |", "|---|" + "---:|" * len(CODES)]
for t in TOWERS:
    md.append(f"| {NAME[t]} | " + " | ".join(f"{fmt(single_dmg[('before', t, c)], 0)} → {fmt(single_dmg[('after', t, c)], 0)}" for c in CODES) + " |")
md += ["", "## 컨셉별 최고 조합 (조정 후, 모든 경우의 수 상위 5)", ""]
for c in CONCEPTS:
    md.append(f"- **{c['code']} {c['name']}**: " + " · ".join(f"{combo_label(k)} ×{fmt(combo_avg[(k, c['code'])])}" for k in top_combos(c["code"], 5)))
md += ["", "## 맵별 표 (컨셉 조합, 조정 전 → 조정 후)", ""]
for c in CONCEPTS:
    md += [f"### {c['code']}. {c['name']}", "", c["idea"], "", f"- 웨이브: {c['waves']}", "",
           "| 맵 | 모양 | 경로 | 부지 | 점수 | 타워 파워량 | 몬스터 소모량 (처치/등장) | 최고 조합 (조정 후) |", "|---|---|---:|---:|---:|---:|---:|---|"]
    for m in concept_maps(A, c["code"]):
        b = BM[m["id"]]
        sa, sb = m["sets"][0], b["sets"][0]
        best = max(m["combos"], key=lambda x: x["score"])
        md.append(f"| {m['id']} | <pre>{'<br>'.join(m['ascii'])}</pre> | {'/'.join(map(str, m['paths']))} | {m['sites']} | ×{fmt(sb['score'])} → **×{fmt(sa['score'])}** | "
                  f"{fmt(sb['damage'], 0)} → {fmt(sa['damage'], 0)} | {sb['kills']}/{sb['spawned']} → {sa['kills']}/{sa['spawned']} | {combo_label(best['key'])} ×{fmt(best['score'])} |")
    md.append("")
md.append("기호: S 스타트 · N 넥서스 · ■ 경로 · □ 잔디(설치) · ♣ 나무(장애물). 위쪽이 y = 6.")
open(os.path.join(root, "Docs", f"LevelDesign_{TAG}.md"), "w", encoding="utf-8").write("\n".join(md) + "\n")
print("ok", len(combo_keys), "combos")
