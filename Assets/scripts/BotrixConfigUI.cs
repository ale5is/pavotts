using System;
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

    [Header("ESTADO")]
    [SerializeField] private TMP_Text textoEstado;

    private const string URL_DEFAULT = "";
    private const string VOZ_DEFAULT = "es_002";
    private const string CARACTER_DEFAULT = "*";

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
    }

    private void Start()
    {
        BuscarReferencias();

        CargarDatos();

        ConfigurarSliderVolumen();

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
                string contenido =
                    File.ReadAllText(
                        RutaConfiguracion
                    );

                datos =
                    JsonUtility.FromJson<DatosConfiguracion>(
                        contenido
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
            datos = new DatosConfiguracion
            {
                botrixUrl = URL_DEFAULT,
                sessionId = "",
                voz = VOZ_DEFAULT,
                caracter = CARACTER_DEFAULT,
                volumen = 1f
            };
        }

        if (string.IsNullOrWhiteSpace(datos.botrixUrl))
            datos.botrixUrl = URL_DEFAULT;

        if (string.IsNullOrWhiteSpace(datos.voz))
            datos.voz = VOZ_DEFAULT;

        if (string.IsNullOrEmpty(datos.caracter))
            datos.caracter = CARACTER_DEFAULT;

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
            caracterInput.text =
                datos.caracter;

        if (volumenSlider != null)
            volumenSlider.value =
                datos.volumen;

        ActualizarTextoVolumen(
            datos.volumen
        );
    }

    public void Guardar()
    {
        string url =
            botrixUrlInput != null
                ? botrixUrlInput.text.Trim()
                : "";

        string session =
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
                : CARACTER_DEFAULT;

        float volumen =
            volumenSlider != null
                ? volumenSlider.value
                : 1f;

        if (string.IsNullOrWhiteSpace(voz))
            voz = VOZ_DEFAULT;

        if (string.IsNullOrEmpty(caracter))
            caracter = CARACTER_DEFAULT;

        volumen =
            Mathf.Clamp01(volumen);

        DatosConfiguracion datos =
            new DatosConfiguracion
            {
                botrixUrl = url,
                sessionId = session,
                voz = voz,
                caracter = caracter,
                volumen = volumen
            };

        try
        {
            string json =
                JsonUtility.ToJson(
                    datos,
                    true
                );

            File.WriteAllText(
                RutaConfiguracion,
                json
            );

            CambiarEstado(
                "Configuración guardada."
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
                : CARACTER_DEFAULT;

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

        if (string.IsNullOrEmpty(caracter))
        {
            CambiarEstado(
                "Falta el carácter del TTS."
            );

            return;
        }

        // Guardar configuración
        Guardar();

        // Buscar referencias nuevamente
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

        // Limpiar chat anterior
        if (botrixChatUI != null)
            botrixChatUI.LimpiarChat();

        // Limpiar cola anterior
        botrixChat.LimpiarCola();

        // Configurar TTS
        tts.Configurar(
            sessionId,
            voz,
            volumen
        );

        // Configurar carácter del TTS
        botrixChat.Configurar(
            caracter
        );

        CambiarEstado(
            "Conectando con Botrix..."
        );

        // Conectar WebView
        botrix.Conectar(
            url
        );

        // Mostrar chat
        MostrarChat();

        Debug.Log(
            "✅ Botrix conectado correctamente."
        );
    }

    public void Volver()
    {
        Debug.Log(
            "🔴 Volviendo a configuración..."
        );

        // ==========================================
        // DETENER TTS
        // ==========================================

        if (tts != null)
        {
            tts.Stop();
        }

        // ==========================================
        // LIMPIAR COLA TTS
        // ==========================================

        if (botrixChat != null)
        {
            botrixChat.LimpiarCola();
        }

        // ==========================================
        // LIMPIAR CHAT VISUAL
        // ==========================================

        if (botrixChatUI != null)
        {
            botrixChatUI.LimpiarChat();
        }

        // ==========================================
        // DESCONECTAR BOTRIX
        // ==========================================

        if (botrix != null)
        {
            botrix.Desconectar();
        }

        // ==========================================
        // IMPORTANTE:
        // NO BORRAR datos.config
        // ==========================================

        // La configuración queda guardada.

        // ==========================================
        // MOSTRAR CONFIGURACIÓN
        // ==========================================

        MostrarConfiguracion();

        CambiarEstado(
            "Desconectado. Chat limpiado."
        );

        Debug.Log(
            "✅ TTS detenido."
        );

        Debug.Log(
            "✅ Cola TTS limpiada."
        );

        Debug.Log(
            "✅ Chat visual limpiado."
        );

        Debug.Log(
            "✅ Botrix desconectado."
        );

        Debug.Log(
            "💾 datos.config conservado."
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
            tts =
                FindFirstObjectByType<UnityTTS>();

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

        tts.Configurar(
            sessionId,
            voz,
            volumen
        );

        CambiarEstado(
            "Probando TTS..."
        );

        tts.Probar();
    }

    private void CambiarEstado(string mensaje)
    {
        if (textoEstado != null)
            textoEstado.text = mensaje;
    }
}