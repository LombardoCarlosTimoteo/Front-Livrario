using System.Collections;
using UnityEngine;
using TMPro;

public class HUDToast : MonoBehaviour
{
    [SerializeField] GameObject panel;          // arrastra UI_Toast
    [SerializeField] TMP_Text label;          // arrastra Txt
    [SerializeField] float autoHideSeconds = 2f;

    Coroutine running;

    void Awake() { if (panel) panel.SetActive(false); }

    public void Show(string message, float duration = -1f)
    {
        if (!panel || !label) return;
        if (running != null) StopCoroutine(running);
        label.text = message;
        panel.SetActive(true);
        running = StartCoroutine(HideAfter(duration < 0 ? autoHideSeconds : duration));
    }

    IEnumerator HideAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        panel.SetActive(false);
        running = null;
    }
}
