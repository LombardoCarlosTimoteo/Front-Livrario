using UnityEngine;

// Render de páginas PDF en Android (Quest) usando android.graphics.pdf.PdfRenderer
public static class PdfRendererAndroid
{
#if UNITY_ANDROID && !UNITY_EDITOR
    // Helpers estáticos para evitar lookups repetidos
    static readonly AndroidJavaClass CLS_PFD        = new AndroidJavaClass("android.os.ParcelFileDescriptor");
    static readonly AndroidJavaClass CLS_BITMAP     = new AndroidJavaClass("android.graphics.Bitmap");
    static readonly AndroidJavaClass CLS_BITMAP_CFG = new AndroidJavaClass("android.graphics.Bitmap$Config");
    static readonly AndroidJavaClass CLS_CMPFMT     = new AndroidJavaClass("android.graphics.Bitmap$CompressFormat");
    static readonly AndroidJavaClass CLS_COLOR      = new AndroidJavaClass("android.graphics.Color");

    // Abre rutas del filesystem, file:// y content:// (Storage Access Framework)
    static AndroidJavaObject OpenPfd(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        if (filePath.StartsWith("content://"))
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var resolver    = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var uriCls      = new AndroidJavaClass("android.net.Uri"))
            using (var uri         = uriCls.CallStatic<AndroidJavaObject>("parse", filePath))
            {
                return resolver.Call<AndroidJavaObject>("openFileDescriptor", uri, "r");
            }
        }
        else
        {
            // Si viene "file://", parsear a path local
            if (filePath.StartsWith("file://"))
            {
                using (var uriCls = new AndroidJavaClass("android.net.Uri"))
                using (var uri = uriCls.CallStatic<AndroidJavaObject>("parse", filePath))
                {
                    filePath = uri.Call<string>("getPath");
                }
            }

            int RO = CLS_PFD.GetStatic<int>("MODE_READ_ONLY");
            using (var file = new AndroidJavaObject("java.io.File", filePath))
            {
                return CLS_PFD.CallStatic<AndroidJavaObject>("open", file, RO);
            }
        }
    }

    public static int GetPageCount(string filePath)
    {
        AndroidJavaObject pfd = null;
        AndroidJavaObject renderer = null;
        try
        {
            pfd = OpenPfd(filePath);
            if (pfd == null) return 0;

            renderer = new AndroidJavaObject("android.graphics.pdf.PdfRenderer", pfd);
            return renderer.Call<int>("getPageCount");
        }
        finally
        {
            if (renderer != null) renderer.Call("close");
            if (pfd != null)      pfd.Call("close");
        }
    }

    public static Texture2D RenderPage(string filePath, int pageIndex, int targetLongSide = 1024)
    {
        AndroidJavaObject pfd = null;
        AndroidJavaObject renderer = null;
        AndroidJavaObject page = null;
        AndroidJavaObject bmp = null;

        try
        {
            pfd = OpenPfd(filePath);
            if (pfd == null) return Texture2D.whiteTexture;

            renderer = new AndroidJavaObject("android.graphics.pdf.PdfRenderer", pfd);
            page = renderer.Call<AndroidJavaObject>("openPage", pageIndex);

            int srcW = page.Call<int>("getWidth");
            int srcH = page.Call<int>("getHeight");
            if (srcW <= 0) srcW = 1;
            if (srcH <= 0) srcH = 1;

            // Escalado manteniendo aspecto, limitado por la GPU
            int maxTex = Mathf.Max(512, SystemInfo.maxTextureSize);
            targetLongSide = Mathf.Clamp(targetLongSide, 16, maxTex);

            float scale = (float)targetLongSide / Mathf.Max(srcW, srcH);
            scale = Mathf.Max(0.01f, scale);

            int outW = Mathf.Clamp(Mathf.RoundToInt(srcW * scale), 2, maxTex);
            int outH = Mathf.Clamp(Mathf.RoundToInt(srcH * scale), 2, maxTex);

            // Bitmap.createBitmap(w, h, Config.ARGB_8888)
            using (var cfg = CLS_BITMAP_CFG.GetStatic<AndroidJavaObject>("ARGB_8888"))
            {
                bmp = CLS_BITMAP.CallStatic<AndroidJavaObject>("createBitmap", outW, outH, cfg);
            }

            // Fondo blanco para PDFs con transparencia
            using (var canvas = new AndroidJavaObject("android.graphics.Canvas", bmp))
            {
                int WHITE = CLS_COLOR.GetStatic<int>("WHITE");
                canvas.Call("drawColor", WHITE);
            }

            // Render con destino exacto (evita “imagen movida” en primera página)
            using (var rect = new AndroidJavaObject("android.graphics.Rect", 0, 0, outW, outH))
            {
                page.Call("render", bmp, rect, null, 1 /*RENDER_MODE_FOR_DISPLAY*/);
            }

            // Bitmap -> PNG -> Texture2D (no-readable = menos RAM)
            using (var baos = new AndroidJavaObject("java.io.ByteArrayOutputStream"))
            {
                using (var PNG = CLS_CMPFMT.GetStatic<AndroidJavaObject>("PNG"))
                {
                    bmp.Call<bool>("compress", PNG, 100, baos);
                }

                byte[] data = baos.Call<byte[]>("toByteArray");

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(data, /*markNonReadable:*/ true); // No llamar a Apply() luego
                tex.wrapMode   = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.anisoLevel = 2;

                return tex;
            }
        }
        finally
        {
            if (bmp      != null) bmp.Call("recycle");
            if (page     != null) page.Call("close");
            if (renderer != null) renderer.Call("close");
            if (pfd      != null) pfd.Call("close");
        }
    }
#else
    public static int GetPageCount(string _) => 0;
    public static Texture2D RenderPage(string _, int __, int ___ = 1024) => Texture2D.whiteTexture;
#endif
}
