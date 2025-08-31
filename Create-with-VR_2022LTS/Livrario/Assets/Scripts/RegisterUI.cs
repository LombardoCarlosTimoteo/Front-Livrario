using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class RegisterUI : MonoBehaviour
{
    [Header("Inputs")]
    [SerializeField] TMP_InputField InputFirstName;
    [SerializeField] TMP_InputField InputLastName;
    [SerializeField] TMP_InputField InputUsername;
    [SerializeField] TMP_InputField InputFechaNacimiento;   // YYYY-MM-DD
    [SerializeField] TMP_InputField InputEmail;
    [SerializeField] TMP_InputField InputPassword;
    [SerializeField] TMP_InputField InputConfirmPassword;

    [Header("Botones")]
    [SerializeField] Button BtnEnviarRegistro;

    [Header("UI Estado (opcional)")]
    [SerializeField] TextMeshProUGUI txtEstado;

    [Header("Navegación")]
    [SerializeField] UIAuthSwitcher switcher;          // arrastrá el mismo del Canvas
    [SerializeField] GameObject loginRootFallback;     // opcional: GO raíz del login si no usás switcher

    [Header("Backend")]
    [SerializeField] string registerUrl = "https://login.nicolasirigoyen.com.ar/api/auth/register/";

    const int TimeoutSec = 12;

    void Awake()
    {
        if (BtnEnviarRegistro) BtnEnviarRegistro.onClick.AddListener(OnClickEnviarRegistro);
    }

    void OnClickEnviarRegistro()
    {
        StartCoroutine(DoRegister());
    }

    IEnumerator DoRegister()
    {
        // URL saneada
        string url = string.IsNullOrWhiteSpace(registerUrl) ? "" : registerUrl.Trim();
        if (string.IsNullOrEmpty(url))
        {
            SetEstado("URL de registro vacía.");
            yield break;
        }
        if (!url.EndsWith("/")) url += "/";

        // Validaciones mínimas
        if (string.IsNullOrWhiteSpace(InputFirstName.text) ||
            string.IsNullOrWhiteSpace(InputLastName.text) ||
            string.IsNullOrWhiteSpace(InputUsername.text) ||
            string.IsNullOrWhiteSpace(InputFechaNacimiento.text) ||
            string.IsNullOrWhiteSpace(InputEmail.text) ||
            string.IsNullOrEmpty(InputPassword.text) ||
            string.IsNullOrEmpty(InputConfirmPassword.text))
        {
            SetEstado("Completá todos los campos.");
            yield break;
        }

        if (InputPassword.text != InputConfirmPassword.text)
        {
            SetEstado("Las contraseñas no coinciden.");
            yield break;
        }

        // Cuerpo JSON EXACTO que espera tu backend
        var payload = new RegisterReq
        {
            first_name = InputFirstName.text.Trim(),
            last_name = InputLastName.text.Trim(),
            username = InputUsername.text.Trim(),
            email = InputEmail.text.Trim(),
            password = InputPassword.text,
            password2 = InputConfirmPassword.text,
            date_of_birth = InputFechaNacimiento.text.Trim() // formato: YYYY-MM-DD
        };
        string json = JsonUtility.ToJson(payload);

        // UI feedback
        SetInteractable(false);
        SetEstado("Creando cuenta...");

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = TimeoutSec;

            Debug.Log($"[Register] POST -> {url}\nBody: {json}");
            yield return req.SendWebRequest();

            SetInteractable(true);

            long code = (long)req.responseCode;
            string body = req.downloadHandler != null ? req.downloadHandler.text : "";

            if (req.result == UnityWebRequest.Result.Success && code >= 200 && code < 300)
            {
                Debug.Log($"[Register OK] code={code}, body={body}");
                SetEstado("¡Cuenta creada! Iniciá sesión.");

                // Ir a Login
                if (switcher != null)
                {
                    switcher.ShowLogin();
                }
                else if (loginRootFallback != null)
                {
                    // Si usás un root alternativo:
                    // apagá mis hermanos y prendé el login
                    var parent = loginRootFallback.transform.parent;
                    if (parent)
                    {
                        foreach (Transform child in parent) child.gameObject.SetActive(false);
                    }
                    loginRootFallback.SetActive(true);
                }
                else
                {
                    Debug.LogWarning("[Register] No hay UIAuthSwitcher ni loginRootFallback asignados.");
                }
            }
            else
            {
                // Parseo simple de errores 400 (ejemplo de tu backend)
                // {"username":["A user with that username already exists."],"email":["user with this email already exists."],"date_of_birth":["Date has wrong format. Use YYYY-MM-DD."]}
                string msgBonito = PrettyErrors(body);
                Debug.LogError($"[Register] Error code={code}, err={req.error}, body={body}");
                SetEstado(string.IsNullOrEmpty(msgBonito) ? $"Error {code}" : msgBonito);
            }
        }
    }

    // ----------------- Helpers -----------------

    void SetEstado(string m)
    {
        if (txtEstado) txtEstado.text = m;
    }

    void SetInteractable(bool on)
    {
        if (BtnEnviarRegistro) BtnEnviarRegistro.interactable = on;
    }

    // Intenta mostrar un mensaje amigable si el backend devolvió un JSON con listas de errores por campo
    string PrettyErrors(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        // Como JsonUtility no maneja diccionarios, hacemos un parseo MUY simple
        // para casos típicos (no es un parser general):
        // Busca cadenas tipo: "username":[ "mensaje" ]
        StringBuilder sb = new StringBuilder();

        AppendIfFound(sb, raw, "first_name", "Nombre:");
        AppendIfFound(sb, raw, "last_name", "Apellido:");
        AppendIfFound(sb, raw, "username", "Usuario:");
        AppendIfFound(sb, raw, "email", "Email:");
        AppendIfFound(sb, raw, "password", "Contraseña:");
        AppendIfFound(sb, raw, "password2", "Confirmación:");
        AppendIfFound(sb, raw, "date_of_birth", "Fecha:");

        return sb.ToString().Trim();
    }

    void AppendIfFound(StringBuilder sb, string raw, string key, string label)
    {
        // Busca "key":[ "algo" ]
        // Esta extracción es naive y funciona para el formato devuelto por tu backend de ejemplo
        string marker = $"\"{key}\":[";
        int i = raw.IndexOf(marker);
        if (i < 0) return;
        int start = raw.IndexOf('"', i + marker.Length);
        if (start < 0) return;
        int end = raw.IndexOf('"', start + 1);
        if (end <= start) return;
        string msg = raw.Substring(start + 1, end - start - 1);
        if (sb.Length > 0) sb.AppendLine();
        sb.Append($"{label} {msg}");
    }

    // DTO exacto
    [System.Serializable]
    class RegisterReq
    {
        public string first_name;
        public string last_name;
        public string username;
        public string email;
        public string password;
        public string password2;
        public string date_of_birth;
    }
}
