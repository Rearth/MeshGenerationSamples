using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class AlignWithPlanetHelper : MonoBehaviour {

    public Vector3 planetCenter;

    public bool reload;

    private void OnValidate() {
        if (reload) {
            reload = false;
            var dir = planetCenter - transform.position;
            transform.up = -dir;
        }
    }
}
