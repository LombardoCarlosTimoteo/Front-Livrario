using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;       // ← reflexión
using UnityEngine;

public class BrowserUrlFromIsbn : MonoBehaviour
{
    [Header("URL (usa {isbn}, {progress} y/o {p20})")]
    // Por defecto, formato /{isbn}/{p20} (bucket 0/20/40/60/80)
    public string template = "http://www.img-front.nicolasirigoyen.com.ar/{isbn}/{p20}";

    [Header("Targets (arrastrá WebView/GeckoView si querés forzar)")]
    public Behaviour[] targets;

    [Header("Opcional – nombres del plugin")]
    public string urlFieldOrProperty = "Url";   // p.ej. Url / URL / url
    public string navigateMethod     = "LoadUrl"; // probables: LoadUrl/OpenURL/Navigate/Load

    [Header("Timing")]
    public bool  applyOnEnable     = true;
    public float applyDelaySeconds = 0.25f;

    [Header("Comportamiento")]
    public bool skipIfEmptyIsbn      = true;
    public bool includeProgress      = true;     // usa {progress} y/o {p20}
    public bool onlyUpdateOnBucketChange = true; // ✅ solo actualizar si cambia el bucket
    public bool requireValidBucketToUpdate = true; // ✅ sólo 0/20/40/60/80 disparan actualización

#if UNITY_EDITOR
    [Header("Debug (Editor)")]
    public string testIsbnIfEmptyInEditor   = "0000000000";
    public int    testProgressIfEmptyInEditor = 0; // 0..100
#endif

    // --- cache para evitar recargas innecesarias ---
    string _lastIsbnSent   = null;
    int    _lastP20Sent    = -1;
    string _lastUrlSent    = null;

    void OnEnable()
    {
        if (applyOnEnable) StartCoroutine(ApplyDelayed());
        if (GlobalBookStore.I != null)
        {
            GlobalBookStore.I.OnMetadataChanged += OnStoreChanged;
            GlobalBookStore.I.OnProgressChanged += OnStoreChanged;
        }
    }

    void OnDisable()
    {
        if (GlobalBookStore.I != null)
        {
            GlobalBookStore.I.OnMetadataChanged -= OnStoreChanged;
            GlobalBookStore.I.OnProgressChanged -= OnStoreChanged;
        }
    }

    IEnumerator ApplyDelayed()
    {
        if (applyDelaySeconds > 0f)
            yield return new WaitForSeconds(applyDelaySeconds);
        Apply();
    }

    void OnStoreChanged(GlobalBookStore.BookRecord _) => Apply();

    [ContextMenu("Apply now")]
    public void Apply()
    {
        // --- ISBN actual ---
        string isbn = GetCurrentIsbn();
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(isbn)) isbn = testIsbnIfEmptyInEditor;
#endif
        if (string.IsNullOrEmpty(isbn) && skipIfEmptyIsbn)
        {
            Debug.Log("[BrowserUrlFromIsbn] No hay ISBN, no navego.");
            return;
        }

        // --- Progreso ---
        int progressPercent = includeProgress ? GetProgressPercent() : 0;
#if UNITY_EDITOR
        if (includeProgress && GlobalBookStore.I == null)
            progressPercent = Mathf.Clamp(testProgressIfEmptyInEditor, 0, 100);
#endif
        int p20 = ToBucket20(progressPercent); // 0/20/40/60/80

        // --- Gating: sólo actualizar cuando corresponde ---
        if (requireValidBucketToUpdate && !IsValidBucket(p20))
        {
            // En la práctica nunca entra (p20 siempre es 0..80 múltiplo de 20),
            // pero lo dejamos por claridad si se cambia la lógica.
            return;
        }

        bool bucketChanged = (p20 != _lastP20Sent);
        bool isbnChanged   = (_lastIsbnSent == null) || !string.Equals(isbn, _lastIsbnSent, StringComparison.Ordinal);

        if (onlyUpdateOnBucketChange && !bucketChanged && !isbnChanged)
        {
            // Nada que hacer (mismo bucket y mismo ISBN)
            return;
        }

        // --- Armar URL ---
        string url = template
            .Replace("{isbn}",     Uri.EscapeDataString(isbn ?? ""))
            .Replace("{progress}", progressPercent.ToString())
            .Replace("{p20}",      p20.ToString());

        // Evitar re-disparar si la URL quedó idéntica
        if (string.Equals(url, _lastUrlSent, StringComparison.Ordinal))
            return;

        // --- Enviar a los viewers ---
        foreach (var c in ResolveTargets()) TrySetUrlAndNavigate(c, url);

        _lastIsbnSent = isbn;
        _lastP20Sent  = p20;
        _lastUrlSent  = url;

        Debug.Log($"[BrowserUrlFromIsbn] URL -> {url}");
    }

    // ================= Helpers =================

    string GetCurrentIsbn()
    {
        var g = GlobalBookStore.I;
        if (g == null) return "";
        if (!string.IsNullOrEmpty(g.isbn)) return g.isbn;
        var rec = g.FindCurrentInLibrary();
        return rec != null ? (rec.isbn ?? "") : "";
    }

    int GetProgressPercent()
    {
        var g = GlobalBookStore.I;
        if (g == null) return 0;
        var rec = g.FindCurrentInLibrary();
        if (rec != null && rec.pageCount > 0)
            return Mathf.Clamp(Mathf.RoundToInt(rec.progress01 * 100f), 0, 100);
        return 0;
    }

    // Floor a múltiplos de 20 y tope en 80 (regla que pediste de “anterior más próximo”)
    static int ToBucket20(int percent)
    {
        int c = Mathf.Clamp(percent, 0, 100);
        int bucket = (c / 20) * 20;   // floor
        return Mathf.Min(bucket, 80); // nunca 100
    }

    static bool IsValidBucket(int p20)
    {
        return p20 == 0 || p20 == 20 || p20 == 40 || p20 == 60 || p20 == 80;
    }

    List<Component> ResolveTargets()
    {
        var list = new List<Component>();
        if (targets != null && targets.Length > 0)
        {
            foreach (var t in targets) if (t) list.Add(t);
        }
        else
        {
            AddByName("WebView", list);
            AddByName("GeckoView", list);
        }
        return list;
    }

    void AddByName(string typeName, List<Component> list)
    {
        var t = GetTypeByName(typeName);
        if (t == null) return;
        var here = GetComponent(t);
        if (here) list.Add(here);
        foreach (var c in GetComponentsInChildren(t, true))
            if (!list.Contains(c)) list.Add(c);
    }

    static Type GetTypeByName(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(name);
            if (t != null) return t;
            try
            {
                foreach (var cand in asm.GetTypes())
                    if (cand.Name == name) return cand;
            }
            catch { /* algunos asm pueden tirar */ }
        }
        return null;
    }

    void TrySetUrlAndNavigate(Component c, string url)
    {
        if (!c) return;
        var t = c.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Campo/propiedad Url (case-insensitive: Url / URL / url)
        var f = GetFieldCaseInsensitive(t, urlFieldOrProperty, flags);
        if (f != null && f.FieldType == typeof(string))
        {
            try { f.SetValue(c, url); } catch { }
        }

        var p = GetPropertyCaseInsensitive(t, urlFieldOrProperty, flags);
        if (p != null && p.CanWrite && p.PropertyType == typeof(string))
        {
            try { p.SetValue(c, url, null); } catch { }
        }

        // Método explícito si lo indicás
        if (!string.IsNullOrEmpty(navigateMethod))
        {
            var mStr = GetMethodCaseInsensitive(t, navigateMethod, new[] { typeof(string) }, flags);
            var mNo  = GetMethodCaseInsensitive(t, navigateMethod, Type.EmptyTypes, flags);
            if (mStr != null) { try { mStr.Invoke(c, new object[] { url }); return; } catch { } }
            if (mNo  != null) { try { mNo.Invoke(c, null); return; } catch { } }
        }

        // Intentos comunes
        if (TryInvoke(t, c, "OpenURL", url, flags)) return;
        if (TryInvoke(t, c, "OpenUrl", url, flags)) return;
        if (TryInvoke(t, c, "Navigate", url, flags)) return;
        if (TryInvoke(t, c, "LoadUrl", url, flags)) return;
        if (TryInvoke(t, c, "Load",    url, flags)) return;
    }

    static FieldInfo GetFieldCaseInsensitive(Type t, string name, BindingFlags flags)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var f = t.GetField(name, flags);
        if (f != null) return f;
        // fallback por mayúsculas/minúsculas
        foreach (var fi in t.GetFields(flags))
            if (string.Equals(fi.Name, name, StringComparison.OrdinalIgnoreCase))
                return fi;
        return null;
    }

    static PropertyInfo GetPropertyCaseInsensitive(Type t, string name, BindingFlags flags)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var p = t.GetProperty(name, flags);
        if (p != null) return p;
        foreach (var pi in t.GetProperties(flags))
            if (string.Equals(pi.Name, name, StringComparison.OrdinalIgnoreCase))
                return pi;
        return null;
    }

    static MethodInfo GetMethodCaseInsensitive(Type t, string name, Type[] sig, BindingFlags flags)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var m = t.GetMethod(name, flags, null, sig, null);
        if (m != null) return m;
        foreach (var mi in t.GetMethods(flags))
            if (string.Equals(mi.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                var pars = mi.GetParameters();
                if ((sig == null && pars.Length == 0) ||
                    (sig != null && pars.Length == sig.Length))
                    return mi;
            }
        return null;
    }

    static bool TryInvoke(Type t, object inst, string methodName, string arg, BindingFlags flags)
    {
        var m = GetMethodCaseInsensitive(t, methodName, new[] { typeof(string) }, flags);
        if (m == null) return false;
        try { m.Invoke(inst, new object[] { arg }); return true; } catch { return false; }
    }
}
