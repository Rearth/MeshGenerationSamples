using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

// writes to heights[3.x]
[BurstCompile]
public struct ChunkStampCountJob : IJob {
    
    [DeallocateOnJobCompletion][ReadOnly] public NativeArray<HeightSample> heights;
    
    [NativeDisableContainerSafetyRestriction]
    [WriteOnly] public NativeArray<float3> meshCalculations;
    
    public void Execute() {
        var maxStamps = 0;
        for (var i = 0; i < heights.Length; i++) {
            var heightSample = heights[i];
            maxStamps = math.max(maxStamps, heightSample.StampCount);
        }

        meshCalculations[3] = new float3(maxStamps, 0, 0);
    }
}