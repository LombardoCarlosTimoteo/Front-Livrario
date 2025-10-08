using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BookItemUI : MonoBehaviour
{
    [Header("Refs del prefab")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI genresText;
    public Button selectButton;

    [Header("Resalte (opcional)")]
    public Image background;                 // puede ser la Image del propio botón
    public Color normalColor = Color.white;
    public Color selectedColor = new Color(0.85f, 0.92f, 1f);

    public GlobalBookStore.BookRecord Record { get; private set; }

    LibraryMenuController _owner;
    int _index = -1;

    public void Init(GlobalBookStore.BookRecord rec, LibraryMenuController owner, int index)
    {
        Record = rec;
        _owner = owner;
        _index = index;

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
    }

    public void SetSelected(bool selected)
    {
        if (background) background.color = selected ? selectedColor : normalColor;
    }

    void OnClick()
    {
        // >>> ESTA ES LA LLAMADA CORRECTA <<<
        _owner?.OnItemSelectedIndex(_index);
    }

    void OnDestroy()
    {
        if (selectButton) selectButton.onClick.RemoveListener(OnClick);
    }
}
