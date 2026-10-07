using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BotrixConfigUI : MonoBehaviour
{
    [Header("REFERENCIAS")]
    [SerializeField] private BotrixWebView botrix;
    [SerializeField] private BotrixChat botrixChat;
    [SerializeField] private BotrixChatUI botrixChatUI;
    [SerializeField] private UnityTTS tts;

    [Header("PANELES PRINCIPALES")]
    [SerializeField] private GameObject objetoConfiguracion;
    [SerializeField] private GameObject objetoChat;

    [Header("CAMPOS")]
    [SerializeField] private TMP_InputField botrixUrlInput;
    [SerializeField] private TMP_InputField sessionIdInput;
    [SerializeField] private TMP_InputField vozInput;
    [SerializeField] private TMP_InputField caracterInput;
    [SerializeField] private Slider volumenSlider;
    [SerializeField] private TMP_Text textoVolumen;

    [Header("BLACKLIST")]
    [SerializeField] private TMP_InputField blacklistInput;
    [SerializeField] private TMP_Text blacklistTexto;

    [Header("ESTADO")]
    [SerializeField] private TMP_Text textoEstado;

    private const string URL_DEFAULT = "";
    private const string VOZ_DEFAULT = "es_002";
    private const string CARACTER_DEFAULT = "";

    private List<string> blacklist = new List<string>();

    private string RutaConfiguracion
    {
        get
        {
            DirectoryInfo directorio =
                Directory.GetParent(Application.dataPath);

            return Path.Combine(
                directorio.FullName,
                "datos.config"
            );
        }
    }

    [Serializable]
    private class DatosConfiguracion
    {
        public string botrixUrl;
        public string sessionId;
        public string voz;
        public string caracter;
        public float volumen;
        public string blacklist;
    }

    private void Start()
    {
        BuscarReferencias();
        CargarDatos();
        ConfigurarSliderVolumen();
        ActualizarTextoBlacklist();
        MostrarConfiguracion();

        CambiarEstado(
            "Ingresá los datos y presioná CONECTAR."
        );
    }

    private void OnDestroy()
    {
        if (volumenSlider != null)
        {
            volumenSlider.onValueChanged.RemoveListener(
                ActualizarTextoVolumen
            );
        }
    }

    private void BuscarReferencias()
    {
        if (botrix == null)
            botrix = FindFirstObjectByType<BotrixWebView>();

        if (botrixChat == null)
            botrixChat = FindFirstObjectByType<BotrixChat>();

        if (botrixChatUI == null)
            botrixChatUI = FindFirstObjectByType<BotrixChatUI>();

        if (tts == null)
            tts = FindFirstObjectByType<UnityTTS>();
    }

    private void ConfigurarSliderVolumen()
    {
        if (volumenSlider == null)
            return;

        volumenSlider.onValueChanged.RemoveListener(
            ActualizarTextoVolumen
        );

        volumenSlider.onValueChanged.AddListener(
            ActualizarTextoVolumen
        );

        ActualizarTextoVolumen(
            volumenSlider.value
        );
    }

    private void ActualizarTextoVolumen(float valor)
    {
        if (textoVolumen == null)
            return;

        int porcentaje =
            Mathf.RoundToInt(
                Mathf.Clamp01(valor) * 100f
            );

        textoVolumen.text =
            porcentaje + "%";
    }

    public void CargarDatos()
    {
        DatosConfiguracion datos = null;

        if (File.Exists(RutaConfiguracion))
        {
            try
            {
                datos =
                    JsonUtility.FromJson<DatosConfiguracion>(
                        File.ReadAllText(RutaConfiguracion)
                    );
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "❌ Error leyendo datos.config: " +
                    e.Message
                );
            }
        }

        if (datos == null)
        {
            datos =
                new DatosConfiguracion
                {
                    botrixUrl = URL_DEFAULT,
                    sessionId = "",
                    voz = VOZ_DEFAULT,
                    caracter = CARACTER_DEFAULT,
                    volumen = 1f,
                    blacklist = ""
                };
        }

        if (string.IsNullOrWhiteSpace(datos.botrixUrl))
            datos.botrixUrl = URL_DEFAULT;

        if (string.IsNullOrWhiteSpace(datos.voz))
            datos.voz = VOZ_DEFAULT;

        if (datos.caracter == null)
            datos.caracter = "";

        if (datos.blacklist == null)
            datos.blacklist = "";

        datos.volumen =
            Mathf.Clamp01(datos.volumen);

        if (botrixUrlInput != null)
            botrixUrlInput.text = datos.botrixUrl;

        if (sessionIdInput != null)
            sessionIdInput.text =
                datos.sessionId ?? "";

        if (vozInput != null)
            vozInput.text = datos.voz;

        if (caracterInput != null)
            caracterInput.text = datos.caracter;

        if (volumenSlider != null)
            volumenSlider.value = datos.volumen;

        blacklist =
            ObtenerBlacklistDesdeTexto(
                datos.blacklist
            );

        ActualizarTextoVolumen(
            datos.volumen
        );

        ActualizarTextoBlacklist();

        Debug.Log(
            "🚫 Blacklist cargada: " +
            blacklist.Count +
            " usuario(s)."
        );
    }

    public void Guardar()
    {
        DatosConfiguracion datos =
            ObtenerDatosActuales();

        try
        {
            File.WriteAllText(
                RutaConfiguracion,
                JsonUtility.ToJson(
                    datos,
                    true
                )
            );

            CambiarEstado(
                "Configuración guardada."
            );

            Debug.Log(
                "💾 Configuración guardada correctamente."
            );

            Debug.Log(
                "🚫 Blacklist guardada: " +
                blacklist.Count +
                " usuario(s)."
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "❌ Error guardando datos.config: " +
                e.Message
            );

            CambiarEstado(
                "Error al guardar la configuración."
            );
        }
    }

    private DatosConfiguracion ObtenerDatosActuales()
    {
        string voz =
            vozInput != null
                ? vozInput.text.Trim()
                : VOZ_DEFAULT;

        if (string.IsNullOrWhiteSpace(voz))
            voz = VOZ_DEFAULT;

        return new DatosConfiguracion
        {
            botrixUrl =
                botrixUrlInput != null
                    ? botrixUrlInput.text.Trim()
                    : "",

            sessionId =
                sessionIdInput != null
                    ? sessionIdInput.text.Trim()
                    : "",

            voz = voz,

            caracter =
                caracterInput != null
                    ? caracterInput.text.Trim()
                    : "",

            volumen =
                volumenSlider != null
                    ? Mathf.Clamp01(volumenSlider.value)
                    : 1f,

            blacklist =
                ConvertirBlacklistATexto(
                    blacklist
                )
        };
    }

    public void AgregarBlacklist()
    {
        string nombre =
            ObtenerNombreBlacklistInput();

        if (string.IsNullOrWhiteSpace(nombre))
        {
            CambiarEstado(
                "Escribí un nombre en la blacklist."
            );

            return;
        }

        if (ExisteEnBlacklist(nombre))
        {
            CambiarEstado(
                "Ese usuario ya está en la blacklist."
            );

            return;
        }

        blacklist.Add(nombre);

        ActualizarTextoBlacklist();

        LimpiarBlacklistInput();

        Guardar();

        CambiarEstado(
            "Usuario agregado a la blacklist: " +
            nombre
        );

        Debug.Log(
            "🚫 Usuario agregado a blacklist: " +
            nombre
        );
    }

    public void QuitarBlacklist()
    {
        string nombre =
            ObtenerNombreBlacklistInput();

        if (string.IsNullOrWhiteSpace(nombre))
        {
            CambiarEstado(
                "Escribí un nombre para quitarlo."
            );

            return;
        }

        int cantidadAntes =
            blacklist.Count;

        blacklist.RemoveAll(
            x => string.Equals(
                x,
                nombre,
                StringComparison.OrdinalIgnoreCase
            )
        );

        if (blacklist.Count == cantidadAntes)
        {
            CambiarEstado(
                "Ese usuario no está en la blacklist."
            );

            return;
        }

        ActualizarTextoBlacklist();

        LimpiarBlacklistInput();

        Guardar();

        CambiarEstado(
            "Usuario eliminado de la blacklist: " +
            nombre
        );

        Debug.Log(
            "✅ Usuario eliminado de blacklist: " +
            nombre
        );
    }

    public void LimpiarBlacklist()
    {
        blacklist.Clear();

        ActualizarTextoBlacklist();

        LimpiarBlacklistInput();

        Guardar();

        CambiarEstado(
            "Blacklist limpiada."
        );

        Debug.Log(
            "🧹 Blacklist limpiada."
        );
    }

    private string ObtenerNombreBlacklistInput()
    {
        if (blacklistInput == null)
            return "";

        return blacklistInput.text.Trim();
    }

    private void LimpiarBlacklistInput()
    {
        if (blacklistInput != null)
            blacklistInput.text = "";
    }

    private bool ExisteEnBlacklist(string nombre)
    {
        return blacklist.Exists(
            x => string.Equals(
                x,
                nombre,
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    private void ActualizarTextoBlacklist()
    {
        if (blacklistTexto == null)
            return;

        blacklistTexto.text =
            blacklist.Count == 0
                ? "Blacklist vacía."
                : string.Join(
                    "\n",
                    blacklist
                );
    }

    private List<string> ObtenerBlacklistDesdeTexto(
        string texto)
    {
        List<string> lista =
            new List<string>();

        if (string.IsNullOrWhiteSpace(texto))
            return lista;

        string[] nombres =
            texto.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            );

        foreach (string nombre in nombres)
        {
            string limpio =
                nombre.Trim();

            if (string.IsNullOrWhiteSpace(limpio))
                continue;

            if (!lista.Exists(
                    x => string.Equals(
                        x,
                        limpio,
                        StringComparison.OrdinalIgnoreCase
                    )))
            {
                lista.Add(limpio);
            }
        }

        return lista;
    }

    private string ConvertirBlacklistATexto(
        List<string> lista)
    {
        if (lista == null ||
            lista.Count == 0)
        {
            return "";
        }

        return string.Join(
            "\n",
            lista
        );
    }

    public void Conectar()
    {
        string url =
            botrixUrlInput != null
                ? botrixUrlInput.text.Trim()
                : "";

        string sessionId =
            sessionIdInput != null
                ? sessionIdInput.text.Trim()
                : "";

        string voz =
            vozInput != null
                ? vozInput.text.Trim()
                : VOZ_DEFAULT;

        string caracter =
            caracterInput != null
                ? caracterInput.text.Trim()
                : "";

        float volumen =
            volumenSlider != null
                ? volumenSlider.value
                : 1f;

        if (string.IsNullOrWhiteSpace(url))
        {
            CambiarEstado(
                "Falta la URL de Botrix."
            );

            return;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            CambiarEstado(
                "Falta el Session ID de TikTok."
            );

            return;
        }

        if (string.IsNullOrWhiteSpace(voz))
        {
            CambiarEstado(
                "Falta la voz de TikTok."
            );

            return;
        }

        Guardar();

        BuscarReferencias();

        if (tts == null)
        {
            CambiarEstado(
                "No se encontró UnityTTS."
            );

            return;
        }

        if (botrixChat == null)
        {
            CambiarEstado(
                "No se encontró BotrixChat."
            );

            return;
        }

        if (botrix == null)
        {
            CambiarEstado(
                "No se encontró BotrixWebView."
            );

            return;
        }

        if (botrixChatUI != null)
            botrixChatUI.LimpiarChat();

        botrixChat.LimpiarCola();

        tts.Configurar(
            sessionId,
            voz,
            Mathf.Clamp01(volumen)
        );

        botrixChat.Configurar(
            caracter
        );

        botrixChat.ConfigurarBlacklist(
            blacklist
        );

        if (string.IsNullOrEmpty(caracter))
        {
            CambiarEstado(
                "Conectando... TTS leerá TODOS los mensajes."
            );
        }
        else
        {
            CambiarEstado(
                "Conectando con Botrix..."
            );
        }

        botrix.Conectar(url);

        MostrarChat();

        Debug.Log(
            "🚫 Blacklist activa: " +
            blacklist.Count +
            " usuario(s)."
        );

        Debug.Log(
            "✅ Botrix conectado correctamente."
        );
    }

    public void Volver()
    {
        if (tts != null)
            tts.Stop();

        if (botrixChat != null)
            botrixChat.LimpiarCola();

        if (botrixChatUI != null)
            botrixChatUI.LimpiarChat();

        if (botrix != null)
            botrix.Desconectar();

        MostrarConfiguracion();

        CambiarEstado(
            "Desconectado. Chat limpiado."
        );
    }

    public void MostrarConfiguracion()
    {
        if (objetoConfiguracion != null)
            objetoConfiguracion.SetActive(true);

        if (objetoChat != null)
            objetoChat.SetActive(false);

        CambiarEstado(
            "Ingresá los datos y presioná CONECTAR."
        );
    }

    public void MostrarChat()
    {
        if (objetoConfiguracion != null)
            objetoConfiguracion.SetActive(false);

        if (objetoChat != null)
            objetoChat.SetActive(true);
    }

    public void Probar()
    {
        if (tts == null)
            tts = FindFirstObjectByType<UnityTTS>();

        if (tts == null)
        {
            CambiarEstado(
                "No se encontró UnityTTS."
            );

            return;
        }

        string sessionId =
            sessionIdInput != null
                ? sessionIdInput.text.Trim()
                : "";

        string voz =
            vozInput != null
                ? vozInput.text.Trim()
                : VOZ_DEFAULT;

        float volumen =
            volumenSlider != null
                ? volumenSlider.value
                : 1f;

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            CambiarEstado(
                "Colocá el Session ID antes de probar."
            );

            return;
        }

        if (string.IsNullOrWhiteSpace(voz))
            voz = VOZ_DEFAULT;

        tts.Configurar(
            sessionId,
            voz,
            Mathf.Clamp01(volumen)
        );

        CambiarEstado(
            "Probando TTS..."
        );

        tts.Probar();
    }

    private void CambiarEstado(
        string mensaje)
    {
        if (textoEstado != null)
            textoEstado.text = mensaje;
    }
}