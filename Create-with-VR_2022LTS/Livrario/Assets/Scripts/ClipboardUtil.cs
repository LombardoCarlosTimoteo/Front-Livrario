using UnityEngine;

public static class ClipboardUtil
{
    public static void Set(string text)
    {
        if (text == null) text = "";
        // En casi todas las plataformas Unity: 
        GUIUtility.systemCopyBuffer = text;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Fallback nativo Android (por si alguna build no copia con systemCopyBuffer)
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                string service = "clipboard";
                using (var clipboard = activity.Call<AndroidJavaObject>("getSystemService", service))
                using (var clipDataCls = new AndroidJavaClass("android.content.ClipData"))
                using (var clip = clipDataCls.CallStatic<AndroidJavaObject>("newPlainText", "text", text))
                {
                    clipboard.Call("setPrimaryClip", clip);
                }
            }
        }
        catch { /* si falla, igual queda en systemCopyBuffer */ }
#endif
        Debug.Log("[Clipboard] Copiado: " + text);
    }
}
