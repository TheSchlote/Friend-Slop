using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Round
{
    // Builds a unit-radius icosphere via classic icosahedron-subdivision (each face
    // becomes four sub-faces by inserting edge midpoints, then renormalizing each
    // midpoint to length 1). Triangles stay roughly equilateral, so noise-driven
    // displacement reads evenly across the surface - far better than UV-sphere primitives
    // which pinch at the poles. Subdivision counts: V = 10*4^N + 2, T = 20*4^N.
    public static class IcosphereMesh
    {
        public readonly struct Geometry
        {
            public readonly Vector3[] Vertices;
            public readonly int[] Triangles;
            public Geometry(Vector3[] vertices, int[] triangles)
            {
                Vertices = vertices;
                Triangles = triangles;
            }
        }

        public static int VertexCount(int subdivisions) => 10 * Pow4(subdivisions) + 2;
        public static int TriangleCount(int subdivisions) => 20 * Pow4(subdivisions);

        public static Mesh BuildMesh(int subdivisions, string meshName = "Icosphere")
        {
            var geom = Build(subdivisions);
            var mesh = new Mesh
            {
                name = meshName,
                indexFormat = geom.Vertices.Length > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            mesh.SetVertices(geom.Vertices);
            mesh.SetTriangles(geom.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Geometry Build(int subdivisions)
        {
            subdivisions = Mathf.Clamp(subdivisions, 0, 7);

            var verts = new List<Vector3>(VertexCount(subdivisions));
            var tris = new List<int>(TriangleCount(subdivisions) * 3);
            SeedIcosahedron(verts, tris);

            // Edge-midpoint cache keyed by the smaller-then-larger vertex index pair.
            // Without it, every shared edge would produce two coincident midpoints,
            // breaking the watertight surface and bloating the vertex count by ~2x.
            var midpointCache = new Dictionary<long, int>();
            for (var step = 0; step < subdivisions; step++)
            {
                midpointCache.Clear();
                var nextTris = new List<int>(tris.Count * 4);
                for (var t = 0; t < tris.Count; t += 3)
                {
                    var a = tris[t];
                    var b = tris[t + 1];
                    var c = tris[t + 2];
                    var ab = GetOrCreateMidpoint(a, b, verts, midpointCache);
                    var bc = GetOrCreateMidpoint(b, c, verts, midpointCache);
                    var ca = GetOrCreateMidpoint(c, a, verts, midpointCache);
                    nextTris.Add(a); nextTris.Add(ab); nextTris.Add(ca);
                    nextTris.Add(b); nextTris.Add(bc); nextTris.Add(ab);
                    nextTris.Add(c); nextTris.Add(ca); nextTris.Add(bc);
                    nextTris.Add(ab); nextTris.Add(bc); nextTris.Add(ca);
                }
                tris = nextTris;
            }

            return new Geometry(verts.ToArray(), tris.ToArray());
        }

        private static void SeedIcosahedron(List<Vector3> verts, List<int> tris)
        {
            // Standard icosahedron in "golden rectangle" form. Each vertex is on the unit
            // sphere after the explicit normalization below.
            var t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            verts.Add(new Vector3(-1,  t,  0).normalized);
            verts.Add(new Vector3( 1,  t,  0).normalized);
            verts.Add(new Vector3(-1, -t,  0).normalized);
            verts.Add(new Vector3( 1, -t,  0).normalized);
            verts.Add(new Vector3( 0, -1,  t).normalized);
            verts.Add(new Vector3( 0,  1,  t).normalized);
            verts.Add(new Vector3( 0, -1, -t).normalized);
            verts.Add(new Vector3( 0,  1, -t).normalized);
            verts.Add(new Vector3( t,  0, -1).normalized);
            verts.Add(new Vector3( t,  0,  1).normalized);
            verts.Add(new Vector3(-t,  0, -1).normalized);
            verts.Add(new Vector3(-t,  0,  1).normalized);

            int[] seed = {
                0,11, 5,   0, 5, 1,   0, 1, 7,   0, 7,10,   0,10,11,
                1, 5, 9,   5,11, 4,  11,10, 2,  10, 7, 6,   7, 1, 8,
                3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
                4, 9, 5,   2, 4,11,   6, 2,10,   8, 6, 7,   9, 8, 1,
            };
            tris.AddRange(seed);
        }

        private static int GetOrCreateMidpoint(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
        {
            // Encode edge as (small, large) so (a,b) and (b,a) hash to the same key.
            var lo = a < b ? a : b;
            var hi = a < b ? b : a;
            var key = ((long)lo << 32) | (uint)hi;
            if (cache.TryGetValue(key, out var existing)) return existing;

            var mid = ((verts[a] + verts[b]) * 0.5f).normalized;
            var index = verts.Count;
            verts.Add(mid);
            cache[key] = index;
            return index;
        }

        private static int Pow4(int n)
        {
            var r = 1;
            for (var i = 0; i < n; i++) r *= 4;
            return r;
        }
    }
}
