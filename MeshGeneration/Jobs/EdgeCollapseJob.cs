using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Assertions;
using Random = Unity.Mathematics.Random;

// ReSharper disable GenericEnumeratorNotDisposed

// ReSharper disable PossiblyImpureMethodCallOnReadonlyVariable

[BurstCompile]
public struct EdgeCollapseJob : IJob {

    public NativeList<int3> triangles;
    
    [ReadOnly] public NativeArray<NormalPassJob.VertexData> vertices;
    [ReadOnly] public GridSettings settings;

    private Random random;

    public ProfilerMarker EdgeStoreMarker, SearchTrianglesAtPointMarker, GatherPointNeighbors, GenerateHolePolygon, DelaunayMarker, InvalidSeachMarker, EdgeComputeMarker;

    public void Execute() {

        // bi-directional
        var edgeCount = triangles.Length * 3 * 3;
        var edges = new NativeMultiHashMap<int, int>(edgeCount, Allocator.Temp);
        
        EdgeStoreMarker.Begin();
        // initialize edges (maybe put this into a separate parallel job later?
        for (var index = 0; index < triangles.Length; index++) {
            var triangle = triangles[index];
            StoreEdgesFromTri(ref edges, triangle);
        }
        EdgeStoreMarker.End();

        for (var i = 0; i < vertices.Length; i++) {
            var vertex = vertices[i];
            var point = LinearArrayHelper.ReverseLinearIndex(i, settings.Count);

            //if (!vertex.Skipped) continue;
            // collapse candidate

            var removedTris = new NativeList<int>(5, Allocator.Temp);
                
            // get all triangles that have this vertex
            // delete triangles, keep list of their vertices (set)
            // get vertex closest to collapsed vertex from set, triangle fan from there
            
            GatherPointNeighbors.Begin();

            SearchTrianglesAtPointMarker.Begin();
            var affectedTris = getTrianglesForPoint(point);
            SearchTrianglesAtPointMarker.End();
            var neighboringVertices = new NativeHashSet<int>(30, Allocator.Temp);
                
            for (var index = 0; index < affectedTris.Length; index++) {
                var tri = affectedTris[index];
                var triData = triangles[tri];
                neighboringVertices.Add(triData.x);
                neighboringVertices.Add(triData.y);
                neighboringVertices.Add(triData.z);
                removedTris.Add(tri);
            }

            // removedTris.Sort();
            for (int j = removedTris.Length - 1; j >= 0; j--) {
                var toRemove = removedTris[j];
                triangles.RemoveAt(toRemove);
            }

            neighboringVertices.Remove(i);
            
            GatherPointNeighbors.End();

            GenerateHolePolygon.Begin();
            // start with the first vertex, insert triangle fan (TODO: pick closest vertex)
            var poly = new NativeList<int2>(neighboringVertices.Count(), Allocator.Temp);
            var array = neighboringVertices.ToNativeArray(Allocator.Temp);
            using var enumerator = array.GetEnumerator();
            while (enumerator.MoveNext()) {
                poly.Add(LinearArrayHelper.ReverseLinearIndex(enumerator.Current, settings.Count));
            }

            var size = getPolygonSize(in poly);
            
            GenerateHolePolygon.End();
            
            DelaunayMarker.Begin();
            var holeTris = SimpleBowyerWatson.Delaunay(ref poly, (int) math.ceil(size), ref InvalidSeachMarker, ref EdgeComputeMarker, ref GenerateHolePolygon);
            
            DelaunayMarker.End();

            for (var index = 0; index < holeTris.Length; index++) {
                var holeTri = holeTris[index];
                // get position of each vertex from the triangle, convert to global correct index
                var indicesA = LinearArrayHelper.GetLinearIndex(poly[holeTri.Indices.x], settings.Count);
                var indicesB = LinearArrayHelper.GetLinearIndex(poly[holeTri.Indices.y], settings.Count);
                var indicesC = LinearArrayHelper.GetLinearIndex(poly[holeTri.Indices.z], settings.Count);
                
                triangles.Add(new int3(indicesA, indicesB, indicesC));
            }

            // at end: remove unused vertices, re-create triangle references for new values (mapping set?)
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreEdgesFromTri(ref NativeMultiHashMap<int, int> edges, int3 triangle) {
        edges.Add(triangle.x, triangle.y);
        edges.Add(triangle.y, triangle.x);
        edges.Add(triangle.y, triangle.z);
        edges.Add(triangle.z, triangle.y);
        edges.Add(triangle.z, triangle.x);
        edges.Add(triangle.x, triangle.z);
    }

    public static float getPolygonSize(in NativeList<int2> polygon) {
        var max = int2.zero;
        var min = new int2(int.MaxValue);

        for (var i = 0; i < polygon.Length; i++) {
            var point = polygon[i];
            if (point.x < min.x) {
                min.x = point.x;
            }
            else if (point.x > max.x) {
                max.x = point.x;
            }

            if (point.y < min.y) {
                min.y = point.y;
            }
            else if (point.y > max.y) {
                max.y = point.y;
            }
        }

        return math.distance(min, max);

    }

    private NativeList<int> getPolygonInConnectedOrder(ref NativeList<int> source, in NativeMultiHashMap<int, int> edges) {

        var result = new NativeList<int>(source.Length, Allocator.Temp);

        var start = source[random.NextInt(0, source.Length - 1)];
        result.Add(start);

        var last = start;
        while (source.Length > 1) {
            var currentConnections = edges.GetValuesForKey(last);
            source.RemoveAt(source.IndexOf(last));
            var next = GetNextConnectedVertex(ref source, last, ref currentConnections);
            if (next < 0)
                Assert.IsTrue(next >= 0);
            
            result.Add(next);
            
            last = next;
        }

        return result;

    }

    private static int GetNextConnectedVertex(ref NativeList<int> source, int start, ref NativeMultiHashMap<int, int>.Enumerator startConnections) {
        var nextInConnection = -1;
        for (var i = 0; i < source.Length; i++) {
            var point = source[i];
            // check if one of the other points has a connected edge
            if (point == start) continue;
            startConnections.Reset();

            while (startConnections.MoveNext()) {
                var candidate = startConnections.Current;
                if (candidate == point) nextInConnection = candidate;
            }
        }

        return nextInConnection;
    }

    private NativeList<int> getTrianglesForPoint(in int2 point) {
        var result = new NativeList<int>(2, Allocator.Temp);

        for (var i = 0; i < triangles.Length; i++) {
            var triangle = triangles[i];
            var pointIndex = LinearArrayHelper.GetLinearIndex(point, settings.Count);
            if (math.any(triangle == pointIndex)) {
                result.Add(i);
            }
        }

        return result;
    }
}