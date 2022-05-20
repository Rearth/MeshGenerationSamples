using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct VertexPassJob : IJobParallelFor {
    
    [ReadOnly] public NativeArray<HeightSample> heights;
    public TerrainStaticData settings;
    public TerrainUVData uvData;

    [WriteOnly] public NativeList<VertexData>.ParallelWriter vertices;
    [WriteOnly] public NativeHashMap<int2, int>.ParallelWriter pointToVertexReferences;

    public void Execute(int index) {
        
        var revIndex = LinearArrayHelper.ReverseLinearIndex(index, settings.VertexCount);
        var x = revIndex.x;
        var y = revIndex.y;

        var isEdge = x == 0 || x == settings.VertexCount - 1 || y == 0 || y == settings.VertexCount - 1;

        var uvSize = uvData.UVSize;
        var uvStartPos = uvData.UVStart;
        var vertexDist = uvSize * 2f / (settings.VertexCount - 1);

        var ownHeight = SampleHeight(x, y);
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

        var biomeSample = SampleBiome(x, y);

        var data = new VertexData {
            Normal = sphereNormal,
            Position = localPosition,
            Color = biomeSample
        };

        var idx = UnsafeListHelper.AddWithIndex(ref vertices, in data);
        pointToVertexReferences.TryAdd(revIndex, idx);

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float SampleHeight(in int x, in int y) {
        var index = LinearArrayHelper.GetLinearIndex(x + 1, y + 1, settings.VertexCount + 2);
        return heights[index].Height;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private half2 SampleBiome(in int x, in int y) {
        var index = LinearArrayHelper.GetLinearIndex(x + 1, y + 1, settings.VertexCount + 2);
        return heights[index].BiomeData;
    }

    private void CalculateNormalSet(in float ownHeight, in int x, in int y, float vertexDist, out float3 normalA, out float3 normalB) {
        var normalCalculationHeightScale = 1 / settings.PlanetRadius;

        var centerPos = new float3(0, ownHeight * settings.HeightScale * normalCalculationHeightScale, 0);

        var sampleA = SampleHeight(x - 1, y) * settings.HeightScale * normalCalculationHeightScale;
        var posA = new float3(-vertexDist, sampleA, 0);

        var sampleB = SampleHeight(x + 1, y) * settings.HeightScale * normalCalculationHeightScale;
        var posB = new float3(vertexDist, sampleB, 0);

        var sampleC = SampleHeight(x, y - 1) * settings.HeightScale * normalCalculationHeightScale;
        var posC = new float3(0, sampleC, -vertexDist);

        var sampleD = SampleHeight(x, y + 1) * settings.HeightScale * normalCalculationHeightScale;
        var posD = new float3(0, sampleD, vertexDist);

        normalA = math.cross(posC - centerPos, posA - centerPos);
        normalB = math.cross(posD - centerPos, posB - centerPos);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct VertexData {
        public float3 Position;
        public float3 Normal;
        public half2 Color;
    }
}