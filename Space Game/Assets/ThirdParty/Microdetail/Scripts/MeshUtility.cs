using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Microdetail
{
    public static class MeshUtility
    {
        private static List<int> paralelepipedTrianglesList = new List<int>()
            {
                0, 2, 1, 1, 2, 3,
                4, 5, 6, 5, 7, 6,
                0, 1, 4, 1, 5, 4,
                2, 6, 3, 3, 6, 7,
                0, 4, 2, 2, 4, 6,
                1, 3, 5, 5, 3, 7
            };

        private static Vector3[] quadVertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0),
                new Vector3(0.5f, 0.5f, 0)
            };
        
        private static int[] quadTriangles = new int[]
            {
                0, 2, 1,
                2, 3, 1
            };
        
        private static Vector2[] quadUV  = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(0, 1),
                new Vector2(1, 1)
            };

        private static Vector3[] quadNormals = new Vector3[]
            {
                Vector3.back,
                Vector3.back,
                Vector3.back,
                Vector3.back
            };

        private static Vector4[] quadTangents = new Vector4[]
            {
                new Vector4(1, 0, 0, -1),
                new Vector4(1, 0, 0, -1),
                new Vector4(1, 0, 0, -1),
                new Vector4(1, 0, 0, -1)
            };

        public static void GenerateParallelepiped(Mesh target, Vector3 size)
        {
            var halfSize = size * 0.5f;

            using var vertices = new NativeList<Vector3>(8, Allocator.Temp)
            {
                new Vector3(-halfSize.x, -halfSize.y, -halfSize.z),
                new Vector3(halfSize.x, -halfSize.y, -halfSize.z),
                new Vector3(-halfSize.x, halfSize.y, -halfSize.z),
                new Vector3(halfSize.x, halfSize.y, -halfSize.z),
                new Vector3(-halfSize.x, -halfSize.y, halfSize.z),
                new Vector3(halfSize.x, -halfSize.y, halfSize.z),
                new Vector3(-halfSize.x, halfSize.y, halfSize.z),
                new Vector3(halfSize.x, halfSize.y, halfSize.z)
            };

            target.SetVertices(vertices.AsArray(), 0, vertices.Length);
            target.SetTriangles(paralelepipedTrianglesList, 0, paralelepipedTrianglesList.Count, 0);

            target.RecalculateNormals();
            target.RecalculateTangents();
        }

        public static void GenerateQuad(Mesh target)
        {
            target.vertices = quadVertices;
            target.triangles = quadTriangles;
            target.uv = quadUV;
            target.normals = quadNormals;
            target.tangents = quadTangents;
        }
    }
}
