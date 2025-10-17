using System.Collections;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ProgressCopy : MonoBehaviour
{
    [Header("Label donde mostrás el progreso (ej. 'Progreso: 26%')")]
    public TMP_Text labelTMP;
    public Text labelUGUI;

    [Header("Texto")]
    public string labelPrefix = "Progreso: ";
    public string copiedMessage = "¡Progreso copiado!";
    public float copiedSeconds = 2f;

    [Tooltip("Si hay label, extrae de allí los dígitos; si no, calcula desde GlobalBookStore.")]
    public bool preferLabel = true;

    Coroutine _flashCo;

    void Awake()
    {
        GetComponent<Button>().onClick.RemoveAllListeners();
        GetComponent<Button>().onClick.AddListener(Copy);
    }

    public void Copy()
    {
        int percent = GetPercent();
        GUIUtility.systemCopyBuffer = percent.ToString(); // ← SOLO número
        Debug.Log("[ProgressCopy] Copiado: " + percent);

        // Feedback visual en el mismo label y luego restaurar "Progreso: XX%"
        if (_flashCo != null) StopCoroutine(_flashCo);
        _flashCo = StartCoroutine(FlashProgress(percent));
    }

    IEnumerator FlashProgress(int originalPercent)
    {
        string restore = $"{labelPrefix}{originalPercent}%";
        SetLabel(copiedMessage);
        yield return new WaitForSeconds(copiedSeconds);
        SetLabel(restore);
        _flashCo = null;
    }

    int GetPercent()
    {
        // 1) Intentar extraer dígitos del label (soporta '26%', 'Progreso 26 %', etc.)
        string txt = labelTMP ? labelTMP.text : (labelUGUI ? labelUGUI.text : null);
        if (preferLabel && !string.IsNullOrEmpty(txt))
        {
            var m = Regex.Match(txt, @"\d+");
            if (m.Success && int.TryParse(m.Value, out int n)) return Mathf.Clamp(n, 0, 100);
        }

        // 2) Calcular desde la librería
        var rec = GlobalBookStore.I?.FindCurrentInLibrary();
        if (rec == null || rec.pageCount <= 0) return 0;
        return Mathf.RoundToInt(Mathf.Clamp01(rec.progress01) * 100f);
    }

    void SetLabel(string s)
    {
        if (labelTMP) labelTMP.text = s;
        if (labelUGUI) labelUGUI.text = s;
    }
}
