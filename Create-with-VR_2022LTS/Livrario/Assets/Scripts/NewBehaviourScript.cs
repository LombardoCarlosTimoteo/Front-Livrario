using UnityEngine;

[ExecuteAlways]
public class ShowHandleGizmo : MonoBehaviour
{
    public Color color = Color.cyan;
    public Vector3 size = new Vector3(1f, 0.07f, 0.02f);
    public float frontOffset = 0.01f;
    void OnDrawGizmos()
    {
        Gizmos.color = color;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0, 0, frontOffset), size);
    }
}
