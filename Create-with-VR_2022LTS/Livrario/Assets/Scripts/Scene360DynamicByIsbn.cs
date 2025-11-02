using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class Scene360DynamicByIsbn : MonoBehaviour
{
    [Header("Render target (elige UNO)")]
    [SerializeField] private MeshRenderer sphereRenderer;
    [SerializeField] private bool applyAsSkybox = false;
    [SerializeField] private Material panoramicSkyboxMaterial;

    [Header("Base del CDN/OBS (bucket permitido)")]
    [SerializeField] private string baseScenesUrl = "https://livrario-books.obs.la-south-2.myhuaweicloud.com/scenes";
    [SerializeField] private bool strictBucketOnly = true;

    [Header("Opcional: subcarpeta género (vacío si no aplica)")]
    [SerializeField] private string genreSubfolder = "";

    [Header("Descubrimiento dinámico")]
    [SerializeField] private string manifestFileName = "manifest.json";
    [SerializeField] private string filePrefix = "scene";   // scene1, scene2, ...
    [SerializeField] private string fileExt = ".jpg";
    [SerializeField] private int maxProbe = 30;
    [SerializeField] private int stopAfterConsecutiveMisses = 4;
    [SerializeField] private float headTimeout = 10f;

    [Header("Navegación y caché")]
    [SerializeField] private bool cycleWrap = true;
    [SerializeField] private float downloadTimeout = 20f;
    [SerializeField] private bool preloadNext = true;

    [Header("Anti-stale (AGRESIVO)")]
    [SerializeField] private bool alwaysCacheBust = true;         // agrega ?cb=<ticks> a TODO (HEAD/GET/PREFETCH)
    [SerializeField] private bool alwaysRedownload = false;       // si true, borra archivo local ANTES de cada GET
    [SerializeField] private bool validateWithHttpMetadata = true;// usa ETag/Last-Modified para invalidar cache local
    [SerializeField] private bool clearCacheOnIsbnChange = true;  // limpia todos los archivos del ISBN al cambiar
    [SerializeField] private bool rejectIfNotImage = true;        // exige Content-Type image/*
    [SerializeField]
    private List<string> blockedSha256 = new List<string>
    {
        // Agregá aquí hashes de imágenes “default” a bloquear (opcional)
        // "e893586c9d919e017fe985298b5801d922ff721bb7f243b291425785eac5c5f1"
    };

    [Header("Debug/Logging")]
    [SerializeField] private bool overrideIsbn = false;
    [SerializeField] private string debugIsbn = "";
    [SerializeField] private bool logVerbose = true;
    [SerializeField] private bool clearOnFail = true;
    [SerializeField] private bool forceRedownloadOnce = false; // borra la escena actual antes de descargar

    string currentIsbn;
    List<string> urls = new List<string>();
    int currentIndex = -1;
    bool isLoading = false;
    HashSet<string> missing = new HashSet<string>();
    string cacheDir;

    string allowedHost;
    string allowedBasePath;

    [Serializable] class Manifest { public string[] items; public string version; }
    [Serializable] class CacheHttpMeta { public string etag; public string lastModified; }

    void OnEnable() { if (GlobalBookStore.I != null) GlobalBookStore.I.OnMetadataChanged += OnMetadataChanged; }
    void OnDisable() { if (GlobalBookStore.I != null) GlobalBookStore.I.OnMetadataChanged -= OnMetadataChanged; }

    void Start()
    {
        Application.targetFrameRate = 72;
        cacheDir = Path.Combine(Application.persistentDataPath, "scenes360");
        Directory.CreateDirectory(cacheDir);
        NormalizeAllowedBucket();
        RebuildFromGlobal();
    }

    void OnValidate()
    {
        if (sphereRenderer == null) sphereRenderer = GetComponent<MeshRenderer>();
        NormalizeAllowedBucket();
    }

    void NormalizeAllowedBucket()
    {
        try
        {
            var u = new Uri(baseScenesUrl);
            allowedHost = u.Host;
            var p = u.AbsolutePath.TrimEnd('/');
            allowedBasePath = string.IsNullOrEmpty(p) ? "/" : p + "/";
        }
        catch { allowedHost = null; allowedBasePath = null; }
    }

    // === Público para UI/XR ===
    public void Next() { if (urls.Count == 0) return; int next = currentIndex + 1; if (next >= urls.Count) next = cycleWrap ? 0 : urls.Count - 1; Show(next); }
    public void Prev() { if (urls.Count == 0) return; int prev = currentIndex - 1; if (prev < 0) prev = cycleWrap ? urls.Count - 1 : 0; Show(prev); }
    public void RefreshNow() => RebuildFromGlobal();

    [ContextMenu("Force Redownload Current")]
    public void ForceRedownloadCurrent()
    {
        if (currentIndex < 0 || currentIndex >= urls.Count) { Log("ForceRedownload: no current."); return; }
        var url = urls[currentIndex];
        var path = Path.Combine(cacheDir, SafeFileName(url));
        TryDelete(path); TryDelete(MetaPathFor(path));
        Log("ForceRedownload: deleted " + path);
        forceRedownloadOnce = false;
        Show(currentIndex);
    }

    [ContextMenu("Clear ALL 360 Cache")]
    public void ClearScenesCacheAll()
    {
        if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
        Directory.CreateDirectory(cacheDir);
        Debug.Log("[Scene360] Cache LIMPIA: " + cacheDir);
    }

    [ContextMenu("Clear Cache for Current ISBN")]
    public void ClearCacheForCurrentIsbn()
    {
        if (string.IsNullOrWhiteSpace(currentIsbn)) return;
        int count = 0;
        foreach (var f in Directory.GetFiles(cacheDir))
        {
            if (f.Contains("_" + currentIsbn + "_")) { TryDelete(f); TryDelete(MetaPathFor(f)); count++; }
        }
        Debug.Log("[Scene360] Cache limpia para ISBN " + currentIsbn + " (" + count + " archivos)");
    }

    void OnMetadataChanged(GlobalBookStore.BookRecord _) { RebuildFromGlobal(); }

    void RebuildFromGlobal()
    {
        string isbn = overrideIsbn ? (debugIsbn ?? "") : GetIsbnFromGlobal();
        if (string.IsNullOrWhiteSpace(isbn))
        {
            Warn("ISBN vacío; no hay escenas para listar.");
            urls.Clear(); currentIndex = -1; if (clearOnFail) ClearVisual(); return;
        }

        isbn = isbn.Trim();
        bool isbnChanged = currentIsbn != isbn;
        currentIsbn = isbn;

        if (isbnChanged && clearCacheOnIsbnChange) ClearCacheForCurrentIsbn();

        missing.Clear(); urls.Clear(); currentIndex = -1;
        if (clearOnFail) ClearVisual();
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

    bool IsNumberedFile(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!name.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (!name.EndsWith(fileExt, StringComparison.OrdinalIgnoreCase)) return false;
        int start = filePrefix.Length;
        int len = name.Length - start - fileExt.Length;
        if (len <= 0) return false;
        for (int i = 0; i < len; i++) if (!char.IsDigit(name[start + i])) return false;
        return true;
    }

    bool IsFromAllowedBucket(string url)
    {
        if (!strictBucketOnly) return true;
        try
        {
            var u = new Uri(url);
            if (!string.Equals(u.Host, allowedHost, StringComparison.OrdinalIgnoreCase)) return false;
            var ap = u.AbsolutePath;
            if (!string.IsNullOrEmpty(allowedBasePath) && !ap.StartsWith(allowedBasePath, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
        catch { return false; }
    }

    IEnumerator DiscoverAndShowFirst()
    {
        string manifestUrl = ComposeUrl(currentIsbn, manifestFileName);
        Log($"DISCOVER: ISBN={currentIsbn} manifest={manifestUrl}");

        Manifest mf = null;
        using (var req = UnityWebRequest.Get(DecorateUrl(manifestUrl)))
        {
            ApplyNoCacheHeaders(req);
            req.timeout = Mathf.CeilToInt(headTimeout);
            yield return req.SendWebRequest();
            LogHttp("MANIFEST", req);
            if (req.result == UnityWebRequest.Result.Success && !string.IsNullOrEmpty(req.downloadHandler.text))
            { try { mf = JsonUtility.FromJson<Manifest>(req.downloadHandler.text); } catch (Exception e) { Warn("Manifest parse fail: " + e.Message); mf = null; } }
        }

        if (mf != null && mf.items != null && mf.items.Length > 0)
        {
            foreach (var it in mf.items)
            {
                var name = (it ?? "").Trim();
                if (IsNumberedFile(name))
                {
                    var u = ComposeUrl(currentIsbn, name);
                    if (IsFromAllowedBucket(u)) urls.Add(u); else Warn("Descartado (fuera de bucket): " + u);
                }
            }
        }
        else
        {
            yield return StartCoroutine(ProbeFiles());
        }

        if (urls.Count == 0)
        {
            Warn("No hay escenas válidas en el bucket para ISBN " + currentIsbn);
            if (clearOnFail) ClearVisual();
            yield break;
        }

        for (int i = 0; i < urls.Count; i++) Log($"ORDER[{i}] {urls[i]}");
        Show(0);
    }

    IEnumerator ProbeFiles()
    {
        int misses = 0;

        for (int i = 1; i <= maxProbe; i++)
        {
            string name = filePrefix + i + fileExt;
            string url = ComposeUrl(currentIsbn, name);
            if (!IsFromAllowedBucket(url)) { Warn("Descartado (fuera de bucket): " + url); continue; }

            bool ok = false;
            yield return StartCoroutine(HeadOrRangeExists(url, b => ok = b));

            if (ok) { urls.Add(url); misses = 0; }
            else
            {
                misses++;
                if (misses >= stopAfterConsecutiveMisses)
                { Log($"Probe stop at {i} por {misses} fallos seguidos"); break; }
            }
        }
    }

    string ComposeUrl(string isbn, string fileName)
    {
        return string.IsNullOrWhiteSpace(genreSubfolder)
            ? $"{baseScenesUrl}/{isbn}/{fileName}"
            : $"{baseScenesUrl}/{isbn}/{genreSubfolder}/{fileName}";
    }

    string DecorateUrl(string url)
    {
        if (!alwaysCacheBust) return url;
        var sep = url.Contains("?") ? "&" : "?";
        return url + sep + "cb=" + DateTime.UtcNow.Ticks;
    }

    void ApplyNoCacheHeaders(UnityWebRequest req)
    {
        req.SetRequestHeader("Cache-Control", "no-cache, no-store, must-revalidate");
        req.SetRequestHeader("Pragma", "no-cache");
    }

    IEnumerator HeadOrRangeExists(string url, Action<bool> cb)
    {
        string u = DecorateUrl(url);
        Log("HEAD " + u);
        using (var head = UnityWebRequest.Head(u))
        {
            ApplyNoCacheHeaders(head);
            head.timeout = Mathf.CeilToInt(headTimeout);
            yield return head.SendWebRequest();
            LogHttp("HEAD", head);

            if (head.result == UnityWebRequest.Result.Success) { cb(true); yield break; }

            int code = (int)head.responseCode;
            if (code == 405) // Method Not Allowed → GET Range 0-0
            {
                u = DecorateUrl(url);
                Log("GET (Range 0-0) " + u);
                using (var get = UnityWebRequest.Get(u))
                {
                    ApplyNoCacheHeaders(get);
                    get.timeout = Mathf.CeilToInt(headTimeout);
                    get.SetRequestHeader("Range", "bytes=0-0");
                    yield return get.SendWebRequest();
                    LogHttp("RANGE", get);
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
        Log($"SHOW index={index} url={urls[index]}");
        StartCoroutine(LoadAndApply(urls[currentIndex], () =>
        {
            if (preloadNext && urls.Count > 1)
            {
                int nxt = (currentIndex + 1) % urls.Count;
                StartCoroutine(PreloadToCache(urls[nxt]));
            }
        }));
    }

    IEnumerator LoadAndApply(string url, Action onDone)
    {
        if (!IsFromAllowedBucket(url))
        {
            Warn("BLOQUEADA URL fuera de bucket: " + url);
            if (clearOnFail) ClearVisual();
            yield break;
        }

        isLoading = true;
        string path = Path.Combine(cacheDir, SafeFileName(url));
        string metaPath = MetaPathFor(path);

        // Forzar redescarga manual/siempre
        if (forceRedownloadOnce || alwaysRedownload)
        {
            TryDelete(path);
            TryDelete(metaPath);
            Log((forceRedownloadOnce ? "ForceRedownloadOnce" : "AlwaysRedownload") + ": deleted " + path);
            forceRedownloadOnce = false;
        }

        bool fromCache = File.Exists(path);
        bool needRedownload = false;
        string reqUrl = DecorateUrl(url); // cache-buster SIEMPRE

        // Validación con metadata si hay archivo cacheado
        if (fromCache && validateWithHttpMetadata)
        {
            var local = LoadMeta(metaPath);
            using (var head = UnityWebRequest.Head(DecorateUrl(url)))
            {
                ApplyNoCacheHeaders(head);
                head.timeout = Mathf.CeilToInt(headTimeout);
                yield return head.SendWebRequest();
                LogHttp("HEAD", head);

                if (head.result == UnityWebRequest.Result.Success)
                {
                    var hdr = head.GetResponseHeaders();
                    var etag = GetHeader(hdr, "ETag");
                    var lm = GetHeader(hdr, "Last-Modified");

                    if ((local != null && !string.IsNullOrEmpty(local.etag) && etag != null && local.etag != etag) ||
                        (local != null && !string.IsNullOrEmpty(local.lastModified) && lm != null && local.lastModified != lm))
                    {
                        Log($"CACHE INVALIDADA por metadata. etag local={local?.etag} srv={etag} lm local={local?.lastModified} srv={lm}");
                        TryDelete(path); TryDelete(metaPath);
                        fromCache = false; needRedownload = true;
                    }
                }
                else if ((int)head.responseCode == 404)
                {
                    Log("HEAD 404: archivo ya no existe en bucket. Se borra caché.");
                    TryDelete(path); TryDelete(metaPath);
                    fromCache = false; needRedownload = false;
                }
            }
        }

        // Descargar si hace falta
        if (!fromCache)
        {
            Log("GET " + reqUrl + " -> " + path);
            using (var req = UnityWebRequest.Get(reqUrl))
            {
                ApplyNoCacheHeaders(req);
                req.timeout = Mathf.CeilToInt(downloadTimeout);
                req.downloadHandler = new DownloadHandlerFile(path);
                yield return req.SendWebRequest();
                LogHttp("GET", req);

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Warn("Descarga falló: " + req.error + " (" + reqUrl + ")");
                    missing.Add(url); isLoading = false;
                    if (clearOnFail) ClearVisual();
                    yield break;
                }

                // Validación de Content-Type (si el server lo envía)
                if (rejectIfNotImage)
                {
                    var hdr = req.GetResponseHeaders();
                    var ct = GetHeader(hdr, "Content-Type");
                    if (ct != null && !ct.ToLowerInvariant().StartsWith("image/"))
                    {
                        Warn("Contenido no imagen (" + ct + "), descartado.");
                        TryDelete(path); TryDelete(metaPath);
                        isLoading = false; if (clearOnFail) ClearVisual(); yield break;
                    }
                    SaveMeta(metaPath, GetHeader(hdr, "ETag"), GetHeader(hdr, "Last-Modified"));
                }
            }
        }
        else
        {
            Log("CACHE " + path);
        }

        // Cargar textura desde disco
        byte[] bytes = null;
        try { bytes = File.ReadAllBytes(path); } catch (Exception e) { Warn("ReadAllBytes fail " + e.Message + " path=" + path); }

        if (bytes == null || bytes.Length == 0)
        {
            Warn("Archivo vacío " + path);
            TryDelete(path); TryDelete(metaPath);
            isLoading = false; if (clearOnFail) ClearVisual(); yield break;
        }

        string sha = Sha256Hex(bytes);
        Log($"FILE ok size={bytes.Length}B sha256={sha}");

        // Bloqueo por lista negra de hashes (opcional)
        if (blockedSha256 != null && blockedSha256.Count > 0)
        {
            foreach (var bad in blockedSha256)
            {
                if (!string.IsNullOrWhiteSpace(bad) &&
                    string.Equals(bad.Trim(), sha, StringComparison.OrdinalIgnoreCase))
                {
                    Warn("Hash bloqueado detectado. Eliminando y reintentando con nuevo cache-buster.");
                    TryDelete(path); TryDelete(metaPath);
                    // Reintento una vez con otro cb
                    isLoading = false; forceRedownloadOnce = true; Show(currentIndex);
                    yield break;
                }
            }
        }

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
        bool okImg = false;
        try { okImg = tex.LoadImage(bytes, true); } catch (Exception e) { Warn("LoadImage ex " + e.Message); }

        if (!okImg || tex.width < 16 || tex.height < 16)
        {
            Warn($"Imagen inválida: ok={okImg} size={tex.width}x{tex.height}");
            TryDelete(path); TryDelete(metaPath);
            // Reintento duro con nuevo cb
            isLoading = false; forceRedownloadOnce = true; Show(currentIndex);
            yield break;
        }

        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;

        if (!applyAsSkybox)
        {
            if (sphereRenderer == null || sphereRenderer.sharedMaterial == null)
            { Warn("MeshRenderer/material no asignado."); isLoading = false; if (clearOnFail) ClearVisual(); yield break; }

            var mat = sphereRenderer.material; // instancia
            string prop = mat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            mat.SetTexture(prop, tex);
            Log($"APPLIED to sphere prop={prop} tex={tex.width}x{tex.height}");
        }
        else
        {
            if (panoramicSkyboxMaterial == null) panoramicSkyboxMaterial = new Material(Shader.Find("Skybox/Panoramic"));
            panoramicSkyboxMaterial.SetTexture("_MainTex", tex);
            panoramicSkyboxMaterial.SetFloat("_Mapping", 0f);   // equirect
            panoramicSkyboxMaterial.SetFloat("_ImageType", 0f); // 360°
            RenderSettings.skybox = panoramicSkyboxMaterial;
            DynamicGI.UpdateEnvironment();
            Log($"APPLIED to skybox tex={tex.width}x{tex.height}");
        }

        isLoading = false; onDone?.Invoke();
    }

    IEnumerator PreloadToCache(string url)
    {
        if (missing.Contains(url)) yield break;
        if (!IsFromAllowedBucket(url)) yield break;

        string reqUrl = DecorateUrl(url);
        string path = Path.Combine(cacheDir, SafeFileName(url));
        if (File.Exists(path))
        {
            if (alwaysRedownload) { TryDelete(path); TryDelete(MetaPathFor(path)); }
            else yield break;
        }

        Log("PREFETCH " + reqUrl + " -> " + path);
        using (var req = UnityWebRequest.Get(reqUrl))
        {
            ApplyNoCacheHeaders(req);
            req.timeout = Mathf.CeilToInt(downloadTimeout);
            req.downloadHandler = new DownloadHandlerFile(path);
            yield return req.SendWebRequest();
            LogHttp("PREFETCH", req);
            if (req.result != UnityWebRequest.Result.Success)
            { missing.Add(url); Warn("Prefetch falló: " + req.error + " (" + reqUrl + ")"); }
        }
    }

    // ==== Utils ====
    static string SafeFileName(string url)
    {
        // Incluir query string en el nombre para diferenciar si alguna vez decidís persistir cb
        string name = url.Replace("://", "_").Replace("/", "_").Replace("?", "_").Replace("&", "_").Replace("=", "-");
        if (!name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            name += ".jpg";
        return name;
    }

    string MetaPathFor(string dataPath) => dataPath + ".httpmeta";

    CacheHttpMeta LoadMeta(string metaPath)
    {
        try { if (File.Exists(metaPath)) return JsonUtility.FromJson<CacheHttpMeta>(File.ReadAllText(metaPath)); }
        catch { }
        return null;
    }

    void SaveMeta(string metaPath, string etag, string lastModified)
    {
        try
        {
            var m = new CacheHttpMeta { etag = etag, lastModified = lastModified };
            File.WriteAllText(metaPath, JsonUtility.ToJson(m));
        }
        catch { }
    }

    string GetHeader(Dictionary<string, string> h, string k)
    {
        if (h == null) return null;
        if (h.TryGetValue(k, out var v)) return v;
        var kl = k.ToLowerInvariant();
        if (h.TryGetValue(kl, out var v2)) return v2;
        foreach (var kv in h) if (string.Equals(kv.Key, k, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    void ClearVisual()
    {
        if (!applyAsSkybox)
        {
            var mat = sphereRenderer ? sphereRenderer.material : null;
            if (mat != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
                else mat.SetTexture("_MainTex", null);
            }
        }
        else
        {
            RenderSettings.skybox = null;
            DynamicGI.UpdateEnvironment();
        }
    }

    void Log(string msg) { if (logVerbose) Debug.Log("[Scene360] " + msg); }
    void Warn(string msg) { Debug.LogWarning("[Scene360] " + msg); }

    void LogHttp(string tag, UnityWebRequest req)
    {
        if (!logVerbose) return;
        var sb = new StringBuilder();
        sb.Append("[Scene360] ").Append(tag)
          .Append(" code=").Append((int)req.responseCode)
          .Append(" result=").Append(req.result)
          .Append(" url=").Append(req.url);

        var headers = req.GetResponseHeaders();
        if (headers != null)
        {
            if (headers.TryGetValue("content-type", out var ct)) sb.Append(" ct=").Append(ct);
            if (headers.TryGetValue("content-length", out var cl)) sb.Append(" len=").Append(cl);
            if (headers.TryGetValue("etag", out var et)) sb.Append(" etag=").Append(et);
            if (headers.TryGetValue("last-modified", out var lm)) sb.Append(" lm=").Append(lm);
        }
        Debug.Log(sb.ToString());
    }

    static string Sha256Hex(byte[] data)
    {
        using (var sha = SHA256.Create())
        {
            var hash = sha.ComputeHash(data);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }

    void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) { Warn("No se pudo borrar " + path + " - " + e.Message); }
    }

#if UNITY_EDITOR
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow)) Next();
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Prev();
    }
#endif
}
