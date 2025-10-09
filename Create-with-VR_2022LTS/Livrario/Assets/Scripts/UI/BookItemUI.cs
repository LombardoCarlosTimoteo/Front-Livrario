using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.IO; // para Path.GetFileNameWithoutExtension

public class BookItemUI : MonoBehaviour
{
    [Header("Refs del prefab")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI genresText;
    public Button selectButton;

    [Header("Cover (carátula)")]
    public Image coverImage;                  // arrastrá la Image "Cover"
    [Tooltip("Resolución larga para la miniatura (p.ej. 512).")]
    public int coverLongSide = 512;

    [Header("Resalte (fondo y marco)")]
    public Image background;                  // Image del ítem o del botón
    public Image selectionFrame;              // (opcional) hijo "SelectionFrame" con Image (Type=Sliced)
    public Color normalColor = new Color(1f, 1f, 1f, 0.18f);          // fondo apagado
    public Color selectedColor = new Color(0.74f, 0.90f, 1f, 0.85f);    // celeste notorio (#BDE4FF aprox)

    [Header("Resalte (fallback Outline si no hay marco)")]
    public bool useOutlineFallback = true;
    public Color outlineColor = new Color32(0, 145, 255, 255);          // #0091FF
    public Vector2 outlineDistance = new Vector2(3f, -3f);

    [Header("Progreso")]
    public Button progressButton;             // botón del %
    public TextMeshProUGUI progressText;      // TMP del %

    // Datos
    public GlobalBookStore.BookRecord Record { get; private set; }

    // Owner que manejará la selección
    LibraryMenuController _owner;

    // Guardamos refs para limpiar
    Texture2D _thumbTex;
    Sprite _thumbSprite;
    Outline _outline; // fallback

    /// Llamado por el controller cuando instancia el ítem
    public void Init(GlobalBookStore.BookRecord rec, LibraryMenuController owner, int index)
    {
        Record = rec;
        _owner = owner;

        if (titleText) titleText.text = BestTitle(Record);
        if (genresText) genresText.text = (rec.genres != null && rec.genres.Length > 0)
                                            ? string.Join(" · ", rec.genres)
                                            : "—";

        if (selectButton)
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(OnClick);
        }

        EnsureRefs();
        SetSelected(false);     // estado visual inicial

        LoadCoverAsync();
        RefreshProgressUI();
    }

    static string BestTitle(GlobalBookStore.BookRecord rec)
    {
        if (rec == null) return "Libro";
        if (!string.IsNullOrEmpty(rec.title)) return rec.title;
        if (!string.IsNullOrEmpty(rec.fileName)) return Path.GetFileNameWithoutExtension(rec.fileName);
        return "Libro";
    }

    public void RefreshMetadataUI()
    {
        if (titleText) titleText.text = BestTitle(Record);
        if (genresText) genresText.text =
            (Record.genres != null && Record.genres.Length > 0) ? string.Join(" · ", Record.genres) : "—";
    }

    // --- Resalte ---
    void EnsureRefs()
    {
        // Fondo
        if (!background)
        {
            background = GetComponent<Image>();
            if (!background && selectButton) background = selectButton.GetComponent<Image>();
        }

        // Marco (si existe un hijo llamado "SelectionFrame")
        if (!selectionFrame)
        {
            var t = transform.Find("SelectionFrame");
            if (t) selectionFrame = t.GetComponent<Image>();
        }
    }

    public void SetSelected(bool selected)
    {
        EnsureRefs();

        // Fondo celeste
        if (background) background.color = selected ? selectedColor : normalColor;

        // Marco celeste (si existe)
        if (selectionFrame)
        {
            selectionFrame.enabled = selected;
            selectionFrame.raycastTarget = false;
        }

        // Fallback Outline si no hay marco
        if (!selectionFrame && useOutlineFallback && background)
        {
            if (_outline == null)
                _outline = background.gameObject.GetComponent<Outline>() ?? background.gameObject.AddComponent<Outline>();

            _outline.effectColor = outlineColor;
            _outline.effectDistance = outlineDistance;
            _outline.useGraphicAlpha = false;
            _outline.enabled = selected;
        }

        // Sin “agrandado” (lo dejamos fijo para que destaque el color/marco)
        var t = transform as RectTransform;
        if (t) t.localScale = Vector3.one;
    }

    // --- Progreso ---
    public void RefreshProgressUI()
    {
        if (Record == null) return;

        int percent = Mathf.RoundToInt(Mathf.Clamp01(Record.progress01) * 100f);

        if (progressText) progressText.text = percent + "%";
        if (progressButton)
        {
            progressButton.gameObject.SetActive(true); // poné false si querés ocultar 0%
            progressButton.onClick.RemoveAllListeners();
            progressButton.onClick.AddListener(OnClick); // el % también selecciona
        }
    }

    void OnClick() => _owner?.OnItemSelected(this);

    public void LoadCoverAsync()
    {
        if (coverImage == null) return;
        coverImage.sprite = null; // limpia

#if UNITY_ANDROID && !UNITY_EDITOR
        // Mejor ruta (local si existe, si no original)
        var best = !string.IsNullOrEmpty(Record?.localPath) && System.IO.File.Exists(Record.localPath)
                    ? Record.localPath
                    : Record?.originalPath;

        if (string.IsNullOrEmpty(best)) return;

        // Render de la página 0
        _thumbTex = PdfRendererAndroid.RenderPage(best, 0, coverLongSide);
        if (_thumbTex != null)
        {
            _thumbSprite = Sprite.Create(_thumbTex, new Rect(0,0,_thumbTex.width,_thumbTex.height),
                                         new Vector2(0.5f,0.5f), 100f);
            coverImage.sprite = _thumbSprite;
            coverImage.preserveAspect = true;
        }
#else
        // En Editor, dejá vacío o asigná un placeholder
#endif
    }

    void OnDestroy()
    {
        if (selectButton) selectButton.onClick.RemoveListener(OnClick);
        if (progressButton) progressButton.onClick.RemoveListener(OnClick);
        if (_thumbSprite) Destroy(_thumbSprite);
        if (_thumbTex) Destroy(_thumbTex);
    }
}
