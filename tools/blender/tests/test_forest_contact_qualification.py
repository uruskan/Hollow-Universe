import importlib.util
from pathlib import Path
import sys
import unittest

spec = importlib.util.spec_from_file_location("_qualification_test", Path(__file__).resolve().parents[1] / "forest_contact_qualification.py")
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class QualificationBridgeTests(unittest.TestCase):
    def setUp(self):
        self.points = [(0., 0., 0.), (1., 0., 0.), (1., 0., 1.), (0., 0., 1.)]
        a,b,c,d = self.points
        self.rows = [dict(positions=[a,b,c], triangles=[(0,1,2)]), dict(positions=[a,c,d], triangles=[(0,1,2)])]

    def test_compaction_rebinds_only_used_descriptors_without_rewriting_payload(self):
        document = dict(meshes=[dict(primitives=[dict(indices=3, attributes=dict(POSITION=2, NORMAL=4))])],
            accessors=[dict(bufferView=i, byteOffset=i*4) for i in range(5)],
            bufferViews=[dict(buffer=0 if i < 2 else 2, byteOffset=100*i, byteLength=60) for i in range(5)],
            buffers=[dict(uri='unused.bin', byteLength=20), dict(uri='unused2.bin', byteLength=30),
                     dict(uri='all-original-bytes.bin', byteLength=1000)])
        compact = module.compact_geometry_descriptors(document)
        self.assertEqual([dict(indices=1, attributes=dict(POSITION=0, NORMAL=2))], compact['meshes'][0]['primitives'])
        self.assertEqual([dict(bufferView=i, byteOffset=(i+2)*4) for i in range(3)], compact['accessors'])
        self.assertEqual([dict(buffer=0, byteOffset=100*i, byteLength=60) for i in range(2,5)], compact['bufferViews'])
        self.assertEqual([document['buffers'][2]], compact['buffers'])
        self.assertEqual(5, len(document['accessors']))
        self.assertEqual(compact, module.compact_geometry_descriptors(compact))

    def test_compaction_refuses_unhandled_accessor_references(self):
        base = dict(meshes=[dict(primitives=[dict(indices=0, attributes=dict(POSITION=1))])],
            accessors=[dict(bufferView=0), dict(bufferView=0)],
            bufferViews=[dict(buffer=0)], buffers=[dict(uri='data.bin', byteLength=128)])
        from copy import deepcopy
        invalid = []
        for change in ('animation', 'sparse', 'unknown', 'escape', 'bool'):
            d = deepcopy(base)
            if change == 'animation': d['animations'] = [{}]
            if change == 'sparse': d['accessors'][0]['sparse'] = {'count': 1}
            if change == 'unknown': d['meshes'][0]['primitives'][0]['attributes']['CUSTOM'] = 1
            if change == 'escape': d['accessors'][0]['bufferView'] = 2
            if change == 'bool': d['meshes'][0]['primitives'][0]['indices'] = True
            invalid.append(d)
        for d in invalid:
            with self.assertRaises(module.author.QualificationError): module.compact_geometry_descriptors(d)

    def test_flatten_keeps_seam_vertices_and_primitive_order(self):
        vertices,triangles = module.flatten(self.rows)
        self.assertEqual(6,len(vertices))
        self.assertEqual([(0,1,2),(3,4,5)],triangles)
        self.assertEqual(vertices[0],vertices[3])

    def test_loop_binds_actual_seam_edges_and_closing_segment(self):
        vertices,triangles = module.flatten(self.rows)
        binding = module.bind_loop(vertices,triangles,self.points)
        self.assertEqual([0,1,2,5],binding['vertices'])
        self.assertEqual([0,1,1,2,4,5,5,3],binding['edgeVertices'])

    def test_coordinate_presence_alone_cannot_prove_an_edge(self):
        vertices,triangles = module.flatten(self.rows)
        for loop in ([self.points[0],self.points[1],self.points[3]], self.points[:-1]+[(0.,0.,1.0000000000000002)]):
            with self.assertRaises(module.author.QualificationError): module.bind_loop(vertices,triangles,loop)

    def test_loop_must_be_unique_and_bounded(self):
        vertices,triangles = module.flatten(self.rows)
        for loop in (self.points[:2], self.points+[self.points[0]], [(float(i),0.,0.) for i in range(513)]):
            with self.assertRaises(module.author.QualificationError): module.bind_loop(vertices,triangles,loop)

    def test_exact_maps_do_not_promote_approximation_or_reverse_winding(self):
        vertices,triangles = module.flatten(self.rows)
        physical = dict(vertices=self.points,triangles=[[0,1,2,0],[0,2,3,0]])
        self.assertEqual([[0,0],[1,1]],module.exact_triangle_maps(physical,vertices,triangles))
        self.assertEqual([[1,1]],module.exact_triangle_maps(physical,vertices,[(0,2,1),triangles[1]]))
        vertices[1]=(1.,0.00001,0.)
        self.assertEqual([[1,1]],module.exact_triangle_maps(physical,vertices,triangles))

    def test_duplicate_visual_triangles_are_ambiguous(self):
        vertices,triangles = module.flatten(self.rows)
        with self.assertRaises(module.author.QualificationError):
            module.exact_triangle_maps(dict(vertices=self.points,triangles=[]),vertices,triangles+[triangles[0]])

    def test_local_index_cannot_escape_primitive(self):
        self.rows[1]['triangles']=[(0,1,3)]
        with self.assertRaises(module.author.QualificationError):module.flatten(self.rows)

    def test_physical_exact_indexing_closes_fan_copies_without_moving_corners(self):
        points=[(0.,0.,0.),(1.,0.,0.),(0.,1.,0.),(0.,0.,1.)]
        faces=[(0,2,1),(0,1,3),(0,3,2),(1,2,3)]
        record=dict(vertices=[points[v] for face in faces for v in face],triangles=[list(range(i*3,i*3+3)) for i in range(4)])
        physical,remap=module.physical_topology(record)
        self.assertEqual(4,len(physical['vertices']))
        self.assertEqual(12,len(remap))
        for old,new in zip(record['triangles'],physical['triangles']):
            self.assertEqual([record['vertices'][v] for v in old],[physical['vertices'][v] for v in new[:3]])
        record['vertices'][3]=(0.,0.,0.00000000000001)
        with self.assertRaises(module.author.QualificationError):module.physical_topology(record)

    def test_physical_open_duplicate_or_reversed_faces_cannot_qualify(self):
        points=[(0.,0.,0.),(1.,0.,0.),(0.,1.,0.),(0.,0.,1.)]
        faces=[(0,2,1),(0,1,3),(0,3,2),(1,2,3)]
        for changed in (faces[:-1],faces+[faces[0]],[(0,1,2)]+faces[1:]):
            with self.assertRaises(module.author.QualificationError):module.physical_topology(dict(vertices=points,triangles=changed))


if __name__=='__main__':unittest.main()
