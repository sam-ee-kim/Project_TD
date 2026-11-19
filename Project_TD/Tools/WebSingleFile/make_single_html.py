# Unity Web 빌드 폴더(index.html + Build/ + TemplateData/)를 HTML 파일 하나로 묶는다.
# 받는 사람이 서버 없이 더블클릭(file://)으로 열 수 있게, 로더·게임 데이터·wasm·이미지를 모두 파일 안에 넣는다.
#   - 큰 파일(.unityweb, gzip)은 base64로 넣고, 실행할 때 blob URL로 바꿔 Unity 로더에 넘긴다.
#     서버가 압축 헤더를 주지 않으므로 로더의 압축 해제 대체 기능(Decompression Fallback)이 직접 푼다 → 빌드 때 켜 둬야 한다
#   - 화면: 창 크기에 꽉 차게 (아래 Unity 바닥줄은 숨김)
# 실행: python make_single_html.py <빌드 폴더> <출력 html>
import base64, os, re, sys

src = sys.argv[1]
out = sys.argv[2]
html = open(os.path.join(src, "index.html"), encoding="utf-8").read()

def b64(path):
    return base64.b64encode(open(path, "rb").read()).decode("ascii")

# 빌드 파일 이름 (WebGL.loader.js / TD.loader.js 등)
build = os.path.join(src, "Build")
names = os.listdir(build)
def find(suffix):
    for n in names:
        if n.endswith(suffix):
            return os.path.join(build, n)
    raise SystemExit(f"Build 폴더에 *{suffix} 가 없습니다")

loader = open(find(".loader.js"), encoding="utf-8").read()
data = find(".data.unityweb") if any(n.endswith(".data.unityweb") for n in names) else find(".data")
framework = find(".framework.js.unityweb") if any(n.endswith(".framework.js.unityweb") for n in names) else find(".framework.js")
wasm = find(".wasm.unityweb") if any(n.endswith(".wasm.unityweb") for n in names) else find(".wasm")

# CSS: 이미지 url(...)을 data URI로
tdir = os.path.join(src, "TemplateData")
css = open(os.path.join(tdir, "style.css"), encoding="utf-8").read()
def img(m):
    name = m.group(1).strip("'\"")
    p = os.path.join(tdir, name)
    if not os.path.exists(p):
        return m.group(0)
    mime = "image/png" if name.endswith(".png") else "image/x-icon"
    return f"url(data:{mime};base64,{b64(p)})"
css = re.sub(r"url\(([^)]+)\)", img, css)
css += """
html, body { margin: 0; padding: 0; width: 100%; height: 100%; overflow: hidden; background: #000; }
#unity-container.unity-desktop, #unity-container.unity-mobile { position: fixed; left: 0; top: 0; width: 100%; height: 100%; transform: none; }
#unity-canvas { width: 100% !important; height: 100% !important; display: block; background: #000; }
#unity-footer { display: none; }
#unity-loading-bar { left: 50%; top: 50%; transform: translate(-50%, -50%); }
"""

html = html.replace('<link rel="shortcut icon" href="TemplateData/favicon.ico">',
                    f'<link rel="shortcut icon" href="data:image/x-icon;base64,{b64(os.path.join(tdir, "favicon.ico"))}">')
html = html.replace('<link rel="stylesheet" href="TemplateData/style.css">', f"<style>{css}</style>")
html = re.sub(r"<title>.*?</title>", "<title>Project TD</title>", html)

# 큰 데이터는 실행하지 않는 script 블록에 base64로 (JS 문자열로 파싱하지 않게)
blobs = (
    f'<script type="application/octet-stream" id="td-data">{b64(data)}</script>\n'
    f'<script type="application/octet-stream" id="td-framework">{b64(framework)}</script>\n'
    f'<script type="application/octet-stream" id="td-wasm">{b64(wasm)}</script>\n'
)

# 로더 실행부: 파일 경로 대신 blob URL, 로더 스크립트는 파일 안에서 바로
start = html.index('      var buildUrl = "Build";')
end = html.index("    </script>", start)
body = html[start:end]
body = re.sub(r'var buildUrl = "Build";\s*var loaderUrl = [^;]+;', "", body)
body = re.sub(r'dataUrl: [^,]+,', 'dataUrl: blobUrl("td-data", "application/octet-stream"),', body)
body = re.sub(r'frameworkUrl: [^,]+,', 'frameworkUrl: blobUrl("td-framework", "application/octet-stream"),', body)
body = re.sub(r'codeUrl: [^,]+,', 'codeUrl: blobUrl("td-wasm", "application/octet-stream"),', body)
body = body.replace('streamingAssetsUrl: "StreamingAssets",', 'streamingAssetsUrl: "StreamingAssets",\n        cacheControl: function () { return "no-store"; },')
# 데스크톱도 창에 꽉 차게
body = body.replace('canvas.style.width = "960px";', 'canvas.style.width = "100%";').replace('canvas.style.height = "600px";', 'canvas.style.height = "100%";')
# 로더 <script src> 대신 바로 실행
body = re.sub(r'var script = document.createElement\("script"\);\s*script.src = loaderUrl;\s*script.onload = \(\) => \{', "(() => {", body)
body = body.replace("document.body.appendChild(script);", "")
body = re.sub(r"\}\);\s*\};\s*$", "});\n      })();\n", body.rstrip()) + "\n"

helper = """
      // 파일 안의 base64 블록 → blob URL (서버 없이 file:// 에서도 Unity 로더가 받아 간다)
      function blobUrl(id, mime) {
        var text = document.getElementById(id).textContent;
        var bin = atob(text);
        var bytes = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        document.getElementById(id).textContent = ""; // 메모리 절약
        return URL.createObjectURL(new Blob([bytes], { type: mime }));
      }
"""
html = html[:start] + helper + body + html[end:]
html = html.replace("<script>\n      var canvas", blobs + f"<script>{loader}</script>\n    <script>\n      var canvas", 1)

open(out, "w", encoding="utf-8").write(html)
print(f"ok {os.path.getsize(out) / 1024 / 1024:.1f} MB -> {out}")
