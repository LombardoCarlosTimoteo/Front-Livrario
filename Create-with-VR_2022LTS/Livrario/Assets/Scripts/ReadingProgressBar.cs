using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ReadingProgressBar : MonoBehaviour
{
    [Header("Asignaciones (elige Slider o Image fill)")]
    [SerializeField] Slider slider;                 // Opción 1: Slider UI (min=0, max=1)
    [SerializeField] Image fillImage;              // Opción 2: Image con Type=Filled (Horizontal)
    [SerializeField] TMP_Text percentText;          // (opcional) 0–100%

    [Header("Comportamiento")]
    [SerializeField] bool hideWhenZero = false;     // ocultar si 0%
    [SerializeField] bool smooth = false;           // animar suavizado
    [SerializeField] float smoothSpeed = 8f;

    float _current01 = 0f;
    float _target01 = 0f;

    void OnEnable()
    {
        // Valor inicial desde la lib
        SetFromLibraryOnce();

        // Suscribirse a cambios de progreso
        if (GlobalBookStore.I != null)
            GlobalBookStore.I.OnProgressChanged += HandleProgressChanged;
    }

    void OnDisable()
    {
        if (GlobalBookStore.I != null)
            GlobalBookStore.I.OnProgressChanged -= HandleProgressChanged;
    }

    void Update()
    {
        if (!smooth) return;
        if (Mathf.Approximately(_current01, _target01)) return;

        _current01 = Mathf.MoveTowards(_current01, _target01, smoothSpeed * Time.deltaTime);
        ApplyUI(_current01);
    }

    // --- API pública por si querés setear manualmente ---
    public void SetProgress01(float v01)
    {
        v01 = Mathf.Clamp01(v01);
        _target01 = v01;
        if (!smooth)
        {
            _current01 = v01;
            ApplyUI(_current01);
        }
    }

    // --- Internos ---
    void HandleProgressChanged(GlobalBookStore.BookRecord rec)
    {
        // Solo nos importa el libro "actual"
        var cur = GlobalBookStore.I?.FindCurrentInLibrary();
        if (cur == null) return;

        // Coincidencia por referencia o por path
        bool same = ReferenceEquals(cur, rec)
                    || (!string.IsNullOrEmpty(cur.localPath) && cur.localPath == rec.localPath)
                    || (!string.IsNullOrEmpty(cur.originalPath) && cur.originalPath == rec.originalPath);

        if (!same) return;

        SetProgress01(rec.progress01);
    }

    void SetFromLibraryOnce()
    {
        var cur = GlobalBookStore.I?.FindCurrentInLibrary();
        if (cur != null) SetProgress01(cur.progress01);
        else ApplyUI(0f);
    }

    void ApplyUI(float v01)
    {
        if (slider) slider.value = v01;
        if (fillImage) fillImage.fillAmount = v01;
        if (percentText) percentText.text = Mathf.RoundToInt(v01 * 100f) + "%";

        if (hideWhenZero)
        {
            bool show = v01 > 0f;
            if (slider) slider.gameObject.SetActive(show);
            if (fillImage) fillImage.gameObject.SetActive(show);
            if (percentText) percentText.gameObject.SetActive(show);
        }
    }
}
