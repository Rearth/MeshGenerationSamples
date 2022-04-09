using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;


public static class Geometry {
    
    public static float3 V3(float2 v, float y = 0) => new float3(v.x, y, v.y);

    public static void InCenter(float2 p1, float2 p2, float2 p3, out float2 inCenter, out float inRadius) {
        var a = math.distance(p1, p2);
        var b = math.distance(p2, p3);
        var c = math.distance(p3, p1);

        var perimeter = (a + b + c);
        var x = (a * p1.x + b * p2.x + c * p3.x) / perimeter;
        var y = (a * p1.y + b * p2.y + c * p3.y) / perimeter;
        inCenter = new float2(x, y);

        var s = perimeter / 2;
        var triangleArea = math.sqrt(s * (s - a) * (s - b) * (s - c));
        inRadius = triangleArea / s;
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