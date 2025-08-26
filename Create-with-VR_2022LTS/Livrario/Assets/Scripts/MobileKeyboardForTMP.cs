using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class MobileKeyboardForTMP : MonoBehaviour, IPointerClickHandler, ISelectHandler
{
    public TMP_InputField field;
    private TouchScreenKeyboard keyboard;

    void Awake()
    {
        if (!field) field = GetComponent<TMP_InputField>();
        // En Android/Quest, mostrar el input overlay del sistema (por si aplica)
        field.shouldHideMobileInput = false;
    }

    public void OnPointerClick(PointerEventData eventData) { OpenKeyboard(); }
    public void OnSelect(BaseEventData eventData) { OpenKeyboard(); }

    private void OpenKeyboard()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Aseguramos foco en el campo
        field.ActivateInputField();
        // Abrimos teclado; si es password, ocultamos
        bool isPassword = field.contentType == TMP_InputField.ContentType.Password;
        keyboard = TouchScreenKeyboard.Open(
            field.text,
            TouchScreenKeyboardType.Default,
            false,   // autocorrection
            false,   // multiline
            isPassword, // secure
            false    // alert
        );
#endif
    }

    void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (keyboard != null)
        {
            // reflejar el texto mientras escribe
            if (field.text != keyboard.text)
                field.text = keyboard.text;

            // si tocó "Done", cerramos y perdemos foco
            if (keyboard.status == TouchScreenKeyboard.Status.Done ||
                keyboard.status == TouchScreenKeyboard.Status.Canceled)
            {
                field.DeactivateInputField();
                keyboard = null;
            }
        }
#endif
    }
}
