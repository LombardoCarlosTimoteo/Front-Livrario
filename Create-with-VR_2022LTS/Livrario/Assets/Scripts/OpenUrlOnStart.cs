using UnityEngine;
using TLab.WebView;

public class OpenUrlOnStart : MonoBehaviour
{
    public BrowserContainer container;
    [TextArea]
    public string url =
        "http://chatbot.nicolasirigoyen.com.ar/chatbot/UkrfHZq08auMUJW4";
    void Start()
    {
        if (container && !string.IsNullOrWhiteSpace(url)) container.Open(url);
    }
}
