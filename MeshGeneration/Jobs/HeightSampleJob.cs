using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// generates a grid of points, and calculates/stores heights for each point (scale 0-1)

[BurstCompile]
public struct HeightSampleJob : IJobParallelFor {
    // index is based on position X/Y. No need to store/output positions, since they are directly computed again
    [WriteOnly] public NativeArray<half> heights;

    public GridSettings settings;
    public ProceduralGenerationSettings generationSettings;

    public void Execute(int index) {
        var revIndex = LinearArrayHelper.ReverseLinearIndex(index, settings.Count);
        var x = revIndex.x;
        var y = revIndex.y;
        
        var localPosition = new float2(x * settings.Distance, y * settings.Distance);
        var heightSample = getAdvPlanetGeneration(new float3(localPosition.x, 0f, localPosition.y), generationSettings.NoiseScale);
        heights[index] = (half) heightSample;
    }
    
    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float getAdvPlanetGeneration(in float3 pos, in float frequency) {

        var res = 0f;

        var baseElevation = getFractalNoise(pos, 2, frequency * 4, 1) + 0.5f;
        baseElevation *= 0.3f;

        var ridges = 1 - getFractalNoise(pos, 2, frequency * 2, 1) + 0.5f;
        ridges *= 0.4f;

        var simplex = getFractalNoise(pos, 5, frequency, 1) * 1.1f + 0.2f;
        var simplexMask = getFractalNoise(pos + new float3(0, 500f, 0), 1, frequency * 4, 1) * 1.2f + 0.7f;
        simplex *= simplexMask;
        res += baseElevation + ridges + simplex * 0.5f - 0.3f;

        return res;

    }
    
    [BurstCompile]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float getFractalNoise(in float3 pos, int octaves, float frequency, float amplitude) {
        float sum = 0;
        float gain = 1;

        for (int i = 0; i < octaves; i++) {
            //sum += noise.snoise(pos * gain / frequency) * amplitude / gain;
            var posNoise = pos * gain / frequency;
            sum += noise.snoise(posNoise) * amplitude / gain;
            gain *= 2;
        }

        return sum;
    }
}

public static class LinearArrayHelper {
    public static int GetLinearIndex(int x, int y, int count) {
        return y + x * count;
    }
    
    public static int GetLinearIndex(int2 pos, int count) {
        return pos.y + pos.x * count;
    }

    public static int GetLinearIndexSafe(int x, int y, int count) {
        x = math.clamp(x, 0, count - 1);
        y = math.clamp(y, 0, count - 1);
        return y + x * count;
    }

    public static int2 ReverseLinearIndex(int index, int count) {
        var y = index % count;
        var x = index / count;
        return new int2(x, y);
    }
}

public static class UnsafeListHelper {

    public static unsafe int AddWithIndex<T>(ref NativeList<T>.ParallelWriter list, in T element) where T : unmanaged {
        var listData = list.ListData;
        var idx = Interlocked.Increment(ref listData->m_length) - 1;
        UnsafeUtility.WriteArrayElement(listData->Ptr, idx, element);

        return idx;
    }
    public static unsafe void Add<T>(ref NativeList<T>.ParallelWriter list, in T element) where T : unmanaged {
        var listData = list.ListData;
        var idx = Interlocked.Increment(ref listData->m_length) - 1;
        UnsafeUtility.WriteArrayElement(listData->Ptr, idx, element);
    }
    
}

[Serializable]
public struct ProceduralGenerationSettings {
    public float NoiseScale;
}

[Serializable]
public struct GridSettings {
    public ushort Count;
    public float Distance;
    public float NormalReduceThreshold;
    public float HeightScale;
    public ushort CoreGridSpacing;
    [Range(0, 150)]
    public int test;
}