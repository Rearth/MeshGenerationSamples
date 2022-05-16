using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

public static class Geometry {
    
    // basically world to local
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 TransformPositionInverse(in float3 pos, in float4x4 matrix) {
        return math.mul(math.fastinverse(matrix), new float4(pos, 1)).xyz;
    }

    // basically local to world
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 TransformPosition(in float3 pos, in float4x4 matrix) {
        return math.mul(matrix, new float4(pos, 1)).xyz;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Unity.Physics.Plane PlaneFromDirection(float3 origin, float3 direction)
    {
        float3 normal = math.normalize(direction);
        return new Unity.Physics.Plane(normal, -math.dot(normal, origin));
    }

    public static float3 PlaneRaycast(in float3 planePosition, in float3 planeNormal, in float3 rayFrom, in float3 rayDirection) {

        var denominator = math.dot(rayDirection, planeNormal);
        Assert.IsTrue(denominator > math.EPSILON);

        var t = math.dot(planePosition - rayFrom, planeNormal) / denominator;
        var p = rayFrom + rayDirection * t;
        return p;

    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 TransformPointFlatToSphere(in float3 sourcePoint, in float3 sphereCenterLocal, in float height) {
        var dirToCenter = sourcePoint - sphereCenterLocal;
        dirToCenter = CubeToSphere(dirToCenter);
        //dirToCenter = math.normalize(dirToCenter);

        var transformedPos = sphereCenterLocal + dirToCenter * height;

        return transformedPos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static quaternion AngleAxis(float aAngle, float3 aAxis) {
        aAxis = math.normalize(aAxis);
        var rad = aAngle * Mathf.Deg2Rad * 0.5f;
        aAxis *= math.sin(rad);
        return new quaternion(aAxis.x, aAxis.y, aAxis.z, math.cos(rad));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static quaternion FromToRotation(float3 aFrom, float3 aTo) {
        
        var axis = math.cross(aFrom, aTo);
        var angle = Vector3.Angle(aFrom, aTo);

        if (angle == 0) return quaternion.identity;
        
        return AngleAxis(angle, math.normalize(axis));
    }

    // basically an improved math.normalize with a more even distribution
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 CubeToSphere(in float3 point) {
        var p2 = point * point;
        var x = point.x * math.sqrt(1 - p2.y / 2 - p2.z / 2 + p2.y * p2.z / 3);
        var y = point.y * math.sqrt(1 - p2.x / 2 - p2.z / 2 + p2.x * p2.z / 3);
        var z = point.z * math.sqrt(1 - p2.x / 2 - p2.y / 2 + p2.x * p2.y / 3);
        return new float3(x, y, z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CircumCircle(float3 a, float3 b, float3 c, out float3 circleCenter, out float circleRadius) {
        PerpendicularBisector(a, b - a, out var perpAbPos, out var perpAbDir);
        PerpendicularBisector(a, c - a, out var perpAcPos, out var perpAcdir);
        var tAb = Intersection(perpAbPos, perpAbPos + perpAbDir, perpAcPos, perpAcPos + perpAcdir);
        circleCenter = perpAbPos + perpAbDir * tAb;

        circleRadius = math.length(circleCenter - a);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PerpendicularBisector(float3 pos, float3 dir, out float3 bisectorPos,
        out float3 bisectorDir) {
        var m = dir * .5f;
        var cross = math.normalize(math.cross(math.normalize(dir), new float3(0, 1, 0)));
        bisectorPos = pos + m;
        bisectorDir = cross;
    }

    // http://paulbourke.net/geometry/pointlineplane/
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Intersection(float3 p1, float3 p2, float3 p3, float3 p4) {
        var tAb = ((p4.x - p3.x) * (p1.z - p3.z) - (p4.z - p3.z) * (p1.x - p3.x)) /
                  ((p4.z - p3.z) * (p2.x - p1.x) - (p4.x - p3.x) * (p2.z - p1.z));
        return tAb;
    }
}

public static class NativeTextureHelper {
    
    // returns in scale 0-1, texturepos is in scale 0-1
    public static float SampleTextureBilinear(ref NativeSlice<ushort> heightMap, in int heightMapSize, float2 texturePos) {

        //texturePos = texturePos;
        
        texturePos *= heightMapSize;
        var x1 = (int) math.floor(texturePos.x);
        var y1 = (int) math.floor(texturePos.y);
        var x2 = math.clamp(x1 + 1, 0, heightMapSize - 1);
        var y2 = math.clamp(y1 + 1, 0, heightMapSize - 1);

        var xp = texturePos.x - x1;
        var yp = texturePos.y - y1;

        var p11 = GetLinearTexturePixel(x1, y1, ref heightMap, heightMapSize);
        var p12 = GetLinearTexturePixel(x2, y1, ref heightMap, heightMapSize);
        var p21 = GetLinearTexturePixel(x1, y2, ref heightMap, heightMapSize);
        var p22 = GetLinearTexturePixel(x2, y2, ref heightMap, heightMapSize);

        var px1 = math.lerp(p11, p12, xp);
        var px2 = math.lerp(p21, p22, xp);

        var res = math.lerp(px1, px2, yp);
        return res;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetLinearTexturePixel(in int x, in int y, ref NativeSlice<ushort> heightMap, in int heightMapSize) {
        var index = y + x * heightMapSize;
        return (float) heightMap[index] / ushort.MaxValue;
    }
}

public static class LinearArrayHelper {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetLinearIndex(int x, int y, int count) {
        return y + x * count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetLinearIndex(int2 pos, int count) {
        return pos.y + pos.x * count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetLinearIndexSafe(int x, int y, int count) {
        x = math.clamp(x, 0, count - 1);
        y = math.clamp(y, 0, count - 1);
        return y + x * count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 ReverseLinearIndex(int index, int count) {
        var y = index % count;
        var x = index / count;
        return new int2(x, y);
    }
}

public static class UnsafeListHelper {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe int AddWithIndex<T>(ref NativeList<T>.ParallelWriter list, in T element) where T : unmanaged {
        var listData = list.ListData;
        var idx = Interlocked.Increment(ref listData->m_length) - 1;
        UnsafeUtility.WriteArrayElement(listData->Ptr, idx, element);

        return idx;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Add<T>(ref NativeList<T>.ParallelWriter list, in T element) where T : unmanaged {
        var listData = list.ListData;
        var idx = Interlocked.Increment(ref listData->m_length) - 1;
        UnsafeUtility.WriteArrayElement(listData->Ptr, idx, element);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T GetRef<T>(this NativeArray<T> array, int index)
        where T : struct
    {
        // You might want to validate the index first, as the unsafe method won't do that.
        if (index < 0 || index >= array.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        unsafe
        {
            return ref UnsafeUtility.ArrayElementAsRef<T>(array.GetUnsafePtr(), index);
        }
    }
}