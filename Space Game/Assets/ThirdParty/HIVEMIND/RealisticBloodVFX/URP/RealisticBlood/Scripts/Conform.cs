using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
public class DecalConformReverse : MonoBehaviour
{
    public float rayStartOffset = 0.05f;
    public float maxRayDistance = 0.25f;
    public bool makeUniqueMeshInstance = true;
    public bool autoConform = true;

    void OnEnable()
    {
        if (autoConform)
            Conform();
    }

    public void Conform()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (!mf) return;

        Mesh mesh = makeUniqueMeshInstance ? Instantiate(mf.sharedMesh) : mf.sharedMesh;
        Vector3[] verts = mesh.vertices;

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(verts[i]);
            if (Physics.Raycast(worldPos - transform.forward * rayStartOffset,
                                transform.forward,
                                out RaycastHit hit,
                                maxRayDistance))
            {
                verts[i] = transform.InverseTransformPoint(hit.point);
            }
        }

        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mf.mesh = mesh;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(DecalConformReverse))]
    public class DecalConformReverseEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button("Conform"))
            {
                ((DecalConformReverse)target).Conform();
            }
        }
    }
#endif
}
