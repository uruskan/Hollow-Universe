"""Stage owned tree models with native UV/PBR, authored LODs and measured trunk fit.

Blender --background --factory-startup --python-exit-code 1 --python this.py -- OUTPUT
Only OUTPUT is written. Source libraries and the reference project remain read-only.
No scene script, GPU render, download or provider job is executed.
"""
import bpy, bmesh, hashlib, json, math, sys
import numpy as np
from pathlib import Path
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ASSETS = Path('D:/GameDev/Assets')
FIR = ASSETS/'Fab/LOLIPOP/uncategorized/Fir-trees-pack-9-models-LODs-and-bi--0015d682-64e8-4d70-9ea6-d5f6faaa22be/blender/e488b0c2-84b3-4816-ba01-cb61ad05f141/christmass_trees_pack.blend'
BIRCH = ASSETS/'Fab/LOLIPOP/Nature-Plants/Five-Birch-trees-pack-lowpoly-LODs--b993c6a4-09bc-4cb4-a0a3-0cfa68299d29/converted-files/783f729a-534b-4e6b-9e96-36905245cbad/five_birch_trees_pack_lowpoly_lods.glb'
PINE = Path('D:/GameDev/Projects/RustMVP/assets/SourceWorld/extras/pine_forest/mature_pine_a.glb')
PINE_ORIGINAL = ASSETS/'Bare-World-Prepared/extras/pine_forest/polyhaven_pine_fir_forest.blend'

def digest(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream,'sha256').hexdigest()

def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

def copy_join(objects, name):
    copies = []
    for source in objects:
        obj = source.copy(); obj.data = source.data.copy()
        bpy.context.scene.collection.objects.link(obj)
        obj.data.transform(source.matrix_world)
        obj.parent = None; obj.matrix_world = Matrix.Identity(4)
        obj.hide_set(False); obj.hide_viewport=False; obj.hide_render=False
        obj.modifiers.clear()
        copies.append(obj)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in copies: obj.select_set(True)
    bpy.context.view_layer.objects.active=copies[0]
    if len(copies)>1: bpy.ops.object.join()
    result=copies[0]; result.name=name; result.data.name=name
    # Source GLB has no useful AO vertex colours. Wind comes from geometric height.
    for attr in list(result.data.color_attributes): result.data.color_attributes.remove(attr)
    return result

def pine_part(source, lod, budget):
    """Keep complete source branch components and leaf cards; never collapse them."""
    obj=source.copy();obj.data=source.data.copy();bpy.context.scene.collection.objects.link(obj)
    material=obj.data.materials[0]
    if 'trunk_02' in material.name:
        # The validated chopping band is immutable. This bundled derived input
        # contains the two prior measured stems, not the rejected crown geometry.
        bpy.data.objects.remove(obj,do_unlink=True)
        obj=copy_join([bpy.data.objects[f'PineStemReference{lod}']],f'PineStem{lod}')
        obj.data.materials.clear();obj.data.materials.append(material)
        for p in obj.data.polygons:p.material_index=0
        smooth_pine_stem(obj)
        return obj
    bm=bmesh.new();bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6)
    seen=set();components=[]
    for first in bm.faces:
        if first in seen:continue
        pending=[first];faces=[]
        while pending:
            f=pending.pop()
            if f in seen:continue
            seen.add(f);faces.append(f)
            pending.extend(n for e in f.edges for n in e.link_faces if n not in seen)
        verts={v for f in faces for v in f.verts}
        centre=sum((v.co for v in verts),Vector())/len(verts)
        h=(int(centre.x*10007)*73856093 ^ int(centre.y*10009)*19349663 ^ int(centre.z*10037)*83492791)&0xffffffff
        area=sum(f.calc_area() for f in faces)
        components.append((faces,h,area))
    remove=[]
    if leaf(material):
        # Hash complete photographed twig islands; never decimate their UVs.
        if lod==1:
            remove=[f for faces,h,_ in components if h%100>=55 for f in faces]
    else:
        # Keep every near branch. The medium LOD keeps the substantial branches
        # first, each with its complete original triangles/UVs. Small twig wood
        # is represented by the photographed needles at this distance.
        if lod==1:
            count=0
            for faces,h,area in sorted(components,key=lambda c:(-c[2],c[1])):
                triangles=sum(len(f.verts)-2 for f in faces)
                if count+triangles<=budget:count+=triangles
                else:remove.extend(faces)
    bmesh.ops.delete(bm,geom=remove,context='FACES')
    bm.to_mesh(obj.data);bm.free()
    fit=PINE_REFERENCE['fit'][0]
    obj.data.transform(obj.matrix_world);obj.parent=None;obj.matrix_world=Matrix.Identity(4)
    centre=Vector(fit['source_bottom']);scale=fit['uniform_height_scale']
    for vertex in obj.data.vertices:vertex.co=(vertex.co-centre)*scale
    return obj

def smooth_pine_stem(obj):
    # No vertex at or below 2.1 m moves. The previous fit shrank to the .14 m
    # scan radius by 4 m. Continue a gradual, monotonic taper through 8.3 m.
    mesh=obj.data;sections=[];prior=(0.,0.)
    for z in np.arange(2.10013,8.701,.2):
        row=stem_profile(mesh,float(z),prior,minimum=3);sections.append(row);prior=row[1:3]
    end_radius=sections[-2][3]
    for vertex in mesh.vertices:
        v=vertex.co
        if v.z<=2.1 or v.z>=8.3:continue
        k=max(0,min(len(sections)-2,int((v.z-sections[0][0])/.2)))
        a,b=sections[k],sections[k+1];f=max(0.,min(1.,(v.z-a[0])/(b[0]-a[0])))
        cx,cy,r=[a[n]*(1-f)+b[n]*f for n in (1,2,3)]
        t=(v.z-2.1)/6.2
        target=.38*(1-t)+end_radius*t
        factor=target/max(r,.00001)
        v.x=cx+(v.x-cx)*factor;v.y=cy+(v.y-cy)*factor
    mesh.update()


def leaf(mat):
    return any(k in mat.name.lower() for k in ('brunch','atlas','needles','leaves'))

def bounds(mesh):
    return (Vector(tuple(min(v.co[a] for v in mesh.vertices) for a in range(3))),
            Vector(tuple(max(v.co[a] for v in mesh.vertices) for a in range(3))))

def wood_vertices(mesh):
    indices = {i for p in mesh.polygons if not leaf(mesh.materials[p.material_index]) for i in p.vertices}
    return [mesh.vertices[i] for i in indices]

def stem_profile(mesh, height, prior, trunk_only=False, minimum=5):
    """Connected cross-section loops; choose the stem continuing from below."""
    mesh.calc_loop_triangles()
    links={}; points={}
    def key(p): return tuple(round(float(x),5) for x in p[:2])
    for tri in mesh.loop_triangles:
        if leaf(mesh.materials[tri.material_index]) or (trunk_only and 'trunk_02' not in mesh.materials[tri.material_index].name): continue
        p=[mesh.vertices[i].co for i in tri.vertices]
        cuts=[]
        for a,b in ((p[0],p[1]),(p[1],p[2]),(p[2],p[0])):
            if (a.z-height)*(b.z-height)<0:
                cuts.append(a+(b-a)*((height-a.z)/(b.z-a.z)))
        if len(cuts)!=2:continue
        a,b=map(key,cuts)
        if a==b:continue
        points[a]=cuts[0];points[b]=cuts[1]
        links.setdefault(a,set()).add(b);links.setdefault(b,set()).add(a)
    components=[];seen=set()
    for start in links:
        if start in seen:continue
        todo=[start];component=[]
        while todo:
            k=todo.pop()
            if k in seen:continue
            seen.add(k);component.append(k);todo.extend(links[k]-seen)
        if len(component)<minimum:continue
        xy=np.array(component,dtype=float)
        # Least-squares circle uses the entire closed contour, not whichever
        # source vertices happen to lie in a broad height band.
        system=np.column_stack((2*xy[:,0],2*xy[:,1],np.ones(len(xy))))
        cx,cy,c=np.linalg.lstsq(system,(xy*xy).sum(axis=1),rcond=None)[0]
        radius=float(np.median(np.linalg.norm(xy-[cx,cy],axis=1)))
        if radius<.015 or radius>1.5:continue
        score=math.hypot(cx-prior[0],cy-prior[1])+max(0.,radius-.55)
        components.append((score,float(cx),float(cy),radius))
    if not components: raise RuntimeError(f'Missing closed stem section at {height}')
    _,cx,cy,r=min(components)
    return (height,cx,cy,r)

def normalize(obj, height, radius, root_flare=False):
    mesh=obj.data
    lo,hi=bounds(mesh)
    wood=wood_vertices(mesh)
    low=min(v.co.z for v in wood)
    foot=[v.co for v in wood if v.co.z < low+(hi.z-low)*.025]
    centre=Vector(((min(v.x for v in foot)+max(v.x for v in foot))*.5,
                   (min(v.y for v in foot)+max(v.y for v in foot))*.5,low))
    scale=height/(hi.z-low)
    for v in mesh.vertices: v.co=(v.co-centre)*scale
    # Existing trees are gameplay capsules. Sample the actual closed stem every
    # 20 cm, then fit each cross-section independently; broad height-band medians
    # fail on crooked stems because their centre wanders with height.
    sections=[];prior=(0.,0.)
    for z in np.arange(.10013,4.301, .2):
        section=stem_profile(mesh,float(z),prior);sections.append(section);prior=section[1:3]
    if root_flare:
        # Pine's scan forks into several asymmetrical roots below 0.9 m. A
        # least-squares circle through a root contour can put its centre outside
        # the trunk. Continue the established stem axis down through that flare;
        # keep its spread only below the playable chop band.
        anchor=sections[4]
        for k in range(4): sections[k]=(sections[k][0],anchor[1],anchor[2],anchor[3])
    # Long source triangles need rings before bending, or their straight edges
    # would cut through the fitted cylinder between sparse source vertices.
    bm=bmesh.new();bm.from_mesh(mesh)
    for z in np.arange(.3,4.01,.2):
        k=max(0,min(len(sections)-1,int((z-sections[0][0])/.2)))
        _,cx,cy,r=sections[k]
        faces=[f for f in bm.faces if not leaf(mesh.materials[f.material_index]) and
               min(v.co.z for v in f.verts)<z<max(v.co.z for v in f.verts) and
               min(math.hypot(v.co.x-cx,v.co.y-cy) for v in f.verts)<r*1.7]
        edges={e for f in faces for e in f.edges};verts={v for e in edges for v in e.verts}
        bmesh.ops.bisect_plane(bm,geom=list(verts)+list(edges)+faces,
            plane_co=(0,0,float(z)),plane_no=(0,0,1),dist=.000001,
            clear_inner=False,clear_outer=False)
    # Add angular support only to the chop stem. Very coarse source rings would
    # otherwise inscribe a square/octagon well inside the gameplay capsule.
    edges=[]
    for e in bm.edges:
        if max(v.co.z for v in e.verts)>2.5 or min(v.co.z for v in e.verts)<.3:continue
        if not any(not leaf(mesh.materials[f.material_index]) for f in e.link_faces):continue
        z=sum(v.co.z for v in e.verts)*.5
        _,cx,cy,r=sections[max(0,min(len(sections)-1,int((z-sections[0][0])/.2)))]
        if max(math.hypot(v.co.x-cx,v.co.y-cy) for v in e.verts)<r*1.7:edges.append(e)
    bmesh.ops.subdivide_edges(bm,edges=edges,cuts=1,use_grid_fill=True)
    bm.to_mesh(mesh);bm.free()
    wood_ids={i for p in mesh.polygons if not leaf(mesh.materials[p.material_index]) for i in p.vertices}
    for vertex in mesh.vertices:
        v=vertex.co
        if v.z>4.0 or vertex.index not in wood_ids: continue
        k=max(0,min(len(sections)-2,int((v.z-sections[0][0])/.2)))
        a,b=sections[k],sections[k+1];t=max(0.,min(1.,(v.z-a[0])/(b[0]-a[0])))
        cx,cy,r=[a[n]*(1-t)+b[n]*t for n in (1,2,3)]
        radial=math.hypot(v.x-cx,v.y-cy)
        stem=max(0.,min(1.,(r*3.-radial)/(r*1.3)))
        if root_flare and .5 <= v.z <= 1.1 and radial < 1.5: stem=1.
        vertical=1. if v.z<=2.25 else max(0.,min(1.,(4.-v.z)/1.75))
        vertical=vertical*vertical*(3-2*vertical)
        weight=stem*vertical
        # Keep up to 2% scanned relief while making the actual target cylinder
        # visible. The supplied texture UVs and the crown above 4m are unchanged.
        target=radius*(1+max(-.02,min(.02,radial/r-1)))
        factor=target/max(radial,.00001)
        v.x += ((v.x-cx)*factor-v.x)*weight
        v.y += ((v.y-cy)*factor-v.y)*weight
    mesh.update()
    return dict(source_bottom=list(centre),uniform_height_scale=float(scale),
                target_chop_radius=radius,source_stem_sections=sections,stem_fit_end_m=4.0)

def trunk_report(obj):
    mesh=obj.data
    faces=[list(p.vertices) for p in mesh.polygons if not leaf(mesh.materials[p.material_index])]
    bvh=BVHTree.FromPolygons([v.co for v in mesh.vertices],faces,all_triangles=False)
    rows=[]
    for z in (.7,1.,1.5,2.):
        radii=[]
        for n in range(16):
            a=2*math.pi*n/16; direction=Vector((math.cos(a),math.sin(a),0))
            hit,_,_,_=bvh.ray_cast(Vector((0,0,z)),direction,2)
            if hit is not None: radii.append(math.hypot(hit.x,hit.y))
        rows.append(dict(height=z,hits=len(radii),minimum=min(radii) if radii else None,
                         maximum=max(radii) if radii else None,median=float(np.median(radii)) if radii else None))
    return rows

def simplify(obj, budget):
    obj.data.calc_loop_triangles()
    if len(obj.data.loop_triangles)<=budget: return
    # Lower stem was fitted and measured: never simplify it back off the capsule.
    # Faces meeting that band stay intact; only the high crown/branches reduce.
    keep=obj.copy();keep.data=obj.data.copy();bpy.context.collection.objects.link(keep)
    bm=bmesh.new();bm.from_mesh(keep.data)
    bmesh.ops.delete(bm,geom=[f for f in bm.faces if min(v.co.z for v in f.verts)>=4.0],context='FACES')
    bm.to_mesh(keep.data);bm.free();keep.data.calc_loop_triangles()
    bm=bmesh.new();bm.from_mesh(obj.data)
    bmesh.ops.delete(bm,geom=[f for f in bm.faces if min(v.co.z for v in f.verts)<4.0],context='FACES')
    bm.to_mesh(obj.data);bm.free();obj.data.calc_loop_triangles()
    activate(obj)
    mod=obj.modifiers.new('Bounded far crown','DECIMATE')
    mod.ratio=max(.03,(budget-len(keep.data.loop_triangles))/max(1,len(obj.data.loop_triangles)))
    bpy.ops.object.modifier_apply(modifier=mod.name)
    activate(obj);keep.select_set(True);bpy.ops.object.join()

def material_from_maps(name, albedo, normal, rough=None, orm=None, alpha=False, roughness=.88, rough_channel=None):
    mat=bpy.data.materials.new(name); mat.use_nodes=True
    mat.use_backface_culling=not alpha
    nodes,links=mat.node_tree.nodes,mat.node_tree.links
    p=nodes.get('Principled BSDF'); p.inputs['Roughness'].default_value=roughness
    def tex(img, color=False):
        n=nodes.new('ShaderNodeTexImage'); n.image=img
        img.colorspace_settings.name='sRGB' if color else 'Non-Color'
        return n
    a=tex(albedo,True); links.new(a.outputs['Color'],p.inputs['Base Color'])
    if alpha:
        links.new(a.outputs['Alpha'],p.inputs['Alpha']); mat.surface_render_method='DITHERED'
    n=nodes.new('ShaderNodeNormalMap'); t=tex(normal)
    links.new(t.outputs['Color'],n.inputs['Color']);links.new(n.outputs['Normal'],p.inputs['Normal'])
    if rough:
        output=tex(rough).outputs['Color']
        if rough_channel is not None:
            sep=nodes.new('ShaderNodeSeparateColor')
            links.new(output,sep.inputs['Color'])
            output=sep.outputs[rough_channel]
        links.new(output,p.inputs['Roughness'])
    if orm:
        t=tex(orm); sep=nodes.new('ShaderNodeSeparateColor')
        links.new(t.outputs['Color'],sep.inputs['Color'])
        links.new(sep.outputs['Green'],p.inputs['Roughness'])
        links.new(sep.outputs['Blue'],p.inputs['Metallic'])
        group=bpy.data.node_groups.get('glTF Material Output')
        if not group:
            group=bpy.data.node_groups.new('glTF Material Output','ShaderNodeTree')
            group.interface.new_socket(name='Occlusion',in_out='INPUT',socket_type='NodeSocketFloat')
        gn=nodes.new('ShaderNodeGroup');gn.node_tree=group
        links.new(sep.outputs['Red'],gn.inputs['Occlusion'])
    return mat

def prepare_fir_materials(dest):
    mappings={}
    for old,prefix,alpha in [('Bark_Mat','Bark',False),('Brunches_Mat','Brunches',True)]:
        src=bpy.data.images[prefix+'_MRAO.png'];src.colorspace_settings.name='Non-Color'
        px=np.empty(src.size[0]*src.size[1]*4,dtype=np.float32);src.pixels.foreach_get(px)
        data=px.reshape((-1,4));data[:,[0,2]]=data[:,[2,0]]
        orm=bpy.data.images.new('fir_'+prefix+'_ORM',width=src.size[0],height=src.size[1],alpha=True)
        orm.colorspace_settings.name='Non-Color';orm.pixels.foreach_set(data.ravel())
        orm.filepath_raw=str(dest/('fir_'+prefix+'_ORM.png'));orm.file_format='PNG';orm.save()
        # Reload gives the glTF exporter a matching on-disk payload.
        bpy.data.images.remove(orm);orm=bpy.data.images.load(str(dest/('fir_'+prefix+'_ORM.png')))
        albedo=bpy.data.images[prefix+'Albedo.png'];normal=bpy.data.images[prefix+'_normal.png']
        albedo.name='fir_'+prefix+'_albedo';normal.name='fir_'+prefix+'_normal'
        mappings[bpy.data.materials[old]]=material_from_maps('fir_'+('leaves' if alpha else 'bark'),albedo,normal,orm=orm,alpha=alpha)
    return mappings

def scalar_texture(socket):
    """Preserve a glTF scalar map's channel, including Blender's separator node.

    A linked Principled socket's default_value is unused, so it must not become
    a multiplier. Reject unsupported graphs instead of silently dropping maps.
    """
    if not socket.is_linked:
        return None, None, float(socket.default_value)
    link=socket.links[0]
    node=link.from_node
    if node.type=='TEX_IMAGE' and node.image:
        return node.image, None, 1.
    if node.type in ('SEPARATE_COLOR','SEPRGB'):
        channel={'R':'Red','G':'Green','B':'Blue'}.get(link.from_socket.name,link.from_socket.name)
        source=node.inputs[0]
        if channel in ('Red','Green','Blue') and source.is_linked:
            image_node=source.links[0].from_node
            if image_node.type=='TEX_IMAGE' and image_node.image:
                return image_node.image, channel, 1.
    raise RuntimeError('Unsupported source scalar texture graph: '+node.name)


def prepare_import_materials(family):
    mappings={}
    for mat in list(bpy.data.materials):
        if not mat.use_nodes: continue
        p=next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
        if not p: continue
        textures=[n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
        albedo=next((n.image for n in textures if any(l.to_node==p and l.to_socket.name=='Base Color' for l in n.outputs['Color'].links)),None)
        normal=next((n.image for n in textures if any(l.to_node.type=='NORMAL_MAP' for l in n.outputs['Color'].links)),None)
        rough,rough_channel,roughness=scalar_texture(p.inputs['Roughness'])
        if albedo is None or normal is None:
            if not leaf(mat): raise RuntimeError('Source material missing maps: '+mat.name)
            # Pine's supplied photographed twig bake has no normal map. Its
            # geometric card normals are retained; use a neutral tangent normal.
            normal=bpy.data.images.get('tree_neutral_normal')
            if not normal:
                normal=bpy.data.images.new('tree_neutral_normal',width=4,height=4)
                normal.pixels[:]=[.5,.5,1.,1.]*16
        for suffix,img in [('albedo',albedo),('normal',normal),('roughness',rough)]:
            if img: img.name=family+'_'+mat.name+'_'+suffix
        mappings[mat]=material_from_maps(family+'_'+('leaves' if leaf(mat) else 'bark_'+str(len(mappings))),albedo,normal,rough=rough,alpha=leaf(mat),roughness=roughness,rough_channel=rough_channel)
    return mappings

def export_family(dest,family,path,variants,height,radius):
    if path.suffix=='.blend': bpy.ops.wm.open_mainfile(filepath=str(path),load_ui=False,use_scripts=False)
    else:
        bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(path))
    mats=prepare_fir_materials(dest) if family=='fir' else prepare_import_materials(family)
    if family=='pine':
        global PINE_REFERENCE
        reference=Path(__file__).parent/'source_geometry/pine_chop_stems.glb'
        PINE_REFERENCE=json.loads(reference.with_suffix('.json').read_text())
        if digest(reference)!=PINE_REFERENCE['sha256']:raise RuntimeError('Validated pine stem reference changed')
        bpy.ops.import_scene.gltf(filepath=str(reference))
    outputs=[];rows=[]
    for index,levels in enumerate(variants):
        row=dict(key=f'{family}_{index:02d}',levels=[])
        for lod,names in enumerate(levels):
            objects=[bpy.data.objects[name] for name in names]
            temporary=[]
            if family=='pine':
                budgets=(0,650,0,0) if lod==0 else (2200,250,0,450)
                temporary=[pine_part(o,lod,b) for o,b in zip(objects,budgets)]
                objects=temporary
            obj=copy_join(objects,f'{family}_{index:02d}_lod{lod}')
            for part in temporary:bpy.data.objects.remove(part,do_unlink=True)
            fit=dict(PINE_REFERENCE['fit'][lod],stem_fit_end_m=8.3,immutable_chop_band_top_m=2.1) if family=='pine' else normalize(obj,height,radius)
            # Fir/Birch use the creator's silhouette LODs. Pine derivative has a
            # single source mesh; its secondary geometry gets a bounded decimate.
            # Pine branch components and leaf cards are immutable after selection.
            # Never run collapse decimation over their disconnected atlas UVs.
            for i,mat in enumerate(obj.data.materials): obj.data.materials[i]=mats[mat]
            obj.data.calc_loop_triangles();lo,hi=bounds(obj.data)
            if family=='pine':
                budget=26000 if lod==0 else 8000
                if len(obj.data.loop_triangles)>budget:raise RuntimeError('Pine LOD exceeds its reviewed triangle budget')
                for tri in obj.data.loop_triangles:
                    if obj.data.materials[tri.material_index].name=='pine_bark_0':
                        a,b,c=[obj.data.vertices[v].co for v in tri.vertices]
                        if (b-a).cross(c-a).length*.5>.02:
                            raise RuntimeError('Pine upper branch triangle was stretched or collapsed')
            radial=max(math.hypot(v.co.x,v.co.y) for v in obj.data.vertices)
            report=trunk_report(obj)
            for sample in report:
                if sample['hits']!=16 or sample['minimum']<radius-.025 or sample['maximum']>radius+.025:
                    raise RuntimeError(f'{obj.name}: invalid chopping-band fit {sample}; target radius {radius}')
            row['levels'].append(dict(node=obj.name,triangles=len(obj.data.loop_triangles),
                height=hi.z,extent=max(hi.z,2*radial,hi.z-2*lo.z)*1.04,
                bounds_min=list(lo),bounds_max=list(hi),fit=fit,chop_band=report))
            outputs.append(obj)
        rows.append(row)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in outputs:obj.select_set(True)
    target=dest/f'authored_{family}.gltf'
    bpy.ops.export_scene.gltf(filepath=str(target),export_format='GLTF_SEPARATE',use_selection=True,
        export_yup=True,export_materials='EXPORT',export_animations=False,export_cameras=False,export_lights=False,
        export_extras=False,export_image_format='AUTO')
    data=json.loads(target.read_text())
    cutoff=.42
    if family=='pine':
        # Keep the supplied photographed twig coverage, not the fir/birch default.
        import struct
        source_bytes=path.read_bytes()
        length,kind=struct.unpack_from('<II',source_bytes,12)
        if kind!=0x4e4f534a:raise RuntimeError('Pine source has no glTF JSON chunk')
        source_data=json.loads(source_bytes[20:20+length])
        leaves=[m for m in source_data['materials'] if m.get('alphaMode')=='MASK']
        if len(leaves)!=1:raise RuntimeError('Pine source leaf material changed')
        cutoff=leaves[0].get('alphaCutoff',.5)
    for mat in data.get('materials',[]):
        if 'leaves' in mat['name']: mat.update(alphaMode='MASK',alphaCutoff=cutoff,doubleSided=True)
    target.write_text(json.dumps(data,separators=(',',':')),encoding='utf8')
    files=[target]+[dest/r['uri'] for field in ('buffers','images') for r in data.get(field,[]) if 'uri' in r]
    print('TREE_EXPORT',family,[(r['key'],[l['triangles'] for l in r['levels']]) for r in rows],flush=True)
    return dict(kind=family,name=family,model='res://assets/surface/trees/'+target.name,scale=1.,near=50.,far=190.,
        source=dict(path=str(path),sha256=digest(path),permission='User-supplied owned assets; source kept read-only'),variants=rows,
        outputs=[dict(file=p.name,sha256=digest(p),bytes=p.stat().st_size) for p in set(files)])

def main():
    dest=Path(sys.argv[sys.argv.index('--')+1]).resolve();dest.mkdir(parents=True,exist_ok=True)
    fir=[[[f'Christmas tree{suffix}_LOD0'],[f'Christmas tree{suffix}_LOD2']] for suffix in ('','_2','_3')]
    birch=[[[f'Birch{suffix}_Birch_bark_0',f'Birch{suffix}_Birch_Atlas_0'],
            [f'Birch{suffix}_LOD2_Birch_bark_0',f'Birch{suffix}_LOD2_Birch_Atlas_0']] for suffix in ('','_2','_3')]
    pine=[[['PinePart','PinePart.001','PinePart.002','PinePart.003']]*2]
    rows=[export_family(dest,'fir',FIR,fir,18.2,.36),export_family(dest,'birch',BIRCH,birch,15.,.24),
          export_family(dest,'pine',PINE,pine,19.2,.38)]
    rows[-1]['source']['original']=dict(path=str(PINE_ORIGINAL),sha256=digest(PINE_ORIGINAL))
    receipt=PINE.parent/'MATURE-PINE-PROVENANCE.json'
    rows[-1]['source']['derivative_receipt']=dict(path=str(receipt),sha256=digest(receipt),content=json.loads(receipt.read_text()))
    catalog=dict(version=1,method='Native UV/PBR; measured foot centred at origin; species target height; lower stem independently fitted to unchanged gameplay capsule; creator LODs where supplied.',species=rows)
    (dest/'catalog.json').write_text(json.dumps(catalog,indent=2),encoding='utf8')
    print('TREES_STAGED',dest,flush=True)

if __name__=='__main__':main()
