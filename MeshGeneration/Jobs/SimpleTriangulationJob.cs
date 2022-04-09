using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

[BurstCompile]
public struct SimpleTriangulationJob : IJobParallelForDefer {
    [ReadOnly] public NativeArray<NormalPassJob.VertexData> vertices;
    [ReadOnly] public NativeHashMap<int, int> vertexReferences;

    public GridSettings settings;

    [WriteOnly] public NativeList<int3>.ParallelWriter triangles;

    public ProfilerMarker NeighborCheck, VertexCreation;

    public void Execute(int index) {

        var ownPosition = vertices[index].Position;
        var ownPoint = (int2) (ownPosition.xz / settings.Distance + 0.001f);
        var x = ownPoint.x;
        var y = ownPoint.y;
        
        NeighborCheck.Begin();

        var createTopLeft = checkIfExists(new int2(x - 1, y), new int2(x, y - 1), ref vertexReferences, in settings, out var x11, out var x12);
        var createBotRight = checkIfExists(new int2(x + 1, y), new int2(x, y + 1), ref vertexReferences, in settings, out var x21, out var x22);
        
        NeighborCheck.End();
        
        VertexCreation.Begin();
        
        //create tri A
        if (createTopLeft) {
            var indexA = x11;
            var indexB = x12;
            var indexC = index;

            var tri = new int3(indexA, indexB, indexC);
            UnsafeListHelper.Add(ref triangles, tri);
        }
        
        
        if (createBotRight) {
            var indexA = x21;
            var indexB = x22;
            var indexC = index;

            var tri = new int3(indexA, indexB, indexC);
            UnsafeListHelper.Add(ref triangles, tri);
        }
        
        VertexCreation.End();
    }

    private static bool checkIfExists(in int2 p0, in int2 p1, ref NativeHashMap<int, int> references, in GridSettings settings, out int indexA, out int indexB) {

        var p0Index = LinearArrayHelper.GetLinearIndex(p0.x, p0.y, settings.Count);
        var p1Index = LinearArrayHelper.GetLinearIndex(p1.x, p1.y, settings.Count);
        
        var checkA = references.TryGetValue(p0Index, out indexA) && IsInBounds(p0, settings);
        var checkB = references.TryGetValue(p1Index, out indexB) && IsInBounds(p1, settings);

        return checkA && checkB;
    }

    private static bool IsInBounds(in int2 p, in GridSettings settings) {
        return math.all(p >= 0) && math.all(p < settings.Count);
    }

    private static bool IsInSkipList(ref NativeArray<int2> list, int2 target) {
        
        for (var i = 0; i < list.Length; i++) {
            var point = list[i];
            if (math.all(point == target)) return true;
        }

        return false;
    }
}