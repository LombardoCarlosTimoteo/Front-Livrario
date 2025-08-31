using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class LoginUI : MonoBehaviour
{
    [Header("Referencias UI")]
    [SerializeField] TMP_InputField inputUsuarioEmail;
    [SerializeField] TMP_InputField inputPassword;
    [SerializeField] Button btnIniciar;
    [SerializeField] TextMeshProUGUI txtEstado;

    [Header("Navegación")]
    [SerializeField] UIAuthSwitcher switcher;       // arrastrá el objeto que tiene UIAuthSwitcher
    [SerializeField] GameObject panelMainMenu;      // opcional, fallback si no usás switcher

    [Header("Backend")]
    [SerializeField] string loginUrl = "https://login.nicolasirigoyen.com.ar/api/auth/login/";
    [SerializeField] string refreshUrl = "https://login.nicolasirigoyen.com.ar/api/auth/token/refresh/";

    // Defaults por código (si el Inspector viene vacío)
    const string DefaultLoginUrl = "https://login.nicolasirigoyen.com.ar/api/auth/login/";
    const string DefaultRefreshUrl = "https://login.nicolasirigoyen.com.ar/api/auth/token/refresh/";

    // Retries y timeouts
    const int kMaxRetries = 3;
    const int kTimeoutSec = 10;
    const float kRetryDelay = 0.4f;

    // Guardas de estado
    bool isLoggingIn = false;

    void Awake()
    {
        Debug.Log("[LoginUI] Awake()");
        if (btnIniciar != null)
        {
            btnIniciar.onClick.RemoveAllListeners();
            btnIniciar.onClick.AddListener(OnPressIniciar);
        }
        else
        {
            Debug.LogWarning("[LoginUI] btnIniciar es NULL. Asignalo en el Inspector.");
        }

        if (switcher == null) switcher = FindObjectOfType<UIAuthSwitcher>(); // fallback

        // Log de referencias
        Debug.Log($"[LoginUI] Refs -> email:{(inputUsuarioEmail ? inputUsuarioEmail.name : "NULL")} " +
                  $"pass:{(inputPassword ? inputPassword.name : "NULL")} " +
                  $"btn:{(btnIniciar ? btnIniciar.name : "NULL")} " +
                  $"switcher:{(switcher ? switcher.name : "NULL")} " +
                  $"panelMainMenu:{(panelMainMenu ? panelMainMenu.name : "NULL")}");

        // Log de endpoints actuales
        var effLogin = string.IsNullOrWhiteSpace(loginUrl) ? DefaultLoginUrl : loginUrl.Trim();
        var effRefresh = string.IsNullOrWhiteSpace(refreshUrl) ? DefaultRefreshUrl : refreshUrl.Trim();
        Debug.Log($"[LoginUI] Endpoints -> login:'{effLogin}' refresh:'{effRefresh}'");
    }

    void Start()
    {
        Debug.Log("[LoginUI] Start() -> BootstrapSession()");
        StartCoroutine(BootstrapSession());
    }

    // ---------- UI: CLICK ----------
    public void OnPressIniciar()
    {
        Debug.Log("[LoginUI] Botón Iniciar Sesión PRESIONADO -> disparo DoLogin()");
        if (isLoggingIn)
        {
            Debug.Log("[LoginUI] Ignoro click: ya hay un login en curso.");
            return;
        }
        StartCoroutine(WrapLogin());
    }

    // Envuelve DoLogin SIN try/catch/finally (para evitar CS1626).
    IEnumerator WrapLogin()
    {
        isLoggingIn = true;
        SetInteractable(false);

        Debug.Log("[LoginUI] WrapLogin() -> DoLogin()");
        yield return StartCoroutine(DoLogin());

        isLoggingIn = false;
        SetInteractable(true);
        Debug.Log("[LoginUI] WrapLogin() FIN");
    }

    // ---------- BOOTSTRAP ----------
    IEnumerator BootstrapSession()
    {
        string rUrl = string.IsNullOrWhiteSpace(refreshUrl) ? DefaultRefreshUrl : refreshUrl.Trim();
        if (!rUrl.EndsWith("/")) rUrl += "/";

        if (!TokenManager.TryLoadTokens(out var access, out var refresh))
        {
            Debug.Log("[LoginUI] Sin tokens. Mostrando pantalla de login (stay here).");
            yield break;
        }

        UserSession.access = access;
        UserSession.refresh = refresh;

        if (JwtUtils.IsExpired(UserSession.access))
        {
            SetEstado("Renovando sesión…");
            Debug.Log("[LoginUI] Access expirado. Intentando refresh…");

            bool refreshedOk = false;
            string newAccess = null;
            string newRefresh = null;

            yield return StartCoroutine(RefreshAccess(rUrl, UserSession.refresh, (ok, a, r) =>
            {
                refreshedOk = ok; newAccess = a; newRefresh = r;
            }));

            if (!refreshedOk)
            {
                Debug.LogWarning("[LoginUI] No se pudo refrescar. Volver a iniciar sesión.");
                SetEstado("Sesión expirada. Iniciá sesión nuevamente.");
                yield break;
            }

            UserSession.access = newAccess;
            if (!string.IsNullOrEmpty(newRefresh)) UserSession.refresh = newRefresh;
            TokenManager.SaveTokens(UserSession.access, UserSession.refresh);
            Debug.Log("[LoginUI] Refresh OK. Access renovado.");
        }

        bool perfilOk = UserSession.LoadProfileFromPrefsIfAvailable();
        if (perfilOk)
        {
            Debug.Log($"[LoginUI] Sesión restaurada. Bienvenido {UserSession.FullName}");
            SetEstado($"Bienvenido {UserSession.FullName}");
        }
        else
        {
            Debug.Log("[LoginUI] Tokens válidos pero sin perfil cacheado.");
            SetEstado("Sesión restaurada.");
        }

        GoToMainMenu();
    }

    // ---------- LOGIN ----------
    IEnumerator DoLogin()
    {
        Debug.Log("[LoginUI] DoLogin() INICIO");

        string url = string.IsNullOrWhiteSpace(loginUrl) ? DefaultLoginUrl : loginUrl.Trim();
        if (!url.EndsWith("/")) url += "/";

        string id = inputUsuarioEmail ? inputUsuarioEmail.text.Trim() : "";
        string pass = inputPassword ? inputPassword.text : "";

        Debug.Log($"[LoginUI] DoLogin -> url:'{url}' idEmpty={string.IsNullOrEmpty(id)} pwLen={(pass?.Length ?? 0)}");

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(pass))
        {
            SetEstado("Completá usuario/email y contraseña");
            yield break;
        }

        var payload = new LoginReq { username_or_email = id, password = pass };
        string json = JsonUtility.ToJson(payload);

        Debug.Log($"[LoginUI] Payload: {json}");

        SetEstado("Conectando…");

        // Preflight
        yield return StartCoroutine(PreflightGet(url));

        // POST con reintentos
        bool success = false;
        string lastErr = "";
        string bodyText = "";

        for (int attempt = 1; attempt <= kMaxRetries && !success; attempt++)
        {
            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = kTimeoutSec;

                Debug.Log($"[LoginUI] POST intento {attempt}/{kMaxRetries} -> {url}");
                yield return req.SendWebRequest();

                var respCode = (long)req.responseCode;
                var respText = req.downloadHandler != null ? req.downloadHandler.text : "";
                Debug.Log($"[LoginUI] RESP intento {attempt} -> result={req.result} code={respCode} err='{req.error}' body='{respText}'");

                if (req.result == UnityWebRequest.Result.Success &&
                    respCode >= 200 && respCode < 300)
                {
                    bodyText = respText;
                    success = true;
                    break;
                }
                else
                {
                    lastErr = $"HTTP {respCode} - {req.error}";
                    yield return new WaitForSeconds(kRetryDelay);
                }
            }
        }

        if (!success)
        {
            Debug.LogError($"[Login] Error tras {kMaxRetries} intentos: {lastErr}");
            SetEstado($"Error: {lastErr}");
            yield break;
        }

        // Parse y guardado
        LoginResp resp = null;
        try { resp = JsonUtility.FromJson<LoginResp>(bodyText); }
        catch (Exception e)
        {
            Debug.LogError($"[Login] Error parseando JSON: {e}\nRespuesta: {bodyText}");
        }

        if (resp != null && !string.IsNullOrEmpty(resp.access))
        {
            UserSession.Apply(resp);
            TokenManager.SaveTokens(resp.access, resp.refresh ?? "");
            UserSession.SaveProfileToPrefs();

            Debug.Log($"[Login OK] Usuario: {UserSession.FullName} (id: {resp.id})");
            SetEstado($"¡Bienvenido, {resp.first_name} {resp.last_name}!");

            GoToMainMenu();
        }
        else
        {
            Debug.LogError($"[Login] Respuesta inválida del servidor.\n{bodyText}");
            SetEstado("Error: respuesta inválida del servidor");
        }
    }

    // ---------- REFRESH TOKEN ----------
    IEnumerator RefreshAccess(string refreshEndpoint, string refreshToken, Action<bool, string, string> onDone)
    {
        if (!refreshEndpoint.EndsWith("/")) refreshEndpoint += "/";

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            onDone?.Invoke(false, null, null);
            yield break;
        }

        var reqObj = new RefreshReq { refresh = refreshToken };
        string json = JsonUtility.ToJson(reqObj);

        bool success = false;
        string lastErr = "";
        string newAccess = null;
        string newRefresh = null;

        for (int attempt = 1; attempt <= kMaxRetries && !success; attempt++)
        {
            using (var req = new UnityWebRequest(refreshEndpoint, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = kTimeoutSec;

                Debug.Log($"[LoginUI] REFRESH intento {attempt}/{kMaxRetries} -> {refreshEndpoint}");
                yield return req.SendWebRequest();

                var respCode = (long)req.responseCode;
                var body = req.downloadHandler != null ? req.downloadHandler.text : "";
                Debug.Log($"[LoginUI] REFRESH resp intento {attempt}: result={req.result} code={respCode} err='{req.error}' body='{body}'");

                if (req.result == UnityWebRequest.Result.Success && respCode >= 200 && respCode < 300)
                {
                    try
                    {
                        var resp = JsonUtility.FromJson<RefreshResp>(body);
                        if (!string.IsNullOrEmpty(resp.access))
                        {
                            newAccess = resp.access;
                            newRefresh = string.IsNullOrEmpty(resp.refresh) ? null : resp.refresh;
                            success = true;
                            break;
                        }
                        else lastErr = "Respuesta de refresh sin 'access'.";
                    }
                    catch (Exception e)
                    {
                        lastErr = $"Error parseando refresh JSON: {e.Message}";
                    }
                }
                else
                {
                    lastErr = $"HTTP {respCode} - {req.error}";
                    yield return new WaitForSeconds(kRetryDelay);
                }
            }
        }

        if (!success)
        {
            Debug.LogError($"[LoginUI] Refresh falló: {lastErr}");
            onDone?.Invoke(false, null, null);
        }
        else
        {
            Debug.Log("[LoginUI] Refresh OK. Access renovado.");
            onDone?.Invoke(true, newAccess, newRefresh);
        }
    }

    // ---------- PRE-FLIGHT ----------
    IEnumerator PreflightGet(string url)
    {
        using (var r = UnityWebRequest.Get(url))
        {
            r.timeout = kTimeoutSec;
            Debug.Log($"[LoginUI] Preflight GET -> {url}");
            yield return r.SendWebRequest();
            Debug.Log($"[LoginUI] Preflight resp -> result={r.result} code={(long)r.responseCode} err='{r.error}'");
        }
    }

    // ---------- NAVEGACIÓN ----------
    void GoToMainMenu()
    {
        Debug.Log("[LoginUI] GoToMainMenu()");
        if (switcher != null)
        {
            Debug.Log("[LoginUI] UIAuthSwitcher.ShowMainMenu()");
            switcher.ShowMainMenu();
        }
        else if (panelMainMenu != null)
        {
            Debug.Log("[LoginUI] Fallback: activando PanelMainMenu manualmente.");
            var parent = panelMainMenu.transform.parent;
            if (parent != null)
            {
                foreach (Transform child in parent) child.gameObject.SetActive(false);
            }
            panelMainMenu.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[LoginUI] No hay UIAuthSwitcher ni panelMainMenu asignados.");
        }

        // Si este script vive en el PanelLogin, lo ocultamos:
        gameObject.SetActive(false);
    }

    void SetEstado(string msg) { if (txtEstado) txtEstado.text = msg; }
    void SetInteractable(bool on) { if (btnIniciar) btnIniciar.interactable = on; }

    // -------- DTOs --------
    [System.Serializable] class LoginReq { public string username_or_email; public string password; }
    [System.Serializable] class RefreshReq { public string refresh; }

    [System.Serializable]
    class RefreshResp
    {
        public string access;
        public string refresh; // puede venir vacío
    }
}

[System.Serializable]
public class LoginResp
{
    public string refresh;
    public string access;
    public int id;
    public string first_name;
    public string last_name;
    public string username;
    public string email;
    public string date_of_birth;
}

public static class UserSession
{
    public static string access;
    public static string refresh;
    public static int id;
    public static string firstName;
    public static string lastName;
    public static string username;
    public static string email;
    public static string dateOfBirth;

    public static string FullName => $"{firstName} {lastName}".Trim();

    public static void Apply(LoginResp r)
    {
        if (r == null) return;
        access = r.access;
        refresh = r.refresh;
        id = r.id;
        firstName = r.first_name;
        lastName = r.last_name;
        username = r.username;
        email = r.email;
        dateOfBirth = r.date_of_birth;
    }

    // Perfil no sensible
    const string K_UserId = "user_id";
    const string K_First = "user_first_name";
    const string K_Last = "user_last_name";
    const string K_User = "user_username";
    const string K_Email = "user_email";
    const string K_Dob = "user_dob";

    public static void SaveProfileToPrefs()
    {
        PlayerPrefs.SetInt(K_UserId, id);
        PlayerPrefs.SetString(K_First, firstName ?? "");
        PlayerPrefs.SetString(K_Last, lastName ?? "");
        PlayerPrefs.SetString(K_User, username ?? "");
        PlayerPrefs.SetString(K_Email, email ?? "");
        PlayerPrefs.SetString(K_Dob, dateOfBirth ?? "");
        PlayerPrefs.Save();
    }

    public static bool LoadProfileFromPrefsIfAvailable()
    {
        if (!PlayerPrefs.HasKey(K_UserId)) return false;
        id = PlayerPrefs.GetInt(K_UserId, 0);
        firstName = PlayerPrefs.GetString(K_First, "");
        lastName = PlayerPrefs.GetString(K_Last, "");
        username = PlayerPrefs.GetString(K_User, "");
        email = PlayerPrefs.GetString(K_Email, "");
        dateOfBirth = PlayerPrefs.GetString(K_Dob, "");
        return id != 0 || !string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(email);
    }

    public static void ClearProfilePrefs()
    {
        PlayerPrefs.DeleteKey(K_UserId);
        PlayerPrefs.DeleteKey(K_First);
        PlayerPrefs.DeleteKey(K_Last);
        PlayerPrefs.DeleteKey(K_User);
        PlayerPrefs.DeleteKey(K_Email);
        PlayerPrefs.DeleteKey(K_Dob);
    }
}

// --- Helper JWT: detecta si el access está vencido ---
public static class JwtUtils
{
    public static bool IsExpired(string jwt)
    {
        if (string.IsNullOrEmpty(jwt) || !jwt.Contains(".")) return true;
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return true;
            string payload = Base64UrlDecode(parts[1]);
            var idx = payload.IndexOf("\"exp\":");
            if (idx < 0) return false;
            int start = idx + 6;
            int end = start;
            while (end < payload.Length && char.IsDigit(payload[end])) end++;
            if (start >= payload.Length || end <= start) return false;
            var expUnixStr = payload.Substring(start, end - start);
            if (!long.TryParse(expUnixStr, out long expUnix)) return false;
            var expUtc = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
            return DateTime.UtcNow > expUtc;
        }
        catch { return true; }
    }

    static string Base64UrlDecode(string input)
    {
        string s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        var bytes = Convert.FromBase64String(s);
        return Encoding.UTF8.GetString(bytes);
    }
}
