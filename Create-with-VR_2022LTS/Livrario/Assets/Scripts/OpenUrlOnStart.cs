using System.Collections;
using UnityEngine;
using TLab.WebView;

public class OpenUrlOnStart : MonoBehaviour
{
    public WebView webView; // arrastrá el componente "Web View (Script)" de View
    public string url = "http://chatbot.nicolasirigoyen.com.ar/chatbot/UkrfHZq08auMUJW4";

    private IEnumerator Start()
    {
        if (!webView) webView = GetComponentInChildren<WebView>(true);
        if (!webView) { Debug.LogError("No encontré TLab.WebView.WebView."); yield break; }

        webView.Init();
        yield return new WaitUntil(() => webView.IsInitialized());

        // Navegar (compatible en todas las versiones)
        var safe = url.Replace("\\", "\\\\").Replace("'", "\\'");
        webView.EvaluateJS($"window.location.href='{safe}';");
        // Si tu versión tiene método directo, también podés:
        // webView.LoadURL(url);
    }
}
