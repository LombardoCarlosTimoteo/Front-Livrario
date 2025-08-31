using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class NetProbe : MonoBehaviour
{
    [SerializeField] string testHost = "https://login.nicolasirigoyen.com.ar";
    [SerializeField] string testPath = "/api/auth/login/"; // GET puede dar 405 y está bien

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        Debug.Log($"[NetProbe] internetReachability = {Application.internetReachability}");

        // 1) ¿Internet general?
        using (var r = UnityWebRequest.Get("https://www.google.com/generate_204"))
        {
            yield return r.SendWebRequest();
            Debug.Log($"[NetProbe] google 204 -> result={r.result}, code={(long)r.responseCode}, error='{r.error}'");
        }

        // 2) ¿Tu host responde (DNS + TLS OK)?
        string url = (testHost + testPath).Trim();
        using (var r = UnityWebRequest.Get(url))
        {
            Debug.Log($"[NetProbe] Probing '{url}' ...");
            yield return r.SendWebRequest();
            Debug.Log($"[NetProbe] host probe -> result={r.result}, code={(long)r.responseCode}, error='{r.error}'");
            // Si ves code 200/301/302/401/403/404/405: DNS y TLS OK (aunque sea error funcional).
            // Si ves 'Cannot resolve destination host': problema de DNS/red.
            // Si ves 'certificate' algo: problema de SSL/certificado/hora del dispositivo.
        }
    }
}
