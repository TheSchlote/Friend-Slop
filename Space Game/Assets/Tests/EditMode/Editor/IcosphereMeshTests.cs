using FriendSlop.Round;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    // The terrain generator displaces icosphere vertices, so the mesh utility's
    // counts and unit-radius invariant are load-bearing for procgen output. If
    // either of these breaks, every procedural planet renders wrong.
    public class IcosphereMeshTests
    {
        [TestCase(0, 12, 20)]
        [TestCase(1, 42, 80)]
        [TestCase(2, 162, 320)]
        [TestCase(3, 642, 1280)]
        [TestCase(4, 2562, 5120)]
        [TestCase(5, 10242, 20480)]
        public void Build_ReturnsExpectedVertexAndTriangleCounts(int subdivisions, int expectedVerts, int expectedTris)
        {
            var geom = IcosphereMesh.Build(subdivisions);
            Assert.AreEqual(expectedVerts, geom.Vertices.Length, $"Vertex count for subdivision {subdivisions}");
            Assert.AreEqual(expectedTris * 3, geom.Triangles.Length, $"Index count for subdivision {subdivisions}");
            Assert.AreEqual(expectedVerts, IcosphereMesh.VertexCount(subdivisions));
            Assert.AreEqual(expectedTris, IcosphereMesh.TriangleCount(subdivisions));
        }

        [Test]
        public void Build_AllVerticesAreUnitLengthWithinTolerance()
        {
            var geom = IcosphereMesh.Build(4);
            for (var i = 0; i < geom.Vertices.Length; i++)
            {
                var len = geom.Vertices[i].magnitude;
                Assert.That(len, Is.EqualTo(1f).Within(1e-4f),
                    $"Vertex {i} should sit on the unit sphere; was {len}");
            }
        }

        [Test]
        public void Build_TriangleIndicesAreInRange()
        {
            var geom = IcosphereMesh.Build(3);
            for (var i = 0; i < geom.Triangles.Length; i++)
            {
                var idx = geom.Triangles[i];
                Assert.GreaterOrEqual(idx, 0);
                Assert.Less(idx, geom.Vertices.Length);
            }
        }
    }
}
