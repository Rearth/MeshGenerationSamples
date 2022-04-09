using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct GridTriangulationJob : IJobParallelFor {
    [NativeDisableParallelForRestriction] [WriteOnly]
    public NativeList<int3>.ParallelWriter triangles;

    public GridSettings settings;

    public void Execute(int index) {
        var point = LinearArrayHelper.ReverseLinearIndex(index, settings.Count - 1);

        var topIndex = LinearArrayHelper.GetLinearIndex(new int2(point.x, point.y + 1), settings.Count);
        var topRightIndex = LinearArrayHelper.GetLinearIndex(point + 1, settings.Count);
        var rightIndex = LinearArrayHelper.GetLinearIndex(new int2(point.x + 1, point.y), settings.Count);
        var ownIndex = LinearArrayHelper.GetLinearIndex(point, settings.Count);
        
        var triA = new int3(topIndex, topRightIndex, ownIndex);
        var triB = new int3(topRightIndex, rightIndex, ownIndex);
        triangles.AddNoResize(triA);
        triangles.AddNoResize(triB);
    }
}