using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using TMPro;            // si usás TextMeshPro
using UnityEngine.UI;  // si usás UI legacy

public class IniciarLecturaController : MonoBehaviour
{
    [Header("Endpoint (NO usar localhost en Quest)")]
    [Tooltip("Ej: http://192.168.0.12:8080")]
    public string baseUrl = "http://192.168.0.12:8080";
    public string ensurePath = "/book/ensure";
    [Tooltip("Timeout de la request en segundos")]
    public int timeoutSeconds = 15;

    [Header("UI Inputs (asigná los que uses)")]
    public TMP_InputField InputTituloLibroTMP;  // opcional (TMP)
    public TMP_InputField InputAutorTMP;        // opcional (TMP)
    public InputField InputTituloLibro;     // opcional (UI legacy)
    public InputField InputAutor;           // opcional (UI legacy)

    [Header("Fallbacks si inputs vacíos")]
    public string fallbackTitle = "Libro";
    public string fallbackAuthor = "";

    [Header("Escenas por género")]
    public string escenaPolicial = "Room_Policial";      // fallback por error
    public string escenaCienciaFiccion = "Room_cienciaFiccion";
    public string escenaFantasia = "Room_Fantasia2";
    public string escenaDefault = "Prueba-1";           // cuando HAY respuesta pero sin géneros soportados

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
        StartCoroutine(SendAndTeleport());
    }

    IEnumerator SendAndTeleport()
    {
        // 0) Validaciones y datos base
        if (GlobalBookStore.I == null)
        {
            Debug.LogWarning("[BtnIniciarLectura] GlobalBookStore no está en la escena. Fallback a Policial.");
            LoadPoliceFallback();
            yield break;
        }

        string pdfPath = GlobalBookStore.I.GetBestPath();
        if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
        {
            Debug.LogWarning("[BtnIniciarLectura] No hay PDF válido. Fallback a Policial.");
            LoadPoliceFallback();
            yield break;
        }

        // 1) Título/Autor desde UI (con fallback) y guardarlos YA en el store,
        //    así quedan persistidos aunque la request falle.
        string title = ReadInputSafely(InputTituloLibroTMP, InputTituloLibro);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = !string.IsNullOrEmpty(GlobalBookStore.I.FileName)
                        ? Path.GetFileNameWithoutExtension(GlobalBookStore.I.FileName)
                        : fallbackTitle;
        }

        string author = ReadInputSafely(InputAutorTMP, InputAutor);
        if (string.IsNullOrWhiteSpace(author)) author = fallbackAuthor;

        // persistir en el store (sin géneros, aún)
        GlobalBookStore.I.UpdateWithServerResponse(title, author, null);

        // 2) Preparar POST multipart
        string url = baseUrl.TrimEnd('/') + ensurePath;
        byte[] pdfBytes = null;

        try { pdfBytes = File.ReadAllBytes(pdfPath); }
        catch (System.Exception e)
        {
            Debug.LogWarning("[BtnIniciarLectura] No se pudo leer el PDF, continuo igual. " + e.Message);
        }

        WWWForm form = new WWWForm();
        form.AddField("title", title);
        form.AddField("author", author);
        if (pdfBytes != null)
        {
            form.AddBinaryData("pdf", pdfBytes, Path.GetFileName(pdfPath), "application/pdf");
        }

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.timeout = Mathf.Max(5, timeoutSeconds);
            req.SetRequestHeader("Accept", "application/json");
            Debug.Log($"[BtnIniciarLectura] POST {url}  file={pdfPath}  title='{title}' author='{author}'");

            yield return req.SendWebRequest();

            // 3) FALLBACK si error/timeout
            if (req.result != UnityWebRequest.Result.Success || req.responseCode < 200 || req.responseCode >= 300)
            {
                Debug.LogWarning($"[BtnIniciarLectura] Error HTTP o timeout. code={req.responseCode} err='{req.error}'. Fallback a Policial.");
                LoadPoliceFallback();
                yield break;
            }

            // 4) Parsear JSON
            string json = req.downloadHandler.text;
            BookEnsureResponse resp = null;
            try
            {
                resp = JsonUtility.FromJson<BookEnsureResponse>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BtnIniciarLectura] JSON inválido. " + e.Message + " Fallback a Policial.");
                LoadPoliceFallback();
                yield break;
            }

            // 5) Actualizar store con géneros confirmados (si los hay)
            GlobalBookStore.I.UpdateWithServerResponse(resp?.title, resp?.author, resp?.genres);

            // 6) Elegir escena por PRIMERA coincidencia soportada. 
            //    Si NO hay coincidencia pero hubo respuesta válida → escenaDefault.
            string scene = EscenaPorPrimerGeneroSoportado(resp?.genres);
            if (string.IsNullOrEmpty(scene)) scene = escenaDefault;

            Debug.Log("[BtnIniciarLectura] Cargando escena: " + scene);
            SceneManager.LoadScene(scene);
        }
    }

    // --- Fallback sólido: carga Policial siempre ---
    void LoadPoliceFallback()
    {
        // No tocamos géneros para no sobreescribir lo que ya tenga el store
        // (el PDF ya quedó guardado por el Picker, y título/autor se guardaron antes).
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
    static bool EsPolicial(string s)
    {
        return s.Contains("policial") || s.Contains("thriller") || s.Contains("detectiv") ||
               s.Contains("crime") || s.Contains("misterio") || s.Contains("mystery");
    }
    static bool EsCienciaFiccion(string s)
    {
        return s.Contains("ciencia fic") || s.Contains("science fiction") || s.Contains("sci-fi") ||
               s.Contains("scifi") || s.Contains("sci fi");
    }
    static bool EsFantasia(string s)
    {
        return s.Contains("fantas"); // cubre "fantasía" y "fantasy"
    }

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
}
