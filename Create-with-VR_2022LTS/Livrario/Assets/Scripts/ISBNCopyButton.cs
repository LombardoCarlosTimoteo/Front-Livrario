using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ISBNCopyButton : MonoBehaviour
{
    [Header("Botón (si se deja vacío usa el propio)")]
    public Button button;

    [Header("Label principal (donde se ve 'ISBN: 978...')")]
    public TMP_Text isbnLabelTMP;
    public Text isbnLabelUGUI;
    public string labelPrefix = "ISBN: ";

    [Header("Overlay opcional de feedback (distinto del principal)")]
    public TMP_Text feedbackLabel;          // si lo dejas vacío, se reusa el principal
    public float feedbackSeconds = 1.2f;
    public string copiedMessage = "¡ISBN copiado!";
    public string emptyMessage = "Sin ISBN";

    Coroutine _co;

    void Awake()
    {
        if (!button) button = GetComponent<Button>();
        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(Copy);
        }
    }

    void OnDestroy()
    {
        if (button) button.onClick.RemoveListener(Copy);
    }

    void Copy()
    {
        string isbn = GetIsbn();
        if (string.IsNullOrEmpty(isbn))
        {
            ShowFeedback(emptyMessage, restoreIsbn: null);
            Debug.LogWarning("[ISBNCopy] No hay ISBN actual para copiar.");
            return;
        }

        // Copia solo el número
        try { GUIUtility.systemCopyBuffer = isbn; } catch { }
        Debug.Log("[ISBNCopy] Copiado: " + isbn);

        ShowFeedback(copiedMessage, restoreIsbn: isbn);
    }

    string GetIsbn()
    {
        if (GlobalBookStore.I == null) return null;

        // 1) ISBN global actual
        var v = GlobalBookStore.I.isbn;
        if (!string.IsNullOrEmpty(v)) return v;

        // 2) Intentar por el record actual de la biblioteca
        var rec = GlobalBookStore.I.FindCurrentInLibrary();
        return rec != null ? rec.isbn : null;
    }

    // --- Feedback: overlay si existe 'feedbackLabel', si no, flashear en el principal ---
    void ShowFeedback(string msg, string restoreIsbn)
    {
        if (_co != null) StopCoroutine(_co);

        if (feedbackLabel != null && feedbackLabel != isbnLabelTMP)
        {
            _co = StartCoroutine(CoOverlay(msg));
        }
        else
        {
            _co = StartCoroutine(CoFlashInMain(msg, restoreIsbn));
        }
    }

    IEnumerator CoOverlay(string msg)
    {
        feedbackLabel.text = msg;
        feedbackLabel.gameObject.SetActive(true);
        yield return new WaitForSeconds(feedbackSeconds);
        feedbackLabel.gameObject.SetActive(false);   // solo ocultamos el overlay
        _co = null;
    }

    IEnumerator CoFlashInMain(string msg, string restoreIsbn)
    {
        // Guardamos el texto actual del label principal
        string restoreText = GetMainLabelText();

        // Mostrar mensaje temporal en el mismo label
        SetMainLabelText(msg);
        yield return new WaitForSeconds(feedbackSeconds);

        // Restaurar
        if (!string.IsNullOrEmpty(restoreIsbn))
            SetMainLabelText(labelPrefix + restoreIsbn);
        else
            SetMainLabelText(restoreText);

        _co = null;
    }

    // --- Helpers para el label principal ---
    string GetMainLabelText()
    {
        if (isbnLabelTMP) return isbnLabelTMP.text;
        if (isbnLabelUGUI) return isbnLabelUGUI.text;
        return "";
    }

    void SetMainLabelText(string s)
    {
        if (isbnLabelTMP) isbnLabelTMP.text = s;
        if (isbnLabelUGUI) isbnLabelUGUI.text = s;
        // MUY IMPORTANTE: nunca desactivamos el GameObject del label principal.
    }
}
