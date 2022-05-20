using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[BurstCompile]
public struct MeshPreparationJob : IJob {
    [WriteOnly] public Mesh.MeshData meshData;
    [NativeDisableContainerSafetyRestriction]
    [WriteOnly] public NativeArray<float3> meshCalculations; //min is at index 0, max at index 1, indices count is 2.x

    [ReadOnly] public NativeArray<VertexPassJob.VertexData> vertices;
    [ReadOnly] public NativeArray<int3> triangles;

    public void Execute() {
        meshData.SetIndexBufferParams(triangles.Length * 3, IndexFormat.UInt32);

        var attributes = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Temp) {
            [0] = new VertexAttributeDescriptor(VertexAttribute.Position),
            [1] = new VertexAttributeDescriptor(VertexAttribute.Normal),
            [2] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float16, 2)
        };

        meshData.SetVertexBufferParams(vertices.Length, attributes);

        var meshVerts = meshData.GetVertexData<VertexPassJob.VertexData>();
        meshVerts.CopyFrom(vertices);

        var meshTris = meshData.GetIndexData<int>();

        for (var i = 0; i < triangles.Length; i++) {
            var triangle = triangles[i];
            var startIndex = i * 3;
            meshTris[startIndex] = triangle.x;
            meshTris[startIndex + 1] = triangle.y;
            meshTris[startIndex + 2] = triangle.z;
        }
        
        ComputeBounds();
        meshCalculations[2] = new float3(triangles.Length * 3, 0, 0);
    }

    private void ComputeBounds() {

        var min = new float3(float.MaxValue);
        var max = new float3(-float.MaxValue);

        for (var i = 0; i < vertices.Length; i++) {
            var vertex = vertices[i];
            // component wise
            var pos = vertex.Position;

            if (pos.x < min.x) min.x = pos.x;
            if (pos.y < min.y) min.y = pos.y;
            if (pos.z < min.z) min.z = pos.z;

            if (pos.x > max.x) max.x = pos.x;
            if (pos.y > max.y) max.y = pos.y;
            if (pos.z > max.z) max.z = pos.z;
        }

        meshCalculations[0] = min;
        meshCalculations[1] = max;
    }
}