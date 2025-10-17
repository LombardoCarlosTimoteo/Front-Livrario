using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ProgressDisplay : MonoBehaviour
{
    [Header("Destino de texto")]
    public TMP_Text tmpLabel;   // Asigná un TextMeshProUGUI (por ej. el del botón)
    public Text uiText;         // O un Text (legacy)

    [Header("Formato")]
    public string prefix = "Progreso: ";
    public Mode mode = Mode.Percent;   // Percent | Fraction | Both
    public bool hideIfEmpty = false;   // Ocultar si no hay datos
    public bool showZeroAsDash = false;// Mostrar “—” cuando es 0%

    public enum Mode { Percent, Fraction, Both }

    void OnEnable()
    {
        Refresh();
        if (GlobalBookStore.I != null)
        {
            // Progreso cambia con UpdateProgress / EnsurePageCount
            GlobalBookStore.I.OnProgressChanged += HandleProgressChanged;
            // Por si cambia el pageCount desde metadata
            GlobalBookStore.I.OnMetadataChanged += HandleMetadataChanged;
        }
    }

    void OnDisable()
    {
        if (GlobalBookStore.I != null)
        {
            GlobalBookStore.I.OnProgressChanged -= HandleProgressChanged;
            GlobalBookStore.I.OnMetadataChanged -= HandleMetadataChanged;
        }
    }

    void HandleProgressChanged(GlobalBookStore.BookRecord _) => Refresh();
    void HandleMetadataChanged(GlobalBookStore.BookRecord _) => Refresh();

    public void Refresh()
    {
        var rec = GlobalBookStore.I?.FindCurrentInLibrary();

        bool has = rec != null && rec.pageCount > 0;
        string body = "—";

        if (has)
        {
            int percent = Mathf.RoundToInt(Mathf.Clamp01(rec.progress01) * 100f);
            int page = rec.lastPage + 1;
            int total = Mathf.Max(1, rec.pageCount);

            if (showZeroAsDash && percent == 0)
            {
                body = "—";
            }
            else
            {
                switch (mode)
                {
                    case Mode.Percent: body = percent + "%"; break;
                    case Mode.Fraction: body = $"{page}/{total}"; break;
                    case Mode.Both: body = $"{percent}% ({page}/{total})"; break;
                }
            }
        }

        string final = string.IsNullOrEmpty(prefix) ? body : (prefix + body);
        if (tmpLabel) tmpLabel.text = final;
        if (uiText) uiText.text = final;

        if (hideIfEmpty) gameObject.SetActive(has);
    }
}
