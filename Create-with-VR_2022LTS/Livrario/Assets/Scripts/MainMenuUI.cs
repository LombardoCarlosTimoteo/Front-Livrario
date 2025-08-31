using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] TextMeshProUGUI TextBienvenido;       // tu "TextBienvenido"
    [SerializeField] TextMeshProUGUI txtEstado;            // opcional: para mensajes tipo "Cerrando sesión..."
    [SerializeField] Button BtnCargarLibro;                // opcional si conectás por OnClick en Inspector
    [SerializeField] Button BtnBiblioteca;
    [SerializeField] Button BtnCerrarSesion;

    [Header("Navegación / Panels")]
    [SerializeField] UIAuthSwitcher switcher;              // arrastrá el mismo del Canvas
    [SerializeField] GameObject panelBiblioteca;           // opcional

    [Header("Backend")]
    [SerializeField] string logoutUrl = "https://login.nicolasirigoyen.com.ar/api/auth/logout/";
    [SerializeField] string refreshUrl = "https://login.nicolasirigoyen.com.ar/api/auth/token/refresh/"; // para renovar access si hace falta

    const int TimeoutSec = 12;

    void Awake()
    {
        if (BtnCargarLibro) BtnCargarLibro.onClick.AddListener(OnClickCargarLibro);
        if (BtnBiblioteca) BtnBiblioteca.onClick.AddListener(OnClickBiblioteca);
        if (BtnCerrarSesion) BtnCerrarSesion.onClick.AddListener(OnClickCerrarSesion);
    }

    void OnEnable()
    {
        string first = !string.IsNullOrEmpty(UserSession.firstName)
            ? UserSession.firstName
            : PlayerPrefs.GetString("user_first_name", "");

        string last = !string.IsNullOrEmpty(UserSession.lastName)
            ? UserSession.lastName
            : PlayerPrefs.GetString("user_last_name", "");

        string nombre = (first + " " + last).Trim();
        if (string.IsNullOrEmpty(nombre)) nombre = "usuario";

        if (TextBienvenido)
            TextBienvenido.text = $"Bienvenido a Livrario, {nombre}";
    }

    // ------------------ Acciones de botones ------------------

    public void OnClickCargarLibro()
    {
        Debug.Log("[MainMenu] Cargar Libro: TODO implementar selector/flujo de PDF en Quest.");
    }

    public void OnClickBiblioteca()
    {
        if (panelBiblioteca != null)
        {
            gameObject.SetActive(false);
            panelBiblioteca.SetActive(true);
        }
        else
        {
            Debug.Log("[MainMenu] PanelBiblioteca no asignado (opcional).");
        }
    }

    public void OnClickCerrarSesion()
    {
        StartCoroutine(DoLogout());
    }

    // ------------------ Lógica de logout ------------------

    IEnumerator DoLogout()
    {
        string url = string.IsNullOrWhiteSpace(logoutUrl) ? "" : logoutUrl.Trim();
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogError("[Logout] logoutUrl vacío.");
            yield break;
        }
        if (!url.EndsWith("/")) url += "/";

        // Aseguramos refresh en memoria (lo tomamos de UserSession o de TokenManager)
        string refresh = UserSession.refresh;
        if (string.IsNullOrEmpty(refresh))
        {
            if (TokenManager.TryLoadTokens(out var acc, out var refTok))
            {
                UserSession.access = string.IsNullOrEmpty(UserSession.access) ? acc : UserSession.access;
                refresh = refTok;
            }
        }
        if (string.IsNullOrEmpty(refresh))
        {
            Debug.LogWarning("[Logout] No hay refresh token disponible. Vuelvo al login sin pegarle al backend.");
            GoToLoginAndClearLocal();
            yield break;
        }

        // (Opcional) renovar access si está vencido, así el backend acepta el Bearer
        if (!string.IsNullOrEmpty(refreshUrl))
        {
            bool ensured = false;
            yield return TokenRefresh.EnsureValidAccess(refreshUrl, ok => ensured = ok);
            if (!ensured)
            {
                Debug.LogWarning("[Logout] No se pudo asegurar access válido. Intento logout de todos modos.");
            }
        }

        // Cuerpo JSON con el refresh
        var payload = new LogoutReq { refresh = refresh };
        string json = JsonUtility.ToJson(payload);

        // UI feedback
        SetInteractable(false);
        SetEstado("Cerrando sesión...");

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            // Autoriza con Bearer access si lo tenemos
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

                // Si querés forzar logout local aunque el backend falle:
                // GoToLoginAndClearLocal();
                // yield break;

                // Por ahora avisamos y mantenemos sesión local
                SetEstado($"Error al cerrar sesión: {(long)req.responseCode}");
            }
        }
    }

    void GoToLoginAndClearLocal()
    {
        // Limpia tokens cifrados y perfil
        TokenManager.ClearTokens();
        UserSession.ClearProfilePrefs();

        // Limpia sesión en memoria
        UserSession.access = null;
        UserSession.refresh = null;
        UserSession.firstName = null;
        UserSession.lastName = null;
        UserSession.username = null;
        UserSession.email = null;
        UserSession.dateOfBirth = null;
        UserSession.id = 0;

        // Cambia a panel login
        if (switcher) switcher.ShowLogin();
        else Debug.LogWarning("[Logout] UIAuthSwitcher no asignado.");

        SetEstado("Sesión cerrada.");
    }

    void SetEstado(string m)
    {
        if (txtEstado) txtEstado.text = m;
    }

    void SetInteractable(bool on)
    {
        if (BtnCargarLibro) BtnCargarLibro.interactable = on;
        if (BtnBiblioteca) BtnBiblioteca.interactable = on;
        if (BtnCerrarSesion) BtnCerrarSesion.interactable = on;
    }

    [Serializable] class LogoutReq { public string refresh; }
}
