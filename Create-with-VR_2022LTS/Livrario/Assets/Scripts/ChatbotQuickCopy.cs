using UnityEngine;

public class ChatbotQuickCopy : MonoBehaviour
{

    public void OnCopyIsbn()
    {
        var g = GlobalBookStore.I;
        string isbn = g?.isbn;
        if (string.IsNullOrEmpty(isbn))
        {
            var rec = g?.FindCurrentInLibrary();
            isbn = rec?.isbn;
        }
        if (string.IsNullOrEmpty(isbn))
        {
            Debug.LogWarning("[Chatbot] No hay ISBN cargado.");
            return;
        }
        AndroidClipboard.SetText(isbn);
    }

    public void OnCopyProgressPercent()
    {
        var rec = GlobalBookStore.I?.FindCurrentInLibrary();
        int percent = Mathf.RoundToInt(Mathf.Clamp01(rec?.progress01 ?? 0f) * 100f);
        AndroidClipboard.SetText(percent.ToString());
    }
}
