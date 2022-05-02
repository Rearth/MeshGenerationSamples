using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct VertexPassJob : IJobParallelFor {
    
    [ReadOnly] public NativeArray<half> heights;
    public TerrainSharedData settings;
    public TerrainInstanceData instanceData;

    [WriteOnly] public NativeList<VertexData>.ParallelWriter vertices;
    [WriteOnly] public NativeHashMap<int2, int>.ParallelWriter pointToVertexReferences;

    public void Execute(int index) {
        
        var revIndex = LinearArrayHelper.ReverseLinearIndex(index, settings.VertexCount);
        var x = revIndex.x;
        var y = revIndex.y;

        var isEdge = x == 0 || x == settings.VertexCount - 1 || y == 0 || y == settings.VertexCount - 1;

        var uvSize = instanceData.UVSize;
        var uvStartPos = instanceData.UVStart;
        var vertexDist = uvSize * 2f / (settings.VertexCount - 1);

        var ownHeight = heights[index];
        CalculateNormalSet(ownHeight, x, y, vertexDist, out var normalA, out var normalB);

        var normal = math.normalize(normalA + normalB);
        var angle = Vector3.Angle(normalA, normalB);

        var isCoreGridPoint = x % settings.CoreGridSpacing == 0 && y % settings.CoreGridSpacing == 0;
        var skipPoint = !isCoreGridPoint && angle < settings.NormalReduceThreshold;
        
        if (skipPoint) return;
        
        var localFlatPosition = new float3(x * vertexDist - 1 + uvStartPos.x * 2f, 1,  y * vertexDist - 1 + uvStartPos.y * 2f);
        
        var sphereCenter = new float3(0, 0, 0);
        var sphereRadius = settings.PlanetRadius;
        
        var localPosition = Geometry.TransformPointFlatToSphere(in localFlatPosition, in sphereCenter, sphereRadius + ownHeight * settings.HeightScale);

        var sphereRot = Geometry.FromToRotation(Vector3.up, localPosition);
        var sphereNormal = math.mul(sphereRot, normal);

        var data = new VertexData {
            Normal = sphereNormal,
            Position = localPosition,
        };

        var idx = UnsafeListHelper.AddWithIndex(ref vertices, in data);
        pointToVertexReferences.TryAdd(revIndex, idx);

    }

    private void CalculateNormalSet(in half ownHeight, in int x, in int y, float vertexDist, out float3 normalA, out float3 normalB) {
        var normalCalculationHeightScale = 1 / settings.PlanetRadius;

        var centerPos = new float3(0, ownHeight * settings.HeightScale * normalCalculationHeightScale, 0);

        var sampleAIndex = LinearArrayHelper.GetLinearIndexSafe(x - 1, y, settings.VertexCount);
        var sampleA = heights[sampleAIndex] * settings.HeightScale * normalCalculationHeightScale;
        var posA = new float3(-vertexDist, sampleA, 0);

        var sampleBIndex = LinearArrayHelper.GetLinearIndexSafe(x + 1, y, settings.VertexCount);
        var sampleB = heights[sampleBIndex] * settings.HeightScale * normalCalculationHeightScale;
        var posB = new float3(vertexDist, sampleB, 0);

        var sampleCIndex = LinearArrayHelper.GetLinearIndexSafe(x, y - 1, settings.VertexCount);
        var sampleC = heights[sampleCIndex] * settings.HeightScale * normalCalculationHeightScale;
        var posC = new float3(0, sampleC, -vertexDist);

        var sampleDIndex = LinearArrayHelper.GetLinearIndexSafe(x, y + 1, settings.VertexCount);
        var sampleD = heights[sampleDIndex] * settings.HeightScale * normalCalculationHeightScale;
        var posD = new float3(0, sampleD, vertexDist);

        normalA = math.cross(posC - centerPos, posA - centerPos);
        normalB = math.cross(posD - centerPos, posB - centerPos);
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