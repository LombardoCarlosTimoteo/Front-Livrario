using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class Scene360Cycler : MonoBehaviour
{
    [Header("URLs de prueba (se pueden completar desde el Inspector)")]
    public string[] urls = new string[] {
        "https://livrario-books.obs.la-south-2.myhuaweicloud.com/scenes/123412341234/scene1/scene.jpg",
        "https://livrario-books.obs.la-south-2.myhuaweicloud.com/scenes/123412341234/scene2/scene2.jpg",
        "https://livrario-books.obs.la-south-2.myhuaweicloud.com/scenes/123412341234/scene3/scene3.jpg"
    };

    [Header("Target de render (elige UNO):")]
    public MeshRenderer sphereRenderer;        // Tu esfera con material Unlit/Lit (Render Face = Back)
    public bool applyAsSkybox = false;         // Si tildas esto, ignora sphereRenderer y usa Skybox/Panoramic
    public Material panoramicSkyboxMaterial;   // Si se deja vacío y applyAsSkybox= true, se crea en runtime

    [Header("Opciones")]
    public int startIndex = 0;
    public bool cycleWrap = true;              // true → al final vuelve al inicio
    public float downloadTimeout = 20f;        // segundos
    public bool preloadNext = true;            // predescarga la siguiente para que el cambio sea instantáneo

    // Internos
    int current = -1;
    bool isLoading = false;

    void Start()
    {
        if ((urls == null) || urls.Length == 0)
        {
            Debug.LogError("[Scene360Cycler] No hay URLs configuradas.");
            return;
        }
        startIndex = Mathf.Clamp(startIndex, 0, urls.Length - 1);

        // Si querés arrancar con una específica:
        Show(startIndex);
    }

    // Conectar estos dos a botones UI/XR (OnClick)
    public void Next()
    {
        if (urls == null || urls.Length == 0) return;
        int next = current + 1;
        if (next >= urls.Length) next = cycleWrap ? 0 : urls.Length - 1;
        Show(next);
    }

    public void Prev()
    {
        if (urls == null || urls.Length == 0) return;
        int prev = current - 1;
        if (prev < 0) prev = cycleWrap ? urls.Length - 1 : 0;
        Show(prev);
    }

    public void Show(int index)
    {
        if (isLoading) return;
        index = Mathf.Clamp(index, 0, urls.Length - 1);
        current = index;
        StartCoroutine(LoadAndApply(urls[current], onDone: () => {
            if (preloadNext && urls.Length > 1)
            {
                int nxt = (current + 1) % urls.Length;
                // Arranca una predescarga silenciosa
                StartCoroutine(PreloadToCache(urls[nxt]));
            }
        }));
    }

    IEnumerator LoadAndApply(string url, Action onDone = null)
    {
        isLoading = true;

        // 1) Asegurar carpeta de caché
        string cacheDir = Path.Combine(Application.persistentDataPath, "scenes360");
        Directory.CreateDirectory(cacheDir);
        string fileName = SafeFileName(url);
        string localPath = Path.Combine(cacheDir, fileName);

        // 2) Descargar si no existe
        if (!File.Exists(localPath))
        {
            using var req = new UnityWebRequest(url, "GET");
            req.timeout = Mathf.CeilToInt(downloadTimeout);
            req.downloadHandler = new DownloadHandlerFile(localPath);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Scene360Cycler] Error descargando {url}: {req.error}");
                isLoading = false;
                yield break;
            }
        }

        // 3) Cargar textura desde disco (sin alocar RAM extra)
        byte[] bytes = File.ReadAllBytes(localPath);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
        tex.LoadImage(bytes, markNonReadable: true);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;

        // 4) Aplicar a esfera o Skybox
        if (!applyAsSkybox)
        {
            if (sphereRenderer == null || sphereRenderer.sharedMaterial == null)
            {
                Debug.LogError("[Scene360Cycler] Asigná el MeshRenderer de tu esfera y su material.");
                isLoading = false;
                yield break;
            }

            var mat = sphereRenderer.material; // instancia
            // URP Lit/Unlit usan _BaseMap; shaders legacy usan _MainTex
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            else mat.SetTexture("_MainTex", tex);
        }
        else
        {
            if (panoramicSkyboxMaterial == null)
                panoramicSkyboxMaterial = new Material(Shader.Find("Skybox/Panoramic"));

            panoramicSkyboxMaterial.SetTexture("_MainTex", tex);
            panoramicSkyboxMaterial.SetFloat("_Mapping", 0f);   // 0 = Latitude-Longitude (equirect)
            panoramicSkyboxMaterial.SetFloat("_ImageType", 0f); // 0 = 360°
            RenderSettings.skybox = panoramicSkyboxMaterial;
            DynamicGI.UpdateEnvironment();
        }

        Debug.Log($"[Scene360Cycler] Aplicado: {url} → {localPath} ({tex.width}x{tex.height})");

        isLoading = false;
        onDone?.Invoke();
    }

    IEnumerator PreloadToCache(string url)
    {
        string cacheDir = Path.Combine(Application.persistentDataPath, "scenes360");
        Directory.CreateDirectory(cacheDir);
        string localPath = Path.Combine(cacheDir, SafeFileName(url));
        if (File.Exists(localPath)) yield break;

        using var req = new UnityWebRequest(url, "GET");
        req.timeout = Mathf.CeilToInt(downloadTimeout);
        req.downloadHandler = new DownloadHandlerFile(localPath);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
        {
            // No romper UX por un fallo de prefetch
            Debug.LogWarning($"[Scene360Cycler] Prefetch falló: {req.error} ({url})");
        }
    }

    string SafeFileName(string url)
    {
        string name = url.Replace("://", "_").Replace("/", "_").Replace("?", "_").Replace("&", "_");
        if (!name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            name += ".jpg";
        return name;
    }

    // Atajos para probar en Editor (opcional)
#if UNITY_EDITOR
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow)) Next();
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Prev();
    }
#endif
}
