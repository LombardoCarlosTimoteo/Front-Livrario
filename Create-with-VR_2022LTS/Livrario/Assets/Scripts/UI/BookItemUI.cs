using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BookItemUI : MonoBehaviour
{
    [Header("Refs del prefab")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI genresText;
    public Button selectButton;

    [Header("Cover (carátula)")]
    public Image coverImage;            // ← arrastrá la Image "Cover"
    [Tooltip("Resolución larga para la miniatura (p.ej. 512).")]
    public int coverLongSide = 512;

    [Header("Resalte (opcional)")]
    public Image background;            // puede ser la Image del propio botón
    public Color normalColor = Color.white;
    public Color selectedColor = new Color(0.85f, 0.92f, 1f);

    // Datos
    public GlobalBookStore.BookRecord Record { get; private set; }

    // Owner que manejará la selección
    LibraryMenuController _owner;

    // Guardamos refs para limpiar
    Texture2D _thumbTex;
    Sprite _thumbSprite;

    /// Llamado por el controller cuando instancia el ítem
    public void Init(GlobalBookStore.BookRecord rec, LibraryMenuController owner, int index)
    {
        Record = rec;
        _owner = owner;

        if (titleText) titleText.text = string.IsNullOrEmpty(rec.title) ? rec.fileName : rec.title;
        if (genresText) genresText.text = (rec.genres != null && rec.genres.Length > 0)
                                            ? string.Join(" · ", rec.genres)
                                            : "—";

        if (selectButton)
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(OnClick);
        }

        SetSelected(false);

        // Lanza carga de carátula
        LoadCoverAsync();
    }

    public void SetSelected(bool selected)
    {
        if (background)
            background.color = selected ? selectedColor : normalColor;
    }

    void OnClick()
    {
        _owner?.OnItemSelected(this);
    }

    public void LoadCoverAsync()
    {
        if (coverImage == null) return;
        coverImage.sprite = null; // limpia

#if UNITY_ANDROID && !UNITY_EDITOR
        // Selecciona la mejor ruta (local si existe, si no original)
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
    coverImage.preserveAspect = true; // ✅
}
#else
        // En Editor, no hay render real → dejá vacío o un placeholder
        // (Si querés, podés asignar un sprite por defecto)
#endif
    }

    void OnDestroy()
    {
        if (selectButton) selectButton.onClick.RemoveListener(OnClick);
        if (_thumbSprite) Destroy(_thumbSprite);
        if (_thumbTex) Destroy(_thumbTex);
    }
}
