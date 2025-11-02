using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;                     // usamos TMP directamente
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Button))]
public class CargarLibroController : MonoBehaviour
{
    [Header("UI (se autocompleta si lo dejas vacío)")]
    [SerializeField] private Button boton;        // este mismo botón
    [SerializeField] private TMP_Text labelTMP;   // hijo con texto (TMP)
    [SerializeField] private Text labelUGUI;      // por si usas Text normal
    [SerializeField] private string textoDefault = "Cargar libro";

    [Header("Lógica")]
    [SerializeField] private bool autoTeleportPolicial = true;

    void Awake()
    {
        if (!boton) boton = GetComponent<Button>();
        if (!labelTMP) labelTMP = GetComponentInChildren<TMP_Text>(true);
        if (!labelUGUI) labelUGUI = GetComponentInChildren<Text>(true);
        SetButtonText(textoDefault);
        Debug.Log($"[CargarLibro] Autowired TMP={(labelTMP != null)} UGUI={(labelUGUI != null)}");
    }

    void SetButtonText(string s)
    {
        if (labelTMP) labelTMP.SetText(s);
        if (labelUGUI) labelUGUI.text = s;
        Debug.Log($"[CargarLibro] SetButtonText -> {s}");
    }

    string Acortar(string nombre, int max = 26)
    {
        if (string.IsNullOrEmpty(nombre)) return textoDefault;
        var baseName = Path.GetFileNameWithoutExtension(nombre);
        return (baseName.Length <= max) ? baseName : (baseName.Substring(0, max - 3) + "...");
    }

    // Conectá ESTE método al OnClick del botón
    public void OnBtnCargarLibro()
    {
        SetButtonText("Abriendo...");
        boton.interactable = false;

        string[] mimes = { "application/pdf" };
        NativeFilePicker.PickFile((path) =>
        {
            boton.interactable = true;

            if (string.IsNullOrEmpty(path))
            {
                SetButtonText(textoDefault);   // cancelado
                return;
            }

            GlobalBookStore.I.SetFromPickerPath(path);

            // Mostrar el nombre del PDF en el botón
            SetButtonText(Acortar(GlobalBookStore.I.FileName));

        }, mimes);
    }
}
