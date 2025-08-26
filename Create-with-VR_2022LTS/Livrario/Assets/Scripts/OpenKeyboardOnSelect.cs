using TMPro;
using UnityEngine;

public class OpenKeyboardOnSelect : MonoBehaviour
{
    public TMP_InputField field;

    void Awake()
    {
        if (!field)
            field = GetComponent<TMP_InputField>();

        field.onSelect.AddListener(_ =>
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            TouchScreenKeyboard.Open(field.text, TouchScreenKeyboardType.Default, false, false, false, false);
#endif
        });
    }
}
