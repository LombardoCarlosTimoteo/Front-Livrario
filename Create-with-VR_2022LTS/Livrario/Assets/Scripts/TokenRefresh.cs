using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Helper mínimo para asegurar que exista un access token válido.
/// - Si el access no existe, intenta cargar tokens cifrados con TokenManager.
/// - Si el access está vencido, hace POST a /api/auth/token/refresh/ usando el refresh guardado.
/// - Actualiza UserSession y TokenManager.SaveTokens() si el refresh es exitoso.
/// </summary>
public static class TokenRefresh
{
    // Usá el mismo endpoint que en tu LoginUI
    private const string DefaultRefreshUrl = "https://login.nicolasirigoyen.com.ar/api/auth/token/refresh/";
    private const int TimeoutSec = 12;

    [Serializable] private class RefreshReq { public string refresh; }
    [Serializable] private class RefreshResp { public string access; public string refresh; }

    /// <summary>
    /// Garantiza que haya un access válido en UserSession.access.
    /// Llama onDone(true) si está todo OK (no vencido o fue renovado).
    /// Llama onDone(false) si no hay refresh o falla el endpoint.
    /// </summary>
    public static IEnumerator EnsureValidAccess(string refreshUrl = null, Action<bool> onDone = null)
    {
        // 1) Si no hay tokens en memoria, intento cargarlos de PlayerPrefs (cifrados)
        if (string.IsNullOrEmpty(UserSession.access) || string.IsNullOrEmpty(UserSession.refresh))
        {
            if (TokenManager.TryLoadTokens(out var access, out var refresh))
            {
                UserSession.access = access;
                UserSession.refresh = refresh;
                // También podés restaurar perfil no sensible si querés:
                UserSession.LoadProfileFromPrefsIfAvailable();
                Debug.Log("[TokenRefresh] Tokens cargados desde almacenamiento cifrado.");
            }
        }

        // 2) Si ahora no hay access, no podemos seguir
        if (string.IsNullOrEmpty(UserSession.access))
        {
            Debug.LogWarning("[TokenRefresh] No hay access en memoria ni en almacenamiento.");
            onDone?.Invoke(false);
            yield break;
        }

        // 3) Si no está vencido, estamos OK
        if (!JwtUtils.IsExpired(UserSession.access))
        {
            onDone?.Invoke(true);
            yield break;
        }

        // 4) Está vencido → necesito un refresh válido
        if (string.IsNullOrEmpty(UserSession.refresh))
        {
            Debug.LogWarning("[TokenRefresh] Access vencido y no hay refresh disponible.");
            onDone?.Invoke(false);
            yield break;
        }

        // 5) Intento refrescar
        string url = string.IsNullOrWhiteSpace(refreshUrl) ? DefaultRefreshUrl : refreshUrl.Trim();
        if (!url.EndsWith("/")) url += "/";

        var reqObj = new RefreshReq { refresh = UserSession.refresh };
        string json = JsonUtility.ToJson(reqObj);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = TimeoutSec;

            Debug.Log($"[TokenRefresh] POST refresh -> {url}");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success &&
                req.responseCode >= 200 && req.responseCode < 300)
            {
                try
                {
                    var body = req.downloadHandler.text;
                    var resp = JsonUtility.FromJson<RefreshResp>(body);

                    if (!string.IsNullOrEmpty(resp.access))
                    {
                        UserSession.access = resp.access;
                        if (!string.IsNullOrEmpty(resp.refresh))
                            UserSession.refresh = resp.refresh; // por si el backend rota refresh

                        TokenManager.SaveTokens(UserSession.access, UserSession.refresh);
                        Debug.Log("[TokenRefresh] Refresh OK. Access renovado.");
                        onDone?.Invoke(true);
                        yield break;
                    }

                    Debug.LogError("[TokenRefresh] Refresh sin 'access' en la respuesta.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TokenRefresh] Error parseando refresh JSON: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[TokenRefresh] Refresh falló. code={(long)req.responseCode}, err={req.error}, body={req.downloadHandler?.text}");
            }
        }

        onDone?.Invoke(false);
    }

    /// <summary>
    /// Ejemplo opcional: hace un POST con JSON agregando Authorization: Bearer ...,
    /// asegurando antes que el access esté vigente.
    /// Si no querés helpers de request, podés ignorar este método.
    /// </summary>
    public static IEnumerator AuthorizedPostJson(
        string url, string jsonBody, Action<UnityWebRequest> onDone,
        string refreshUrl = null, int timeoutSec = 12)
    {
        bool ok = false;
        yield return EnsureValidAccess(refreshUrl, (res) => ok = res);

        if (!ok)
        {
            Debug.LogWarning("[TokenRefresh] No hay sesión válida para AuthorizedPostJson.");
            onDone?.Invoke(null);
            yield break;
        }

        using (var req = new UnityWebRequest(url, "POST"))
        {
            if (!string.IsNullOrEmpty(jsonBody))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                req.SetRequestHeader("Content-Type", "application/json");
            }

            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSec;
            req.SetRequestHeader("Authorization", "Bearer " + UserSession.access);

            yield return req.SendWebRequest();
            onDone?.Invoke(req);
        }
    }
}
