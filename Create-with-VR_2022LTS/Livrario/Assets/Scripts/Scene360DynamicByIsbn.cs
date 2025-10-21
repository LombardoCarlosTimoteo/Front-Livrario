using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class Scene360DynamicByIsbn : MonoBehaviour
{
    // === Render target PRIMERO para que lo veas en el Inspector ===
    [Header("Render target (elige UNO)")]
    [SerializeField] private MeshRenderer sphereRenderer;       // arrastra aquí la esfera (URP Unlit/Lit, Render Face=Back)
    [SerializeField] private bool applyAsSkybox = false;        // si true, usa Skybox/Panoramic
    [SerializeField] private Material panoramicSkyboxMaterial;  // opcional; si null se crea en runtime

    [Header("Base del CDN/OBS")]
    [SerializeField] private string baseScenesUrl = "https://livrario-books.obs.la-south-2.myhuaweicloud.com/scenes";

    [Header("Opcional: subcarpeta género (vacío si no aplica)")]
    [SerializeField] private string genreSubfolder = ""; // ej: "fantasia" | "scifi" | "policial"

    [Header("Descubrimiento dinámico")]
    [SerializeField] private string manifestFileName = "manifest.json"; // si existe, se usa
    [SerializeField] private string filePrefix = "scene";               // "scene"
    [SerializeField] private string fileExt = ".jpg";                   // ".jpg"
    [SerializeField] private int maxProbe = 30;                      // scene1..scene30
    [SerializeField] private int stopAfterConsecutiveMisses = 4;     // corta si hay N fallos seguidos
    [SerializeField] private float headTimeout = 10f;                  // s

    [Header("Navegación y cache")]
    [SerializeField] private bool cycleWrap = true;
    [SerializeField] private float downloadTimeout = 20f;
    [SerializeField] private bool preloadNext = true;

    [Header("Debug")]
    [SerializeField] private bool overrideIsbn = false;
    [SerializeField] private string debugIsbn = "";                     // para pruebas
    [SerializeField] private bool logVerbose = false;

    // --- internos ---
    string currentIsbn;
    List<string> urls = new List<string>();
    int currentIndex = -1;
    bool isLoading = false;
    HashSet<string> missing = new HashSet<string>();
    string cacheDir;

    [Serializable] class Manifest { public string[] items; public string version; }

    void OnEnable() { if (GlobalBookStore.I != null) GlobalBookStore.I.OnMetadataChanged += OnMetadataChanged; }
    void OnDisable() { if (GlobalBookStore.I != null) GlobalBookStore.I.OnMetadataChanged -= OnMetadataChanged; }

    void Start()
    {
        Application.targetFrameRate = 72; // Quest
        cacheDir = Path.Combine(Application.persistentDataPath, "scenes360");
        Directory.CreateDirectory(cacheDir);
        RebuildFromGlobal();
    }

    void OnValidate()
    {
        // ayuda: si lo pusiste en la misma esfera, autocompleta el MeshRenderer
        if (sphereRenderer == null) sphereRenderer = GetComponent<MeshRenderer>();
    }

    // === Público para UI/XR ===
    public void Next() { if (urls.Count == 0) return; int next = currentIndex + 1; if (next >= urls.Count) next = cycleWrap ? 0 : urls.Count - 1; Show(next); }
    public void Prev() { if (urls.Count == 0) return; int prev = currentIndex - 1; if (prev < 0) prev = cycleWrap ? urls.Count - 1 : 0; Show(prev); }
    public void RefreshNow() => RebuildFromGlobal();

    // === Core ===
    void OnMetadataChanged(GlobalBookStore.BookRecord _) { RebuildFromGlobal(); }

    void RebuildFromGlobal()
    {
        string isbn = overrideIsbn ? (debugIsbn ?? "") : GetIsbnFromGlobal();
        if (string.IsNullOrWhiteSpace(isbn))
        {
            Debug.LogWarning("[Scene360Dynamic] ISBN vacío; no hay escenas para listar.");
            urls.Clear(); currentIndex = -1; return;
        }

        isbn = isbn.Trim();
        if (currentIsbn == isbn && urls.Count > 0) return;

        currentIsbn = isbn;
        missing.Clear(); urls.Clear(); currentIndex = -1;
        StartCoroutine(DiscoverAndShowFirst());
    }

    string GetIsbnFromGlobal()
    {
        if (GlobalBookStore.I == null) return null;
        var v = GlobalBookStore.I.isbn;
        if (!string.IsNullOrWhiteSpace(v)) return v;
        var rec = GlobalBookStore.I.FindCurrentInLibrary();
        return (rec != null && !string.IsNullOrWhiteSpace(rec.isbn)) ? rec.isbn : null;
    }

    IEnumerator DiscoverAndShowFirst()
    {
        // 1) manifest.json si existe
        string manifestUrl = ComposeUrl(currentIsbn, manifestFileName);
        Manifest mf = null;
        using (var req = UnityWebRequest.Get(manifestUrl))
        {
            req.timeout = Mathf.CeilToInt(headTimeout);
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success && !string.IsNullOrEmpty(req.downloadHandler.text))
            { try { mf = JsonUtility.FromJson<Manifest>(req.downloadHandler.text); } catch { mf = null; } }
        }
        if (mf != null && mf.items != null && mf.items.Length > 0)
        {
            foreach (var it in mf.items) if (!string.IsNullOrWhiteSpace(it)) urls.Add(ComposeUrl(currentIsbn, it.Trim()));
        }
        else { yield return StartCoroutine(ProbeFiles()); }

        if (urls.Count == 0) { Debug.LogWarning("[Scene360Dynamic] No hay escenas para ISBN " + currentIsbn); yield break; }
        Show(0);
    }

    IEnumerator ProbeFiles()
    {
        int misses = 0;

        // A) "scene.jpg"
        string bare = filePrefix + fileExt;
        string bareUrl = ComposeUrl(currentIsbn, bare);
        bool okBare = false; yield return StartCoroutine(HeadOrRangeExists(bareUrl, b => okBare = b));
        if (okBare) urls.Add(bareUrl); else misses++;

        // B) "scene1.jpg"... "sceneN.jpg"
        for (int i = 1; i <= maxProbe; i++)
        {
            string name = filePrefix + i + fileExt;
            string url = ComposeUrl(currentIsbn, name);
            bool ok = false; yield return StartCoroutine(HeadOrRangeExists(url, b => ok = b));
            if (ok) { urls.Add(url); misses = 0; }
            else if (++misses >= stopAfterConsecutiveMisses) { if (logVerbose) Debug.Log("[Scene360Dynamic] stop probe por fallos seguidos."); break; }
        }
    }

    string ComposeUrl(string isbn, string fileName)
    {
        return string.IsNullOrWhiteSpace(genreSubfolder)
            ? $"{baseScenesUrl}/{isbn}/{fileName}"
            : $"{baseScenesUrl}/{isbn}/{genreSubfolder}/{fileName}";
    }

    IEnumerator HeadOrRangeExists(string url, Action<bool> cb)
    {
        using (var head = UnityWebRequest.Head(url))
        {
            head.timeout = Mathf.CeilToInt(headTimeout);
            yield return head.SendWebRequest();
            if (head.result == UnityWebRequest.Result.Success) { cb(true); yield break; }
            if ((int)head.responseCode == 405) // sin HEAD → probar GET Range 0-0
            {
                using (var get = UnityWebRequest.Get(url))
                {
                    get.timeout = Mathf.CeilToInt(headTimeout);
                    get.SetRequestHeader("Range", "bytes=0-0");
                    yield return get.SendWebRequest();
                    if (get.result == UnityWebRequest.Result.Success || (int)get.responseCode == 206) { cb(true); yield break; }
                }
            }
        }
        cb(false);
    }

    void Show(int index)
    {
        if (isLoading || urls.Count == 0) return;
        index = Mathf.Clamp(index, 0, urls.Count - 1);
        currentIndex = index;
        StartCoroutine(LoadAndApply(urls[currentIndex], () =>
        {
            if (preloadNext && urls.Count > 1)
            { int nxt = (currentIndex + 1) % urls.Count; StartCoroutine(PreloadToCache(urls[nxt])); }
        }));
    }

    IEnumerator LoadAndApply(string url, Action onDone)
    {
        isLoading = true;
        string path = Path.Combine(cacheDir, SafeFileName(url));

        if (!File.Exists(path))
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = Mathf.CeilToInt(downloadTimeout);
                req.downloadHandler = new DownloadHandlerFile(path);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                { Debug.LogWarning("[Scene360Dynamic] descarga falló: " + req.error + " (" + url + ")"); missing.Add(url); isLoading = false; yield break; }
            }
        }

        var bytes = File.ReadAllBytes(path);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
        tex.LoadImage(bytes, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;

        if (!applyAsSkybox)
        {
            if (sphereRenderer == null || sphereRenderer.sharedMaterial == null)
            { Debug.LogError("[Scene360Dynamic] Asigná el MeshRenderer de la esfera y su material."); isLoading = false; yield break; }

            var mat = sphereRenderer.material; // instancia
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex); else mat.SetTexture("_MainTex", tex);
        }
        else
        {
            if (panoramicSkyboxMaterial == null) panoramicSkyboxMaterial = new Material(Shader.Find("Skybox/Panoramic"));
            panoramicSkyboxMaterial.SetTexture("_MainTex", tex);
            panoramicSkyboxMaterial.SetFloat("_Mapping", 0f);   // equirect
            panoramicSkyboxMaterial.SetFloat("_ImageType", 0f); // 360°
            RenderSettings.skybox = panoramicSkyboxMaterial;
            DynamicGI.UpdateEnvironment();
        }

        if (logVerbose) Debug.Log("[Scene360Dynamic] aplicado: " + url + " (" + tex.width + "x" + tex.height + ")");
        isLoading = false; onDone?.Invoke();
    }

    IEnumerator PreloadToCache(string url)
    {
        if (missing.Contains(url)) yield break;
        string path = Path.Combine(cacheDir, SafeFileName(url));
        if (File.Exists(path)) yield break;

        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.CeilToInt(downloadTimeout);
            req.downloadHandler = new DownloadHandlerFile(path);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            { missing.Add(url); if (logVerbose) Debug.LogWarning("[Scene360Dynamic] prefetch falló: " + req.error + " (" + url + ")"); }
        }
    }

    static string SafeFileName(string url)
    {
        string name = url.Replace("://", "_").Replace("/", "_").Replace("?", "_").Replace("&", "_");
        if (!name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            name += ".jpg";
        return name;
    }

#if UNITY_EDITOR
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow)) Next();
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Prev();
    }
#endif
}
