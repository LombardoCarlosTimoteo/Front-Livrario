using System;
using System.Collections;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class RegisterUI : MonoBehaviour
{
    [Header("Inputs")]
    public TMP_InputField inputFirstName;
    public TMP_InputField inputLastName;
    public TMP_InputField inputUsername;
    public TMP_InputField inputEmail;
    public TMP_InputField inputPassword;
    public TMP_InputField inputPasswordConfirm;
    public TMP_InputField inputFechaNacimiento; // YYYY-MM-DD

    [Header("UI")]
    public Button btnEnviar;
    public Button btnVolver;
    public TextMeshProUGUI txtEstado;

    [Header("Backend")]
    public string registerUrl = "http://101.44.12.140:8000/api/auth/register/";

    void Awake()
    {
        if (btnEnviar) btnEnviar.onClick.AddListener(() => StartCoroutine(DoRegister()));
    }

    IEnumerator DoRegister()
    {
        string fn  = inputFirstName?.text.Trim() ?? "";
        string ln  = inputLastName?.text.Trim() ?? "";
        string un  = inputUsername?.text.Trim() ?? "";
        string em  = inputEmail?.text.Trim() ?? "";
        string pw  = inputPassword?.text ?? "";
        string pw2 = inputPasswordConfirm?.text ?? "";
        string dob = inputFechaNacimiento?.text.Trim() ?? "";

        // Validaciones
        if (string.IsNullOrEmpty(fn) || string.IsNullOrEmpty(ln) ||
            string.IsNullOrEmpty(un) || string.IsNullOrEmpty(em) ||
            string.IsNullOrEmpty(pw) || string.IsNullOrEmpty(pw2) ||
            string.IsNullOrEmpty(dob))
        {
            MostrarError("Completá todos los campos.");
            yield break;
        }
        if (pw != pw2)
        {
            MostrarError("Las contraseñas no coinciden.");
            yield break;
        }
        if (!DateTime.TryParseExact(dob, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            MostrarError("Fecha inválida. Formato: YYYY-MM-DD (ej. 1992-05-10)");
            yield break;
        }

        var payload = new RegisterReq {
            first_name    = fn,
            last_name     = ln,
            username      = un,
            email         = em,
            password      = pw,
            password2     = pw2,
            date_of_birth = dob
        };

        string json = JsonUtility.ToJson(payload);

        using (var req = new UnityWebRequest(registerUrl, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            SetInteractable(false);
            SetEstado("Enviando...");

            yield return req.SendWebRequest();
            SetInteractable(true);

            string body = req.downloadHandler?.text ?? "";

            if (req.result == UnityWebRequest.Result.Success && (req.responseCode == 201 || req.responseCode == 200))
            {
                var ok = JsonUtility.FromJson<RegisterResp>(body);
                Debug.Log($"[Register OK] {body}");
                SetEstado($"¡Cuenta creada! Bienvenido {ok.first_name} {ok.last_name}");

                // Cambiar a la pantalla de login después de un pequeño delay
                yield return new WaitForSeconds(1f);
                FindObjectOfType<UIAuthSwitcher>()?.ShowLogin();
            }
            else if (req.responseCode == 400 && !string.IsNullOrEmpty(body))
            {
                string msg = BuildReadableError(body);
                MostrarError(msg);
            }
            else
            {
                MostrarError($"Error {req.responseCode}: {req.error}");
            }
        }
    }

    void MostrarError(string mensaje)
    {
        Debug.LogError($"[Register ERROR] {mensaje}");
        SetEstado(mensaje);
    }

    string BuildReadableError(string json)
    {
        try
        {
            var err = JsonUtility.FromJson<RegisterErr>(json);
            var sb = new StringBuilder();

            if (err.username != null && err.username.Length > 0)
                sb.AppendLine($"Usuario: {string.Join(" ", err.username)}");
            if (err.email != null && err.email.Length > 0)
                sb.AppendLine($"Email: {string.Join(" ", err.email)}");
            if (err.date_of_birth != null && err.date_of_birth.Length > 0)
                sb.AppendLine($"Fecha de nacimiento: {string.Join(" ", err.date_of_birth)}");

            return sb.Length > 0 ? sb.ToString().TrimEnd() : $"Error: {json}";
        }
        catch
        {
            return $"Error: {json}";
        }
    }

    void SetEstado(string m) { if (txtEstado) txtEstado.text = m; }
    void SetInteractable(bool on)
    {
        if (btnEnviar) btnEnviar.interactable = on;
        if (btnVolver) btnVolver.interactable = on;
    }

    [Serializable] class RegisterReq
    {
        public string first_name, last_name, username, email, password, password2, date_of_birth;
    }

    [Serializable] class RegisterResp
    {
        public string first_name, last_name, username, email, date_of_birth;
    }

    [Serializable] class RegisterErr
    {
        public string[] username;
        public string[] email;
        public string[] date_of_birth;
    }
}
