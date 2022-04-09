using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;

// ReSharper disable PossiblyImpureMethodCallOnReadonlyVariable

[BurstCompile]
public static class SimpleBowyerWatson {
    public static NativeList<Triangle> Delaunay(ref NativeList<int2> points, in int size, 
        ref ProfilerMarker invalidSearchMarker, ref ProfilerMarker edgeComputeMarker, ref ProfilerMarker holeTriangulationMarker) {

        var triangles = new NativeList<Triangle>(points.Length * 2, Allocator.Temp);
        var firstPoint = points[0];
        
        var superTriangleA = new int2(size / 2, -3 * size) + firstPoint;
        var superTriangleB = new int2(-3 * size,3 * size) + firstPoint;
        var superTriangleC = new int2(3 * size, 3 * size) + firstPoint;
        var superTriIndexStart = points.Length;
        points.Add(superTriangleA);
        points.Add(superTriangleB);
        points.Add(superTriangleC);
        var superTri = insertTriangle(ref triangles, new int3(superTriIndexStart, superTriIndexStart + 1, superTriIndexStart + 2), in points);
        triangles.Add(superTri);

        for (var pointIndex = 0; pointIndex < points.Length; pointIndex++) {
            var point = points[pointIndex];
            var badTris = new NativeList<int>(2, Allocator.Temp);

            invalidSearchMarker.Begin();
            // find bad triangles
            for (var triIndex = triangles.Length - 1; triIndex >= 0; triIndex--) {
                var triangle = triangles[triIndex];

                var distSq = math.distancesq(triangle.Center, point);
                var isInside = distSq < triangle.RadiusSq;

                if (isInside) badTris.Add(triIndex);
            }
            
            invalidSearchMarker.End();

            // int2 values are vertex positions (represents edges)
            
            var polygon = new NativeHashSet<int2>(6, Allocator.Temp);

            edgeComputeMarker.Begin();
            // compute valid polygon
            for (var badTriIterator = 0; badTriIterator < badTris.Length; badTriIterator++) {
                var badTriIndex = badTris[badTriIterator];
                var badTri = triangles[badTriIndex];
                if (!isEdgeShared(badTri.EdgeA, badTriIndex, in badTris, in triangles)) polygon.Add(badTri.EdgeA);
                if (!isEdgeShared(badTri.EdgeB, badTriIndex, in badTris, in triangles)) polygon.Add(badTri.EdgeB);
                if (!isEdgeShared(badTri.EdgeC, badTriIndex, in badTris, in triangles)) polygon.Add(badTri.EdgeC);
            }
            edgeComputeMarker.End();

            badTris.Sort();
            // remove tris that overlap with the new point
            for (int i = badTris.Length - 1; i >= 0; i--) {
                var badTriIndex = badTris[i];
                triangles.RemoveAtSwapBack(badTriIndex);
            }
            
            holeTriangulationMarker.Begin();
            
            // triangulate new hole
            using var polyIterator = polygon.ToNativeArray(Allocator.Temp).GetEnumerator();
            while (polyIterator.MoveNext()) {
                var edge = polyIterator.Current;
                var newTriIndices = new int3(edge.x, edge.y, pointIndex);
                var tri = insertTriangle(ref triangles, newTriIndices, in points);
                if (!float.IsNaN(tri.RadiusSq)) triangles.Add(tri);
            }
            
            holeTriangulationMarker.End();
            
        }
        
        // clean up
        // remove triangles from super triangle
        var toRemove = new NativeList<int>(3, Allocator.Temp);
        for (var i = 0; i < triangles.Length; i++) {
            var triangle = triangles[i];
            if (math.any(triangle.Indices == superTriIndexStart) || math.any(triangle.Indices == superTriIndexStart + 1) || math.any(triangle.Indices == superTriIndexStart + 2)) {
                toRemove.Add(i);
            }
        }

        toRemove.Sort();
        for (int i = toRemove.Length - 1; i >= 0; i--) {
            var removeIndex = toRemove[i];
            triangles.RemoveAtSwapBack(removeIndex);
        }
        
        // remove vertices of super tri
        // remove 3 times the same index, since the next item in the list is being moved "back"
        points.RemoveRangeSwapBack(superTriIndexStart, 3);

        return triangles;
    }

    private static bool isEdgeShared(in int2 edge, in int selfIndex, in NativeList<int> badTris, in NativeList<Triangle> triangles) {
        for (var i = 0; i < badTris.Length; i++) {
            var curBadTriIndex = badTris[i];
            if (curBadTriIndex == selfIndex) continue;
            var badTri = triangles[curBadTriIndex];

            if (triangleContainsEdge(badTri, edge)) return true;

        }

        return false;

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool triangleContainsEdge(in Triangle triangle, in int2 edge) {
        return isCommonEdge(edge, triangle.EdgeA) || isCommonEdge(edge, triangle.EdgeB) || isCommonEdge(edge, triangle.EdgeC);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool isCommonEdge(in int2 edgeA, in int2 edgeB) {
        return math.all(edgeA == edgeB) || edgeA.x == edgeB.y && edgeA.y == edgeB.x;
    }

    private static Triangle insertTriangle(ref NativeList<Triangle> list, in int3 indices, in NativeList<int2> points) {

        var posA = points[indices.x];
        var posB = points[indices.y];
        var posC = points[indices.z];
        var v1 = new float3(posA.x, 0, posA.y);
        var v2 = new float3(posB.x, 0, posB.y);
        var v3 = new float3(posC.x, 0, posC.y);
        
        Geometry.CircumCircle(v1, v2, v3, out var center, out var radius);
        var tri = new Triangle {Indices = indices, RadiusSq = radius * radius, Center = center.xz};
        return tri;
    }

    public struct Triangle {
        public int3 Indices;
        internal float RadiusSq;   //squared
        internal float2 Center;

        internal int2 EdgeA => new int2(Indices.xy);
        internal int2 EdgeB => new int2(Indices.yz);
        internal int2 EdgeC => new int2(Indices.zx);
    }
}