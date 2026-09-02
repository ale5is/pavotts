using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BotrixConfigUI : MonoBehaviour
{
    // ==================================================
    // REFERENCIAS
    // ==================================================

    [Header("REFERENCIAS")]

    [SerializeField]
    private BotrixWebView botrix;

    [SerializeField]
    private BotrixChat botrixChat;

    [SerializeField]
    private UnityTTS tts;


    // ==================================================
    // PANELES PRINCIPALES
    // ==================================================

    [Header("PANELES PRINCIPALES")]

    [SerializeField]
    private GameObject objetoConfiguracion;

    [SerializeField]
    private GameObject objetoChat;


    // ==================================================
    // CAMPOS
    // ==================================================

    [Header("CAMPOS")]

    [SerializeField]
    private TMP_InputField botrixUrlInput;

    [SerializeField]
    private TMP_InputField sessionIdInput;

    [SerializeField]
    private TMP_InputField vozInput;

    [SerializeField]
    private TMP_InputField caracterInput;

    [SerializeField]
    private Slider volumenSlider;

    [SerializeField]
    private TMP_Text textoVolumen;


    // ==================================================
    // ESTADO
    // ==================================================

    [Header("ESTADO")]

    [SerializeField]
    private TMP_Text textoEstado;


    // ==================================================
    // DEFAULTS
    // ==================================================

    private const string URL_DEFAULT = "";

    private const string VOZ_DEFAULT =
        "es_002";

    private const string CARACTER_DEFAULT =
        "*";


    // ==================================================
    // ARCHIVO DE CONFIGURACIÓN
    // ==================================================

    private string RutaConfiguracion
    {
        get
        {
            DirectoryInfo directorioJuego =
                Directory.GetParent(
                    Application.dataPath
                );

            return Path.Combine(
                directorioJuego.FullName,
                "datos.config"
            );
        }
    }


    // ==================================================
    // DATOS DE CONFIGURACIÓN
    // ==================================================

    [Serializable]
    private class DatosConfiguracion
    {
        public string botrixUrl;
        public string sessionId;
        public string voz;
        public string caracter;
        public float volumen;
    }


    // ==================================================
    // START
    // ==================================================

    private void Start()
    {
        BuscarReferencias();

        CargarDatos();

        ConfigurarSliderVolumen();

        MostrarConfiguracion();

        CambiarEstado(
            "Ingresá los datos y presioná CONECTAR."
        );

        Debug.Log(
            "✅ Configuración lista"
        );

        Debug.Log(
            "📂 Archivo: " +
            RutaConfiguracion
        );
    }


    // ==================================================
    // BUSCAR REFERENCIAS
    // ==================================================

    private void BuscarReferencias()
    {
        if (botrix == null)
        {
            botrix =
                FindFirstObjectByType<BotrixWebView>();
        }

        if (botrixChat == null)
        {
            botrixChat =
                FindFirstObjectByType<BotrixChat>();
        }

        if (tts == null)
        {
            tts =
                FindFirstObjectByType<UnityTTS>();
        }
    }


    // ==================================================
    // SLIDER DE VOLUMEN
    // ==================================================

    private void ConfigurarSliderVolumen()
    {
        if (volumenSlider == null)
        {
            return;
        }

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


    // ==================================================
    // TEXTO DE VOLUMEN
    // ==================================================

    private void ActualizarTextoVolumen(
        float valor
    )
    {
        if (textoVolumen == null)
        {
            return;
        }

        int porcentaje =
            Mathf.RoundToInt(
                Mathf.Clamp01(valor) * 100f
            );

        textoVolumen.text =
            porcentaje + "%";
    }


    // ==================================================
    // CARGAR DATOS
    // ==================================================

    public void CargarDatos()
    {
        DatosConfiguracion datos = null;


        // ==================================================
        // COMPROBAR ARCHIVO
        // ==================================================

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

                Debug.Log(
                    "📂 datos.config cargado"
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


        // ==================================================
        // SI NO EXISTE
        // ==================================================

        if (datos == null)
        {
            datos =
                new DatosConfiguracion();

            datos.botrixUrl =
                URL_DEFAULT;

            datos.sessionId =
                "";

            datos.voz =
                VOZ_DEFAULT;

            datos.caracter =
                CARACTER_DEFAULT;

            datos.volumen =
                1f;

            Debug.Log(
                "📄 No existe datos.config. Usando valores por defecto."
            );
        }


        // ==================================================
        // VALIDAR DATOS
        // ==================================================

        if (string.IsNullOrWhiteSpace(datos.botrixUrl))
        {
            datos.botrixUrl =
                URL_DEFAULT;
        }

        if (string.IsNullOrWhiteSpace(datos.voz))
        {
            datos.voz =
                VOZ_DEFAULT;
        }

        if (string.IsNullOrEmpty(datos.caracter))
        {
            datos.caracter =
                CARACTER_DEFAULT;
        }

        datos.volumen =
            Mathf.Clamp01(
                datos.volumen
            );


        // ==================================================
        // MOSTRAR EN UI
        // ==================================================

        if (botrixUrlInput != null)
        {
            botrixUrlInput.text =
                datos.botrixUrl;
        }

        if (sessionIdInput != null)
        {
            sessionIdInput.text =
                datos.sessionId;
        }

        if (vozInput != null)
        {
            vozInput.text =
                datos.voz;
        }

        if (caracterInput != null)
        {
            caracterInput.text =
                datos.caracter;
        }

        if (volumenSlider != null)
        {
            volumenSlider.value =
                datos.volumen;
        }

        ActualizarTextoVolumen(
            datos.volumen
        );
    }


    // ==================================================
    // GUARDAR
    // ==================================================

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
                ? caracterInput.text
                : CARACTER_DEFAULT;

        float volumen =
            volumenSlider != null
                ? volumenSlider.value
                : 1f;


        // ==================================================
        // VALORES POR DEFECTO
        // ==================================================

        if (string.IsNullOrWhiteSpace(url))
        {
            url =
                URL_DEFAULT;
        }

        if (string.IsNullOrWhiteSpace(voz))
        {
            voz =
                VOZ_DEFAULT;
        }

        if (string.IsNullOrEmpty(caracter))
        {
            caracter =
                CARACTER_DEFAULT;
        }

        volumen =
            Mathf.Clamp01(
                volumen
            );


        // ==================================================
        // CREAR DATOS
        // ==================================================

        DatosConfiguracion datos =
            new DatosConfiguracion();

        datos.botrixUrl =
            url;

        datos.sessionId =
            session;

        datos.voz =
            voz;

        datos.caracter =
            caracter;

        datos.volumen =
            volumen;


        // ==================================================
        // CONVERTIR A JSON
        // ==================================================

        string json =
            JsonUtility.ToJson(
                datos,
                true
            );


        // ==================================================
        // GUARDAR DATOS.CONFIG
        // ==================================================

        try
        {
            File.WriteAllText(
                RutaConfiguracion,
                json
            );

            Debug.Log(
                "💾 Configuración guardada correctamente"
            );

            Debug.Log(
                "📂 Ubicación:"
            );

            Debug.Log(
                RutaConfiguracion
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

            return;
        }


        ActualizarTextoVolumen(
            volumen
        );
    }


    // ==================================================
    // CONECTAR
    // ==================================================

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
                ? caracterInput.text
                : CARACTER_DEFAULT;

        float volumen =
            volumenSlider != null
                ? volumenSlider.value
                : 1f;


        // ==================================================
        // VALIDACIONES
        // ==================================================

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


        // ==================================================
        // GUARDAR
        // ==================================================

        Guardar();


        // ==================================================
        // CONFIGURAR TTS
        // ==================================================

        if (tts == null)
        {
            CambiarEstado(
                "No se encontró UnityTTS."
            );

            return;
        }

        tts.Configurar(
            sessionId,
            voz,
            volumen
        );


        // ==================================================
        // CONFIGURAR CHAT
        // ==================================================

        if (botrixChat == null)
        {
            CambiarEstado(
                "No se encontró BotrixChat."
            );

            return;
        }

        botrixChat.Configurar(
            caracter
        );


        // ==================================================
        // BOTRIX
        // ==================================================

        if (botrix == null)
        {
            CambiarEstado(
                "No se encontró BotrixWebView."
            );

            return;
        }


        CambiarEstado(
            "Conectando con Botrix..."
        );


        // ==================================================
        // CONECTAR
        // ==================================================

        botrix.Conectar(
            url
        );


        // ==================================================
        // MOSTRAR CHAT
        // ==================================================

        MostrarChat();


        Debug.Log(
            "✅ Configuración ocultada"
        );

        Debug.Log(
            "💬 Chat mostrado"
        );
    }


    // ==================================================
    // VOLVER
    // ==================================================

    public void Volver()
    {
        MostrarConfiguracion();

        Debug.Log(
            "⚙️ Volviendo a configuración"
        );
    }


    // ==================================================
    // MOSTRAR CONFIGURACIÓN
    // ==================================================

    public void MostrarConfiguracion()
    {
        if (objetoConfiguracion != null)
        {
            objetoConfiguracion.SetActive(
                true
            );
        }

        if (objetoChat != null)
        {
            objetoChat.SetActive(
                false
            );
        }


        CambiarEstado(
            "Ingresá los datos y presioná CONECTAR."
        );
    }


    // ==================================================
    // MOSTRAR CHAT
    // ==================================================

    public void MostrarChat()
    {
        if (objetoConfiguracion != null)
        {
            objetoConfiguracion.SetActive(
                false
            );
        }

        if (objetoChat != null)
        {
            objetoChat.SetActive(
                true
            );
        }
    }


    // ==================================================
    // PROBAR TTS
    // ==================================================

    public void Probar()
    {
        if (tts == null)
        {
            tts =
                FindFirstObjectByType<UnityTTS>();
        }

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


    // ==================================================
    // ESTADO
    // ==================================================

    private void CambiarEstado(
        string mensaje
    )
    {
        if (textoEstado != null)
        {
            textoEstado.text =
                mensaje;
        }
    }
}