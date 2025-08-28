using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class MobileKeyboardForTMP : MonoBehaviour, IPointerClickHandler, ISelectHandler
{
    [Header("Asigná el TMP_InputField (o se autodescubre en Awake)")]
    public TMP_InputField field;

    private TouchScreenKeyboard keyboard;

    void Awake()
    {
        if (!field) field = GetComponent<TMP_InputField>();
        if (!field)
        {
            Debug.LogError("[Keyboard] No se encontró TMP_InputField en este objeto.");
            enabled = false;
            return;
        }

        // En Android/Quest, mostrar overlay si aplica
        field.shouldHideMobileInput = false;

        Debug.Log($"[Keyboard] Inicializado en '{gameObject.name}'. " +
                  $"Password={(field.contentType == TMP_InputField.ContentType.Password)}");
    }

    // Click del rayo (gatillo) sobre el input
    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"[Keyboard] Click detectado en '{gameObject.name}'.");
        OpenKeyboard();
    }

    // Cuando el input gana foco (por navegación o click)
    public void OnSelect(BaseEventData eventData)
    {
        Debug.Log($"[Keyboard] Select detectado en '{gameObject.name}'.");
        OpenKeyboard();
    }

    private void OpenKeyboard()
    {
        // Aseguramos foco en el campo SIEMPRE (también en Editor)
        field.ActivateInputField();

#if UNITY_ANDROID && !UNITY_EDITOR
        bool isPassword = field.contentType == TMP_InputField.ContentType.Password;

        Debug.Log($"[Keyboard] Abriendo teclado en '{gameObject.name}'. isPassword={isPassword}");
        keyboard = TouchScreenKeyboard.Open(
            field.text,
            TouchScreenKeyboardType.Default,
            false,   // autocorrection
            false,   // multiline
            isPassword, // secure
            false    // alert
        );
#else
        Debug.Log("[Keyboard] (Editor/Plataforma no-Android) No se abre teclado del sistema.");
#endif
    }

    void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (keyboard != null)
        {
            // Reflejar texto que escribe el usuario
            if (field.text != keyboard.text)
            {
                field.text = keyboard.text;
                // Evitá loguear cada frame si molesta; útil para confirmar que llega input
                Debug.Log($"[Keyboard] Texto actualizado '{gameObject.name}': \"{keyboard.text}\"");
            }

            // Done / Cancel cierra el teclado
            if (keyboard.status == TouchScreenKeyboard.Status.Done ||
                keyboard.status == TouchScreenKeyboard.Status.Canceled)
            {
                Debug.Log($"[Keyboard] Teclado cerrado en '{gameObject.name}'. Status={keyboard.status}");
                field.DeactivateInputField();
                keyboard = null;
            }
        }
#endif
    }

    void OnDisable()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (keyboard != null)
        {
            Debug.Log($"[Keyboard] OnDisable: cerrando teclado en '{gameObject.name}'.");
            keyboard.active = false;
            keyboard = null;
        }
#endif
    }
}
