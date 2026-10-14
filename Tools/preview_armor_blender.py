"""Offline preview of the ArmorKit fit on the TimmyRobot rig, rendered with Blender Cycles (pip install bpy).

Usage: python preview_armor_blender.py -- TimmyRobot.fbx rooftop_night_2k.hdr out.png Hero|Scrapper|Juggernaut
Mirrors the plate layout in Assets/HackAndSlash/Scripts/ArmorKit.cs (bevels are approximate).
"""
import bpy, sys, math, mathutils
from mathutils import Vector, Matrix, Quaternion
args = sys.argv[sys.argv.index('--')+1:]
fbx, hdri, OUTPATH, style = args[0], args[1], args[2], args[3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = [o for o in bpy.data.objects if o.type == 'MESH'][0]
def bone(n):
    b = arm.data.bones.get(n); return arm.matrix_world @ b.head_local if b else None
RIGHT, UP, FWD = Vector((-1,0,0)), Vector((0,0,1)), Vector((0,-1,0))
def At(x,y,z): return RIGHT*x + UP*y + FWD*z
def look(z_axis, y_hint):
    z = z_axis.normalized(); y = (y_hint - z*y_hint.dot(z)).normalized(); x = y.cross(z)  # unity: x = cross(y, z) in LH; in RH use y x z
    m = Matrix((x, y, z)).transposed(); return m
UPRIGHT = look(FWD, UP); VERTICAL = look(UP, FWD)
P = {
 'Hero': dict(paint=(0.86,0.88,0.92), trim=(0.16,0.17,0.2), glow=(0.3,1.25,3.2), bulk=1.0, metal=0.05, rough=0.14),
 'Scrapper': dict(paint=(0.82,0.8,0.76), trim=(0.9,0.32,0.08), glow=(3.2,0.75,0.12), bulk=0.95, metal=0.05, rough=0.14),
 'Juggernaut': dict(paint=(0.86,0.62,0.06), trim=(0.08,0.08,0.09), glow=(3.2,1.1,0.15), bulk=1.35, metal=0.05, rough=0.35),
}[style]
def mat(name, col, metal, rough, emit=None, coat=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*[min(1,c) for c in col], 1); b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    if 'Coat Weight' in b.inputs: b.inputs['Coat Weight'].default_value = coat
    if emit: b.inputs['Emission Color'].default_value = (*emit, 1); b.inputs['Emission Strength'].default_value = 3.0
    return m
paint = mat('paint', P['paint'], P['metal'], P['rough'], coat=1.0); trim = mat('trim', P['trim'], 0.9, 0.28); joint = mat('joint', (0.035,0.036,0.04), 0.95, 0.2)
glow = mat('glow', [c*0.2 for c in P['glow']], 0, 0.1, emit=[min(c,1) for c in P['glow']])
suit = mat('suit', (0.06,0.062,0.07), 0.6, 0.45)
body.data.materials.clear(); body.data.materials.append(suit)
B = P['bulk']
def box(name, center, rot, size, radius, m, taper=1.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    o = bpy.context.active_object; o.name = name
    o.matrix_world = Matrix.Translation(center) @ rot.to_4x4() @ Matrix.Diagonal((*size, 1))
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True) if False else None
    md = o.modifiers.new('bev', 'BEVEL'); md.width = radius / max(1e-4, max(size)) * 0.9 ; md.segments = 3; md.limit_method = 'NONE'
    o.data.materials.append(m)
    for p in o.data.polygons: p.use_smooth = True
    return o
def limb(name, a, b, w, d, lenFrac, cf, taper, m):
    A, Bp = bone(a), bone(b)
    if A is None or Bp is None: return
    dvec = Bp - A; L = dvec.length; dv = dvec / L
    upHint = FWD - dv * FWD.dot(dv)
    if upHint.length < 1e-3: upHint = UP - dv*UP.dot(dv)
    rot = look(dv, upHint)
    size = (w*B, d*B, L*lenFrac)
    box(name, A + dv*(L*cf), rot, size, min(size[0], size[1])*0.36, m)
def ball(name, at, r): box(name, at, UPRIGHT, (2*r,2*r,2*r), r*0.99, joint)
# torso
box('Pelvis', At(0,1.0,0), UPRIGHT, (0.34*B,0.15,0.25*B), 0.06, trim)
box('Codpiece', At(0,0.95,0.12*B), UPRIGHT, (0.12,0.1,0.06), 0.025, paint)
box('Abdomen', At(0,1.17,0), UPRIGHT, (0.27*B,0.16,0.23*B), 0.06, joint)
box('AbPlate', At(0,1.16,0.105*B), UPRIGHT, (0.17*B,0.11,0.04), 0.018, paint)
box('Chest', At(0,1.37,0.015), VERTICAL, (0.4*B,0.33*B,0.3), 0.085, paint)
box('ChestCore', At(0,1.405,0.175*B), UPRIGHT, (0.085,0.085,0.03), 0.02, glow)
box('CollarL', At(0.12*B,1.505,0.03), UPRIGHT, (0.12,0.035,0.2*B), 0.012, trim)
box('CollarR', At(-0.12*B,1.505,0.03), UPRIGHT, (0.12,0.035,0.2*B), 0.012, trim)
box('Pack', At(0,1.38,-0.2*B), UPRIGHT, (0.27*B,0.25,0.12), 0.035, trim)
for i in range(3): box('Vent', At(0,1.32+i*0.055,-0.262*B), UPRIGHT, (0.18*B,0.013,0.012), 0.004, glow)
box('Neck', At(0,1.535,0), UPRIGHT, (0.1,0.09,0.1), 0.04, joint)
# head shrink
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='POSE'); arm.pose.bones['Head'].scale = (0.62,0.62,0.62); bpy.ops.object.mode_set(mode='OBJECT')
if style == 'Hero':
    box('Helmet', At(0,1.665,0), UPRIGHT, (0.24,0.28,0.29), 0.09, paint)
    box('Visor', At(0,1.67,0.128), UPRIGHT, (0.2,0.045,0.05), 0.016, glow)
    box('VisorBar', At(0,1.62,0.125), UPRIGHT, (0.035,0.07,0.05), 0.012, glow)
    box('Faceplate', At(0,1.59,0.115), UPRIGHT, (0.16,0.07,0.06), 0.025, trim)
    box('Crest', At(0,1.8,-0.01), UPRIGHT, (0.025,0.06,0.22), 0.01, trim)
elif style == 'Scrapper':
    box('Helmet', At(0,1.66,0), UPRIGHT, (0.26,0.26,0.27), 0.115, paint)
    box('Eye', At(0.052,1.67,0.128), UPRIGHT, (0.055,0.032,0.03), 0.012, glow); box('Eye', At(-0.052,1.67,0.128), UPRIGHT, (0.055,0.032,0.03), 0.012, glow)
    box('Jaw', At(0,1.585,0.09), UPRIGHT, (0.17,0.06,0.1), 0.025, trim)
else:
    box('Helm', At(0,1.65,0.01), UPRIGHT, (0.3,0.24,0.3), 0.06, paint)
    box('Cyclops', At(0,1.66,0.155), UPRIGHT, (0.09,0.05,0.04), 0.02, glow)
    box('Grille', At(0,1.585,0.14), UPRIGHT, (0.18,0.06,0.05), 0.015, trim)
for sx in (1,-1):
    box('Ear', At(0.135*sx,1.65,0), UPRIGHT, (0.045,0.1,0.1), 0.02, trim); box('EarL', At(0.16*sx,1.65,0), UPRIGHT, (0.012,0.05,0.05), 0.005, glow)
pad = 1.3 if style == 'Juggernaut' else 1.0
for side in ('Left','Right'):
    sh = bone(side+'Arm'); el = bone(side+'Forearm'); wr = bone(side+'Hand')
    out = (sh - Vector((0,0,sh.z))); out.z = 0; out.normalize()
    ang = 18 if out.dot(RIGHT) < 0 else -18
    q = Quaternion(FWD, math.radians(-ang))  # unity LH positive -> RH negative
    padRot = q.to_matrix() @ UPRIGHT
    box('Pauldron', sh + out*(0.075*B) + UP*0.065, padRot, (0.19*pad*B,0.12*pad,0.21*pad*B), 0.055, paint)
    box('PauldronTrim', sh + out*(0.075*B) + UP*(0.065+0.065*pad), padRot, (0.15*pad*B,0.02,0.16*pad*B), 0.008, trim)
    limb('UpperArm', side+'Arm', side+'Forearm', 0.1, 0.1, 0.62, 0.56, 0.9, trim)
    ball('Elbow', el, 0.062*B)
    limb('Gauntlet', side+'Forearm', side+'Hand', 0.13, 0.14, 0.8, 0.5, 0.82, paint)
    dv = (wr - el); L = dv.length; dv.normalize(); rot = look(dv, FWD - dv*FWD.dot(dv))
    box('GauntletLight', el + dv*(L*0.5) + UP*(0.072*B), rot, (0.022,0.012,L*0.5), 0.005, glow)
    box('Hand', wr + dv*0.05, rot, (0.1,0.065,0.12), 0.025, trim)
    th = bone(side+'Leg'); kn = bone(side+'Calf'); ft = bone(side+'Foot')
    limb('Thigh', side+'Leg', side+'Calf', 0.16, 0.17, 0.78, 0.48, 0.85, paint)
    ball('Knee', kn, 0.066*B)
    box('Kneecap', kn + FWD*(0.07*B), UPRIGHT, (0.11*B,0.11,0.06), 0.025, trim)
    limb('Shin', side+'Calf', side+'Foot', 0.13, 0.15, 0.8, 0.5, 0.8, paint)
    mid = kn.lerp(ft, 0.42); box('ShinLight', mid + FWD*(0.078*B), VERTICAL, (0.022,0.01,(kn-ft).length*0.4), 0.004, glow)
    box('Boot', ft + FWD*0.05 + UP*0.01, UPRIGHT, (0.13*B,0.1,0.27), 0.035, trim)
    box('ToeCap', ft + FWD*0.16, UPRIGHT, (0.12*B,0.06,0.08), 0.025, paint)
# world + camera
w = bpy.data.worlds.new('w'); bpy.context.scene.world = w; w.use_nodes = True
env = w.node_tree.nodes.new('ShaderNodeTexEnvironment'); env.image = bpy.data.images.load(hdri)
w.node_tree.links.new(env.outputs['Color'], w.node_tree.nodes['Background'].inputs['Color']); w.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.2
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 48; sc.cycles.device = 'CPU'; sc.render.resolution_x = 900; sc.render.resolution_y = 700
sc.view_settings.view_transform = 'AgX' if 'AgX' in [i.identifier for i in sc.view_settings.bl_rna.properties['view_transform'].enum_items] else 'Filmic'
key = bpy.data.lights.new('key', 'AREA'); key.energy = 400; key.size = 2; ko = bpy.data.objects.new('key', key); sc.collection.objects.link(ko); ko.location = (-2.5, -3, 3.5); ko.rotation_euler = (math.radians(50), 0, math.radians(-35))
rim = bpy.data.lights.new('rim', 'AREA'); rim.energy = 300; rim.color = (0.4,0.7,1); ro = bpy.data.objects.new('rim', rim); sc.collection.objects.link(ro); ro.location = (2.5, 3, 2.5); ro.rotation_euler = (math.radians(-60), 0, math.radians(140))
bpy.ops.mesh.primitive_plane_add(size=20); fl = bpy.context.active_object; fl.data.materials.append(mat('floor',(0.08,0.08,0.09),0,0.15))
cam = bpy.data.cameras.new('c'); cam.lens = 55; co = bpy.data.objects.new('c', cam); sc.collection.objects.link(co); sc.camera = co
co.location = (-2.6, -3.6, 1.7); d = Vector((0,0,1.0)) - co.location; co.rotation_euler = d.to_track_quat('-Z','Y').to_euler()
sc.render.filepath = OUTPATH; bpy.ops.render.render(write_still=True)
print("rendered", OUTPATH)
