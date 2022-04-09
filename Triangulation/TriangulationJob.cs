using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine.Assertions;

[BurstCompile]
public struct BowyerWatsonJob : IJob {
    public NativeList<int2> Points;
    public float Size;
    public TriangleStorage Storage;

    public ProfilerMarker InvalidTrianglesMarker,
        FindHoleBoundariesMarker,
        RetriangulateMarker,
        BadTrianglesClearMarker;

    public void Execute() => BowyerWatson(Size, Points);

    private void BowyerWatson(float size, NativeList<int2> points) {

        var pointCount = points.Length;
        
        // Add all points to triangulate
        for (int i = 0; i < pointCount; i++)
            Storage.AddVertex(i, points[i]);

        // Compute the super triangle vertices and add them at the end
        var superTriangleA = new float2(0.5f * size, -2.5f * size);
        Storage.AddVertex(pointCount, superTriangleA);
        var superTriangleB = new float2(-1.5f * size, 2.5f * size);
        Storage.AddVertex(pointCount + 1, superTriangleB);
        var superTriangleC = new float2(2.5f * size, 2.5f * size);
        Storage.AddVertex(pointCount + 2, superTriangleC);

        // Add the triangle itself
        Storage.AddTriangle((ushort) pointCount, (ushort) (pointCount + 1), (ushort) (pointCount + 2));

        // List of triangle indices that intersect with the newly added point.
        var badTriangles = new NativeList<ushort>(100, Allocator.Temp);
        // The list of edges forming the contour around the hole created when we delete the bad triangles
        // We'll recreate a new valid triangle with the point being added and each edge 
        var polygon = new NativeList<TriangleStorage.EdgeRef>(10, Allocator.Temp);

        // a list for newly created triangles. we'll patch each new triangle's neighbors 
        NativeList<ushort> newTriangles = new NativeList<ushort>(polygon.Length, Allocator.Temp);

        for (int ip = 0; ip < pointCount; ip++) {
            BadTrianglesClearMarker.Begin();
            badTriangles.Clear();
            BadTrianglesClearMarker.End();

            InvalidTrianglesMarker.Begin();
            var point = Storage.Points[ip];

            // first find all the triangles that are no longer valid due to the insertion
            // slowest part (~93% of the time is spent here)
            var trianglesLength = Storage.Triangles.Length;
            // flood fill
            for (ushort triangleIndex = (ushort) (trianglesLength - 1); triangleIndex >= 0; triangleIndex--) {
                var triangle = Storage.Triangles[triangleIndex];
                if (triangle.IsDeleted)
                    continue;
                // if point is inside circumcircle of triangle (cached in the triangle)
                if (math.distancesq(point, triangle.CircumCircleCenter.xz) <
                    triangle.CircumCircleRadiusSquared) {
                    // found first bad triangle
                    // recurse to find other bad triangles which will all be connected to this one
                    badTriangles.Add(triangleIndex);
                    FloodFillBadTriangles(in point, triangleIndex, ref badTriangles);
                    break;
                }
            }

            InvalidTrianglesMarker.End();

            FindHoleBoundariesMarker.Begin();
            polygon.Clear();
            // find the boundary of the polygonal hole
            for (int index = 0; index < badTriangles.Length; index++) {
                ushort badTriangle = badTriangles[index];
                AddNonSharedEdgeToPolygon(Storage, badTriangles, badTriangle, 0, ref polygon);
                AddNonSharedEdgeToPolygon(Storage, badTriangles, badTriangle, 1, ref polygon);
                AddNonSharedEdgeToPolygon(Storage, badTriangles, badTriangle, 2, ref polygon);
            }

            FindHoleBoundariesMarker.End();

            RetriangulateMarker.Begin();
            // remove them from the data structure
            for (int index = badTriangles.Length - 1; index >= 0; index--) {
                ushort i = badTriangles[index];
                Storage.RemoveTriangle(i);
            }

            Storage.SwapPool();

            newTriangles.Clear();
            // re-triangulate the polygonal hole
            for (int index = 0; index < polygon.Length; index++) {
                var edge = polygon[index];
                // potential optim: use deleted triangles as some kind of quadtree (tritree ?)
                // index tris by their circumcenter
                // that sets t1. t2,t3 set below
                ushort newTriangleIndex = Storage.AddTriangle(edge, (ushort) ip);
                newTriangles.Add(newTriangleIndex);
            }

            // set t2,t3 as t1 is always the polygon boundary
            for (int i = 0; i < newTriangles.Length; i++) {
                var t1 = Storage.Triangles[newTriangles[i]];
                for (int j = 0; j < newTriangles.Length; j++) {
                    if (i == j)
                        continue;
                    var t2 = Storage.Triangles[newTriangles[j]];
                    if (t1.Edge1.B == t2.Edge1.A) {
                        t1.T2 = newTriangles[j];
                        t2.T3 = newTriangles[i];
                        Storage.Triangles[newTriangles[i]] = t1;
                        Storage.Triangles[newTriangles[j]] = t2;
                    }
                }
            }

            RetriangulateMarker.End();
        }

        // cleanup
        for (ushort index = 0; index < Storage.Triangles.Length; index++) {
            var triangle = Storage.Triangles[index];
            // if triangle contains a vertex from original super-triangle
            if (triangle.IsDeleted || (!triangle.ContainsVertex(pointCount) && !triangle.ContainsVertex(pointCount + 1) && !triangle.ContainsVertex(pointCount + 2))) continue;
            ref var t = ref Storage.RemoveTriangle(index);
            /*if (t.T1 != -1)
                Storage.SetNeighbour(ref Storage.Triangles.ElementAt(t.T1), t.Edge1.A, t.Edge1.B, 0);
            if (t.T2 != -1)
                Storage.SetNeighbour(ref Storage.Triangles.ElementAt(t.T2), t.Edge2.A, t.Edge2.B, 0);
            if (t.T3 != -1)
                Storage.SetNeighbour(ref Storage.Triangles.ElementAt(t.T3), t.Edge3.A, t.Edge3.B, 0);*/
            Storage.Triangles[index] = t;
        }
    }

    private static void AddNonSharedEdgeToPolygon(TriangleStorage storage, NativeList<ushort> badTriangleIndices,
        ushort badTriangleIndex, int edgeIndex,
        ref NativeList<TriangleStorage.EdgeRef> polygon) {
        var badTriangle = storage.Triangles[badTriangleIndex];
        var edge = edgeIndex == 0 ? badTriangle.Edge1 : edgeIndex == 1 ? badTriangle.Edge2 : badTriangle.Edge3;
        // if edge is not shared by any other triangles in badTriangles
        bool any = false;
        for (var index = 0; index < badTriangleIndices.Length; index++) {
            var t = badTriangleIndices[index];
            if (t != badTriangleIndex) {
                var otherBadTriangle = storage.Triangles[t];
                if (otherBadTriangle.Edge1 == edge || otherBadTriangle.Edge2 == edge ||
                    otherBadTriangle.Edge3 == edge) {
                    any = true;
                    break;
                }
            }
        }

        if (!any)
            polygon.Add(new TriangleStorage.EdgeRef(badTriangleIndex, edgeIndex));
    }

    private void FloodFillBadTriangles(in float2 v, int triangleIndex, ref NativeList<ushort> badTriangles) {
        var triangle = Storage.Triangles[triangleIndex];
        CheckNeighbour(in v, triangle.T1, ref badTriangles);
        CheckNeighbour(in v, triangle.T2, ref badTriangles);
        CheckNeighbour(in v, triangle.T3, ref badTriangles);
    }

    private void CheckNeighbour(in float2 v, ushort triangleIndex, ref NativeList<ushort> badTriangles) {
        var n1 = Storage.Triangles[triangleIndex];
        var badTrianglesLength = badTriangles.Length;
        for (int i = 0; i < badTrianglesLength; i++) {
            if (badTriangles[i] == triangleIndex) // already added
                return;
        }

        if (math.distancesq(v, n1.CircumCircleCenter.xz) < n1.CircumCircleRadiusSquared) {
            badTriangles.Add(triangleIndex);
            FloodFillBadTriangles(v, triangleIndex, ref badTriangles);
        }
    }
}