using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic; // arriba
public class LibraryMenuController : MonoBehaviour
{
    [Header("UI")]
    public GameObject panelRoot;       // UI_Biblioteca (raíz)
    public Transform listContent;      // Content del ScrollRect
    public GameObject itemPrefab;      // Prefab del ítem (con BookItemUI o niños "Title"/"Genre")
    public Button btnVolver;
    public Button btnContinuar;
    public GameObject backTargetToShow; // menú anterior (p/mostrar al cerrar)

    [Header("Visual")]
    public Color normalColor = new Color(1, 1, 1, 0.35f); // subí alfa para verlos claro
    public Color selectedColor = new Color(1, 1, 1, 0.75f);

    [Header("Debug")]
    public bool logVerbose = true;

    int selectedIndex = -1;
    GlobalBookStore.BookRecord[] data;
    readonly List<BookItemUI> spawned = new List<BookItemUI>(); // (usa esta forma si tu C# no acepta 'new()')

    bool _listeningProgress = false;
    bool _listeningMetadata = false; // ← NUEVO

    void Awake()
    {
        if (panelRoot) panelRoot.SetActive(false);
        if (btnVolver) btnVolver.onClick.AddListener(OnBtnVolver);
        if (btnContinuar) btnContinuar.onClick.AddListener(OnBtnContinuar);
        if (btnEliminar) btnEliminar.onClick.AddListener(OnBtnEliminar);

    }

    // Llamá esto desde BtnBiblioteca → OnClick: LibraryMenuController.Open()
    public void Open()
    {
        // Trae lo último del disco
        GlobalBookStore.I?.ReloadLibrary();

        // Suscripciones (una sola vez)
        if (GlobalBookStore.I != null)
        {
            if (!_listeningMetadata)
            {
                GlobalBookStore.I.OnMetadataChanged += HandleMetadataChanged;
                _listeningMetadata = true;
            }
            if (!_listeningProgress)
            {
                GlobalBookStore.I.OnProgressChanged += HandleProgressChanged;
                _listeningProgress = true;
            }
        }

        // UI
        if (backTargetToShow) backTargetToShow.SetActive(false);
        if (panelRoot) panelRoot.SetActive(true);

        // Reiniciar selección/estado del botón
        selectedIndex = -1;
        if (btnContinuar) btnContinuar.interactable = false;

        RefreshList();
    }

    public void Close()
    {
        // Desuscribir eventos (simetría con Open)
        if (GlobalBookStore.I != null)
        {
            if (_listeningMetadata)
            {
                GlobalBookStore.I.OnMetadataChanged -= HandleMetadataChanged;
                _listeningMetadata = false;
            }
            if (_listeningProgress)
            {
                GlobalBookStore.I.OnProgressChanged -= HandleProgressChanged;
                _listeningProgress = false;
            }
        }

        // UI
        if (panelRoot) panelRoot.SetActive(false);
        if (backTargetToShow) backTargetToShow.SetActive(true);
    }

    void HandleMetadataChanged(GlobalBookStore.BookRecord rec)
    {
        // Si usamos BookItemUI: refrescar SOLO ese ítem
        if (spawned != null && spawned.Count > 0)
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                var it = spawned[i];
                if (!it || it.Record == null) continue;

                if (ReferenceEquals(it.Record, rec) || SameRecord(it.Record, rec))
                {
                    it.RefreshMetadataUI();
                    return;
                }
            }
        }

        // Fallback: si no lo encontramos, repintar la lista
        RefreshList();
    }

  

    void HandleProgressChanged(GlobalBookStore.BookRecord rec)
    {
        // Si tenemos items con BookItemUI, refrescamos SOLO el que cambió
        if (spawned != null && spawned.Count > 0)
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                var it = spawned[i];
                if (!it || it.Record == null) continue;

                // Coincidencia por referencia o por path (por si se recarga la lib)
                if (ReferenceEquals(it.Record, rec) || SameRecord(it.Record, rec))
                {
                    // Actualizo la data y el UI del % sin reinstanciar
                    it.RefreshProgressUI();
                    return;
                }
            }
        }

        // Fallback: si no lo encontramos, repintar toda la lista
        RefreshList();
    }

    // Compara por rutas (local u original) para robustez
    static bool SameRecord(GlobalBookStore.BookRecord a, GlobalBookStore.BookRecord b)
    {
        if (a == null || b == null) return false;
        if (!string.IsNullOrEmpty(a.localPath) && a.localPath == b.localPath) return true;
        if (!string.IsNullOrEmpty(a.originalPath) && a.originalPath == b.originalPath) return true;
        return false;
    }

    void RefreshList()
    {
        if (!listContent || !itemPrefab)
        {
            Debug.LogWarning("[Library] Falta listContent o itemPrefab en el Inspector.");
            return;
        }

        for (int i = listContent.childCount - 1; i >= 0; i--)
            Destroy(listContent.GetChild(i).gameObject);
        spawned.Clear(); // ✅

        selectedIndex = -1;
        if (btnContinuar) btnContinuar.interactable = false;

        var lib = GlobalBookStore.I?.GetLibrary();
        int count = lib?.Count ?? 0;
        if (logVerbose) Debug.Log($"[Library] Total guardados: {count}");
        if (lib == null || lib.Count == 0) return;

        data = lib.ToArray();

        for (int i = 0; i < data.Length; i++)
        {
            var rec = data[i];
            var go = Instantiate(itemPrefab, listContent);
            go.name = $"BookItem_{i}";
            go.SetActive(true);

            var bi = go.GetComponent<BookItemUI>();
            if (bi)
            {
                bi.normalColor = normalColor;
                bi.selectedColor = selectedColor;
                bi.Init(rec, this, i);   // pasa índice
                spawned.Add(bi);         // ✅ guardo para resaltar después
            }
            else
            {
                var title = go.transform.Find("Title")?.GetComponent<TMP_Text>();
                var genre = go.transform.Find("Genre")?.GetComponent<TMP_Text>();
                var btn = go.GetComponent<Button>();
                var img = go.GetComponent<Image>();

                if (title)
                    title.text = !string.IsNullOrEmpty(rec.title)
                               ? rec.title
                               : (!string.IsNullOrEmpty(rec.fileName)
                                    ? Path.GetFileNameWithoutExtension(rec.fileName)
                                    : "Libro");

                if (genre)
                    genre.text = FormatPrimaryGenre(rec.genres);

                // --- NUEVO: progreso ---
                var progress = go.transform.Find("Panel/Progress")?.GetComponent<TMP_Text>();
                if (progress)
                {
                    int percent = Mathf.RoundToInt(Mathf.Clamp01(rec.progress01) * 100f);
                    progress.gameObject.SetActive(percent > 0);
                    progress.text = percent + "%";
                }

                int idx = i;
                if (btn) btn.onClick.AddListener(() => OnSelect(idx));
                if (img) img.color = normalColor;
            }
        }

        var rt = listContent as RectTransform;
        if (rt) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }


    // Selección desde el modo "sin BookItemUI"
    void OnSelect(int idx)
    {
        selectedIndex = idx;

        // Resaltar con BookItemUI
        if (spawned != null && spawned.Count > 0)
        {
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i]) spawned[i].SetSelected(i == selectedIndex);
        }
        else
        {
            // Fallback: colorear Image del item
            for (int i = 0; i < listContent.childCount; i++)
            {
                var img = listContent.GetChild(i).GetComponent<Image>();
                if (img) img.color = (i == selectedIndex) ? selectedColor : normalColor;
            }
        }

        if (btnContinuar) btnContinuar.interactable = (selectedIndex >= 0);
    }



    // Selección llamada por BookItemUI
    public void OnItemSelected(BookItemUI item)
    {
        if (item == null || data == null) return;

        int idx = System.Array.FindIndex(data, d => d == item.Record);
        if (idx < 0) idx = spawned.IndexOf(item); // fallback por posición
        if (idx >= 0) OnSelect(idx);
    }


    void OnBtnVolver() => Close();

    void OnBtnContinuar()
    {
        if (selectedIndex < 0 || data == null || selectedIndex >= data.Length) return;

        var rec = data[selectedIndex];

        // Selecciono el libro actual SIN tocar progreso
        GlobalBookStore.I.SetCurrentFromRecord(rec);

        // Aseguro tener la librería al día (por si otra escena la actualizó)
        GlobalBookStore.I.ReloadLibrary();

        // Si el registro en disco tiene géneros, usalos; si no, quedate sin géneros (irá al fallback)
        var currentRec = GlobalBookStore.I.FindCurrentInLibrary() ?? rec;
        GlobalBookStore.I.UpdateWithServerResponse(
        currentRec.title,
        currentRec.author,
        currentRec.genres,
        currentRec.isbn
    );
        string[] genres = (currentRec.genres != null && currentRec.genres.Length > 0)
                            ? currentRec.genres
                            : System.Array.Empty<string>();


        // Elegir la escena por género
        string room = MapGenresToRoom(genres);
        if (string.IsNullOrEmpty(room)) room = "Room_Policial"; // fallback final

        Debug.Log($"[Library] Continuar: '{currentRec.title}' genres=[{string.Join(",", genres ?? new string[0])}] -> {room}");
        SceneManager.LoadScene(room);
    }


    // --- Helpers de género/escena ---
    static string FormatPrimaryGenre(string[] genres)
    {
        if (genres == null || genres.Length == 0) return "—";
        string[] prefs = { "Fantasía", "Ciencia ficción", "Policial" };
        foreach (var p in prefs)
            if (genres.Any(g => Normalize(g) == Normalize(p)))
                return p;
        return genres[0];
    }

    static string MapGenresToRoom(string[] genres)
    {
        if (genres != null)
        {
            foreach (var g in genres)
            {
                var s = Normalize(g);
                if (s.Contains("fantasia") || s.Contains("fantasy")) return "Room_Fantasia2";
                if (s.Contains("ciencia fic") || s.Contains("science fiction") || s.Contains("sci-fi") ||
                    s.Contains("scifi") || s.Contains("sci fi")) return "Room_cienciaFiccion";
                if (s.Contains("policial") || s.Contains("thriller") || s.Contains("detectiv") ||
                    s.Contains("crime") || s.Contains("misterio") || s.Contains("mystery")) return "Room_Policial";
            }
        }
        return null;
    }


    static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var nf = input.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(nf.Length);
        foreach (char c in nf)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    void MakeRecordCurrent(GlobalBookStore.BookRecord rec)
    {
        if (rec == null || GlobalBookStore.I == null) return;
        GlobalBookStore.I.SetCurrentFromRecord(rec); // ✅ no resetea progreso
        GlobalBookStore.I.UpdateWithServerResponse(rec.title, rec.author, rec.genres, rec.isbn);
    }


    public void OnItemSelectedIndex(int idx)
    {
        OnSelect(idx);
    }
    public Button btnEliminar;

    void OnBtnEliminar()
    {
        if (selectedIndex < 0 || data == null || selectedIndex >= data.Length) return;

        var rec = data[selectedIndex];
        GlobalBookStore.I.DeleteRecordAndFile(rec, true); // true = borrar archivo local también

        selectedIndex = -1;
        if (btnContinuar) btnContinuar.interactable = false;
        RefreshList(); // repintar la librería
    }

}
