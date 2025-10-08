using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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

    void Awake()
    {
        if (panelRoot) panelRoot.SetActive(false);
        if (btnVolver) btnVolver.onClick.AddListener(OnBtnVolver);
        if (btnContinuar) btnContinuar.onClick.AddListener(OnBtnContinuar);
    }

    // Llamá esto desde BtnBiblioteca → OnClick: LibraryMenuController.Open()
    public void Open()
    {
        if (backTargetToShow) backTargetToShow.SetActive(false);
        if (panelRoot) panelRoot.SetActive(true);
        RefreshList();
    }

    public void Close()
    {
        if (panelRoot) panelRoot.SetActive(false);
        if (backTargetToShow) backTargetToShow.SetActive(true);
    }

    void RefreshList()
    {
        if (!listContent || !itemPrefab)
        {
            Debug.LogWarning("[Library] Falta listContent o itemPrefab en el Inspector.");
            return;
        }

        // limpiar lista
        for (int i = listContent.childCount - 1; i >= 0; i--)
            Destroy(listContent.GetChild(i).gameObject);

        selectedIndex = -1;
        if (btnContinuar) btnContinuar.interactable = false;

        var lib = GlobalBookStore.I?.GetLibrary();
        int count = lib?.Count ?? 0;
        if (logVerbose) Debug.Log($"[Library] Total guardados: {count}");

        if (lib == null || lib.Count == 0) return;

        data = lib.ToArray();

        int created = 0;
        for (int i = 0; i < data.Length; i++)
        {
            var rec = data[i];
            var go = Instantiate(itemPrefab, listContent);
            go.name = $"BookItem_{i}";
            go.SetActive(true);

            // Si el prefab tiene BookItemUI, usalo
            var bi = go.GetComponent<BookItemUI>();
            if (bi)
            {
                bi.normalColor = normalColor;
                bi.selectedColor = selectedColor;
                bi.Init(rec, this, i);   // <<--- IMPORTANTE: pasar "i"
            }
            else
            {
                // Fallback: niños "Title" y "Genre" + Button + Image opcional
                var title = go.transform.Find("Title")?.GetComponent<TMP_Text>();
                var genre = go.transform.Find("Genre")?.GetComponent<TMP_Text>();
                var btn = go.GetComponent<Button>();
                var img = go.GetComponent<Image>();

                if (!title && logVerbose) Debug.LogWarning($"[Library] Prefab sin hijo 'Title' en {go.name}");
                if (!genre && logVerbose) Debug.LogWarning($"[Library] Prefab sin hijo 'Genre' en {go.name}");
                if (!btn && logVerbose) Debug.LogWarning($"[Library] Prefab sin Button en {go.name}");

                if (title)
                    title.text = !string.IsNullOrEmpty(rec.title)
                               ? rec.title
                               : (!string.IsNullOrEmpty(rec.fileName)
                                    ? Path.GetFileNameWithoutExtension(rec.fileName)
                                    : "Libro");

                if (genre)
                    genre.text = FormatPrimaryGenre(rec.genres);

                int idx = i;
                if (btn) btn.onClick.AddListener(() => OnSelect(idx));
                if (img) img.color = normalColor;
            }

            created++;
        }

        if (logVerbose)
        {
            Debug.Log($"[Library] Instanciados {created} ítems. Hijos en Content: {listContent.childCount}");
        }

        // Forzar rebuild de layout para que aparezcan
        var rt = listContent as RectTransform;
        if (rt) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    // Selección desde el modo "sin BookItemUI"
    void OnSelect(int idx)
    {
        selectedIndex = idx;
        // resaltar seleccionado
        for (int i = 0; i < listContent.childCount; i++)
        {
            var img = listContent.GetChild(i).GetComponent<Image>();
            if (img) img.color = (i == selectedIndex) ? selectedColor : normalColor;
        }
        if (btnContinuar) btnContinuar.interactable = true;
    }

    // Selección llamada por BookItemUI
    public void OnItemSelected(BookItemUI item)
    {
        if (item == null || data == null) return;
        int idx = System.Array.FindIndex(data, d => d == item.Record);
        if (idx >= 0) OnSelect(idx);
    }

    void OnBtnVolver() => Close();

    void OnBtnContinuar()
    {
        if (selectedIndex < 0 || data == null || selectedIndex >= data.Length) return;
        var rec = data[selectedIndex];

        // volver “actual” el libro elegido
        MakeRecordCurrent(rec);

        // elegir escena por género (fallback a Policial)
        string room = MapGenresToRoom(rec.genres);
        if (string.IsNullOrEmpty(room)) room = "Room_Policial";

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
                if (s.Contains("fantasia")) return "Room_Fantasia2";
                if (s.Contains("ciencia ficcion") || s.Contains("cienciaficcion") || s.Contains("science fiction") || s.Contains("scifi"))
                    return "Room_cienciaFiccion";
                if (s.Contains("policial") || s.Contains("thriller") || s.Contains("detectiv") || s.Contains("misterio"))
                    return "Room_Policial";
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

        string best = !string.IsNullOrEmpty(rec.localPath) && File.Exists(rec.localPath)
                        ? rec.localPath
                        : rec.originalPath;

        if (!string.IsNullOrEmpty(best))
            GlobalBookStore.I.SetFromPickerPath(best);

        GlobalBookStore.I.UpdateWithServerResponse(rec.title, rec.author, rec.genres);
    }

    public void OnItemSelectedIndex(int idx)
    {
        OnSelect(idx);
    }


}
