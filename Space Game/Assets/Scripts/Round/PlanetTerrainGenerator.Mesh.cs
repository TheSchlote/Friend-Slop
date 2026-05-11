using UnityEngine;

namespace FriendSlop.Round
{
    public partial class PlanetTerrainGenerator
    {
        private void BuildMesh()
        {
            if (meshFilter == null || sphereWorld == null) return;

            var geom = IcosphereMesh.Build(subdivisions);
            var radius = sphereWorld.Radius;
            var verts = new Vector3[geom.Vertices.Length];
            var dispAtVert = new float[geom.Vertices.Length];
            var minDisp = float.PositiveInfinity;
            var maxDisp = float.NegativeInfinity;

            for (var i = 0; i < verts.Length; i++)
            {
                var n = geom.Vertices[i];
                var disp = SampleDisplacement(n);
                verts[i] = n * (radius + disp);
                dispAtVert[i] = disp;
                if (disp < minDisp) minDisp = disp;
                if (disp > maxDisp) maxDisp = disp;
            }

            // Stash extremes so GetNormalizedElevationAt can normalize ad-hoc queries
            // (rock scatterer, future band-aware systems) without rewalking the mesh.
            _minDisplacement = minDisp;
            _maxDisplacement = maxDisp;

            // UV.y in [0, 1] = normalized height. UV.x is held at 0.5 since the
            // gradient texture is a horizontal strip and the V axis carries the band.
            var range = Mathf.Max(0.0001f, maxDisp - minDisp);
            var uvs = new Vector2[verts.Length];
            for (var i = 0; i < uvs.Length; i++)
            {
                uvs[i] = new Vector2(0.5f, (dispAtVert[i] - minDisp) / range);
            }

            var mesh = new Mesh
            {
                name = "ProceduralPlanetSurface",
                indexFormat = verts.Length > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(geom.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;
            if (meshCollider != null) meshCollider.sharedMesh = mesh;

            ApplyTerrainMaterial();
        }
    }
}
