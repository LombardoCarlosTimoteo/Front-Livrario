using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ISBNDisplay : MonoBehaviour
{
    [Header("Destino de texto")]
    public TMP_Text tmpLabel; // Asigná un TextMeshProUGUI
    public Text uiText;       // O un Text (legacy), si no usás TMP

    [Header("Formato")]
    public string prefix = "ISBN: ";
    public bool hideIfEmpty = false;

    void OnEnable()
    {
        Refresh();
        if (GlobalBookStore.I != null)
            GlobalBookStore.I.OnMetadataChanged += HandleMetadataChanged;
    }

    void OnDisable()
    {
        if (GlobalBookStore.I != null)
            GlobalBookStore.I.OnMetadataChanged -= HandleMetadataChanged;
    }

    void HandleMetadataChanged(GlobalBookStore.BookRecord _)
    {
        Refresh();
    }

    public void Refresh()
    {
        string value = (GlobalBookStore.I != null) ? (GlobalBookStore.I.isbn ?? "") : "";
        bool empty = string.IsNullOrEmpty(value);
        string text = empty ? (prefix + "—") : (prefix + value);

        if (tmpLabel) tmpLabel.text = text;
        if (uiText) uiText.text = text;

        if (hideIfEmpty) gameObject.SetActive(!empty);
    }
}
