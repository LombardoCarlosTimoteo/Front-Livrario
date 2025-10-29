using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Interfaz opcional para visores. Si tu visor tiene este método, impleméntala y
/// el script usará la llamada directa (sin reflexión).
public interface IBookPager
{
    int PageCount { get; }
    int CurrentPage { get; }
    void GoToPage(int pageZeroBased);
}

public class ReadingProgressBar : MonoBehaviour
{
    [Header("UI")]
    public Slider slider;                 // 0..1 (normalizado)
    public TMP_Text percentLabel;         // opcional: "Progreso: 26%"

    [Header("Visor del libro (uno de estos)")]
    public MonoBehaviour bookViewer;      // arrastrá tu PDFBookViewer (o similar)
    public string goToPageMethod = "GoToPage"; // si no implementa IBookPager, tratará de invocar esto
    public string totalPagesProp  = "TotalPages";  // usado si no hay IBookPager
    public string currentPageProp = "CurrentPage"; // usado si no hay IBookPager

    [Header("Opciones")]
    public bool updateWhileDragging = true;   // si false, salta al soltar (OnPointerUp -> ver nota)
    public bool wholeNumbersPercent = false;  // etiqueta % redondeada

    bool _ignoreSliderCallback;               // evita bucles

    void Awake()
    {
        if (!slider) slider = GetComponentInChildren<Slider>(true);
        if (slider)
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.onValueChanged.AddListener(OnSliderChanged);
        }
    }

    void OnEnable()
    {
        // Reflejar progreso global
        if (GlobalBookStore.I != null)
            GlobalBookStore.I.OnProgressChanged += HandleProgressChanged;

        // Pintar estado inicial si ya hay libro
        var rec = GlobalBookStore.I?.FindCurrentInLibrary();
        if (rec != null) ApplyToUI(rec.lastPage, rec.pageCount, rec.progress01);
        else ApplyToUI(0, 0, 0f);
    }

    void OnDisable()
    {
        if (GlobalBookStore.I != null)
            GlobalBookStore.I.OnProgressChanged -= HandleProgressChanged;

        if (slider) slider.onValueChanged.RemoveListener(OnSliderChanged);
    }

    // -------- Lectura del visor ----------
    int GetTotalPages()
    {
        if (bookViewer is IBookPager pager) return Mathf.Max(0, pager.PageCount);

        if (bookViewer != null)
        {
            var t = bookViewer.GetType();
            var p = t.GetProperty(totalPagesProp) ?? t.GetProperty("pageCount") ?? t.GetProperty("Pages");
            if (p != null && p.PropertyType == typeof(int))
                return Mathf.Max(0, (int)p.GetValue(bookViewer));
        }

        // Fallback: lo que tengamos en la biblioteca
        var rec = GlobalBookStore.I?.FindCurrentInLibrary();
        return Mathf.Max(0, rec?.pageCount ?? 0);
    }

    void JumpToPage(int page)
    {
        if (bookViewer is IBookPager pager)
        {
            pager.GoToPage(page);
            return;
        }

        if (bookViewer != null)
        {
            var t = bookViewer.GetType();
            // GoToPage(int), SetPage(int), ShowPage(int)…
            var m =
                t.GetMethod(goToPageMethod, new[] { typeof(int) }) ??
                t.GetMethod("SetPage", new[] { typeof(int) }) ??
                t.GetMethod("ShowPage", new[] { typeof(int) }) ??
                t.GetMethod("GotoPage", new[] { typeof(int) });

            if (m != null) { m.Invoke(bookViewer, new object[] { page }); return; }
        }
        Debug.LogWarning("[ReadingProgressBar] No pude invocar GoToPage en el visor.");
    }

    // --------- Evento: cambio de slider ----------
    void OnSliderChanged(float v)
    {
        if (_ignoreSliderCallback) return;
        if (!updateWhileDragging && Input.GetMouseButton(0)) return; // si querés solo al soltar (Editor)

        int total = GetTotalPages();
        if (total <= 0) return;

        // Convertir 0..1 a índice de página (0-based)
        int targetPage = Mathf.Clamp(Mathf.RoundToInt(v * (total - 1)), 0, total - 1);

        // Saltar y persistir progreso global
        JumpToPage(targetPage);
        GlobalBookStore.I?.UpdateProgress(targetPage, total);
        // OnProgressChanged re-sincroniza UI y dispara BrowserUrlFromIsbn
    }

    // --------- Evento: progreso cambió en el store ----------
    void HandleProgressChanged(GlobalBookStore.BookRecord rec)
    {
        if (rec == null) return;
        ApplyToUI(rec.lastPage, rec.pageCount, rec.progress01);
    }

    void ApplyToUI(int pageZero, int total, float progress01)
    {
        // Slider
        if (slider)
        {
            _ignoreSliderCallback = true;
            float val = (total > 1) ? (pageZero / (float)(total - 1)) : 0f;
            slider.value = Mathf.Clamp01(val);
            _ignoreSliderCallback = false;
        }

        // Etiqueta
        if (percentLabel)
        {
            float pct = Mathf.Clamp01(progress01) * 100f;
            percentLabel.text = wholeNumbersPercent
                ? $"Progreso: {Mathf.RoundToInt(pct)}%"
                : $"Progreso: {pct:0.0}%";
        }
    }
}
