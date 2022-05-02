using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

public class MultiStageGenerator : MonoBehaviour {
    public bool reload;
    public bool drawTriangles;
    public bool drawPoints;
    public GridSettings settings;

    /*public TerrainStampParent stampCollector;
    private void OnValidate() {
        
        if (!this.gameObject.activeInHierarchy) return;
        
        Generate();
        if (reload) {
            reload = false;
        }
    }

    private List<(float3 a, float3 b, float3 c)> cachedTriangles = new List<(float3 a, float3 b, float3 c)>();
    private List<VertexPassJob.VertexData> cachedPoints = new List<VertexPassJob.VertexData>();

    private void OnDrawGizmosSelected() {

        if (drawPoints) {
            Gizmos.color = Color.green;
            foreach (var cachedPoint in cachedPoints) {
                Gizmos.DrawRay(cachedPoint.Position, cachedPoint.Normal);
            }
        }
        
        if (drawTriangles) {
            Gizmos.color = Color.cyan;
            foreach (var elem in cachedTriangles) {
                var posA = elem.a + (float3) transform.position;
                var posB = elem.b + (float3) transform.position;
                var posC = elem.c + (float3) transform.position;
                Gizmos.DrawLine(posA, posB);
                Gizmos.DrawLine(posB, posC);
                Gizmos.DrawLine(posC, posA);
            }
        }
    }

    private void Generate() {
        
        if (!stampCollector.stamps.IsCreated) return;
        
        var pointCountInitial = settings.Count * settings.Count;
        var triangleCountHalf = (settings.Count - 1) * (settings.Count - 1);

        if (settings.Count % settings.CoreGridSpacing != 1) {
            Debug.Log("invalid core grid spacing");
            return;
        }

        // allocate a ton of memory for all output data
        // lists are allocated to maximum possible length, even if it is not used most of the time, but we can't resize it when writing to it in parallel
        var heights = new NativeArray<half>(pointCountInitial, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var vertices = new NativeList<VertexPassJob.VertexData>(pointCountInitial, Allocator.TempJob);
        var triangles = new NativeList<int3>(triangleCountHalf * 2, Allocator.TempJob);
        var pointToVertexRefs = new NativeHashMap<int2, int>(pointCountInitial, Allocator.TempJob);

        var stamps = stampCollector.stamps;
        var heightmap = stampCollector.heightmapData;
        
        var unscaledTerrainMatrix = float4x4.TRS(transform.position, transform.rotation, 1f);

        try {
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            var heightHandle = new HeightSampleJob {
                settings = settings,
                heights = heights,
                stamps = stamps,
                heightmap = heightmap,
                terrainLTW = unscaledTerrainMatrix
            }.Schedule(pointCountInitial, settings.Count);

            var normalsHandle = new VertexPassJob {
                settings = settings,
                heights = heights,
                vertices = vertices.AsParallelWriter(),
                pointToVertexReferences = pointToVertexRefs.AsParallelWriter()
            }.Schedule(pointCountInitial, settings.Count, heightHandle);

            var patchCountPerLine = (settings.Count - 1) / settings.CoreGridSpacing;
            var patchCount = patchCountPerLine * patchCountPerLine;

            var triangulationHandle = new PatchTriangulationJob {
                settings = settings,
                vertexReferences = pointToVertexRefs,
                triangles = triangles.AsParallelWriter(),
                EdgeComputeMarker = new ProfilerMarker("EdgeComputeMarker"),
                InvalidSearchMarker = new ProfilerMarker("InvalidSearchMarker"),
                VertexGatherMarker = new ProfilerMarker("VertexGatherMarker"),
                TriangulationMarker = new ProfilerMarker("TriangulationMarker"),
                HoleTriangulationMarker = new ProfilerMarker("HoleTriangulationMarker")
            }.Schedule(patchCount, 1, normalsHandle);


            var meshData = Mesh.AllocateWritableMeshData(1);
            var mainMesh = meshData[0];
            
            var meshingHandle = new MeshPreparationJob {
                meshData = mainMesh,
                triangles = triangles.AsDeferredJobArray(),
                vertices = vertices.AsDeferredJobArray()
            }.Schedule(triangulationHandle);
            
            meshingHandle.Complete();
            
            var indicesCount = triangles.Length * 3;
            mainMesh.subMeshCount = 1;
            mainMesh.SetSubMesh(0, new SubMeshDescriptor(0, indicesCount), NoCalculations());
            
            var mesh = new Mesh {name = this.transform.name};
            var meshSize = settings.Count * 1f;
            mesh.bounds = new Bounds(new Vector3(meshSize / 2, settings.HeightScale / 2, meshSize / 2), new Vector3(meshSize, settings.HeightScale, meshSize));
            Mesh.ApplyAndDisposeWritableMeshData(meshData, mesh, NoCalculations());
            mesh.RecalculateBounds();
            //mesh.RecalculateNormals();
            //mesh.RecalculateTangents();

            gameObject.GetComponent<MeshFilter>().mesh = mesh;
            
            stopWatch.Stop();
            Debug.Log("Generation took: " + stopWatch.ElapsedMilliseconds + "ms or ticks: " + stopWatch.ElapsedTicks);
            
            if (drawPoints) {
                cachedPoints.Clear();
                foreach (var vertex in vertices) {
                    cachedPoints.Add(vertex);
                }
            }

            if (drawTriangles) {
                cachedTriangles.Clear();
                foreach (var triangle in triangles) {
                    cachedTriangles.Add((vertices[triangle.x].Position, vertices[triangle.y].Position, vertices[triangle.z].Position));
                }
            }
        }
        finally {
            heights.Dispose();
            vertices.Dispose();
            triangles.Dispose();
            pointToVertexRefs.Dispose();
        }
    }

    private static MeshUpdateFlags NoCalculations() {
        return MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;
    }*/
}