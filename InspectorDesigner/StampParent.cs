#if UNITY_EDITOR
using UnityEngine;

[ExecuteInEditMode]
public class StampParent : MonoBehaviour {
    
    public bool openWindow;

    [Space]
    public GameObject stampPrefab;

    private void OnValidate() {
        if (openWindow) {
            openWindow = false;
            StampPlaceEditor.Init(this);
        }
    }
}
#endif