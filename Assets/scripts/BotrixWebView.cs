using System;
using System.Collections;
using UnityEngine;
using Gree.UnityWebView;

public class BotrixWebView : MonoBehaviour
{
    [Header("BOTRIX")]
    [SerializeField] private string botrixUrl = "";

    [Header("POSICIÓN")]
    [SerializeField] private int marginLeft = 0;
    [SerializeField] private int marginTop = 0;
    [SerializeField] private int marginRight = 0;
    [SerializeField] private int marginBottom = 0;

    private WebViewObject webView;

    private bool conectado;
    private bool iniciando;

    public event Action<string, string, string> OnChatMessage;

    public void Conectar(string nuevaUrl)
    {
        if (iniciando)
            return;

        if (conectado)
        {
            Debug.LogWarning(
                "⚠️ Botrix ya está conectado."
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(nuevaUrl))
        {
            Debug.LogError(
                "❌ URL de Botrix vacía."
            );
            return;
        }

        botrixUrl = nuevaUrl.Trim();

        DestruirWebView();

        StartCoroutine(IniciarWebView());
    }

    private IEnumerator IniciarWebView()
    {
        iniciando = true;

        GameObject objeto =
            new GameObject("BotrixWebView");

        webView =
            objeto.AddComponent<WebViewObject>();

        webView.Init(
            cb: RecibirCallback,

            err: mensaje =>
            {
                Debug.LogError(
                    "[Botrix ERROR] " + mensaje
                );
            },

            httpErr: mensaje =>
            {
                Debug.LogError(
                    "[Botrix HTTP ERROR] " + mensaje
                );
            },

            started: mensaje =>
            {
                if (string.IsNullOrEmpty(mensaje))
                    return;

                if (mensaje.StartsWith(
                        "unity:BOTRIX|",
                        StringComparison.Ordinal))
                {
                    RecibirDesdeJavaScript(
                        mensaje.Substring(
                            "unity:".Length
                        )
                    );
                }
            },

            hooked: mensaje =>
            {
                Debug.Log(
                    "[Botrix] Hook: " + mensaje
                );
            },

            cookies: mensaje =>
            {
                // No necesitamos las cookies.
            },

            ld: mensaje =>
            {
                PrepararJavaScript();
            }
        );

        while (webView != null &&
               !webView.IsInitialized())
        {
            yield return null;
        }

        if (webView == null)
        {
            iniciando = false;
            yield break;
        }

        webView.SetMargins(
            marginLeft,
            marginTop,
            marginRight,
            marginBottom
        );

        webView.SetVisibility(false);

        webView.LoadURL(botrixUrl);

        conectado = true;
        iniciando = false;

        Debug.Log(
            "✅ Botrix conectado."
        );
    }

    private void RecibirCallback(string mensaje)
    {
        if (string.IsNullOrEmpty(mensaje))
            return;

        RecibirDesdeJavaScript(mensaje);
    }

    public void Desconectar()
    {
        DestruirWebView();

        conectado = false;
        iniciando = false;

        Debug.Log(
            "🔴 Botrix desconectado."
        );
    }

    private void DestruirWebView()
    {
        if (webView == null)
            return;

        Destroy(webView.gameObject);
        webView = null;
    }

    private void PrepararJavaScript()
    {
        if (webView == null)
            return;

        string javascript = @"
(function () {

    if (window.__UNITY_BOTRIX_INSTALLED)
        return;

    window.__UNITY_BOTRIX_INSTALLED = true;

    const procesados = new WeakSet();

    function obtenerPlataforma(elemento) {

        const clase =
            typeof elemento.className === 'string'
                ? elemento.className.toLowerCase()
                : '';

        if (clase.includes('twitch'))
            return 'Twitch';

        if (clase.includes('youtube'))
            return 'YouTube';

        if (clase.includes('kick'))
            return 'Kick';

        return 'Unknown';
    }

    function limpiarNombre(nombre, mensaje) {

        nombre = (nombre || '').trim();
        mensaje = (mensaje || '').trim();

        if (!nombre)
            return 'User';

        if (
            mensaje &&
            nombre.endsWith(mensaje)
        ) {
            nombre = nombre
                .substring(
                    0,
                    nombre.length - mensaje.length
                )
                .trim();
        }

        if (nombre.startsWith('@'))
            nombre = nombre.substring(1);

        return nombre.trim() || 'User';
    }

    function enviarUnity(
        nombre,
        mensaje,
        plataforma
    ) {

        const datos = {
            nombre: nombre,
            mensaje: mensaje,
            plataforma: plataforma
        };

        const json =
            JSON.stringify(datos);

        const mensajeUnity =
            'BOTRIX|' +
            encodeURIComponent(json);

        if (
            window.Unity &&
            typeof window.Unity.call === 'function'
        ) {
            window.Unity.call(mensajeUnity);
            return;
        }

        window.location =
            'unity:' + mensajeUnity;
    }

    function revisarChat() {

        const mensajes =
            document.querySelectorAll('.chatMsg');

        mensajes.forEach(function (elemento) {

            if (procesados.has(elemento))
                return;

            const nombreElemento =
                elemento.querySelector('.name');

            const mensajeElemento =
                elemento.querySelector('.message');

            if (
                !nombreElemento ||
                !mensajeElemento
            ) {
                return;
            }

            let nombre =
                nombreElemento.textContent.trim();

            let mensaje =
                mensajeElemento.textContent.trim();

            if (!nombre || !mensaje)
                return;

            nombre =
                limpiarNombre(
                    nombre,
                    mensaje
                );

            const plataforma =
                obtenerPlataforma(elemento);

            procesados.add(elemento);

            enviarUnity(
                nombre,
                mensaje,
                plataforma
            );
        });
    }

    function iniciarObserver() {

        if (!document.body) {
            setTimeout(
                iniciarObserver,
                100
            );

            return;
        }

        const observer =
            new MutationObserver(
                function () {
                    revisarChat();
                }
            );

        observer.observe(
            document.body,
            {
                childList: true,
                subtree: true
            }
        );

        revisarChat();
    }

    iniciarObserver();

})();
";

        webView.EvaluateJS(javascript);
    }

    private void RecibirDesdeJavaScript(
        string mensaje)
    {
        if (string.IsNullOrEmpty(mensaje))
            return;

        if (mensaje.StartsWith("unity:"))
        {
            mensaje =
                mensaje.Substring(
                    "unity:".Length
                );
        }

        if (!mensaje.StartsWith(
                "BOTRIX|",
                StringComparison.Ordinal))
        {
            return;
        }

        string json =
            mensaje.Substring(
                "BOTRIX|".Length
            );

        try
        {
            json =
                Uri.UnescapeDataString(json);

            BotrixMessage data =
                JsonUtility.FromJson<BotrixMessage>(
                    json
                );

            if (data == null)
                return;

            string nombre =
                LimpiarTexto(data.nombre);

            string texto =
                LimpiarTexto(data.mensaje);

            string plataforma =
                LimpiarTexto(data.plataforma);

            if (string.IsNullOrEmpty(nombre))
                nombre = "User";

            if (string.IsNullOrEmpty(plataforma))
                plataforma = "Unknown";

            if (string.IsNullOrEmpty(texto))
                return;

            OnChatMessage?.Invoke(
                nombre,
                texto,
                plataforma
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "❌ Error procesando Botrix: " +
                e.Message
            );
        }
    }

    private string LimpiarTexto(string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return "";

        return texto
            .Replace("\u200B", "")
            .Replace("\u200C", "")
            .Replace("\u200D", "")
            .Replace("\uFEFF", "")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Trim();
    }

    [Serializable]
    private class BotrixMessage
    {
        public string nombre;
        public string mensaje;
        public string plataforma;
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        DestruirWebView();

        conectado = false;
        iniciando = false;
    }
}