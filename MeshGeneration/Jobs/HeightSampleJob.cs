using System;
using System.Runtime.CompilerServices;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEditor.PackageManager.UI;
using UnityEngine;

// ReSharper disable ForCanBeConvertedToForeach

// generates a grid of points, and calculates/stores heights for each point (scale 0-1)

[BurstCompile]
public struct HeightSampleJob : IJobParallelFor {
    // index is based on position X/Y. No need to store/output positions, since they are directly computed again
    [WriteOnly] public NativeArray<HeightSample> heights;

    [ReadOnly] public NativeArray<TerrainStampData> stamps;
    [ReadOnly] public NativeArray<ushort> heightmapLinear;
    [ReadOnly] public NativeHashMap<ushort, uint> beginIndices;
    [ReadOnly] public float4x4 terrainLTW;

    public TerrainStaticData settings;
    public TerrainUVData uvData;

    public void Execute(int index) {
        var revIndex = LinearArrayHelper.ReverseLinearIndex(index, settings.VertexCount + 2); // sample one height to left and right aswell for normals, thus +2
        var x = revIndex.x - 1;
        var y = revIndex.y - 1;

        // calculate ray to intersect with stamps
        // iterate through stamp candidates, get intersection point with ray, calculate uv based on intersection point, sample if uv in 0-1 range

        var pointOnSphere = GetSphereDirection(x, y, settings, in uvData);

        var heightSample = SampleFromStamps(pointOnSphere, out var stampCount);
        heights[index] = new HeightSample{ Height = heightSample, StampCount = stampCount};
    }

    private static float3 GetSphereDirection(in int x, in int y, in TerrainStaticData settings, in TerrainUVData uvData) {
        var uvSize = uvData.UVSize;
        var uvStartPos = uvData.UVStart;
        var vertexDist = uvSize * 2f / (settings.VertexCount - 1);

        var localFlatPosition = new float3(x * vertexDist - 1 + uvStartPos.x * 2f, 1, y * vertexDist - 1 + uvStartPos.y * 2f);
        var sphereCenter = new float3(0, 0, 0);
        var sphereRadius = settings.PlanetRadius;

        return Geometry.TransformPointFlatToSphere(in localFlatPosition, in sphereCenter, sphereRadius);
    }

    private float SampleFromStamps(in float3 pointOnSphere, out ushort stampCount) {
        var combinedHeight = settings.StartHeight;
        stampCount = 0;

        for (var i = 0; i < stamps.Length; i++) {
            var stampData = stamps[i];

            var sphereDirection = math.normalize(pointOnSphere);

            var stampLTWCorrected = math.mul(math.fastinverse(terrainLTW), stampData.LTW);

            var plane = new LocalToWorld {Value = stampLTWCorrected};

            if (math.dot(sphereDirection, plane.Up) <= 0) continue;
            var intersectionPoint = Geometry.PlaneRaycast(plane.Position, plane.Up, float3.zero, sphereDirection);

            var pointInStampSpace = Geometry.TransformPositionInverse(intersectionPoint, plane.Value).xz;
            var stampUVPos = ToStampUVCoords(pointInStampSpace, stampData.Stamp.Extends);

            if (!IsInStamp(stampUVPos)) continue;

            stampCount++;

            var heightmap = GetHeightmapFromStack(stampData.Stamp.TextureID, stampData.Stamp.TextureSize * stampData.Stamp.TextureSize, in beginIndices, in heightmapLinear);

            var sampledHeight = NativeTextureHelper.SampleTextureBilinear(ref heightmap, stampData.Stamp.TextureSize, stampUVPos);

            var heightInfluence = SampleFalloffCurve(stampUVPos);
            var combinedInfluence = math.min(heightInfluence.x, heightInfluence.y);

            combinedHeight = BlendStamp(in combinedHeight, in sampledHeight, in combinedInfluence, in stampData.Stamp);
        }

        return combinedHeight;
    }

    private static float BlendStamp(in float lastHeight, in float stampSample, in float falloff, in HeightmapStamp stamp) {

        var stampHeight = stampSample * stamp.HeightScale * falloff + stamp.HeightOffset;
        
        switch (stamp.BlendMode) {
            case HeightmapStamp.BlendType.Add:
                return lastHeight + stampHeight;
            case HeightmapStamp.BlendType.Subtract:
                return lastHeight - stampHeight;
            case HeightmapStamp.BlendType.Max:
                return math.max(lastHeight, stampHeight);
            case HeightmapStamp.BlendType.Min:
                return math.min(lastHeight, stampHeight);
            case HeightmapStamp.BlendType.Blend:
                stampHeight = stampSample * stamp.HeightScale + stamp.HeightOffset;
                var lerp = math.min(falloff, 0.5f);
                return math.lerp(lastHeight, stampHeight, lerp);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static NativeSlice<ushort> GetHeightmapFromStack(in ushort id, in int length, in NativeHashMap<ushort, uint> beginIndices, in NativeArray<ushort> stack) {
        var startAt = beginIndices[id];
        var slice = stack.Slice((int) startAt, length);
        return slice;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float2 ToStampUVCoords(in float2 point, in float stampExtends) {
        return point / stampExtends / 2f + 0.5f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInStamp(in float2 point) {
        return (math.all(point >= 0f) && math.all(point < 1f));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float2 SampleFalloffCurve(in float2 origin) {
        return new float2(math.min(SampleFalloffCurve(1 - origin.x), SampleFalloffCurve(origin.x)), math.min(SampleFalloffCurve(1 - origin.y), SampleFalloffCurve(origin.y)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // x is in range 0-1, return influence from 1-0
    private static float SampleFalloffCurve(in float x) {
        return 1f - (0.00001322098f / -13.86292f) * (1 - math.pow(math.E, (13.86292f * x)));
    }
}

public struct HeightSample {
    public float Height;
    public ushort StampCount;
}

[Serializable]
public struct TerrainStampData {
    public float4x4 LTW;
    public HeightmapStamp Stamp;
}