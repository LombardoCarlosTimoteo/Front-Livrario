using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Globalization; // <-- añadido para normalización
using UnityEngine;
using UnityEngine.SceneManagement;

public class GlobalBookStore : MonoBehaviour
{
    public static GlobalBookStore I { get; private set; }

    [Header("Estado del libro actual")]
    [SerializeField] string originalPath;     // path que entrega el picker (puede ser content://)
    [SerializeField] string localPath;        // copia en almacenamiento de la app
    [SerializeField] string fileName;         // nombre visible
    [SerializeField] string title;            // título (puede venir del input / backend)
    [SerializeField] string author;           // autor (input / backend)
    [SerializeField] string[] genres;         // géneros (del backend)

    [Header("Opciones")]
    public bool copyToAppStorage = true;
    public bool persistAcrossLaunches = true;

    public string OriginalPath => originalPath;
    public string LocalPath => localPath;
    public string FileName => fileName;
    public string Title => title;
    public string Author => author;
    public string[] Genres => genres;
    public bool HasBook => !string.IsNullOrEmpty(GetBestPath());

    // ==== Compatibilidad con scripts viejos ====
    // Calcula "policial" a partir de genres (si hay) o por heurística del nombre de archivo.
    public bool IsPolicial
    {
        get
        {
            if (genres != null)
            {
                foreach (var g in genres)
                {
                    var s = Normalize(g);
                    if (IsPolicialStr(s)) return true;
                }
            }
            var lower = (fileName ?? "").ToLowerInvariant();
            return lower.Contains("policia") || lower.Contains("policial");
        }
    }

    static bool IsPolicialStr(string s)
    {
        return s.Contains("policial") || s.Contains("thriller") ||
               s.Contains("detectiv") || s.Contains("crime") ||
               s.Contains("misterio") || s.Contains("mystery");
    }

    // Normaliza a minúsculas y sin tildes (para comparar géneros robustamente)
    static string Normalize(string input)
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

    // ==== Biblioteca persistente ====
    [Serializable]
    public class BookRecord
    {
        public string title;
        public string author;
        public string[] genres;
        public string originalPath;
        public string localPath;
        public string fileName;
        public string addedAtIso;
    }
    [Serializable] class BookLibrary { public List<BookRecord> items = new List<BookRecord>(); }

    BookLibrary library = new BookLibrary();
    string LibraryFilePath => Path.Combine(Application.persistentDataPath, "library.json");

    // ===== Bootstrap =====
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

        LoadLibraryFromDisk();

        if (persistAcrossLaunches)
            LoadFromPrefs();
    }

    // ====== Picker: setea el libro actual (se llama cuando elegís un PDF) ======
    public void SetFromPickerPath(string pickedPath)
    {
        if (string.IsNullOrEmpty(pickedPath))
            return;

        originalPath = pickedPath;
        fileName = Path.GetFileName(pickedPath);
        if (string.IsNullOrEmpty(fileName)) fileName = "Libro.pdf";

        // Si aún no tenemos título/autor (los puede definir el usuario y/o backend)
        if (string.IsNullOrEmpty(title)) title = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(author)) author = "";

        // Copia a storage propio (si es file:// real)
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

        // Guardar/actualizar en Biblioteca
        UpsertCurrentIntoLibrary();

        Debug.Log($"[GlobalBookStore] Libro listo. local='{localPath}' original='{originalPath}' title='{title}' author='{author}'");
        StartCoroutine(DebugLibraryFileNamesNextFrame());
        DebugLibraryJsonPathAndSize();
    }

    // El backend puede confirmar/ajustar título/autor/géneros
    public void UpdateWithServerResponse(string confirmedTitle, string confirmedAuthor, string[] confirmedGenres)
    {
        if (!string.IsNullOrEmpty(confirmedTitle)) title = confirmedTitle;
        if (!string.IsNullOrEmpty(confirmedAuthor)) author = confirmedAuthor;
        genres = confirmedGenres;

        if (persistAcrossLaunches)
            SaveToPrefs();

        UpsertCurrentIntoLibrary();
    }

    // Ruta preferida para usar dentro de la app
    public string GetBestPath()
        => !string.IsNullOrEmpty(localPath) && File.Exists(localPath) ? localPath : originalPath;

    public void ClearCurrent()
    {
        originalPath = localPath = fileName = title = author = null;
        genres = null;

        if (persistAcrossLaunches)
        {
            PlayerPrefs.DeleteKey("book_original");
            PlayerPrefs.DeleteKey("book_local");
            PlayerPrefs.DeleteKey("book_name");
            PlayerPrefs.DeleteKey("book_title");
            PlayerPrefs.DeleteKey("book_author");
            PlayerPrefs.DeleteKey("book_genres");
            PlayerPrefs.Save();
        }
    }

    void SaveToPrefs()
    {
        PlayerPrefs.SetString("book_original", originalPath ?? "");
        PlayerPrefs.SetString("book_local", localPath ?? "");
        PlayerPrefs.SetString("book_name", fileName ?? "");
        PlayerPrefs.SetString("book_title", title ?? "");
        PlayerPrefs.SetString("book_author", author ?? "");
        PlayerPrefs.SetString("book_genres", genres != null ? string.Join("|", genres) : "");
        PlayerPrefs.Save();
    }

    void LoadFromPrefs()
    {
        originalPath = PlayerPrefs.GetString("book_original", "");
        localPath = PlayerPrefs.GetString("book_local", "");
        fileName = PlayerPrefs.GetString("book_name", "");
        title = PlayerPrefs.GetString("book_title", "");
        author = PlayerPrefs.GetString("book_author", "");
        var g = PlayerPrefs.GetString("book_genres", "");
        genres = string.IsNullOrEmpty(g) ? null : g.Split('|');
    }

    // ===== Biblioteca: API pública =====
    public IReadOnlyList<BookRecord> GetLibrary() => library.items;

    public void DeleteFromLibrary(BookRecord rec)
    {
        if (rec == null) return;
        library.items.Remove(rec);
        SaveLibraryToDisk();
    }

    public void ClearLibrary()
    {
        library.items.Clear();
        SaveLibraryToDisk();
    }

    // ===== Biblioteca: persistencia en JSON =====
    void LoadLibraryFromDisk()
    {
        try
        {
            if (!File.Exists(LibraryFilePath))
            {
                library = new BookLibrary();
                return;
            }
            var json = File.ReadAllText(LibraryFilePath, Encoding.UTF8);
            library = JsonUtility.FromJson<BookLibrary>(json) ?? new BookLibrary();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GlobalBookStore] No se pudo leer library.json: " + e.Message);
            library = new BookLibrary();
        }
    }

    void SaveLibraryToDisk()
    {
        try
        {
            var json = JsonUtility.ToJson(library, false);
            File.WriteAllText(LibraryFilePath, json, Encoding.UTF8);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GlobalBookStore] No se pudo escribir library.json: " + e.Message);
        }
    }

    // ===== Biblioteca: upsert del libro actual =====
    void UpsertCurrentIntoLibrary()
    {
        var rec = new BookRecord
        {
            title = string.IsNullOrEmpty(title) ? Path.GetFileNameWithoutExtension(fileName ?? "Libro") : title,
            author = author ?? "",
            genres = genres,
            originalPath = originalPath,
            localPath = localPath,
            fileName = fileName,
            addedAtIso = DateTime.UtcNow.ToString("o")
        };

        // clave por ruta preferida (local si existe, si no original)
        string key = !string.IsNullOrEmpty(localPath) ? localPath : originalPath;

        int idx = -1;
        if (!string.IsNullOrEmpty(key))
        {
            idx = library.items.FindIndex(x =>
                (!string.IsNullOrEmpty(x.localPath) && x.localPath == key) ||
                (!string.IsNullOrEmpty(x.originalPath) && x.originalPath == key));
        }
        if (idx < 0 && !string.IsNullOrEmpty(fileName))
        {
            // fallback por nombre (para content://)
            idx = library.items.FindIndex(x => x.fileName == fileName && x.author == author);
        }

        if (idx >= 0)
            library.items[idx] = rec;   // update
        else
            library.items.Add(rec);     // insert

        SaveLibraryToDisk();
    }

    // ===== (Opcional) Teleport según género ya conocido =====
    public void TeleportIfPolicial(string escenaPolicial = "Room_Policial")
    {
        if (genres != null)
        {
            foreach (var g in genres)
            {
                var s = (g ?? "").ToLowerInvariant();
                if (s.Contains("policial") || s.Contains("thriller") || s.Contains("detectiv"))
                {
                    SceneManager.LoadScene(escenaPolicial);
                    return;
                }
            }
        }
    }

    System.Collections.IEnumerator DebugLibraryFileNamesNextFrame()
    {
        // Salimos del callback del picker y del Update de ese frame
        yield return null;
        yield return null;

        var list = GetLibrary();
        int total = (list != null) ? list.Count : 0;
        Debug.Log($"[Library] Total libros guardados: {total}");
        if (total == 0) yield break;

        // ------ Línea única con todos los nombres (CSV) ------
        var names = new System.Collections.Generic.List<string>(total);
        for (int i = 0; i < list.Count; i++)
        {
            var it = list[i];
            string name =
                !string.IsNullOrEmpty(it.fileName) ? it.fileName :
                !string.IsNullOrEmpty(it.localPath) ? System.IO.Path.GetFileName(it.localPath) :
                !string.IsNullOrEmpty(it.originalPath) ? System.IO.Path.GetFileName(it.originalPath) :
                "(sin nombre)";
            names.Add(name);
        }
        Debug.Log($"[LibraryList] {string.Join(", ", names)}");

        // ------ Una línea por ítem (con yield para que no se “pierdan”) ------
        for (int i = 0; i < list.Count; i++)
        {
            var it = list[i];
            string name =
                !string.IsNullOrEmpty(it.fileName) ? it.fileName :
                !string.IsNullOrEmpty(it.localPath) ? System.IO.Path.GetFileName(it.localPath) :
                !string.IsNullOrEmpty(it.originalPath) ? System.IO.Path.GetFileName(it.originalPath) :
                "(sin nombre)";

            Debug.Log($"[LibraryItem] {i + 1}/{total} -> {name}");
            yield return null; // da tiempo a Logcat a mostrar la línea
        }
    }

    void DebugLibraryJsonPathAndSize()
    {
        string p = System.IO.Path.Combine(Application.persistentDataPath, "library.json");
        if (System.IO.File.Exists(p))
        {
            var len = new System.IO.FileInfo(p).Length;
            Debug.Log($"[Library] JSON: {p} ({len} bytes)");
        }
        else
        {
            Debug.Log("[Library] library.json NO existe");
        }
    }



}
