using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile]
public struct TriangleCountJob : IJob {
    
    [ReadOnly]
    public NativeArray<TriangleStorage.Triangle> Triangles;

    [WriteOnly] public NativeArray<int> result;

    public void Execute() {

        var res = 0;
        
        for (var i = 0; i < Triangles.Length; i++) {
            var triangle = Triangles[i];
            if (!triangle.IsDeleted) res++;
        }

        result[0] = res;
    }
}