using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

public class BrowserUrlFromIsbn : MonoBehaviour
{
    [Header("URL (usá {isbn} y opcional {progress})")]
    public string template = "http://www.img-front.nicolasirigoyen.com.ar/?isbn={isbn}";

    [Header("Targets (arrastrá tus componentes WebView y/o GeckoView)")]
    public Behaviour[] targets;

    [Header("Opcional – nombres del plugin")]
    public string urlFieldOrProperty = "Url";   // suele llamarse Url
    public string navigateMethod = "LoadUrl";   // probá LoadUrl / OpenURL / Navigate

    [Header("Timing")]
    public bool applyOnEnable = true;
    public float applyDelaySeconds = 0.25f;

    [Header("Otros")]
    public bool skipIfEmptyIsbn = true;
    public bool includeProgress = false; // habilitalo si usás {progress}

#if UNITY_EDITOR
    [Header("Debug (Editor)")]
    public string testIsbnIfEmptyInEditor = "0000000000";
#endif

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

    IEnumerator ApplyDelayed() { yield return new WaitForSeconds(applyDelaySeconds); Apply(); }
    void OnStoreChanged(GlobalBookStore.BookRecord _) => Apply();

    [ContextMenu("Apply now")]
    public void Apply()
    {
        string isbn = GlobalBookStore.I?.isbn ?? GlobalBookStore.I?.FindCurrentInLibrary()?.isbn;
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(isbn)) isbn = testIsbnIfEmptyInEditor;
#endif
        if (string.IsNullOrEmpty(isbn) && skipIfEmptyIsbn)
        {
            Debug.Log("[BrowserUrlFromIsbn] No ISBN, salto.");
            return;
        }

        int progress = 0;
        if (includeProgress && GlobalBookStore.I != null)
        {
            var rec = GlobalBookStore.I.FindCurrentInLibrary();
            if (rec != null && rec.pageCount > 0)
                progress = Mathf.RoundToInt(Mathf.Clamp01(rec.progress01) * 100f);
        }

        string url = template.Replace("{isbn}", Uri.EscapeDataString(isbn ?? ""))
                             .Replace("{progress}", progress.ToString());

        var comps = ResolveTargets();
        foreach (var c in comps) TrySetUrlAndNavigate(c, url);

        Debug.Log("[BrowserUrlFromIsbn] URL -> " + url);
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
            foreach (var cand in asm.GetTypes())
                if (cand.Name == name) return cand;
        }
        return null;
    }

    void TrySetUrlAndNavigate(Component c, string url)
    {
        if (!c) return;
        var t = c.GetType();

        // Campo/propiedad "Url"
        var f = t.GetField(urlFieldOrProperty) ?? t.GetField(urlFieldOrProperty.ToLower());
        if (f != null && f.FieldType == typeof(string)) f.SetValue(c, url);

        var p = t.GetProperty(urlFieldOrProperty) ?? t.GetProperty(urlFieldOrProperty.ToLower());
        if (p != null && p.CanWrite && p.PropertyType == typeof(string)) p.SetValue(c, url);

        // Método de navegación principal
        if (!string.IsNullOrEmpty(navigateMethod))
        {
            var mStr = t.GetMethod(navigateMethod, new[] { typeof(string) });
            var mNo = t.GetMethod(navigateMethod, Type.EmptyTypes);
            if (mStr != null) { mStr.Invoke(c, new object[] { url }); return; }
            if (mNo != null) { mNo.Invoke(c, null); return; }
        }

        // Intentos comunes (corregido: nada de "||" suelto)
        bool invoked = false;
        if (!invoked) invoked = TryInvoke(t, c, "OpenURL", url);
        if (!invoked) invoked = TryInvoke(t, c, "OpenUrl", url);
        if (!invoked) invoked = TryInvoke(t, c, "Navigate", url);
        if (!invoked) invoked = TryInvoke(t, c, "Load", url);
    }

    static bool TryInvoke(Type t, object inst, string name, string arg)
    {
        var m = t.GetMethod(name, new[] { typeof(string) });
        if (m == null) return false;
        m.Invoke(inst, new object[] { arg });
        return true;
    }
}
