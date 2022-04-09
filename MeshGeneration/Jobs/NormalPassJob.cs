using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct NormalPassJob : IJobParallelFor {
    
    [ReadOnly] public NativeArray<half> heights;
    public GridSettings settings;

    [WriteOnly] public NativeList<VertexData>.ParallelWriter vertices;
    [WriteOnly] public NativeHashMap<int2, int>.ParallelWriter pointToVertexReferences;

    public void Execute(int index) {
        
        var revIndex = LinearArrayHelper.ReverseLinearIndex(index, settings.Count);
        var x = revIndex.x;
        var y = revIndex.y;

        var isEdge = x == 0 || x == settings.Count - 1 || y == 0 || y == settings.Count - 1;

        var ownHeight = heights[index];
        var centerPos = new float3(0, ownHeight * settings.HeightScale, 0);

        var sampleAIndex = LinearArrayHelper.GetLinearIndexSafe(x - 1, y, settings.Count);
        var sampleA = heights[sampleAIndex] * settings.HeightScale;
        var posA = new float3(-settings.Distance, sampleA, 0);

        var sampleBIndex = LinearArrayHelper.GetLinearIndexSafe(x + 1, y, settings.Count);
        var sampleB = heights[sampleBIndex] * settings.HeightScale;
        var posB = new float3(settings.Distance, sampleB, 0);

        var sampleCIndex = LinearArrayHelper.GetLinearIndexSafe(x, y - 1, settings.Count);
        var sampleC = heights[sampleCIndex] * settings.HeightScale;
        var posC = new float3(0, sampleC, -settings.Distance);

        var sampleDIndex = LinearArrayHelper.GetLinearIndexSafe(x, y + 1, settings.Count);
        var sampleD = heights[sampleDIndex] * settings.HeightScale;
        var posD = new float3(0, sampleD, settings.Distance);

        var normalA = math.cross(posC - centerPos, posA - centerPos);
        var normalB = math.cross(posD - centerPos, posB - centerPos);

        var normal = math.normalize(normalA + normalB);
        var angle = Vector3.Angle(normalA, normalB);

        var isCoreGridPoint = x % settings.CoreGridSpacing == 0 && y % settings.CoreGridSpacing == 0;
        var skipPoint = !isEdge && !isCoreGridPoint && angle < settings.NormalReduceThreshold;
        
        if (skipPoint) return;
        
        var localPosition = new float3(x * settings.Distance, ownHeight * settings.HeightScale,  y * settings.Distance);

        var data = new VertexData {
            Normal = normal,
            Position = localPosition,
        };

        var idx = UnsafeListHelper.AddWithIndex(ref vertices, in data);
        pointToVertexReferences.TryAdd(revIndex, idx);

    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct VertexData {
        public float3 Position;
        public float3 Normal;

        public override string ToString() {
            return $"{nameof(Normal)}: {Normal}";
        }
    }
}