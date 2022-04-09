using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

// ReSharper disable PossibleLossOfFraction
// ReSharper disable UseMethodAny.2

[BurstCompile]
public struct TriangleHoleSearchJob : IJobParallelFor {
    public static readonly int THREAD_COUNT = 16;

    [ReadOnly] public GridSettings settings;
    [ReadOnly] public NativeHashSet<int2> skippedPoints;

    [WriteOnly] public NativeMultiHashMap<int, int2>.ParallelWriter foundHoles; // index = 'hole' id

    public ProfilerMarker FloodFill, FindStartIndex, AllocateSets, StoreMarker;

    public void Execute(int index) {
        // each thread (index) is responsible for one part of the grid. Holes are assigned based on their most top-left (first row, first index) starting point which thread they belong to
        
        // number of points for each thread to check
        var searchSize = (int) math.sqrt(settings.Count * settings.Count / THREAD_COUNT);

        // which cell this thread takes care of
        var cellPos = LinearArrayHelper.ReverseLinearIndex(index, 4);

        // check all points in own cell
        for (int x = searchSize * cellPos.x; x < searchSize * cellPos.x + searchSize; x++) {
            for (int y = searchSize * cellPos.y; y < searchSize * cellPos.y + searchSize; y++) {
                var pos = new int2(x, y);
                
                
                if (skippedPoints.Contains(pos)) {
                    AllocateSets.Begin();
                    var hole = new NativeHashSet<int2>(searchSize * searchSize * 2, Allocator.Temp);
                    var edges = new NativeHashSet<int2>(searchSize * searchSize, Allocator.Temp);
                    AllocateSets.End();

                    FloodFill.Begin();
                    var interrupt = false;
                    FloodFillPoint(ref edges, ref hole, ref skippedPoints, pos, pos, ref interrupt);
                    FloodFill.End();

                    FindStartIndex.Begin();
                    var holeStart = HoleStartIndex(ref hole);
                    FindStartIndex.End();
                    if (hole.Count() == 0 || math.any(holeStart != pos)) {
                        continue;
                    }

                    StoreMarker.Begin();
                    var holeIndex = x * settings.Count + y;
                    using var enumerator = edges.GetEnumerator();
                    while (enumerator.MoveNext()) {
                        foundHoles.Add(holeIndex, enumerator.Current);
                    }
                    
                    StoreMarker.End();
                }
            }
        }
    }

    // return first index in first row of the hole
    private static int2 HoleStartIndex(ref NativeHashSet<int2> hole) {
        var min = new int2(int.MaxValue, int.MaxValue);
        using var enumerator = hole.GetEnumerator();
        while (enumerator.MoveNext()) {
            var val = enumerator.Current;
            if (val.y < min.y || val.y == min.y && val.x < min.x) {
                min = val;
            }
        }

        return min;
    }

    private static void FloodFillPoint(ref NativeHashSet<int2> edges, ref NativeHashSet<int2> hole, ref NativeHashSet<int2> skippedPoints, in int2 pos,
        in int2 startPosition, ref bool interrupt) {
        if (interrupt || hole.Contains(pos) || edges.Contains(pos)) return;

        if (skippedPoints.Contains(pos)) {
            hole.Add(pos);
        }
        else {
            // include bordering existing vertices
            edges.Add(pos);
            return;
        }

        // terminate if we know that the hole has already been processed, add an invalid item and set interrupt to true
        if (pos.y < startPosition.y || pos.y == startPosition.y && pos.x < startPosition.x) {
            hole.Add(-1);
            interrupt = true;
            return;
        }

        var top = new int2(pos.x, pos.y - 1);
        var topright = new int2(pos.x + 1, pos.y - 1);
        var topleft = new int2(pos.x - 1, pos.y - 1);
        var left = new int2(pos.x - 1, pos.y);
        var right = new int2(pos.x + 1, pos.y);
        var bot = new int2(pos.x, pos.y + 1);
        var botleft = new int2(pos.x - 1, pos.y + 1);
        var botright = new int2(pos.x + 1, pos.y + 1);

        //FloodFillPoint(ref edges, ref hole, ref skippedPoints, topleft, in startPosition, ref interrupt);
        FloodFillPoint(ref edges, ref hole, ref skippedPoints, top, in startPosition, ref interrupt);
        FloodFillPoint(ref edges, ref hole, ref skippedPoints, topright, in startPosition, ref interrupt);
        FloodFillPoint(ref edges, ref hole, ref skippedPoints, left, in startPosition, ref interrupt);
        FloodFillPoint(ref edges, ref hole, ref skippedPoints, right, in startPosition, ref interrupt);
        FloodFillPoint(ref edges, ref hole, ref skippedPoints, bot, in startPosition, ref interrupt);
        FloodFillPoint(ref edges, ref hole, ref skippedPoints, botleft, in startPosition, ref interrupt);
        //FloodFillPoint(ref edges, ref hole, ref skippedPoints, botright, in startPosition, ref interrupt);
    }
}

[BurstCompile]
public struct GetHoleIndicesJob : IJob {
    
    [ReadOnly] public NativeMultiHashMap<int, int2> foundHoles;

    public NativeList<int> holeIDs;
    
    public void Execute() {

        var keys = foundHoles.GetKeyArray(Allocator.Temp);
        
        for (var index = 0; index < keys.Length; index++) {
            var holeID = keys[index];
            if (!holeIDs.Contains(holeID)) holeIDs.Add(holeID);
        }
    }
}