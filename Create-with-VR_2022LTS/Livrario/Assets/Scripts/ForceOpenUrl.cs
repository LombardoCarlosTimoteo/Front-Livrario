using System.Collections;
using UnityEngine;
using TLab.WebView;

public class ForceOpenUrl : MonoBehaviour
{
    public WebView webView;                 // arrastra aquí el componente "Web View (Script)"
    public string url = "https://www.wikipedia.org";

    private IEnumerator Start()
    {
        if (!webView) webView = GetComponent<WebView>();
        if (!webView) { Debug.LogError("[ForceOpenUrl] no hay WebView."); yield break; }

        webView.Init();
        yield return new WaitUntil(() => webView.IsInitialized());

        // Asegura color visible (por si el RawImage quedó transparente)
        var raw = webView.rawImage;
        if (raw) raw.color = Color.white;

        // Navegación
        var safe = url.Replace("\\", "\\\\").Replace("'", "\\'");
        webView.EvaluateJS($"window.location.href='{safe}';");
        // Si tu versión tiene método directo, también podés:
        // webView.LoadURL(url);
    }
}
