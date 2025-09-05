using UnityEngine;

// Render de páginas PDF en Android (Quest) usando android.graphics.pdf.PdfRenderer
public static class PdfRendererAndroid
{
#if UNITY_ANDROID && !UNITY_EDITOR
    // Helpers estáticos para evitar lookups repetidos
    static readonly AndroidJavaClass CLS_PFD = new AndroidJavaClass("android.os.ParcelFileDescriptor");
    static readonly AndroidJavaClass CLS_BITMAP = new AndroidJavaClass("android.graphics.Bitmap");
    static readonly AndroidJavaClass CLS_BITMAP_CFG = new AndroidJavaClass("android.graphics.Bitmap$Config");
    static readonly AndroidJavaClass CLS_CMPFMT = new AndroidJavaClass("android.graphics.Bitmap$CompressFormat");

    public static int GetPageCount(string filePath)
    {
        AndroidJavaObject pfd = null;
        AndroidJavaObject renderer = null;
        try
        {
            using (var file = new AndroidJavaObject("java.io.File", filePath))
            {
                int RO = CLS_PFD.GetStatic<int>("MODE_READ_ONLY");
                pfd = CLS_PFD.CallStatic<AndroidJavaObject>("open", file, RO);
                renderer = new AndroidJavaObject("android.graphics.pdf.PdfRenderer", pfd);
                return renderer.Call<int>("getPageCount");
            }
        }
        finally
        {
            // cerrar en orden inverso
            if (renderer != null) renderer.Call("close");
            if (pfd != null) pfd.Call("close");
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
            using (var file = new AndroidJavaObject("java.io.File", filePath))
            {
                int RO = CLS_PFD.GetStatic<int>("MODE_READ_ONLY");
                pfd = CLS_PFD.CallStatic<AndroidJavaObject>("open", file, RO);
                renderer = new AndroidJavaObject("android.graphics.pdf.PdfRenderer", pfd);
                page = renderer.Call<AndroidJavaObject>("openPage", pageIndex);

                int srcW = page.Call<int>("getWidth");
                int srcH = page.Call<int>("getHeight");
                float scale = (float)targetLongSide / Mathf.Max(srcW, srcH);
                int outW = Mathf.Max(2, Mathf.RoundToInt(srcW * scale));
                int outH = Mathf.Max(2, Mathf.RoundToInt(srcH * scale));

                // Bitmap.createBitmap(w, h, Config.ARGB_8888)
                using (var cfg = CLS_BITMAP_CFG.GetStatic<AndroidJavaObject>("ARGB_8888"))
                {
                    bmp = CLS_BITMAP.CallStatic<AndroidJavaObject>("createBitmap", outW, outH, cfg);
                }

                // page.render(bitmap, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY (=1))
                page.Call("render", bmp, null, null, 1);

                // Pasamos Bitmap -> PNG bytes -> Texture2D
                using (var baos = new AndroidJavaObject("java.io.ByteArrayOutputStream"))
                {
                    using (var PNG = CLS_CMPFMT.GetStatic<AndroidJavaObject>("PNG"))
                    {
                        bmp.Call<bool>("compress", PNG, 100, baos);
                    }

                    byte[] data = baos.Call<byte[]>("toByteArray");
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.LoadImage(data);
                    tex.Apply();
                    return tex;
                }
            }
        }
        finally
        {
            if (bmp != null) bmp.Call("recycle");
            if (page != null) page.Call("close");
            if (renderer != null) renderer.Call("close");
            if (pfd != null) pfd.Call("close");
        }
    }
#else
    public static int GetPageCount(string _) => 0;
    public static Texture2D RenderPage(string _, int __, int ___ = 1024) => Texture2D.whiteTexture;
#endif
}
