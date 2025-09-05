using System.Collections.Generic;
using UnityEngine;

public class PDFBookViewer : MonoBehaviour
{
    [Header("Páginas (arrastrá los MeshRenderer de los Quads)")]
    public Renderer leftPage;      // Quad izquierdo
    public Renderer rightPage;     // Quad derecho

    [Header("Calidad / Cache")]
    [Tooltip("Lado mayor de la textura de página (1024–2048 recomendado).")]
    public int targetLongSide = 1024;
    [Tooltip("Cantidad de páginas en memoria (LRU).")]
    public int cachePages = 8;

    string path;
    int pageCount;
    int leftIndex;  // índice de la página izquierda (0,2,4...)
    readonly Dictionary<int, Texture2D> cache = new();
    readonly List<int> lruOrder = new();

    void OnEnable()  { OpenFromGlobal(); }
    void OnDisable() { ClearPage(leftPage); ClearPage(rightPage); }

    // Por si querés poner un botón "Recargar"
    public void ReloadFromGlobal() => OpenFromGlobal();

    public void OpenFromGlobal()
    {
        if (GlobalBookStore.I == null)
        {
            Debug.LogWarning("[PDFBookViewer] GlobalBookStore no existe aún.");
            return;
        }

        path = GlobalBookStore.I.GetBestPath();
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("[PDFBookViewer] No hay libro en GlobalBookStore.");
            ClearPage(leftPage); ClearPage(rightPage);
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            pageCount = Mathf.Max(0, PdfRendererAndroid.GetPageCount(path));
            leftIndex = 0;
            Debug.Log($"[PDFBookViewer] Abierto: '{path}'  pages={pageCount}");
            RefreshSpread();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[PDFBookViewer] Error al abrir/renderizar: " + e);
        }
#else
        Debug.Log("[PDFBookViewer] El render real de PDF corre en Android (Quest).");
        // Preview en Editor
        ApplyTexture(leftPage,  Texture2D.whiteTexture);
        ApplyTexture(rightPage, Texture2D.whiteTexture);
#endif
    }

    public void NextSpread()
    {
        if (pageCount <= 0) return;
        int maxLeft = (pageCount % 2 == 0) ? pageCount - 2 : pageCount - 1;
        leftIndex = Mathf.Min(leftIndex + 2, maxLeft);
        RefreshSpread();
    }

    public void PrevSpread()
    {
        if (pageCount <= 0) return;
        leftIndex = Mathf.Max(0, leftIndex - 2);
        RefreshSpread();
    }

    void RefreshSpread()
    {
        SetPage(leftPage, leftIndex);
        SetPage(rightPage, leftIndex + 1);
    }

    void SetPage(Renderer r, int page)
    {
        if (!r) return;
        if (page < 0 || page >= pageCount) { ClearPage(r); return; }

        var tex = FromCache(page);
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!tex) tex = AddToCache(page, PdfRendererAndroid.RenderPage(path, page, targetLongSide));
#else
        if (!tex) tex = Texture2D.whiteTexture;
#endif
        ApplyTexture(r, tex);
    }

    void ApplyTexture(Renderer r, Texture2D tex)
    {
        if (!r || !tex) return;

        // Ajustes de la textura (suavizado y evitar bleeding)
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 2;

        // ⚠️ Usar material INSTANCIADO por renderer (NO sharedMaterial)
        var mat = r.material;
        if (mat != null)
        {
            // URP/Unlit usa _BaseMap, pero mainTexture también sirve
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;

            // Forzar Opaque (evita transparencias raras por ojo)
            if (mat.HasFloat("_Surface")) mat.SetFloat("_Surface", 0f); // 0 = Opaque
            if (mat.HasFloat("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasInt("_CullMode")) mat.SetInt("_CullMode", 0);   // 0=Front; 2=None (depende shader)
                                                                       // Si tu shader expone "_Cull", podrías usar 2=None para ver ambas caras
            if (mat.HasInt("_Cull")) mat.SetInt("_Cull", 2);       // Both (opcional)
        }

        FitAspect(r.transform, tex);
    }

    void ClearPage(Renderer r)
    {
        if (!r) return;
        var mat = r.sharedMaterial;
        if (mat) mat.mainTexture = null;
    }

    void FitAspect(Transform t, Texture2D tex)
    {
        if (!t || !tex) return;
        float aspect = (float)tex.width / tex.height; // alto = 1m, ancho = aspect
        var s = t.localScale; s.x = aspect; s.y = 1f; s.z = 1f; t.localScale = s;
    }

    Texture2D FromCache(int i) => cache.TryGetValue(i, out var tex) ? tex : null;

    Texture2D AddToCache(int i, Texture2D tex)
    {
        if (!tex) return null;
        cache[i] = tex; lruOrder.Add(i);
        if (lruOrder.Count > cachePages)
        {
            int old = lruOrder[0]; lruOrder.RemoveAt(0);
            if (cache.TryGetValue(old, out var tOld) && tOld && tOld != tex) Object.Destroy(tOld);
            cache.Remove(old);
        }
        return tex;
    }
}
