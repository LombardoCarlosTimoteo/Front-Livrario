

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Globalization; // <-- añadido para normalización
using UnityEngine;
using UnityEngine.SceneManagement;

public class GlobalBookStore : MonoBehaviour
{


    public event Action<BookRecord> OnProgressChanged; // se dispara cuando cambia el progreso

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
    public string isbn;        // ← ISBN actual del libro cargado globalmente
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
    public event Action<BookRecord> OnMetadataChanged; // ← NUEVO

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
        public string isbn;
        // NUEVO
        public int lastPage = 0;      // índice 0-based
        public int pageCount = 0;     // total de páginas
        public float progress01 = 0f; // 0..1
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
        title = Path.GetFileNameWithoutExtension(fileName);
        author = "";
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
                dest = EnsureUniquePath(dest);     // <- NUEVO, evita overwrite
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
        // --- Seguro: calcular pageCount sin pisar progreso existente ---
        string bestPath = GetBestPath();
        int pc = 0;
        try { pc = PdfRendererAndroid.GetPageCount(bestPath); } catch { pc = 0; }

        var rec = FindCurrentInLibrary();
        if (rec != null)
        {
            if (rec.pageCount == 0 && pc > 0) rec.pageCount = pc; // solo si no estaba
                                                                  // NO tocar lastPage si ya existe; recalcular progress solo si tiene datos
            if (rec.pageCount > 0)
                rec.progress01 = (rec.lastPage + 1f) / rec.pageCount;

            SaveLibraryToDisk();
        }


        Debug.Log($"[GlobalBookStore] Libro listo. local='{localPath}' original='{originalPath}' title='{title}' author='{author}'");
        StartCoroutine(DebugLibraryFileNamesNextFrame());
        DebugLibraryJsonPathAndSize();
    }
    static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path);
        string baseName = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        int i = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
            i++;
        } while (File.Exists(candidate) && i < 1000);
        return candidate;
    }
    // Recarga la biblioteca desde disco (lo usa el menú)
    public void ReloadLibrary() => LoadLibraryFromDisk();

    // El backend puede confirmar/ajustar título/autor/géneros
    public void UpdateWithServerResponse(string newTitle, string newAuthor, string[] newGenres, string newIsbn)
    {
        if (!string.IsNullOrEmpty(newTitle)) title = newTitle;
        if (!string.IsNullOrEmpty(newAuthor)) author = newAuthor;
        if (newGenres != null && newGenres.Length > 0) genres = newGenres;
        if (!string.IsNullOrEmpty(newIsbn)) isbn = newIsbn;   // ← guarda ISBN
        var rec = FindCurrentInLibrary();
        if (rec != null)
        {
            if (!string.IsNullOrEmpty(newTitle)) rec.title = newTitle;
            if (!string.IsNullOrEmpty(newAuthor)) rec.author = newAuthor;
            if (newGenres != null && newGenres.Length > 0) rec.genres = newGenres;
            if (!string.IsNullOrEmpty(newIsbn)) rec.isbn = newIsbn; // ← guarda ISBN también en el record
            SaveLibraryToDisk();
            OnMetadataChanged?.Invoke(rec); // ← AVISAR A LA UI
        }

        if (persistAcrossLaunches) SaveToPrefs();
    }

    public void UpdateWithServerResponse(string newTitle, string newAuthor, string[] newGenres)
    {
        UpdateWithServerResponse(newTitle, newAuthor, newGenres, null);
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
            PlayerPrefs.DeleteKey("book_isbn");
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
        PlayerPrefs.SetString("book_isbn", isbn ?? "");
        PlayerPrefs.Save();
    }

    void LoadFromPrefs()
    {
        originalPath = PlayerPrefs.GetString("book_original", "");
        localPath = PlayerPrefs.GetString("book_local", "");
        fileName = PlayerPrefs.GetString("book_name", "");
        title = PlayerPrefs.GetString("book_title", "");
        author = PlayerPrefs.GetString("book_author", "");
        isbn = PlayerPrefs.GetString("book_isbn", "");
        var g = PlayerPrefs.GetString("book_genres", "");
        genres = string.IsNullOrEmpty(g) ? null : g.Split('|');
    }

    // ===== Biblioteca: API pública =====
    public IReadOnlyList<BookRecord> GetLibrary() => library.items;
    // Devuelve el registro en la biblioteca que corresponde al libro "actual"
    public BookRecord FindCurrentInLibrary()
    {
        foreach (var it in library.items)
        {
            if (!string.IsNullOrEmpty(localPath) && it.localPath == localPath) return it;
            if (!string.IsNullOrEmpty(originalPath) && it.originalPath == originalPath) return it;
        }
        return null;
    }

    // Guarda la página actual y recalcula %; también actualiza pageCount si viene
    public void UpdateProgress(int pageIndex, int totalPages)
    {
        var rec = FindCurrentInLibrary();
        if (rec == null) return;

        if (totalPages > 0) rec.pageCount = totalPages;
        rec.lastPage = Mathf.Clamp(pageIndex, 0, Mathf.Max(0, rec.pageCount - 1));
        rec.progress01 = (rec.pageCount > 0) ? (rec.lastPage + 1f) / rec.pageCount : 0f;

        SaveLibraryToDisk();
        OnProgressChanged?.Invoke(rec);

    }

    // Asegura tener el total de páginas del libro actual
    public void EnsurePageCount(int totalPages)
    {
        var rec = FindCurrentInLibrary();
        if (rec == null) return;
        if (totalPages > 0 && rec.pageCount != totalPages)
        {
            rec.pageCount = totalPages;
            rec.progress01 = (rec.lastPage + 1f) / totalPages;
            SaveLibraryToDisk();
            OnProgressChanged?.Invoke(rec);

        }
    }

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
        if (library == null || library.items == null) return;

        // 1) Buscar existente por path (local u original)
        int idx = -1;
        for (int i = 0; i < library.items.Count; i++)
        {
            var it = library.items[i];
            bool matchLocal = !string.IsNullOrEmpty(localPath) && !string.IsNullOrEmpty(it.localPath) && it.localPath == localPath;
            bool matchOriginal = !string.IsNullOrEmpty(originalPath) && !string.IsNullOrEmpty(it.originalPath) && it.originalPath == originalPath;
            if (matchLocal || matchOriginal) { idx = i; break; }
        }

        // 2) Fallback por nombre + autor (útil para content:// que cambia el path)
        if (idx < 0 && !string.IsNullOrEmpty(fileName))
        {
            idx = library.items.FindIndex(x =>
                x.fileName == fileName &&
                (string.IsNullOrEmpty(author) || x.author == author));
        }

        if (idx >= 0)
        {
            // --- UPDATE preservando progreso y fecha de alta ---
            var it = library.items[idx];

            int keepLastPage = it.lastPage;
            int keepPageCount = it.pageCount;
            float keepProgress = it.progress01;
            string keepAddedAt = it.addedAtIso;

            it.title = string.IsNullOrEmpty(title) ? Path.GetFileNameWithoutExtension(fileName ?? "Libro") : title;
            it.author = author ?? "";
            it.genres = genres;
            it.isbn = isbn;

            it.originalPath = originalPath;
            it.localPath = localPath;
            it.fileName = fileName ?? it.fileName;

            // Restaurar progreso previo (no pisar con 0)
            it.lastPage = keepLastPage;
            it.pageCount = keepPageCount;
            it.progress01 = keepProgress;
            it.addedAtIso = string.IsNullOrEmpty(keepAddedAt) ? DateTime.UtcNow.ToString("o") : keepAddedAt;

            library.items[idx] = it;
        }
        else
        {
            // --- INSERT nuevo ---
            var rec = new BookRecord
            {
                title = string.IsNullOrEmpty(title) ? Path.GetFileNameWithoutExtension(fileName ?? "Libro") : title,
                author = author ?? "",
                genres = genres,
                isbn = isbn, // ← dentro del inicializador del rec

                originalPath = originalPath,
                localPath = localPath,
                fileName = fileName,
                addedAtIso = DateTime.UtcNow.ToString("o"),
                // progreso inicial en 0 solo para NUEVOS
                lastPage = 0,
                pageCount = 0,
                progress01 = 0f
            };
            library.items.Add(rec);
        }

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


    public void SetCurrentFromRecord(BookRecord rec)
    {
        if (rec == null) return;

        // Solo selecciona el libro actual. NO modificar progreso acá.
        originalPath = rec.originalPath;
        localPath = rec.localPath;
        fileName = rec.fileName;
        title = rec.title;
        author = rec.author;
        genres = rec.genres;
        isbn = rec.isbn;
        if (persistAcrossLaunches)
            SaveToPrefs();

        // ⚠️ NO LLAMAR UpsertCurrentIntoLibrary() AQUÍ
    }


    public void DeleteRecordAndFile(BookRecord rec, bool deleteFile = true)
    {
        if (rec == null) return;

        // Si era el libro actual, limpiá estado
        bool isCurrent = string.Equals(rec.localPath, localPath) || string.Equals(rec.originalPath, originalPath);
        if (isCurrent) ClearCurrent();

        // Borrar archivo local si corresponde
        if (deleteFile && !string.IsNullOrEmpty(rec.localPath))
        {
            try { if (System.IO.File.Exists(rec.localPath)) System.IO.File.Delete(rec.localPath); }
            catch (System.Exception e) { Debug.LogWarning("[BookStore] No se pudo borrar archivo: " + e.Message); }
        }

        // Quitar de la biblioteca y persistir
        library.items.Remove(rec);
        SaveLibraryToDisk();
    }



}
