import bpy, sys, mathutils
argv = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=argv[0])
for o in list(bpy.data.objects):
    n=o.name.lower()
    if o.type=='MESH' and not ('_lod0' in n and 'convcol' not in n and 'occ' not in n):
        bpy.data.objects.remove(o)
dg=bpy.context.evaluated_depsgraph_get(); scene=bpy.context.scene
# front: rays along +Y at a grid; report first hit y per (x,z); stern: along -Y
for label, y0, dy in (('FRONT',-60,1),('STERN',50,-1)):
    print(label, "z|x", " ".join(f"{x:6.1f}" for x in [i*0.75 for i in range(-7,8)]))
    for iz in range(0,30):
        z=iz*0.5
        row=[]
        for i in range(-7,8):
            x=i*0.75
            hit,loc,nor,idx,obj,mat=scene.ray_cast(dg,mathutils.Vector((x,y0,z)),mathutils.Vector((0,dy,0)))
            row.append(f"{loc.y:6.1f}" if hit else "   ---")
        print(label, f"{z:4.1f}", " ".join(row))
