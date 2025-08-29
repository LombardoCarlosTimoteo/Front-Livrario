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

    [Header("Backend")]
    [SerializeField] string loginUrl = "http://101.44.12.140:8000/api/auth/login/";

    void Awake()
    {
        if (btnIniciar != null)
            btnIniciar.onClick.AddListener(() => StartCoroutine(DoLogin()));
    }

    IEnumerator DoLogin()
    {
        string id = inputUsuarioEmail ? inputUsuarioEmail.text.Trim() : "";
        string pass = inputPassword ? inputPassword.text : "";

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(pass))
        {
            SetEstado("Completá usuario/email y contraseña");
            yield break;
        }

        var payload = new LoginReq { username_or_email = id, password = pass };
        string json = JsonUtility.ToJson(payload);

        using (var req = new UnityWebRequest(loginUrl, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            SetInteractable(false);
            SetEstado("Conectando...");

            yield return req.SendWebRequest();

            SetInteractable(true);

            if (req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300)
            {
                string bodyText = req.downloadHandler.text;

                LoginResp resp = null;
                try
                {
                    resp = JsonUtility.FromJson<LoginResp>(bodyText);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Login] Error parseando JSON: {e}\nRespuesta: {bodyText}");
                }

                if (resp != null && !string.IsNullOrEmpty(resp.access))
                {
                    // Guardar sesión global
                    UserSession.Apply(resp);

                    // Persistencia opcional
                    PlayerPrefs.SetString("auth_access", resp.access);
                    PlayerPrefs.SetString("auth_refresh", resp.refresh ?? "");
                    PlayerPrefs.SetInt("user_id", resp.id);
                    PlayerPrefs.SetString("user_first_name", resp.first_name ?? "");
                    PlayerPrefs.SetString("user_last_name", resp.last_name ?? "");
                    PlayerPrefs.SetString("user_username", resp.username ?? "");
                    PlayerPrefs.SetString("user_email", resp.email ?? "");
                    PlayerPrefs.SetString("user_dob", resp.date_of_birth ?? "");
                    PlayerPrefs.Save();

                    // Log completo
                    Debug.Log($"[Login OK]\n" +
                              $"id: {resp.id}\n" +
                              $"first_name: {resp.first_name}\n" +
                              $"last_name: {resp.last_name}\n" +
                              $"username: {resp.username}\n" +
                              $"email: {resp.email}\n" +
                              $"date_of_birth: {resp.date_of_birth}\n" +
                              $"access: {resp.access}\n" +
                              $"refresh: {resp.refresh}");

                    SetEstado($"¡Bienvenido, {resp.first_name} {resp.last_name}!");
                    // TODO: habilitar UI o cargar escena
                }
                else
                {
                    Debug.LogError($"[Login] Respuesta sin access token o inválida.\n{bodyText}");
                    SetEstado("Error: respuesta inválida del servidor");
                }
            }
            else
            {
                string errBody = req.downloadHandler != null ? req.downloadHandler.text : "(sin cuerpo)";
                Debug.LogError($"[Login] Error HTTP {req.responseCode} - {req.error}\nCuerpo: {errBody}");
                SetEstado($"Error {req.responseCode}: {errBody}");
            }
        }
    }

    void SetEstado(string msg) { if (txtEstado) txtEstado.text = msg; }
    void SetInteractable(bool on) { if (btnIniciar) btnIniciar.interactable = on; }

    [System.Serializable] class LoginReq { public string username_or_email; public string password; }
}

/// <summary>
/// DTO PÚBLICO para que sea accesible desde cualquier clase.
/// Los nombres coinciden con las claves snake_case del JSON del backend.
/// </summary>
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

/// <summary>
/// Sesión global simple para reutilizar los datos del usuario en toda la app.
/// </summary>
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
}
