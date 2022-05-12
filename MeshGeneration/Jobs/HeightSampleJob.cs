using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
// ReSharper disable ForCanBeConvertedToForeach

// generates a grid of points, and calculates/stores heights for each point (scale 0-1)

[BurstCompile]
public struct HeightSampleJob : IJobParallelFor {
    // index is based on position X/Y. No need to store/output positions, since they are directly computed again
    [WriteOnly] public NativeArray<float> heights;

    [ReadOnly] public NativeArray<TerrainStampData> stamps;
    [ReadOnly] public NativeArray<ushort> heightmapLinear;
    [ReadOnly] public NativeHashMap<ushort, uint> beginIndices;
    [ReadOnly] public float4x4 terrainLTW;

    public TerrainStaticData settings;
    public TerrainUVData uvData;

    public void Execute(int index) {
        var revIndex = LinearArrayHelper.ReverseLinearIndex(index, settings.VertexCount + 2);   // sample one height to left and right aswell for normals, thus +2
        var x = revIndex.x - 1;
        var y = revIndex.y - 1;

        // calculate ray to intersect with stamps
        // iterate through stamp candidates, get intersection point with ray, calculate uv based on intersection point, sample if uv in 0-1 range

        var pointOnSphere = GetSphereDirection(x, y, settings, in uvData);
        
        var heightSample = SampleFromStamps(pointOnSphere);
        heights[index] = heightSample;
    }

    private static float3 GetSphereDirection(in int x, in int y, in TerrainStaticData settings, in TerrainUVData uvData) {

        var uvSize = uvData.UVSize;
        var uvStartPos = uvData.UVStart;
        var vertexDist = uvSize * 2f / (settings.VertexCount - 1);
        
        var localFlatPosition = new float3(x * vertexDist - 1 + uvStartPos.x * 2f, 1,  y * vertexDist - 1 + uvStartPos.y * 2f);
        var sphereCenter = new float3(0, 0, 0);
        var sphereRadius = settings.PlanetRadius;

        return Geometry.TransformPointFlatToSphere(in localFlatPosition, in sphereCenter, sphereRadius);
    }

    private float SampleFromStamps(in float3 pointOnSphere) {

        var combinedHeight = 0f;
        
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

            var heightmap = GetHeightmapFromStack(stampData.Stamp.TextureID, stampData.Stamp.TextureSize * stampData.Stamp.TextureSize, in beginIndices, in heightmapLinear);
            
            var sampledHeight = NativeTextureHelper.SampleTextureBilinear(ref heightmap, stampData.Stamp.TextureSize, stampUVPos);
            combinedHeight += sampledHeight;
        }

        return combinedHeight;
    }

    private static NativeSlice<ushort> GetHeightmapFromStack(in ushort id, in int length, in NativeHashMap<ushort, uint> beginIndices, in NativeArray<ushort> stack) {
        var startAt = beginIndices[id];
        var slice = stack.Slice((int) startAt, length);
        return slice;
    }

    private static float2 ToStampUVCoords(in float2 point, in float stampExtends) {
        return point / stampExtends / 2f + 0.5f;
    }

    private static bool IsInStamp(in float2 point) {
        return (math.all(point >= 0f) && math.all(point < 1f));
    }
}

[Serializable]
public struct TerrainStampData {
    public float4x4 LTW;
    public HeightmapStamp Stamp;
}