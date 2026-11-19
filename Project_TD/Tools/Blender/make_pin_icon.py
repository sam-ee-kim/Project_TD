# 로비 핀 아이콘 PNG (외부 라이브러리 없이 직접 그린다). 흰색 모양이라 유니티에서 Image.color로 건물마다 색을 입힌다.
#   UI_Pin.png    : 물방울 모양 핀 (위 원 + 아래 뾰족한 끝, 가운데 구멍)
#   UI_PinDot.png : 핀 구멍에 넣는 흰 원 (따로 두어 몸통 색과 상관없이 하얗게)
#   UI_PinShadow.png : 핀 끝 아래 바닥에 까는 흐린 타원 그림자
# 실행: python make_pin_icon.py <Assets/Art/Lobby 폴더>
import math, struct, sys, zlib, os

out = sys.argv[1]
SS = 4  # 슈퍼샘플 (가장자리 부드럽게)

def png(path, w, h, rgba):
    raw = b"".join(b"\x00" + bytes(rgba[y * w * 4:(y + 1) * w * 4]) for y in range(h))
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))

def render(w, h, inside, color=(255, 255, 255)):
    px = bytearray(w * h * 4)
    for y in range(h):
        for x in range(w):
            a = 0.0
            for sy in range(SS):
                for sx in range(SS):
                    a += inside((x + (sx + 0.5) / SS) / w, (y + (sy + 0.5) / SS) / h)
            a /= SS * SS
            i = (y * w + x) * 4
            px[i:i + 4] = bytes((color[0], color[1], color[2], int(round(255 * min(1.0, max(0.0, a))))))
    return px

# 핀: 좌표 0~1, y는 아래로. 원 중심 (0.5, 0.36), 반지름 0.34. 끝점 (0.5, 0.98).
CX, CY, R, TIP = 0.5, 0.36, 0.34, 0.98
HOLE = 0.14

def pin_body(u, v):
    du, dv = (u - CX) * 0.8, v - CY  # 가로 0.8배 (이미지 가로세로 비 4:5)
    d = math.hypot(du, dv)
    if d <= R:
        return 0.0 if d < HOLE else 1.0
    # 원에서 끝점으로 가는 접선 사이 (물방울 아래쪽)
    L = TIP - CY
    if dv <= 0 or v > TIP:
        return 0.0
    half = math.asin(R / L)
    ang = math.atan2(abs(du), (TIP - v))
    return 1.0 if ang <= half and (TIP - v) <= L else 0.0

def dot(u, v):
    return 1.0 if math.hypot(u - 0.5, v - 0.5) <= 0.5 else 0.0

def shadow(u, v):
    d = math.hypot((u - 0.5) / 0.5, (v - 0.5) / 0.5)
    return max(0.0, 1.0 - d) ** 1.5 * 0.55

os.makedirs(out, exist_ok=True)
png(os.path.join(out, "UI_Pin.png"), 96, 120, render(96, 120, pin_body))
png(os.path.join(out, "UI_PinDot.png"), 32, 32, render(32, 32, dot))
png(os.path.join(out, "UI_PinShadow.png"), 64, 24, render(64, 24, shadow, (0, 0, 0)))
print("ok")
