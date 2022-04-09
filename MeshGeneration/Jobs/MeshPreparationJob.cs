using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[BurstCompile]
public struct MeshPreparationJob : IJob {
    [WriteOnly] public Mesh.MeshData meshData;

    [ReadOnly] public NativeArray<NormalPassJob.VertexData> vertices;
    [ReadOnly] public NativeArray<int3> triangles;

    public void Execute() {
        meshData.SetIndexBufferParams(triangles.Length * 3, IndexFormat.UInt32);

        var attributes = new NativeArray<VertexAttributeDescriptor>(2, Allocator.Temp);
        attributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position);
        attributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal);
        
        meshData.SetVertexBufferParams(vertices.Length, attributes);

        var meshVerts = meshData.GetVertexData<NormalPassJob.VertexData>();
        meshVerts.CopyFrom(vertices);

        var meshTris = meshData.GetIndexData<int>();

        for (var i = 0; i < triangles.Length; i++) {
            var triangle = triangles[i];
            var startIndex = i * 3;
            meshTris[startIndex] = triangle.x;
            meshTris[startIndex + 1] = triangle.y;
            meshTris[startIndex + 2] = triangle.z;
        }
    }
}