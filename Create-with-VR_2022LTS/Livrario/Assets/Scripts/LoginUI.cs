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

        using var req = new UnityWebRequest(loginUrl, "POST");
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
            SetEstado("¡Login OK!");
            // TODO: manejar token/respuesta acá
        }
        else
        {
            string err = req.downloadHandler != null ? req.downloadHandler.text : req.error;
            SetEstado($"Error {req.responseCode}: {err}");
        }
    }

    void SetEstado(string msg) { if (txtEstado) txtEstado.text = msg; }
    void SetInteractable(bool on) { if (btnIniciar) btnIniciar.interactable = on; }

    [System.Serializable] class LoginReq { public string username_or_email; public string password; }
}
