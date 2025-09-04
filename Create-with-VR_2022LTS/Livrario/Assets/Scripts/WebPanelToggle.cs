using UnityEngine;

public class WebPanelToggle : MonoBehaviour
{
    public GameObject webCanvas;  // arrastrá el GO raíz del canvas web

    // (Opcional) recordar y restaurar la pose original por si otro script la mueve
    public bool restoreInitialTransform = true;
    Vector3 _pos; Quaternion _rot; Vector3 _scale;

    void Awake()
    {
        if (!webCanvas) return;
        _pos = webCanvas.transform.position;
        _rot = webCanvas.transform.rotation;
        _scale = webCanvas.transform.localScale;
        webCanvas.SetActive(false); // empieza oculto
    }

    // Botón "Abrir Chatbot"
    public void Open()
    {
        if (!webCanvas) return;
        if (restoreInitialTransform)
        {
            webCanvas.transform.SetPositionAndRotation(_pos, _rot);
            webCanvas.transform.localScale = _scale;
        }
        webCanvas.SetActive(true);  // <-- no movemos nada
    }

    // Botón "Ocultar"
    public void Hide()
    {
        if (webCanvas) webCanvas.SetActive(false);
    }
}
