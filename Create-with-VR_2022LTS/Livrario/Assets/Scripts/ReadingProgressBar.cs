using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// Interfaz opcional para visores. Si tu visor tiene este método, impleméntala y
/// el script usará la llamada directa (sin reflexión).
public interface IBookPager
{
    int PageCount { get; }
    int CurrentPage { get; }
    void GoToPage(int pageZeroBased);
}

public class ReadingProgressBar : MonoBehaviour, IPointerUpHandler, IEndDragHandler
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
    public bool updateWhileDragging = false;   // si false, salta al soltar
    public bool wholeNumbersPercent = false;   // etiqueta % redondeada

    bool _ignoreSliderCallback;               // evita bucles
    float _lastSliderValue;                   // guarda último valor del slider
    bool _isDragging;                         // indica si el usuario está arrastrando

    void Awake()
    {
        if (!slider) slider = GetComponentInChildren<Slider>(true);
        if (slider)
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            // Durante el arrastre SOLO tocamos la etiqueta (y opcionalmente vista previa)
            slider.onValueChanged.AddListener(OnSliderDragging);
        }
    }

    void OnEnable()
    {
        if (GlobalBookStore.I != null)
            GlobalBookStore.I.OnProgressChanged += HandleProgressChanged;

        // Estado inicial
        var rec = GlobalBookStore.I?.FindCurrentInLibrary();
        if (rec != null) ApplyToUI(rec.lastPage, rec.pageCount, rec.progress01);
        else ApplyToUI(0, 0, 0f);
    }

    void OnDisable()
    {
        if (GlobalBookStore.I != null)
            GlobalBookStore.I.OnProgressChanged -= HandleProgressChanged;

        if (slider) slider.onValueChanged.RemoveListener(OnSliderDragging);
        _isDragging = false;
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

    // --------- Arrastre del slider ----------
    void OnSliderDragging(float v)
    {
        if (_ignoreSliderCallback) return;

        _lastSliderValue = v;
        _isDragging = true;

        // Actualiza solo la etiqueta durante el arrastre (liviano)
        UpdateLabelOnly(v);

        if (updateWhileDragging)
        {
            // Vista previa opcional (no recomendado con PDFs pesados)
            ApplyJump(v);
        }
    }

    // --------- Al soltar el slider ----------
    public void OnPointerUp(PointerEventData eventData) => OnPointerUp(); // interfaz
    public void OnEndDrag(PointerEventData eventData) => OnPointerUp();   // por si termina drag fuera

    // Método público por si usás EventTrigger → Pointer Up
    public void OnPointerUp()
    {
        if (!_isDragging) return;
        _isDragging = false;
        ApplyJump(_lastSliderValue);
    }

    void ApplyJump(float v)
    {
        int total = GetTotalPages();
        if (total <= 0) return;

        int targetPage = Mathf.Clamp(Mathf.RoundToInt(v * (total - 1)), 0, total - 1);
        JumpToPage(targetPage);
        GlobalBookStore.I?.UpdateProgress(targetPage, total);
    }

    void UpdateLabelOnly(float v)
    {
        if (percentLabel)
        {
            float pct = Mathf.Clamp01(v) * 100f;
            percentLabel.text = wholeNumbersPercent
                ? $"Progreso: {Mathf.RoundToInt(pct)}%"
                : $"Progreso: {pct:0.0}%";
        }
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
