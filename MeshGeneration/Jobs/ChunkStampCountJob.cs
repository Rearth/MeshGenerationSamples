using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

// writes to heights[3.x]
public struct ChunkStampCountJob : IJob {
    
    [DeallocateOnJobCompletion][ReadOnly] public NativeArray<HeightSample> heights;
    
    [NativeDisableContainerSafetyRestriction]
    [WriteOnly] public NativeArray<float3> meshCalculations;
    
    public void Execute() {
        var maxStamps = 0;
        foreach (var heightSample in heights) {
            maxStamps = math.max(maxStamps, heightSample.StampCount);
        }

        meshCalculations[3] = new float3(maxStamps, 0, 0);
    }
}