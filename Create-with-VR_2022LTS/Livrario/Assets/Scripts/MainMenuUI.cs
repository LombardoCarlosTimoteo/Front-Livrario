using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] TextMeshProUGUI TextBienvenido;
    [SerializeField] TextMeshProUGUI txtEstado;
    [SerializeField] Button BtnCargarLibro;
    [SerializeField] Button BtnBiblioteca;
    [SerializeField] Button BtnCerrarSesion;

    [Header("Navegación / Panels")]
    [SerializeField] UIAuthSwitcher switcher;
    [SerializeField] GameObject panelBiblioteca;

    [Header("Backend")]
    [SerializeField] string logoutUrl = "https://login.nicolasirigoyen.com.ar/api/auth/logout/";
    [SerializeField] string refreshUrl = "https://login.nicolasirigoyen.com.ar/api/auth/token/refresh/";

    [Header("Debug")]
    [SerializeField] bool verboseLogging = true;   // ← marcá esto en el Inspector si querés más logs

    const int TimeoutSec = 12;
    Coroutine welcomeRetryCo;

    void Awake()
    {
        if (!switcher) switcher = FindObjectOfType<UIAuthSwitcher>(true);

        if (BtnCargarLibro) BtnCargarLibro.onClick.AddListener(OnClickCargarLibro);
        if (BtnBiblioteca) BtnBiblioteca.onClick.AddListener(OnClickBiblioteca);
        if (BtnCerrarSesion) BtnCerrarSesion.onClick.AddListener(OnClickCerrarSesion);

        if (TextBienvenido) TextBienvenido.raycastTarget = false;
    }

    void OnEnable()
    {
        if (verboseLogging)
        {
            Debug.Log($"[MainMenuUI] OnEnable. UserSession: " +
                      $"first='{UserSession.firstName}', last='{UserSession.lastName}', " +
                      $"username='{UserSession.username}', email='{UserSession.email}'. " +
                      $"Prefs first='{PlayerPrefs.GetString("user_first_name", "")}', last='{PlayerPrefs.GetString("user_last_name", "")}'.");
        }

        bool ok = UpdateWelcomeImmediate();
        StartCoroutine(RefreshWelcomeEndOfFrame());

        if (!ok)
        {
            if (welcomeRetryCo != null) StopCoroutine(welcomeRetryCo);
            welcomeRetryCo = StartCoroutine(WelcomeRetry());
        }

        StartCoroutine(RefreshWelcomeAfterDelay(0.5f));
    }

    void OnDisable()
    {
        if (welcomeRetryCo != null)
        {
            StopCoroutine(welcomeRetryCo);
            welcomeRetryCo = null;
        }
    }

    // <-- Método que llama LoginUI después de navegar
    public void ForceRefreshWelcome()
    {
        if (verboseLogging) Debug.Log("[MainMenuUI] ForceRefreshWelcome() llamado.");
        if (!UpdateWelcomeImmediate())
        {
            if (welcomeRetryCo != null) StopCoroutine(welcomeRetryCo);
            welcomeRetryCo = StartCoroutine(WelcomeRetry());
        }
    }

    // <-- Alias por compatibilidad si en algún lado quedó RefreshWelcome()
    public void RefreshWelcome() => ForceRefreshWelcome();

    IEnumerator RefreshWelcomeEndOfFrame()
    {
        yield return null;
        UpdateWelcomeImmediate();
    }

    IEnumerator RefreshWelcomeAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        UpdateWelcomeImmediate();
    }

    bool UpdateWelcomeImmediate()
    {
        if (!TextBienvenido)
        {
            Debug.LogWarning("[MainMenuUI] TextBienvenido NO asignado en el Inspector.");
            return false;
        }

        // 1) Intento con memoria
        string first = UserSession.firstName;
        string last = UserSession.lastName;

        // 2) Si falta, intento levantar de Prefs
        if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last))
        {
            bool had = UserSession.LoadProfileFromPrefsIfAvailable();
            if (verboseLogging) Debug.Log($"[MainMenuUI] Perfil cacheado cargado={had}");

            first = string.IsNullOrWhiteSpace(UserSession.firstName) ? PlayerPrefs.GetString("user_first_name", "") : UserSession.firstName;
            last = string.IsNullOrWhiteSpace(UserSession.lastName) ? PlayerPrefs.GetString("user_last_name", "") : UserSession.lastName;
        }

        string nombre = ((first ?? "") + " " + (last ?? "")).Trim();

        if (string.IsNullOrWhiteSpace(nombre))
        {
            // 3) Fallback: username/email
            string fallback = !string.IsNullOrWhiteSpace(UserSession.username) ? UserSession.username
                            : !string.IsNullOrWhiteSpace(UserSession.email) ? UserSession.email
                            : PlayerPrefs.GetString("user_username", "");

            if (string.IsNullOrWhiteSpace(fallback))
                fallback = PlayerPrefs.GetString("user_email", "");

            if (string.IsNullOrWhiteSpace(fallback))
            {
                if (verboseLogging) Debug.Log("[MainMenuUI] Sin datos aún para bienvenida.");
                return false;
            }

            TextBienvenido.text = $"Bienvenido a Livrario, {fallback}";
            if (verboseLogging) Debug.Log($"[MainMenuUI] Bienvenida (fallback): '{TextBienvenido.text}'");
            return true;
        }
        else
        {
            TextBienvenido.text = $"Bienvenido a Livrario, {nombre}";
            if (verboseLogging) Debug.Log($"[MainMenuUI] Bienvenida: '{TextBienvenido.text}'");
            return true;
        }
    }

    IEnumerator WelcomeRetry()
    {
        const int tries = 10;
        const float delay = 0.2f;

        for (int i = 0; i < tries; i++)
        {
            if (UpdateWelcomeImmediate())
                yield break;

            yield return new WaitForSeconds(delay);
        }

        if (TextBienvenido && string.IsNullOrWhiteSpace(TextBienvenido.text))
        {
            TextBienvenido.text = "Bienvenido a Livrario";
            if (verboseLogging) Debug.Log("[MainMenuUI] Bienvenida genérica por timeout.");
        }
    }

    // -------- Botones --------
    public void OnClickCargarLibro() => Debug.Log("[MainMenu] TODO: flujo de carga de libro.");
    public void OnClickBiblioteca()
    {
        if (panelBiblioteca) { gameObject.SetActive(false); panelBiblioteca.SetActive(true); }
        else Debug.Log("[MainMenu] panelBiblioteca no asignado.");
    }
    public void OnClickCerrarSesion() => StartCoroutine(DoLogout());

    // -------- Logout --------
    IEnumerator DoLogout()
    {
        string url = string.IsNullOrWhiteSpace(logoutUrl) ? "" : logoutUrl.Trim();
        if (string.IsNullOrEmpty(url)) { Debug.LogError("[Logout] logoutUrl vacío."); yield break; }
        if (!url.EndsWith("/")) url += "/";

        string refresh = UserSession.refresh;
        if (string.IsNullOrEmpty(refresh))
        {
            if (TokenManager.TryLoadTokens(out var acc, out var refTok))
            {
                if (string.IsNullOrEmpty(UserSession.access)) UserSession.access = acc;
                refresh = refTok;
            }
        }
        if (string.IsNullOrEmpty(refresh))
        {
            Debug.LogWarning("[Logout] No hay refresh. Limpio local y vuelvo al login.");
            GoToLoginAndClearLocal();
            yield break;
        }

        if (!string.IsNullOrEmpty(refreshUrl))
        {
            bool ensured = false;
            yield return TokenRefresh.EnsureValidAccess(refreshUrl, ok => ensured = ok);
            if (!ensured) Debug.LogWarning("[Logout] No pude asegurar access válido; intento igual.");
        }

        var payload = new LogoutReq { refresh = refresh };
        string json = JsonUtility.ToJson(payload);

        SetInteractable(false);
        SetEstado("Cerrando sesión...");

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(UserSession.access))
                req.SetRequestHeader("Authorization", "Bearer " + UserSession.access);
            req.timeout = TimeoutSec;

            Debug.Log($"[Logout] POST -> {url}\nBody: {json}");
            yield return req.SendWebRequest();

            SetInteractable(true);

            if (req.result == UnityWebRequest.Result.Success &&
                req.responseCode >= 200 && req.responseCode < 300)
            {
                Debug.Log($"[Logout OK] code={(long)req.responseCode}, body={req.downloadHandler.text}");
                GoToLoginAndClearLocal();
            }
            else
            {
                var body = req.downloadHandler != null ? req.downloadHandler.text : "(sin cuerpo)";
                Debug.LogError($"[Logout] Error code={(long)req.responseCode}, err={req.error}, body={body}");
                SetEstado($"Error al cerrar sesión: {(long)req.responseCode}");
            }
        }
    }

    void GoToLoginAndClearLocal()
    {
        TokenManager.ClearTokens();
        UserSession.ClearProfilePrefs();

        UserSession.access = null;
        UserSession.refresh = null;
        UserSession.firstName = null;
        UserSession.lastName = null;
        UserSession.username = null;
        UserSession.email = null;
        UserSession.dateOfBirth = null;
        UserSession.id = 0;

        if (switcher) switcher.ShowLogin();
        else Debug.LogWarning("[Logout] UIAuthSwitcher no asignado.");

        SetEstado("Sesión cerrada.");
    }

    void SetEstado(string m) { if (txtEstado) txtEstado.text = m; }
    void SetInteractable(bool on)
    {
        if (BtnCargarLibro) BtnCargarLibro.interactable = on;
        if (BtnBiblioteca) BtnBiblioteca.interactable = on;
        if (BtnCerrarSesion) BtnCerrarSesion.interactable = on;
    }

    [Serializable] class LogoutReq { public string refresh; }
}
