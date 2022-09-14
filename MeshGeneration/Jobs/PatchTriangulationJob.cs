using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

[BurstCompile]
public struct PatchTriangulationJob : IJobParallelFor {

    public TerrainStaticData settings;

    [ReadOnly] public NativeHashMap<int2, int> vertexReferences;

    [WriteOnly] public NativeList<int3>.ParallelWriter triangles;

    public ProfilerMarker InvalidSearchMarker;
    public ProfilerMarker EdgeComputeMarker;
    public ProfilerMarker VertexGatherMarker;
    public ProfilerMarker TriangulationMarker;
    public ProfilerMarker HoleTriangulationMarker;
    public ProfilerMarker FloodFillMarker;
    public ProfilerMarker ConnectivityMarker;
    
    
    public void Execute(int patchIndex) {
        
        VertexGatherMarker.Begin();
        
        var patchVertexCountPerLine = (settings.VertexCount - 1) / settings.CoreGridSpacing;
        var patchPosition = LinearArrayHelper.ReverseLinearIndex(patchIndex, patchVertexCountPerLine);

        var patchLineVertVertexCount = (settings.VertexCount - 1) / patchVertexCountPerLine;

        var startVertex = patchPosition * patchLineVertVertexCount;

        var patchVertices = new UnsafeList<int2>(patchLineVertVertexCount * patchLineVertVertexCount / 2, Allocator.Temp);

        var size = math.length(new int2(patchLineVertVertexCount + 1));

        // get all non-skipped vertices
        for (int x = startVertex.x; x < startVertex.x + patchLineVertVertexCount + 1; x++) {
            for (int y = startVertex.y; y < startVertex.y + patchLineVertVertexCount + 1; y++) {
                var candidatePos = new int2(x, y);
                var isValid = vertexReferences.ContainsKey(candidatePos);
                if (isValid) patchVertices.Add(candidatePos);
            }
        }
        
        VertexGatherMarker.End();

        TriangulationMarker.Begin();
        var triangulation = SimpleBowyerWatson.Delaunay(ref patchVertices, (int) size, ref InvalidSearchMarker, ref EdgeComputeMarker, ref HoleTriangulationMarker, ref FloodFillMarker, ref ConnectivityMarker);
        
        // add triangles to global triangle list, while getting the correct global indices. 
        for (var i = 0; i < triangulation.Length; i++) {
            var triangle = triangulation[i];
            var indices = triangle.Indices;
            
            var posA = patchVertices[indices.x];
            var posB = patchVertices[indices.y];
            var posC = patchVertices[indices.z];
            var indexA = vertexReferences[posA];
            var indexB = vertexReferences[posB];
            var indexC = vertexReferences[posC];
            
            var globalIndices = new int3(indexA, indexB, indexC);

            triangles.AddNoResize(globalIndices);
        }
        
        TriangulationMarker.End();
    }
}