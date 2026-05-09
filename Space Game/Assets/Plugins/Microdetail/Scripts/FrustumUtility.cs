using System.Collections.Generic;
using UnityEngine;

namespace Microdetail
{
    public static class FrustumUtility
    {
        private static List<(int First, int Second)> IntersectionPairs = new List<(int First, int Second)>()
            {
                (2, 3),
                (3, 4),
                (4, 5),
                (5, 2)
            };

        private static Vector3[] points = new Vector3[8];
        private static Vector2[] projectedPoints = new Vector2[8];
        private static Vector2[] convexHull = new Vector2[8];

        public static Bounds ComputeFrustumPropertiesFromPlanes(FrustumPlane[] planes)
        {
            var min = Vector3.one * float.MaxValue;
            var max = Vector3.one * float.MinValue;

            var pointIndex = 0;
            for (var mainPlaneIndex = 0; mainPlaneIndex < 2; mainPlaneIndex++)
            {
                foreach (var pair in IntersectionPairs)
                {
                    if (!TryIntersectPlanes(planes[mainPlaneIndex], planes[pair.First], planes[pair.Second], out var intersection)) 
                        continue;

                    points[pointIndex++] = intersection;
                    min = Vector3.Min(min, intersection);
                    max = Vector3.Max(max, intersection);
                }
            }

            return new Bounds((min + max) / 2.0f, max - min);
        }
        
        private static bool TryIntersectPlanes(FrustumPlane a, FrustumPlane b, FrustumPlane c, out Vector3 intersection)
        {
            var normalA = a.Normal;
            var normalB = b.Normal;
            var normalC = c.Normal;

            var determinant = Vector3.Dot(normalA, Vector3.Cross(normalB, normalC));
            if (Mathf.Abs(determinant) < 1e-6f)
            {
                intersection = Vector3.zero;
                return false;
            }

            var positionA = a.Position;
            var positionB = b.Position;
            var positionC = c.Position;

            var rhs = (-Vector3.Dot(normalA, positionA) * Vector3.Cross(normalB, normalC)) +
                      (-Vector3.Dot(normalB, positionB) * Vector3.Cross(normalC, normalA)) +
                      (-Vector3.Dot(normalC, positionC) * Vector3.Cross(normalA, normalB));

            intersection = -rhs / determinant;
            return true;
        }
        
        public static float CalculateProjectedArea(Vector3[] frustumPoints)
        {
            if (frustumPoints.Length != 8)
            {
                Debug.LogError("Frustum must have exactly 8 points.");
                return 0f;
            }

            for (var i = 0; i < 8; i++)
                projectedPoints[i] = new Vector2(frustumPoints[i].x, frustumPoints[i].z);

            var hullSize = ComputeConvexHull(projectedPoints, convexHull);
            return CalculatePolygonArea(convexHull, hullSize);
        }

        private static int ComputeConvexHull(Vector2[] points, Vector2[] hull)
        {
            var n = points.Length;
            if (n < 3) return 0;

            System.Array.Sort(points, (a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var hullSize = 0;

            for (var i = 0; i < n; i++)
            {
                while (hullSize >= 2 && Cross(hull[hullSize - 2], hull[hullSize - 1], points[i]) <= 0)
                    hullSize--;
                hull[hullSize++] = points[i];
            }

            var lowerHullCount = hullSize + 1;
            for (var i = n - 2; i >= 0; i--)
            {
                var point = points[i];
                while (hullSize >= lowerHullCount && Cross(hull[hullSize - 2], hull[hullSize - 1], point) <= 0)
                    hullSize--;
                hull[hullSize++] = point;
            }

            return hullSize - 1;
        }

        private static float Cross(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p2.x - p1.x) * (p3.y - p1.y) - (p2.y - p1.y) * (p3.x - p1.x);
        }

        private static float CalculatePolygonArea(Vector2[] points, int count)
        {
            var area = 0f;
            for (var i = 0; i < count; i++)
            {
                var p1 = points[i];
                var p2 = points[(i + 1) % count];
                area += (p1.x * p2.y - p1.y * p2.x);
            }
            return Mathf.Abs(area) * 0.5f;
        }
    }
}