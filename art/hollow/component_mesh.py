"""Small deterministic mesh helpers for original cosmetic component assets.

These build presentation geometry, not mechanically accurate assemblies. Texture
regions use top-down image coordinates; UVs are flipped for Blender on assignment.
"""
import math
import bpy
import bmesh
from mathutils import Vector
from .export import select_only


class ComponentMesh:
    def __init__(self, collection, material, regions, lod):
        self.collection, self.material, self.regions, self.lod = collection, material, regions, lod
        self.parts = []

    def mesh(self, name, vertices, faces, region, smooth=False, uvs=None):
        me = bpy.data.meshes.new(name)
        me.from_pydata(vertices, [], faces)
        me.update()
        bm = bmesh.new(); bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me); bm.free()
        obj = bpy.data.objects.new(name, me); self.collection.objects.link(obj)
        me.materials.append(self.material)
        layer = me.uv_layers.new(name='UVMap')
        lo = [min(v[i] for v in vertices) for i in range(3)]
        span = [max(max(v[i] for v in vertices)-lo[i], .0001) for i in range(3)]
        x,y,w,h = self.regions[region]
        for face in me.polygons:
            face.use_smooth = smooth
            axis = max(range(3), key=lambda i:abs(face.normal[i]))
            a,b = [(1,2),(0,2),(0,1)][axis]
            for loop in face.loop_indices:
                idx = me.loops[loop].vertex_index
                u,v = uvs[idx] if uvs else ((vertices[idx][a]-lo[a])/span[a],1-(vertices[idx][b]-lo[b])/span[b])
                layer.data[loop].uv = (x+(.015+.97*u)*w,1-y-(.015+.97*v)*h)
        self.parts.append(obj)
        return obj

    def profile(self, name, points, front, back, region, bevel=.001):
        n=len(points)
        verts=[(x,y,z) for y in (front,back) for x,z in points]
        faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]
        faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        obj=self.mesh(name,verts,faces,region)
        if bevel and self.lod<2:
            select_only([obj]);mod=obj.modifiers.new('Soft manufactured edges','BEVEL')
            mod.width=bevel;mod.segments=2 if self.lod==0 else 1
            bpy.ops.object.modifier_apply(modifier=mod.name)
            # Flat panels retain their own normals while the bevel catches light.
        return obj

    def box(self, name, centre, size, region, bevel=.001):
        x,y,z=centre;w,d,h=size
        return self.profile(name,[(x-w/2,z-h/2),(x+w/2,z-h/2),(x+w/2,z+h/2),(x-w/2,z+h/2)],y-d/2,y+d/2,region,bevel)

    def lathe_x(self, name, rings, centre_yz, region, segments=None):
        """Profile of x/radius rings; caller closes ends with zero-radius rings."""
        count=segments or (40,24,14)[self.lod];cy,cz=centre_yz
        verts=[];uvs=[];faces=[]
        low=min(x for x,r in rings);length=max(x for x,r in rings)-low
        for x,radius in rings:
            for j in range(count+1):
                a=math.tau*j/count
                verts.append((x,cy+radius*math.cos(a),cz+radius*math.sin(a)))
                uvs.append(((x-low)/max(length,.001),j/count))
        for k in range(len(rings)-1):
            for j in range(count):
                a=k*(count+1)+j;faces.append((a,a+1,a+count+2,a+count+1))
        obj=self.mesh(name,verts,faces,region,True,uvs)
        # Merge UV seam duplicate positions for normal continuity.
        bm=bmesh.new();bm.from_mesh(obj.data)
        bmesh.ops.remove_doubles(bm,verts=bm.verts,dist=.000001)
        bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
        bm.to_mesh(obj.data);bm.free()
        return obj

    def cable(self, name, points, radius, region, closed=True, sides=None):
        count=sides or (8,6,4)[self.lod]
        pts=[Vector(p) for p in points];verts=[];uvs=[];faces=[]
        for i,p in enumerate(pts):
            tangent=(pts[(i+1)%len(pts)]-pts[(i-1)%len(pts)]).normalized() if closed else (pts[min(i+1,len(pts)-1)]-pts[max(i-1,0)]).normalized()
            axis=tangent.cross(Vector((1,0,0)))
            if axis.length<.01:axis=tangent.cross(Vector((0,0,1)))
            axis.normalize();other=tangent.cross(axis).normalized()
            for j in range(count):
                a=math.tau*j/count;v=p+radius*(math.cos(a)*axis+math.sin(a)*other)
                verts.append(tuple(v));uvs.append((i/max(len(pts)-1,1),j/count))
        for i in range(len(pts) if closed else len(pts)-1):
            nxt=(i+1)%len(pts)
            for j in range(count):faces.append((i*count+j,i*count+(j+1)%count,nxt*count+(j+1)%count,nxt*count+j))
        return self.mesh(name,verts,faces,region,True,uvs)

    def finish(self,name):
        select_only(self.parts);bpy.ops.object.join()
        obj=bpy.context.object;obj.name=name;obj.data.name=name
        # All parts share the same atlas, so collapse repeated material slots.
        obj.data.materials.clear();obj.data.materials.append(self.material)
        for p in obj.data.polygons:p.material_index=0
        return obj
