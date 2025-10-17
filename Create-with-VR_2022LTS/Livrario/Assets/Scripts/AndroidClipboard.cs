using UnityEngine;

public static class AndroidClipboard
{
    public static void SetText(string text)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var cm          = activity.Call<AndroidJavaObject>("getSystemService", "clipboard"))
            using (var clipDataCls = new AndroidJavaClass("android.content.ClipData"))
            using (var clip        = clipDataCls.CallStatic<AndroidJavaObject>("newPlainText", "text", text ?? "")) {
                cm.Call("setPrimaryClip", clip);
            }
        } catch (System.Exception e) {
            Debug.LogWarning("[Clipboard] Falló Android clipboard, uso fallback: " + e.Message);
            GUIUtility.systemCopyBuffer = text ?? "";
        }
#else
        GUIUtility.systemCopyBuffer = text ?? "";
#endif
        Debug.Log("[Clipboard] Copiado: " + (text ?? "(vacío)"));
    }
}
