using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GlobalBookStore : MonoBehaviour
{
    public static GlobalBookStore I { get; private set; }

    [Header("Estado del libro")]
    [SerializeField] string originalPath;     // lo que entrega el picker (puede ser file:// o content://)
    [SerializeField] string localPath;        // copia en persistentDataPath si fue posible
    [SerializeField] string fileName;         // nombre visible
    [SerializeField] bool isPolicial;

    [Header("Opciones")]
    public bool copyToAppStorage = true;      // intenta copiar a Application.persistentDataPath
    public bool persistAcrossLaunches = true; // guardar/recuperar de PlayerPrefs

    public string OriginalPath => originalPath;
    public string LocalPath => localPath;
    public string FileName => fileName;
    public bool HasBook => !string.IsNullOrEmpty(originalPath) || !string.IsNullOrEmpty(localPath);
    public bool IsPolicial => isPolicial;

    // Auto-bootstrap: si no existe en la escena, se crea solo antes de cargar la primera escena
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (I == null)
        {
            var go = new GameObject("GlobalBookStore");
            go.AddComponent<GlobalBookStore>();
            DontDestroyOnLoad(go);
        }
    }

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        if (persistAcrossLaunches)
            LoadFromPrefs();
    }

    // Usá esto después del file picker
    public void SetFromPickerPath(string pickedPath)
    {
        if (string.IsNullOrEmpty(pickedPath))
            return;

        originalPath = pickedPath;
        fileName = Path.GetFileName(pickedPath);
        if (string.IsNullOrEmpty(fileName))
            fileName = "Libro.pdf";

        // Heurística de "policial"
        var lower = fileName.ToLowerInvariant();
        isPolicial = lower.Contains("policia") || lower.Contains("policial");

        // Intentar copiar a almacenamiento propio (si el path es un archivo real)
        localPath = null;
        if (copyToAppStorage && File.Exists(pickedPath))
        {
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "Books");
                Directory.CreateDirectory(dir);
                string dest = Path.Combine(dir, fileName);
                File.Copy(pickedPath, dest, true);
                localPath = dest;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GlobalBookStore] No se pudo copiar a storage local: {e.Message}");
            }
        }

        if (persistAcrossLaunches)
            SaveToPrefs();

        Debug.Log($"[GlobalBookStore] Libro listo. local='{localPath}' original='{originalPath}' policial={isPolicial}");
    }

    // Ruta preferida para usar dentro de la app
    public string GetBestPath()
        => !string.IsNullOrEmpty(localPath) && File.Exists(localPath) ? localPath : originalPath;

    public void Clear()
    {
        originalPath = localPath = fileName = null;
        isPolicial = false;
        if (persistAcrossLaunches)
        {
            PlayerPrefs.DeleteKey("book_original");
            PlayerPrefs.DeleteKey("book_local");
            PlayerPrefs.DeleteKey("book_name");
            PlayerPrefs.DeleteKey("book_policial");
            PlayerPrefs.Save();
        }
    }

    void SaveToPrefs()
    {
        PlayerPrefs.SetString("book_original", originalPath ?? "");
        PlayerPrefs.SetString("book_local", localPath ?? "");
        PlayerPrefs.SetString("book_name", fileName ?? "");
        PlayerPrefs.SetInt("book_policial", isPolicial ? 1 : 0);
        PlayerPrefs.Save();
    }

    void LoadFromPrefs()
    {
        originalPath = PlayerPrefs.GetString("book_original", "");
        localPath = PlayerPrefs.GetString("book_local", "");
        fileName = PlayerPrefs.GetString("book_name", "");
        isPolicial = PlayerPrefs.GetInt("book_policial", 0) == 1;
    }

    // (Opcional) abrir con visor externo del sistema
    public void OpenInExternalViewer()
    {
        var uri = GetBestPath();
        if (string.IsNullOrEmpty(uri))
        {
            Debug.LogWarning("[GlobalBookStore] No hay libro para abrir.");
            return;
        }
        Application.OpenURL(uri); // en Android abre la app de PDFs si está disponible
    }

    // (Opcional) teletransportar según género
    public void TeleportIfPolicial(string escenaPolicial = "Room_Policial")
    {
        if (IsPolicial)
            SceneManager.LoadScene(escenaPolicial);
    }
}
