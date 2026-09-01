using System;
using System.Collections;
using UnityEngine;
using Gree.UnityWebView;

public class BotrixWebView : MonoBehaviour
{
    // ==================================================
    // BOTRIX
    // ==================================================

    [Header("BOTRIX")]

    [SerializeField]
    private string botrixUrl =
        "https://botrix.live/widgets/chat/?bid=6PInjEti9a6JAkQK0M8FjA&bots=false&emojis=true";


    // ==================================================
    // POSICIÓN
    // ==================================================

    [Header("POSICIÓN")]

    [SerializeField]
    private int marginLeft = 0;

    [SerializeField]
    private int marginTop = 0;

    [SerializeField]
    private int marginRight = 0;

    [SerializeField]
    private int marginBottom = 0;


    // ==================================================
    // WEBVIEW
    // ==================================================

    private WebViewObject webView;

    private bool conectado = false;

    private bool iniciando = false;


    // ==================================================
    // EVENTO
    // ==================================================

    public event Action<string, string, string>
        OnChatMessage;


    // ==================================================
    // CONECTAR
    // ==================================================

    public void Conectar(string nuevaUrl)
    {
        if (conectado)
        {
            Debug.LogWarning(
                "⚠️ Botrix ya está conectado"
            );

            return;
        }

        if (iniciando)
        {
            Debug.LogWarning(
                "⚠️ Botrix ya se está iniciando"
            );

            return;
        }

        if (string.IsNullOrWhiteSpace(nuevaUrl))
        {
            Debug.LogError(
                "❌ URL de Botrix vacía"
            );

            return;
        }

        botrixUrl =
            nuevaUrl.Trim();

        StartCoroutine(
            IniciarWebView()
        );
    }


    // ==================================================
    // INICIAR WEBVIEW
    // ==================================================

    private IEnumerator IniciarWebView()
    {
        iniciando = true;

        Debug.Log(
            "🔄 Iniciando Botrix..."
        );


        // ==================================================
        // CREAR WEBVIEW
        // ==================================================

        GameObject objeto =
            new GameObject(
                "BotrixWebView"
            );

        webView =
            objeto.AddComponent<WebViewObject>();


        // ==================================================
        // INIT
        // ==================================================

        webView.Init(

            // ==================================================
            // JAVASCRIPT -> UNITY
            // ==================================================

            cb: msg =>
            {
                Debug.Log(
                    "[Botrix -> Unity] " +
                    msg
                );

                RecibirDesdeJavaScript(
                    msg
                );
            },


            // ==================================================
            // ERROR
            // ==================================================

            err: msg =>
            {
                Debug.LogError(
                    "[Botrix ERROR] " +
                    msg
                );
            },


            // ==================================================
            // HTTP ERROR
            // ==================================================

            httpErr: msg =>
            {
                Debug.LogError(
                    "[Botrix HTTP ERROR] " +
                    msg
                );
            },


            // ==================================================
            // STARTED
            // ==================================================

            started: msg =>
            {
                Debug.Log(
                    "Botrix iniciado: " +
                    msg
                );

                if (string.IsNullOrEmpty(msg))
                {
                    return;
                }

                if (
                    msg.StartsWith(
                        "unity:BOTRIX|"
                    )
                )
                {
                    string datos =
                        msg.Substring(
                            "unity:".Length
                        );

                    RecibirDesdeJavaScript(
                        datos
                    );
                }
            },


            // ==================================================
            // HOOKED
            // ==================================================

            hooked: msg =>
            {
                Debug.Log(
                    "Botrix hook: " +
                    msg
                );
            },


            // ==================================================
            // COOKIES
            // ==================================================

            cookies: msg =>
            {
            },


            // ==================================================
            // PAGE LOADED
            // ==================================================

            ld: msg =>
            {
                Debug.Log(
                    "✅ Botrix página cargada"
                );

                PrepararJavaScript();
            }
        );


        // ==================================================
        // ESPERAR WEBVIEW
        // ==================================================

        while (
            webView != null &&
            !webView.IsInitialized()
        )
        {
            yield return null;
        }


        if (webView == null)
        {
            iniciando = false;

            yield break;
        }


        // ==================================================
        // MÁRGENES
        // ==================================================

        webView.SetMargins(
            marginLeft,
            marginTop,
            marginRight,
            marginBottom
        );


        // ==================================================
        // CARGAR URL
        // ==================================================

        webView.LoadURL(
            botrixUrl.Replace(
                " ",
                "%20"
            )
        );


        // ==================================================
        // OCULTAR WEBVIEW
        // ==================================================

        webView.SetVisibility(
            false
        );


        conectado = true;

        iniciando = false;


        Debug.Log(
            "✅ Botrix conectado"
        );

        Debug.Log(
            "👻 WebView Botrix oculto"
        );
    }


    // ==================================================
    // DESCONECTAR
    // ==================================================

    public void Desconectar()
    {
        if (webView != null)
        {
            Destroy(
                webView.gameObject
            );

            webView = null;
        }

        conectado = false;

        iniciando = false;

        Debug.Log(
            "🔴 Botrix desconectado"
        );
    }


    // ==================================================
    // JAVASCRIPT
    // ==================================================

    private void PrepararJavaScript()
    {
        if (webView == null)
        {
            return;
        }


        string javascript = @"
(function() {

    // ==================================================
    // EVITAR INSTALAR DOS VECES
    // ==================================================

    if (window.__UNITY_BOTRIX_INSTALLED)
        return;

    window.__UNITY_BOTRIX_INSTALLED = true;


    console.log(
        '[Unity] Detector Botrix iniciado'
    );


    // ==================================================
    // MENSAJES YA PROCESADOS
    // ==================================================

    const procesados =
        new WeakSet();


    // ==================================================
    // OBTENER PLATAFORMA
    // ==================================================

    function obtenerPlataforma(elemento)
    {
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


    // ==================================================
    // LIMPIAR NOMBRE
    // ==================================================

    function limpiarNombre(
        nombre,
        mensaje
    )
    {
        nombre =
            nombre.trim();

        mensaje =
            mensaje.trim();


        if (
            mensaje &&
            nombre.endsWith(mensaje)
        )
        {
            nombre =
                nombre.substring(
                    0,
                    nombre.length -
                    mensaje.length
                ).trim();
        }


        // --------------------------------------------------
        // Si quedó separado por espacios
        // --------------------------------------------------

        if (
            mensaje &&
            nombre.endsWith(
                ' ' + mensaje
            )
        )
        {
            nombre =
                nombre.substring(
                    0,
                    nombre.length -
                    mensaje.length -
                    1
                ).trim();
        }


        return nombre;
    }


    // ==================================================
    // ENVIAR A UNITY
    // ==================================================

    function enviarUnity(
        nombre,
        mensaje,
        plataforma
    )
    {
        const datos = {

            nombre: nombre,

            mensaje: mensaje,

            plataforma: plataforma

        };


        const json =
            JSON.stringify(
                datos
            );


        const mensajeUnity =
            'BOTRIX|' +
            encodeURIComponent(
                json
            );


        // ==================================================
        // UNITY WEBVIEW
        // ==================================================

        if (
            window.Unity &&
            typeof window.Unity.call === 'function'
        )
        {
            window.Unity.call(
                mensajeUnity
            );

            return;
        }


        // ==================================================
        // FALLBACK
        // ==================================================

        window.location =
            'unity:' +
            mensajeUnity;
    }


    // ==================================================
    // REVISAR CHAT
    // ==================================================

    function revisarChat()
    {
        const mensajes =
            document.querySelectorAll(
                '.chatMsg'
            );


        mensajes.forEach(
            function(elemento)
            {
                // ==========================================
                // YA PROCESADO
                // ==========================================

                if (
                    procesados.has(
                        elemento
                    )
                )
                {
                    return;
                }


                // ==========================================
                // NOMBRE
                // ==========================================

                const nombreElemento =
                    elemento.querySelector(
                        '.name'
                    );


                // ==========================================
                // MENSAJE
                // ==========================================

                const mensajeElemento =
                    elemento.querySelector(
                        '.message'
                    );


                if (
                    !nombreElemento ||
                    !mensajeElemento
                )
                {
                    return;
                }


                let nombre =
                    nombreElemento.textContent.trim();


                let mensaje =
                    mensajeElemento.textContent.trim();


                // ==========================================
                // VALIDAR
                // ==========================================

                if (
                    !nombre ||
                    !mensaje
                )
                {
                    return;
                }


                // ==========================================
                // CORREGIR NOMBRE
                // ==========================================

                nombre =
                    limpiarNombre(
                        nombre,
                        mensaje
                    );


                if (!nombre)
                {
                    nombre = 'User';
                }


                // ==========================================
                // MARCAR PROCESADO
                // ==========================================

                procesados.add(
                    elemento
                );


                // ==========================================
                // PLATAFORMA
                // ==========================================

                const plataforma =
                    obtenerPlataforma(
                        elemento
                    );


                // ==========================================
                // DEBUG
                // ==========================================

                console.log(
                    '[Unity] Chat:',
                    nombre,
                    mensaje,
                    plataforma
                );


                // ==========================================
                // ENVIAR
                // ==========================================

                enviarUnity(
                    nombre,
                    mensaje,
                    plataforma
                );
            }
        );
    }


    // ==================================================
    // OBSERVER
    // ==================================================

    const observer =
        new MutationObserver(
            function()
            {
                revisarChat();
            }
        );


    function iniciarObserver()
    {
        if (!document.body)
        {
            setTimeout(
                iniciarObserver,
                100
            );

            return;
        }


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


    // ==================================================
    // SEGURIDAD
    // ==================================================

    setInterval(
        revisarChat,
        500
    );


})();
";


        Debug.Log(
            "✅ Instalando detector JavaScript de Botrix"
        );


        webView.EvaluateJS(
            javascript
        );
    }


    // ==================================================
    // RECIBIR JAVASCRIPT
    // ==================================================

    private void RecibirDesdeJavaScript(
        string mensaje
    )
    {
        if (
            string.IsNullOrEmpty(
                mensaje
            )
        )
        {
            return;
        }


        Debug.Log(
            "📩 Botrix recibido: " +
            mensaje
        );


        // ==================================================
        // UNITY:
        // ==================================================

        if (
            mensaje.StartsWith(
                "unity:"
            )
        )
        {
            mensaje =
                mensaje.Substring(
                    "unity:".Length
                );
        }


        // ==================================================
        // BOTRIX:
        // ==================================================

        if (
            !mensaje.StartsWith(
                "BOTRIX|"
            )
        )
        {
            return;
        }


        // ==================================================
        // JSON
        // ==================================================

        string json =
            mensaje.Substring(
                "BOTRIX|".Length
            );


        try
        {
            json =
                Uri.UnescapeDataString(
                    json
                );


            Debug.Log(
                "📦 Botrix JSON: " +
                json
            );


            BotrixMessage data =
                JsonUtility.FromJson<BotrixMessage>(
                    json
                );


            if (data == null)
            {
                Debug.LogError(
                    "❌ Botrix devolvió datos vacíos"
                );

                return;
            }


            // ==================================================
            // LIMPIEZA FINAL EN UNITY
            // ==================================================

            string nombre =
                data.nombre != null
                    ? data.nombre.Trim()
                    : "";

            string texto =
                data.mensaje != null
                    ? data.mensaje.Trim()
                    : "";

            string plataforma =
                data.plataforma != null
                    ? data.plataforma.Trim()
                    : "Unknown";


            if (
                !string.IsNullOrEmpty(texto) &&
                nombre.EndsWith(texto)
            )
            {
                nombre =
                    nombre.Substring(
                        0,
                        nombre.Length -
                        texto.Length
                    ).Trim();
            }


            if (
                !string.IsNullOrEmpty(texto) &&
                nombre.EndsWith(
                    " " + texto
                )
            )
            {
                nombre =
                    nombre.Substring(
                        0,
                        nombre.Length -
                        texto.Length -
                        1
                    ).Trim();
            }


            if (string.IsNullOrEmpty(nombre))
            {
                nombre = "User";
            }


            if (string.IsNullOrEmpty(texto))
            {
                return;
            }


            Debug.Log(
                "👤 Nombre: " +
                nombre
            );


            Debug.Log(
                "💬 Mensaje: " +
                texto
            );


            Debug.Log(
                "🌐 Plataforma: " +
                plataforma
            );


            // ==================================================
            // EVENTO
            // ==================================================

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


    // ==================================================
    // DATOS
    // ==================================================

    [Serializable]
    private class BotrixMessage
    {
        public string nombre;

        public string mensaje;

        public string plataforma;
    }


    // ==================================================
    // DESTROY
    // ==================================================

    private void OnDestroy()
    {
        if (webView != null)
        {
            Destroy(
                webView.gameObject
            );

            webView = null;
        }


        conectado = false;

        iniciando = false;
    }
}