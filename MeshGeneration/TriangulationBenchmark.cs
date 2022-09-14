using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = Unity.Mathematics.Random;

[ExecuteInEditMode]
public class TriangulationBenchmark : MonoBehaviour {

    public int gridSize;
    public float skipChance;
    
    public bool drawPoints;
    public bool drawTriangles;
    public int iterationCount;
    
    public bool reload;

    private void OnValidate() {
        if (reload) {
            reload = false;
            print("Benchmarking");
            DoTriangulation();
        }
    }

    private ProfilerMarker invalidSearchMarker = new ProfilerMarker("invalidSearchMarker");
    private ProfilerMarker edgeComputeMarker = new ProfilerMarker("edgeComputeMarker");
    private ProfilerMarker holeTriangulationMarker = new ProfilerMarker("holeTriangulationMarker");
    private ProfilerMarker floodFillMarker = new ProfilerMarker("floodFillMarker");
    private ProfilerMarker connectivityMarker = new ProfilerMarker("connectivityMarker");

    private void DoTriangulation() {
        
        var random = Random.CreateFromIndex((uint) (gridSize * skipChance));

        var points = new UnsafeList<int2>(gridSize * 2, Allocator.TempJob);
        
        // get debug points
        for (int x = 0; x <= gridSize; x++) {
            for (int y = 0; y <= gridSize; y++) {
                var point = new int2(x, y);
                var isCorner = x % gridSize == 0 && y % gridSize == 0;
                var skipped = random.NextFloat() > skipChance && !isCorner;

                if (skipped) continue;
                points.Add(point);
                if (drawPoints) {
                    Debug.DrawRay(new Vector3(point.x, 0 , point.y), Vector3.up, Color.green, 5f);
                }
            }
        }

        var timer = new Stopwatch();
        timer.Start();

        var targetTriangulation = new NativeList<SimpleBowyerWatson.Triangle>();
        
        for (int i = 0; i < iterationCount; i++) {
            var triangulation = SimpleBowyerWatson.Delaunay(ref points, gridSize, ref invalidSearchMarker, ref edgeComputeMarker, ref holeTriangulationMarker, ref floodFillMarker, ref connectivityMarker);
            if (!targetTriangulation.IsCreated) {
                targetTriangulation = triangulation;
            }
            else {
                triangulation.Dispose();
            }
        }
        
        timer.Stop();
        var avgRuntime = timer.ElapsedMilliseconds / (float) iterationCount;
        Debug.Log("Took " + timer.ElapsedMilliseconds + " ms for " + iterationCount + " iterations, avg time: " + avgRuntime + " ms");
        
        if (drawTriangles) DrawTriangles(ref points, ref targetTriangulation);

        points.Dispose();
        targetTriangulation.Dispose();

    }

    private void DrawTriangles(ref UnsafeList<int2> points, ref NativeList<SimpleBowyerWatson.Triangle> triangles) {

        foreach (var triangle in triangles) {
            
            if (triangle.deleted) continue;

            var posA = new Vector3(points[triangle.Indices.x].x, 0 , points[triangle.Indices.x].y);
            var posB = new Vector3(points[triangle.Indices.y].x, 0 , points[triangle.Indices.y].y);
            var posC = new Vector3(points[triangle.Indices.z].x, 0 , points[triangle.Indices.z].y);
            
            Debug.DrawLine(posA, posB, Color.cyan, 5f);
            Debug.DrawLine(posB, posC, Color.cyan, 5f);
            Debug.DrawLine(posC, posA, Color.cyan, 5f);
        }
        
    }
}