using System;
using System.Runtime.CompilerServices;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using Assert = UnityEngine.Assertions.Assert;

// ReSharper disable PossiblyImpureMethodCallOnReadonlyVariable

[BurstCompile]
public static class SimpleBowyerWatson {
    public static NativeList<Triangle> Delaunay(ref NativeList<int2> points, in int size,
        ref ProfilerMarker invalidSearchMarker, ref ProfilerMarker edgeComputeMarker, ref ProfilerMarker holeTriangulationMarker, ref ProfilerMarker floodFillMarker,
        ref ProfilerMarker connectivityMarker) {
        var triangles = new NativeList<Triangle>(points.Length * 2, Allocator.Temp);
        var connectivity = new NativeHashMap<int2, TrianglePair>(points.Length, Allocator.Temp);
        var firstPoint = points[0];

        var superTriangleA = new int2(size / 2, -3 * size) + firstPoint;
        var superTriangleB = new int2(-3 * size, 3 * size) + firstPoint;
        var superTriangleC = new int2(3 * size, 3 * size) + firstPoint;
        var superTriIndexStart = points.Length;
        points.Add(superTriangleA);
        points.Add(superTriangleB);
        points.Add(superTriangleC);
        var superTri = getTriangle(new int3(superTriIndexStart, superTriIndexStart + 1, superTriIndexStart + 2), in points);
        triangles.Add(superTri);

        for (var pointIndex = 0; pointIndex < points.Length; pointIndex++) {
            var point = points[pointIndex];
            var badTris = new NativeList<int>(2, Allocator.Temp);

            invalidSearchMarker.Begin();
            // find bad triangles
            for (var triIndex = triangles.Length - 1; triIndex >= 0; triIndex--) {
                //for (var triIndex = 0; triIndex < triangles.Length; triIndex++) {
                var triangle = triangles[triIndex];
                if (triangle.deleted) continue;

                var distSq = math.distancesq(triangle.Center, point);
                var isInside = distSq < triangle.RadiusSq;

                if (!isInside) continue;

                badTris.Add(triIndex);

                floodFillMarker.Begin();
                floodFill(in connectivity, in triangles, in triIndex, point, ref badTris);
                floodFillMarker.End();
                break;
            }

            invalidSearchMarker.End();

            // int2 values are vertex positions (represents edges)

            var polygon = new NativeHashSet<int2>(6, Allocator.Temp);

            edgeComputeMarker.Begin();
            // compute valid polygon
            for (var badTriIterator = 0; badTriIterator < badTris.Length; badTriIterator++) {
                var badTriIndex = badTris[badTriIterator];
                var badTri = triangles[badTriIndex];
                if (!isEdgeShared(badTri.EdgeA, badTriIndex, in badTris, in connectivity)) polygon.Add(badTri.EdgeA);
                if (!isEdgeShared(badTri.EdgeB, badTriIndex, in badTris, in connectivity)) polygon.Add(badTri.EdgeB);
                if (!isEdgeShared(badTri.EdgeC, badTriIndex, in badTris, in connectivity)) polygon.Add(badTri.EdgeC);
            }

            edgeComputeMarker.End();

            // remove tris that overlap with the new point
            for (int i = badTris.Length - 1; i >= 0; i--) {
                var badTriIndex = badTris[i];
                var badTri = triangles[badTriIndex];

                // remove tri from connectivity graph
                connectivityMarker.Begin();
                removeFromConnectivity(ref connectivity, badTriIndex, badTri.EdgeA);
                removeFromConnectivity(ref connectivity, badTriIndex, badTri.EdgeB);
                removeFromConnectivity(ref connectivity, badTriIndex, badTri.EdgeC);
                connectivityMarker.End();

                //triangles.RemoveAtSwapBack(badTriIndex);
                badTri.deleted = true;
                triangles[badTriIndex] = badTri;
            }

            holeTriangulationMarker.Begin();

            // triangulate new hole
            using var polyIterator = polygon.ToNativeArray(Allocator.Temp).GetEnumerator();
            while (polyIterator.MoveNext()) {
                var edge = polyIterator.Current;
                var newTriIndices = new int3(edge.x, edge.y, pointIndex);
                var tri = getTriangle(newTriIndices, in points);

                if (float.IsNaN(tri.RadiusSq)) continue;

                triangles.Add(tri);
                var triIndex = triangles.Length - 1;

                // add to connectivity
                connectivityMarker.Begin();
                addToConnectivity(ref connectivity, in triIndex, tri.EdgeA);
                addToConnectivity(ref connectivity, in triIndex, tri.EdgeB);
                addToConnectivity(ref connectivity, in triIndex, tri.EdgeC);
                connectivityMarker.End();
            }

            holeTriangulationMarker.End();
        }

        // clean up
        // remove triangles connected to super triangle
        var toRemove = new NativeList<int>(3, Allocator.Temp);
        for (var i = 0; i < triangles.Length; i++) {
            var triangle = triangles[i];
            if (triangle.deleted || math.any(triangle.Indices == superTriIndexStart) || math.any(triangle.Indices == superTriIndexStart + 1) || math.any(triangle.Indices == superTriIndexStart + 2)) {
                toRemove.Add(i);
            }
        }

        //toRemove.Sort();
        for (int i = toRemove.Length - 1; i >= 0; i--) {
            var removeIndex = toRemove[i];
            triangles.RemoveAtSwapBack(removeIndex);
        }

        // remove vertices of super tri
        // remove 3 times the same index, since the next item in the list is being moved "back"
        points.RemoveRangeSwapBack(superTriIndexStart, 3);

        return triangles;
    }

    private static bool isEdgeShared(in int2 edge, in int selfIndex, in NativeList<int> badTris, in NativeHashMap<int2, TrianglePair> pairs) {
        var sortedIndices = edge;
        if (edge.x > edge.y) sortedIndices = edge.yx;
        if (!pairs.TryGetValue(sortedIndices, out var pair)) return false;

        var other = pair.triA == selfIndex ? pair.triB : pair.triA;
        for (var i = 0; i < badTris.Length; i++) {
            var badTri = badTris[i];
            if (badTri == other) return true;
        }

        return false;
    }

    private static Triangle getTriangle(in int3 indices, in NativeList<int2> points) {
        var posA = points[indices.x];
        var posB = points[indices.y];
        var posC = points[indices.z];
        var v1 = new float3(posA.x, 0, posA.y);
        var v2 = new float3(posB.x, 0, posB.y);
        var v3 = new float3(posC.x, 0, posC.y);

        Geometry.CircumCircle(v1, v2, v3, out var center, out var radius);
        var tri = new Triangle(indices, radius * radius, center.xz);
        return tri;
    }

    private static void addToConnectivity(ref NativeHashMap<int2, TrianglePair> pairs, in int index, in int2 edge) {
        if (index == 0) return;
        var sortedIndices = edge;
        if (edge.x > edge.y) sortedIndices = edge.yx;

        if (pairs.TryGetValue(sortedIndices, out var existingPair)) {
            if (existingPair.triA == 0) {
                existingPair.triA = index;
            }
            else {
                existingPair.triB = index;
            }

            pairs[sortedIndices] = existingPair;
        }
        else {
            var newPair = new TrianglePair {triA = index};
            pairs.Add(sortedIndices, newPair);
        }
    }

    private static void removeFromConnectivity(ref NativeHashMap<int2, TrianglePair> pairs, in int index, in int2 edge) {
        if (index == 0) return;
        var sortedIndices = edge;
        if (edge.x > edge.y) sortedIndices = edge.yx;


        var pair = pairs[sortedIndices];
        if (pair.triA == index) {
            pair.triA = 0;
        }
        else {
            pair.triB = 0;
        }

        pairs[sortedIndices] = pair;
    }

    private static int getNeighbor(in NativeHashMap<int2, TrianglePair> pairs, in int2 edge, in int source) {
        var sortedIndices = edge;
        if (edge.x > edge.y) sortedIndices = edge.yx;
        if (!pairs.TryGetValue(sortedIndices, out var pair)) {
            return 0;
        }

        return pair.triA == source ? pair.triB : pair.triA;
    }

    private static void floodFill(in NativeHashMap<int2, TrianglePair> pairs, in NativeList<Triangle> triangles, in int triIndex, in float2 point, ref NativeList<int> badTris) {
        var triangle = triangles[triIndex];
        var neighborA = getNeighbor(in pairs, triangle.EdgeA, triIndex);
        var neighborB = getNeighbor(in pairs, triangle.EdgeB, triIndex);
        var neighborC = getNeighbor(in pairs, triangle.EdgeC, triIndex);
        CheckNeighbor(in pairs, in triangles, neighborA, in point, ref badTris);
        CheckNeighbor(in pairs, in triangles, neighborB, in point, ref badTris);
        CheckNeighbor(in pairs, in triangles, neighborC, in point, ref badTris);
    }

    private static void CheckNeighbor(in NativeHashMap<int2, TrianglePair> pairs, in NativeList<Triangle> triangles, in int triIndex, in float2 point, ref NativeList<int> badTris) {
        if (triIndex == 0) return;

        for (var i = 0; i < badTris.Length; i++) {
            var badTri = badTris[i];
            if (triIndex == badTri) return; // already added
        }

        var triangle = triangles[triIndex];

        if (math.distancesq(point, triangle.Center) < triangle.RadiusSq) {
            badTris.Add(triIndex);
            floodFill(in pairs, in triangles, in triIndex, in point, ref badTris);
        }
    }

    public struct TrianglePair {
        public int triA;
        public int triB;
    }

    public struct Triangle {
        public readonly int3 Indices;
        public bool deleted;
        internal readonly float RadiusSq; //squared
        internal readonly float2 Center;

        internal int2 EdgeA => new int2(Indices.xy);
        internal int2 EdgeB => new int2(Indices.yz);
        internal int2 EdgeC => new int2(Indices.zx);

        public Triangle(int3 indices, float radiusSq, float2 center) {
            Indices = indices;
            RadiusSq = radiusSq;
            Center = center;
            deleted = false;
        }
    }
}