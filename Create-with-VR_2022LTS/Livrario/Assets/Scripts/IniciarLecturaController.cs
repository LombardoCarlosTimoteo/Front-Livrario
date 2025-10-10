using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using TMPro;            // TextMeshPro
using UnityEngine.UI;  // UI legacy

public class IniciarLecturaController : MonoBehaviour
{
    [Header("Endpoint (usar HTTPS del ingest)")]
    public string baseUrl = "https://ingest.nicolasirigoyen.com.ar";
    public string ensurePath = "/book/ensure";
    [Tooltip("Timeout de la request en segundos")]
    public int timeoutSeconds = 30;

    [Header("UI Inputs (opcionales)")]
    public TMP_InputField InputTituloLibroTMP;
    public TMP_InputField InputAutorTMP;
    public InputField InputTituloLibro;
    public InputField InputAutor;

    [Header("Salida (opcional)")]
    [Tooltip("Si lo asignás, muestra el JSON crudo y el resumen en pantalla.")]
    public TMP_Text responseText;

    [Header("Fallbacks si inputs vacíos")]
    public string fallbackTitle = "Libro";
    public string fallbackAuthor = "";

    [Header("Escenas por género")]
    public string escenaPolicial = "Room_Policial";      // fallback por error
    public string escenaCienciaFiccion = "Room_cienciaFiccion";
    public string escenaFantasia = "Room_Fantasia2";
    public string escenaDefault = "Room_cienciaFiccion";       // si hay respuesta pero sin géneros soportados
    [Header("Loading UI")]
    public GameObject LoadingCanvasRoot;   // ← Canvas con “Cargando…”
    public GameObject MenuCanvasRoot;      // ← Canvas del menú
    public float minLoadingSeconds = 5f;   // ← mínimo que debe verse el loading

    [System.Serializable]
    public class BookEnsureResponse
    {
        public string title;
        public string author;
        public string isbn;
        public string[] genres;
    }

    // Hookeá este método al OnClick de BtnIniciarLectura
    public void OnBtnIniciarLectura()
    {
        // Mostrar loading y ocultar menú inmediatamente
        if (LoadingCanvasRoot) LoadingCanvasRoot.SetActive(true);
        if (MenuCanvasRoot) MenuCanvasRoot.SetActive(false);

        // Lanzar la rutina principal con “mínimo 5s de loading”
        StartCoroutine(SendAndTeleport_WithMinDelay());
    }

    IEnumerator WaitMinFrom(float t0)
    {
        float elapsed = Time.realtimeSinceStartup - t0;
        float left = minLoadingSeconds - elapsed;
        if (left > 0f) yield return new WaitForSeconds(left);
    }
    IEnumerator SendAndTeleport_WithMinDelay()
    {
        float t0 = Time.realtimeSinceStartup;

        // 0) Validaciones y datos base
        if (GlobalBookStore.I == null)
        {
            Debug.LogWarning("[BtnIniciarLectura] GlobalBookStore no está en la escena. Fallback a Policial.");
            yield return WaitMinFrom(t0);
            LoadPoliceFallback();
            yield break;
        }

        string pdfPath = GlobalBookStore.I.GetBestPath();
        if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
        {
            Debug.LogWarning("[BtnIniciarLectura] No hay PDF válido. Fallback a Policial.");
            yield return WaitMinFrom(t0);
            LoadPoliceFallback();
            yield break;
        }

        // 1) Título/Autor desde UI (con fallback) y persistir
        string title = ReadInputSafely(InputTituloLibroTMP, InputTituloLibro);
        if (string.IsNullOrWhiteSpace(title))
            title = !string.IsNullOrEmpty(GlobalBookStore.I.FileName)
                        ? Path.GetFileNameWithoutExtension(GlobalBookStore.I.FileName)
                        : fallbackTitle;

        string author = ReadInputSafely(InputAutorTMP, InputAutor);
        if (string.IsNullOrWhiteSpace(author)) author = fallbackAuthor;

        GlobalBookStore.I.UpdateWithServerResponse(title, author, null);

        // 2) Preparar POST multipart
        string url = baseUrl.TrimEnd('/') + ensurePath;
        byte[] pdfBytes = null;
        try { pdfBytes = File.ReadAllBytes(pdfPath); }
        catch (System.Exception e)
        {
            Debug.LogWarning("[BtnIniciarLectura] No se pudo leer el PDF, continuo igual. " + e.Message);
        }

        var form = new WWWForm();
        form.AddField("title", title);
        form.AddField("author", author);
        if (pdfBytes != null)
            form.AddBinaryData("pdf", pdfBytes, Path.GetFileName(pdfPath), "application/pdf");

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.timeout = Mathf.Max(5, timeoutSeconds);
            req.SetRequestHeader("Accept", "application/json");
            Debug.Log($"[Ingest] POST {url}  file={pdfPath}  title='{title}' author='{author}'");

            yield return req.SendWebRequest();

            // 3) FALLBACK si error/timeout
            if (req.result != UnityWebRequest.Result.Success || req.responseCode < 200 || req.responseCode >= 300)
            {
                Debug.LogWarning($"[Ingest] HTTP {req.responseCode} err='{req.error}'. Fallback a Policial.");
                yield return WaitMinFrom(t0);
                LoadPoliceFallback();
                yield break;
            }

            // 4) Parsear JSON (sin yield en catch)
            string json = req.downloadHandler.text;
            Debug.Log($"[Ingest] HTTP {req.responseCode} | {json}");

            BookEnsureResponse resp = null;
            bool parseOk = true;
            try
            {
                resp = JsonUtility.FromJson<BookEnsureResponse>(json);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Ingest] JSON inválido. " + ex.Message);
                parseOk = false;
            }

            if (!parseOk)
            {
                yield return WaitMinFrom(t0);
                LoadPoliceFallback();
                yield break;
            }

            // 5) Actualizar store con géneros confirmados
            GlobalBookStore.I.UpdateWithServerResponse(resp?.title, resp?.author, resp?.genres);

            // 6) Elegir escena
            string scene = EscenaPorPrimerGeneroSoportado(resp?.genres);
            if (string.IsNullOrEmpty(scene)) scene = escenaDefault;

            // 7) Respetar mínimo de loading antes de cambiar de escena
            yield return WaitMinFrom(t0);

            Debug.Log("[Ingest] Cargando escena: " + scene);
            SceneManager.LoadScene(scene);
        }
    }


    IEnumerator SendAndTeleport()
    {
        // 0) Validaciones y datos base
        if (GlobalBookStore.I == null)
        {
            LogToUI("[BtnIniciarLectura] GlobalBookStore no está en la escena. Fallback a Policial.");
            LoadPoliceFallback();
            yield break;
        }

        string pdfPath = GlobalBookStore.I.GetBestPath();
        if (string.IsNullOrEmpty(pdfPath))
        {
            LogToUI("[BtnIniciarLectura] No hay PDF. Fallback a Policial.");
            LoadPoliceFallback();
            yield break;
        }

        // 1) Título/Autor desde UI (con fallback) y guardarlos YA en el store
        string title = ReadInputSafely(InputTituloLibroTMP, InputTituloLibro);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = !string.IsNullOrEmpty(GlobalBookStore.I.FileName)
                        ? Path.GetFileNameWithoutExtension(GlobalBookStore.I.FileName)
                        : fallbackTitle;
        }
        string author = ReadInputSafely(InputAutorTMP, InputAutor);
        if (string.IsNullOrWhiteSpace(author)) author = fallbackAuthor;

        GlobalBookStore.I.UpdateWithServerResponse(title, author, null);

        // 2) Preparar POST multipart
        string url = baseUrl.TrimEnd('/') + ensurePath;
        byte[] pdfBytes = TryReadPdfBytes(pdfPath); // maneja errores y content:// si es posible

        var form = new WWWForm();
        form.AddField("title", title);
        form.AddField("author", author);
        if (pdfBytes != null)
        {
            form.AddBinaryData("pdf", pdfBytes, Path.GetFileName(pdfPath), "application/pdf");
        }
        else
        {
            LogToUI($"[BtnIniciarLectura] Advertencia: no pude leer bytes del PDF en '{pdfPath}'. Envío sin archivo.");
        }

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.timeout = Mathf.Max(5, timeoutSeconds);
            req.SetRequestHeader("Accept", "application/json");
            req.downloadHandler = new DownloadHandlerBuffer();
            Debug.Log($"[BtnIniciarLectura] POST {url}  file={pdfPath}  title='{title}' author='{author}'");

            yield return req.SendWebRequest();

            string body = req.downloadHandler != null ? req.downloadHandler.text : "";
            int code = (int)req.responseCode;

            // --- IMPRIMIR SIEMPRE LO QUE RESPONDE ---
            Debug.Log($"[Ingest] HTTP {code}\n{body}");
            LogToUI($"HTTP {code}\n{Pretty(body)}");
            var headers = req.GetResponseHeaders();
            string headersStr = HeadersToString(headers);
            // 1) Un log de una sola línea (útil en logcat)
            Debug.Log($"[Ingest] HTTP {code} | {TrimForLog(OneLine(body), 2000)}");

            // 2) Un log multilínea con headers + cuerpo (útil en Editor)
            Debug.Log($"[Ingest] HTTP {code}\n{headersStr}\n{body}");

            // 3) (opcional) mostrar en pantalla si asignaste responseText en el Inspector
            LogToUI($"HTTP {code}\n{headersStr}\n{Pretty(body)}");
            // 3) FALLBACK si error/timeout
            if (req.result != UnityWebRequest.Result.Success || code < 200 || code >= 300)
            {
                Debug.LogWarning($"[BtnIniciarLectura] Error HTTP/timeout. code={code} err='{req.error}'. Fallback a Policial.");
                LoadPoliceFallback();
                yield break;
            }

            // 4) Parsear JSON (si se puede)
            BookEnsureResponse resp = null;
            try { resp = JsonUtility.FromJson<BookEnsureResponse>(body); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BtnIniciarLectura] JSON inválido. " + e.Message + " Fallback a Policial.");
                LoadPoliceFallback();
                yield break;
            }

            // 5) Actualizar store con géneros confirmados (si los hay)
            GlobalBookStore.I.UpdateWithServerResponse(resp?.title, resp?.author, resp?.genres);

            // 6) Elegir escena por PRIMERA coincidencia soportada
            string scene = EscenaPorPrimerGeneroSoportado(resp?.genres);
            if (string.IsNullOrEmpty(scene)) scene = escenaDefault;

            Debug.Log("[BtnIniciarLectura] Cargando escena: " + scene);
            SceneManager.LoadScene(scene);
        }
    }
    static string OneLine(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("\r", "").Replace("\n", " ");
    static string TrimForLog(string s, int max) => (s != null && s.Length > max) ? s.Substring(0, max) + " ...[trimmed]" : (s ?? "");

    static string HeadersToString(System.Collections.Generic.Dictionary<string, string> dict)
    {
        if (dict == null || dict.Count == 0) return "(sin headers)";
        var sb = new StringBuilder();
        foreach (var kv in dict) sb.AppendLine($"{kv.Key}: {kv.Value}");
        return sb.ToString();
    }


    // --- Fallback sólido ---
    void LoadPoliceFallback()
    {
        SceneManager.LoadScene(escenaPolicial);
    }

    // === Lógica de selección: respeta el ORDEN devuelto por el backend ===
    string EscenaPorPrimerGeneroSoportado(string[] genres)
    {
        if (genres == null || genres.Length == 0) return null;
        foreach (var g in genres)
        {
            string s = Normalizar(g);
            if (EsFantasia(s)) return escenaFantasia;
            if (EsCienciaFiccion(s)) return escenaCienciaFiccion;
            if (EsPolicial(s)) return escenaPolicial;
        }
        return null;
    }

    // --- Matchers tolerantes ---
    static bool EsPolicial(string s) =>
        s.Contains("policial") || s.Contains("thriller") || s.Contains("detectiv") ||
        s.Contains("crime") || s.Contains("misterio") || s.Contains("mystery");

    static bool EsCienciaFiccion(string s) =>
        s.Contains("ciencia fic") || s.Contains("science fiction") || s.Contains("sci-fi") ||
        s.Contains("scifi") || s.Contains("sci fi");

    static bool EsFantasia(string s) => s.Contains("fantas");

    // Lee de TMP o de UI legacy
    static string ReadInputSafely(TMP_InputField tmp, InputField legacy)
    {
        if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return tmp.text.Trim();
        if (legacy != null && !string.IsNullOrEmpty(legacy.text)) return legacy.text.Trim();
        return string.Empty;
    }

    // Normaliza (minúsculas, sin tildes)
    static string Normalizar(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        string lower = input.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(lower.Length);
        foreach (char c in lower)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // --- Utilidades ---
    void LogToUI(string msg)
    {
        Debug.Log(msg);
        if (responseText != null) responseText.text = msg;
    }

    static string Pretty(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var sb = new StringBuilder(json.Length + 128);
        int indent = 0; bool quoted = false; char last = '\0';
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (c == '"' && last != '\\') quoted = !quoted;

            if (!quoted)
            {
                if (c == '{' || c == '[')
                {
                    sb.Append(c).Append('\n');
                    indent++;
                    sb.Append(' ', indent * 2);
                    last = c; continue;
                }
                if (c == '}' || c == ']')
                {
                    sb.Append('\n');
                    indent = Mathf.Max(0, indent - 1);
                    sb.Append(' ', indent * 2).Append(c);
                    last = c; continue;
                }
                if (c == ',')
                {
                    sb.Append(c).Append('\n').Append(' ', indent * 2);
                    last = c; continue;
                }
                if (c == ':')
                {
                    sb.Append(": ");
                    last = c; continue;
                }
            }
            sb.Append(c);
            last = c;
        }
        return sb.ToString();
    }

    static byte[] TryReadPdfBytes(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        // Si es ruta de archivo, intentá File.ReadAllBytes
        if (!path.StartsWith("content://"))
        {
            try { return File.ReadAllBytes(path); } catch { return null; }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // content:// en Android (si no tenés copia local)
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var resolver    = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var uriCls      = new AndroidJavaClass("android.net.Uri"))
            using (var uri         = uriCls.CallStatic<AndroidJavaObject>("parse", path))
            using (var stream      = resolver.Call<AndroidJavaObject>("openInputStream", uri))
            {
                if (stream == null) return null;
                // Leer a un MemoryStream .NET
                using (var ms = new MemoryStream())
                {
                    byte[] buffer = new byte[16 * 1024];
                    while (true)
                    {
                        int read = stream.Call<int>("read", buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        ms.Write(buffer, 0, read);
                    }
                    return ms.ToArray();
                }
            }
        }
        catch { return null; }
#else
        return null;
#endif
    }
}
