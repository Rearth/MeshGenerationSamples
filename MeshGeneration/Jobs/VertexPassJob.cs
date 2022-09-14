using System;
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

        var normalAngle = Vector3.Angle(normal, Vector3.up);

        var biomeSample = SampleBiome(x, y);
        var biomeCalculated = CalculateBiomeColors(in biomeSample, in normalAngle, in angle, in settings.BiomeConfig);
        var colorData = new Color(EncodeToFloat(new Color(biomeCalculated.c0.x, biomeCalculated.c0.y, biomeCalculated.c0.z, biomeCalculated.c0.w)), EncodeToFloat(biomeCalculated.c1), 0f, 0f);

        var uvPosition = uvStartPos + (uvSize / (settings.VertexCount - 1) * new float2(x, y));
        
        var data = new VertexData {
            Normal = sphereNormal,
            Position = localPosition,
            Color = colorData,
            UvPosition = uvPosition
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

    private static float4x2 CalculateBiomeColors(in half2 biomeData, in float angle, in float featureAngle, in BiomeConfiguration biomeConfiguration) {

        var temperature = math.clamp(biomeData.x, -1, 1);
        var humidity = math.clamp(biomeData.y, -1, 1);
        
        // texture order:
        /* 0 normal dry
         * 1 normal wet
         * 2 cold dry
         * 3 cold wet
         * 4 warm dry
         * 5 warm wet
         * 6 cliff normal
         * 7 cliff hot
         */

        var a = float4.zero;
        var b = float4.zero;

        var coldRange = biomeConfiguration.temperateEdge + 1f;       // equal to coldEdge - (-1)
        var temperateRange = biomeConfiguration.warmEdge - biomeConfiguration.temperateEdge;
        var warmRange = 1f - biomeConfiguration.warmEdge;

        var coldDist = math.distance(-1f + coldRange * 0.5f, temperature);
        var temperateDist = math.distance(biomeConfiguration.temperateEdge + temperateRange * 0.5f, temperature);
        var warmthDist = math.distance(biomeConfiguration.warmEdge + warmRange * 0.5f, temperature);
        
        var cold = math.smoothstep(coldRange * 0.5f, 0, coldDist / biomeConfiguration.falloffStrength);
        var temperate = math.smoothstep(temperateRange * 0.5f, 0, temperateDist / biomeConfiguration.falloffStrength);
        var warmth = math.smoothstep(warmRange * 0.5f, 0, warmthDist / biomeConfiguration.falloffStrength);

        var dryRange = biomeConfiguration.wetEdge + 1f;
        var dryDist = math.distance(-1f + dryRange * 0.5f, humidity);
        var wetRange = 1f - biomeConfiguration.wetEdge;
        var wetDist = math.distance(biomeConfiguration.wetEdge + wetRange * 0.5f, humidity);
        
        var dry = math.smoothstep(dryRange * 0.5f, 0, dryDist / biomeConfiguration.falloffStrength);
        var wet = math.smoothstep(wetRange * 0.5f, 0, wetDist / biomeConfiguration.falloffStrength);

        // set wet or dry to 1, depending on which is bigger
        dry = dry > wet ? 1f : dry;
        wet = wet > dry ? 1f : wet;

        cold = cold > temperate && cold > warmth ? 1f : cold;
        temperate = temperate > cold && temperate > warmth ? 1f : temperate;
        warmth = warmth > temperate && warmth > cold ? 1f : warmth;

        var cliff = math.smoothstep(biomeConfiguration.cliffAngle - biomeConfiguration.falloffStrength * 2, biomeConfiguration.cliffAngle + biomeConfiguration.falloffStrength * 2, angle);

        var cliffHot = temperature > biomeConfiguration.warmEdge ? 1f : 0f;
        
        dry *= 1 - cliff;
        wet *= 1 - cliff;

        temperate *= (1 - (cold + warmth));
        
        // cliff = cliff > feature ? 1f : cliff;
        // feature = feature > cliff ? 1f : feature;
        
        a = new float4(temperate * dry, temperate * wet, cold * dry, cold * wet);
        b = new float4(warmth * dry, warmth * wet, cliff * (1f - cliffHot), cliff * cliffHot);
        
        // Debug.Log(temperate + " " + humidity + " " + a);

        return new float4x2(a, b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void fallOffSigmoid(in float x, in float falloffStrength) {
        
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color DecodeToColor(float v) {
        var vi = (uint) (v * (256.0f * 256.0f * 256.0f * 256.0f));
        var ex = (int) (vi / (256 * 256 * 256) % 256);
        var ey = (int) ((vi / (256 * 256)) % 256);
        var ez = (int) ((vi / (256)) % 256);
        var ew = (int) (vi % 256);
        var e = new Color(ex / 255.0f, ey / 255.0f, ez / 255.0f, ew / 255.0f);
        return e;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EncodeToFloat(Color enc) {
        var ex = (uint) (enc.r * 255);
        var ey = (uint) (enc.g * 255);
        var ez = (uint) (enc.b * 255);
        var ew = (uint) (enc.a * 255);
        var v = (ex << 24) + (ey << 16) + (ez << 8) + ew;
        return v / (256.0f * 256.0f * 256.0f * 256.0f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EncodeToFloat(float4 enc) {
        var ex = (uint) (enc.x * 255);
        var ey = (uint) (enc.y * 255);
        var ez = (uint) (enc.z * 255);
        var ew = (uint) (enc.w * 255);
        var v = (ex << 24) + (ey << 16) + (ez << 8) + ew;
        return v / (256.0f * 256.0f * 256.0f * 256.0f);
    }
    
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct VertexData {
        public float3 Position;
        public float3 Normal;
        public Color Color;
        public float2 UvPosition;
    }
    
    [Serializable]
    public struct BiomeConfiguration {
        public float falloffStrength;
        public float temperateEdge;
        public float warmEdge;
        public float wetEdge;
        public float cliffAngle;
    }
}