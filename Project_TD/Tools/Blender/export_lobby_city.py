# 로비 도시(SF_City.blend) → 유니티용 가벼운 FBX + 바닥 텍스처 + 재질 표.
# 원본 .blend는 건드리지 않는다 (백그라운드로 열어 메모리에서만 바꾸고 저장하지 않는다).
#
# 실행: blender -b SF_City.blend --python export_lobby_city.py -- <Project_TD 폴더>
#
# 하는 일 (렌더링 최적화):
#   1) 납작한 바닥 객체(도로·차선·횡단보도·보도·보도 줄눈·부지)를 위에서 찍어 텍스처 1장으로 굽는다.
#      굽는 범위는 도시 구역(도로 객체 범위)만. 그 바깥 땅(Ground)은 단색 사각형 그대로 둔다.
#      삼각형 약 23만 개 → 사각형 1장. 탑뷰 카메라에서는 높이가 거의 없는 객체라 차이가 보이지 않는다.
#   2) 가로등·신호등 기둥처럼 멀리서 점으로 보이는 반복 객체는 면 수를 줄인다 (Decimate).
#   3) 카메라·조명은 빼고(유니티 씬 조명을 쓴다), 컬렉션마다 빈 부모를 만들어 묶는다 (유니티에서 건물 단위로 다룬다).
#   4) 재질 색(Principled BSDF 값)을 JSON으로 남긴다. 유니티가 FBX 재질을 URP 재질로 다시 만들 때 쓴다.
import bpy, bmesh, json, os, sys

project = sys.argv[sys.argv.index("--") + 1]
model_dir = os.path.join(project, "Assets", "Models", "Lobby")
art_dir = os.path.join(project, "Assets", "Art", "Lobby")
os.makedirs(model_dir, exist_ok=True)
os.makedirs(art_dir, exist_ok=True)

BAKE = {"Road_Asphalt", "Road_CenterLine_Yellow", "Road_Crosswalks", "Road_LaneDash_White", "Road_StopLines",
        "Sidewalk_Curbs", "Sidewalk_Joints", "Sidewalks", "City_BlockLots"}
DECIMATE = {"StreetLights_Body": 0.2, "StreetLights_Glow": 0.3, "TrafficLights_Body": 0.3}
GROUND_TOP = 0.18      # 보도 윗면 높이: 구운 바닥을 이 높이에 깐다 (건물·정류장 바닥과 맞음)
TEX_WIDTH = 4096       # 굽는 해상도: 도시 860m → 약 0.21m/픽셀 (8192로 올리면 더 선명, 파일 4배)

scene = bpy.context.scene

# 렌더에 안 나오는 보조 객체(불리언 커터 등)는 내보내지 않는다. 지우면 불리언 결과가 사라지므로 선택에서만 뺀다
HELPERS = {o.name for o in bpy.data.objects if o.type == 'MESH' and (o.hide_render or o.display_type in {'WIRE', 'BOUNDS'})}
print("[LobbyExport] 보조 객체 제외:", sorted(HELPERS))

# --- 재질 표 (구운 객체 재질 포함, 전부) ---
def inp(node, name, default):
    s = node.inputs.get(name)
    if s is None:
        return default
    v = s.default_value
    try:
        return [round(x, 4) for x in v]
    except TypeError:
        return round(v, 4)

mats = {}
for m in bpy.data.materials:
    b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if m.use_nodes else None
    if b is None:
        continue
    mats[m.name] = dict(base=inp(b, "Base Color", [1, 1, 1, 1]), metallic=inp(b, "Metallic", 0), roughness=inp(b, "Roughness", 0.5),
                        emission=inp(b, "Emission Color", [0, 0, 0, 1]), emissionStrength=inp(b, "Emission Strength", 0),
                        alpha=inp(b, "Alpha", 1))
    m.diffuse_color = b.inputs["Base Color"].default_value  # 워크벤치 렌더는 이 색을 쓴다
mats["City_GroundBaked"] = dict(base=[1, 1, 1, 1], metallic=0, roughness=0.85, emission=[0, 0, 0, 1], emissionStrength=0, alpha=1,
                                texture="Assets/Art/Lobby/LobbyCity_Ground.png")

# --- 1) 바닥 굽기: 위에서 정사영으로 찍는다 ---
from mathutils import Vector
lo, hi = Vector((1e9, 1e9, 0)), Vector((-1e9, -1e9, 0))
for name in BAKE:
    o = bpy.data.objects[name]
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        lo.x, lo.y = min(lo.x, w.x), min(lo.y, w.y)
        hi.x, hi.y = max(hi.x, w.x), max(hi.y, w.y)
gx, gy = hi.x - lo.x, hi.y - lo.y
cx, cy = (lo.x + hi.x) / 2, (lo.y + hi.y) / 2
ground = bpy.data.objects["Ground"]
if scene.world is None:
    scene.world = bpy.data.worlds.new("BakeWorld")
scene.world.color = mats["Street_Ground"]["base"][:3] if "Street_Ground" in mats else (0.07, 0.075, 0.08)  # 빈 곳 = 땅 색
for o in bpy.data.objects:
    o.hide_render = o.name not in BAKE
cam_data = bpy.data.cameras.new("BakeCam")
cam_data.type = 'ORTHO'
cam_data.ortho_scale = max(gx, gy)
cam_data.clip_start, cam_data.clip_end = 0.1, 200
cam = bpy.data.objects.new("BakeCam", cam_data)
scene.collection.objects.link(cam)
cam.location = (cx, cy, 100)
cam.rotation_euler = (0, 0, 0)
scene.camera = cam
r = scene.render
r.engine = 'BLENDER_WORKBENCH'
r.resolution_x = TEX_WIDTH
r.resolution_y = round(TEX_WIDTH * gy / gx / 4) * 4  # 4의 배수: 유니티 블록 압축(DXT1)은 4×4 단위라 아니면 무압축이 된다
r.resolution_percentage = 100
r.film_transparent = False
r.image_settings.file_format = 'PNG'
r.image_settings.color_mode = 'RGB'
sh = scene.display.shading
sh.light = 'FLAT'
sh.color_type = 'MATERIAL'
sh.show_shadows = False
sh.show_cavity = False
sh.show_object_outline = False
sh.show_specular_highlight = False
scene.display.render_aa = '16'
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
r.filepath = os.path.join(art_dir, "LobbyCity_Ground.png")
bpy.ops.render.render(write_still=True)

# --- 구운 객체를 지우고 텍스처를 붙인 바닥 한 장으로 바꾼다 ---
streets = ground.users_collection[0]
for name in BAKE:
    bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
bpy.data.objects.remove(cam, do_unlink=True)
mesh = bpy.data.meshes.new("City_GroundBaked")
bm = bmesh.new()
hx, hy = gx / 2, gy / 2
vs = [bm.verts.new((cx - hx, cy - hy, GROUND_TOP)), bm.verts.new((cx + hx, cy - hy, GROUND_TOP)),
      bm.verts.new((cx + hx, cy + hy, GROUND_TOP)), bm.verts.new((cx - hx, cy + hy, GROUND_TOP))]
face = bm.faces.new(vs)
uv = bm.loops.layers.uv.new("UVMap")
for loop, (u, v) in zip(face.loops, [(0, 0), (1, 0), (1, 1), (0, 1)]):
    loop[uv].uv = (u, v)
bm.to_mesh(mesh)
bm.free()
mat = bpy.data.materials.new("City_GroundBaked")
mesh.materials.append(mat)
plane = bpy.data.objects.new("City_GroundBaked", mesh)
streets.objects.link(plane)

# --- 2) 반복 소품 면 수 줄이기 ---
for name, ratio in DECIMATE.items():
    o = bpy.data.objects.get(name)
    if o is None:
        continue
    d = o.modifiers.new("Decimate", 'DECIMATE')
    d.ratio = ratio

# --- 3) 카메라·조명 빼기, 컬렉션마다 빈 부모 ---
for o in list(bpy.data.objects):
    if o.type in ('CAMERA', 'LIGHT'):
        bpy.data.objects.remove(o, do_unlink=True)
for c in bpy.data.collections:
    members = [o for o in c.objects if o.type == 'MESH' and o.parent is None and o.name not in HELPERS]
    if not members:
        continue
    root = bpy.data.objects.new(c.name, None)
    c.objects.link(root)
    for o in members:
        mw = o.matrix_world.copy()
        o.parent = root
        o.matrix_world = mw

tris = 0
dg = bpy.context.evaluated_depsgraph_get()
for o in bpy.data.objects:
    if o.type == 'MESH' and o.name not in HELPERS:
        tris += sum(len(p.vertices) - 2 for p in o.evaluated_get(dg).data.polygons)

# --- 4) 내보내기 ---
for o in bpy.data.objects:
    o.hide_set(False)
    o.hide_select = False
    o.select_set(o.name not in HELPERS)
bpy.ops.export_scene.fbx(filepath=os.path.join(model_dir, "SF_City_Lobby.fbx"), use_selection=True,
                         object_types={'MESH', 'EMPTY'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True, use_mesh_modifiers=True,
                         mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False, path_mode='STRIP',
                         use_custom_props=False)
json.dump(dict(materials=mats, triangles=tris), open(os.path.join(model_dir, "SF_City_Materials.json"), "w", encoding="utf-8"),
          ensure_ascii=False, indent=1)
print("[LobbyExport] 삼각형", tris, "텍스처", r.resolution_x, "x", r.resolution_y)
