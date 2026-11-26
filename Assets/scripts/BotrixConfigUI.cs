using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BotrixConfigUI : MonoBehaviour
{
    [Header("REFERENCIAS")]
    [SerializeField] private NativeChatManager nativeChat;
    [SerializeField] private BotrixChat botrixChat;
    [SerializeField] private BotrixChatUI botrixChatUI;
    [SerializeField] private UnityTTS tts;

    [Header("PANELES PRINCIPALES")]
    [SerializeField] private GameObject objetoConfiguracion;
    [SerializeField] private GameObject objetoChat;

    [Header("CAMPOS")]
    private TMP_InputField twitchChannelInput;
    private TMP_InputField twitchUsernameInput;
    private TMP_InputField twitchTokenInput;
    private TMP_InputField youtubeChannelInput;
    private TMP_InputField kickChannelInput;
    private GameObject nativeSettingsPanel;
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
        public string twitchChannel;
        public string twitchUsername;
        public string twitchAccessToken;
        public string youtubeChannel;
        public string kickChannel;
        public string sessionId;
        public string voz;
        public string caracter;
        public float volumen;
        public string blacklist;
    }

    private void Start()
    {
        BuscarReferencias();
        CrearCamposNativos();
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

        if (nativeChat != null)
            nativeChat.OnPlatformStatus -= OnNativePlatformStatus;
    }

    private void OnNativePlatformStatus(string platform, string status)
    {
        CambiarEstado(platform + ": " + status);
    }

    private void BuscarReferencias()
    {
        if (nativeChat == null)
            nativeChat = FindFirstObjectByType<NativeChatManager>();

        if (nativeChat == null)
            nativeChat = gameObject.AddComponent<NativeChatManager>();

        nativeChat.OnPlatformStatus -= OnNativePlatformStatus;
        nativeChat.OnPlatformStatus += OnNativePlatformStatus;

        if (botrixChat == null)
            botrixChat = FindFirstObjectByType<BotrixChat>();

        if (botrixChatUI == null)
            botrixChatUI = FindFirstObjectByType<BotrixChatUI>();

        if (tts == null)
            tts = FindFirstObjectByType<UnityTTS>();
    }

    private void CrearCamposNativos()
    {
        if (nativeSettingsPanel != null || objetoConfiguracion == null)
            return;

        Transform oldUrlField = objetoConfiguracion.transform.Find("InputBotrixURL");
        if (oldUrlField != null)
            oldUrlField.gameObject.SetActive(false);

        Transform oldUrlLabel = objetoConfiguracion.transform.Find("Url");
        if (oldUrlLabel != null)
            oldUrlLabel.gameObject.SetActive(false);

        nativeSettingsPanel = new GameObject(
            "NativeChatSettings",
            typeof(RectTransform),
            typeof(Image)
        );

        nativeSettingsPanel.transform.SetParent(objetoConfiguracion.transform, false);

        RectTransform panelRect = nativeSettingsPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = new Vector2(620f, 0f);
        panelRect.sizeDelta = new Vector2(520f, 680f);

        nativeSettingsPanel.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.065f, 0.94f);

        CrearEtiqueta("CHAT NATIVO", nativeSettingsPanel.transform, new Vector2(0f, 300f), 32f);
        CrearEtiqueta("CANAL DE TWITCH", nativeSettingsPanel.transform, new Vector2(0f, 242f), 18f);
        CrearEtiqueta("USUARIO DE LA CUENTA TWITCH", nativeSettingsPanel.transform, new Vector2(0f, 132f), 18f);
        CrearEtiqueta("TOKEN OAUTH TWITCH (chat:read)", nativeSettingsPanel.transform, new Vector2(0f, 22f), 18f);
        CrearEtiqueta("CANAL DE YOUTUBE (@handle)", nativeSettingsPanel.transform, new Vector2(0f, -88f), 18f);
        CrearEtiqueta("CANAL DE KICK", nativeSettingsPanel.transform, new Vector2(0f, -198f), 18f);

        twitchChannelInput = CrearCampoNativo(
            "TwitchChannel", "Ej.: ale5is14", nativeSettingsPanel.transform, 205f);
        twitchUsernameInput = CrearCampoNativo(
            "TwitchUsername", "Usuario asociado al token", nativeSettingsPanel.transform, 95f);
        twitchTokenInput = CrearCampoNativo(
            "TwitchToken", "Token OAuth sin el prefijo oauth:", nativeSettingsPanel.transform, -15f);
        youtubeChannelInput = CrearCampoNativo(
            "YouTubeChannel", "Ej.: @ale5is", nativeSettingsPanel.transform, -125f);
        kickChannelInput = CrearCampoNativo(
            "KickChannel", "Ej.: ale5is", nativeSettingsPanel.transform, -235f);

        twitchTokenInput.contentType = TMP_InputField.ContentType.Password;
    }

    private void CrearEtiqueta(string texto, Transform parent, Vector2 posicion, float tamano)
    {
        GameObject labelObject = new GameObject(texto, typeof(RectTransform));
        labelObject.SetActive(false);
        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        labelObject.transform.SetParent(parent, false);
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(470f, 42f);
        rect.anchoredPosition = posicion;
        label.font = TMP_Settings.defaultFontAsset;
        label.text = texto;
        label.fontSize = tamano;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        labelObject.SetActive(true);
    }

    private TMP_InputField CrearCampoNativo(
        string nombre,
        string placeholder,
        Transform parent,
        float y)
    {
        GameObject fieldObject = new GameObject(
            nombre,
            typeof(RectTransform)
        );

        fieldObject.SetActive(false);
        fieldObject.transform.SetParent(parent, false);
        fieldObject.AddComponent<CanvasRenderer>();

        RectTransform fieldRect = fieldObject.GetComponent<RectTransform>();
        fieldRect.sizeDelta = new Vector2(460f, 48f);
        fieldRect.anchoredPosition = new Vector2(0f, y);

        Image background = fieldObject.AddComponent<Image>();
        fieldObject.AddComponent<RectMask2D>();
        background.color = new Color(0.12f, 0.14f, 0.18f, 1f);

        GameObject textObject = new GameObject(
            nombre + "Text",
            typeof(RectTransform)
        );
        textObject.SetActive(false);
        textObject.AddComponent<CanvasRenderer>();
        TextMeshProUGUI textComponent = textObject.AddComponent<TextMeshProUGUI>();
        textObject.transform.SetParent(fieldObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 4f);
        textRect.offsetMax = new Vector2(-12f, -4f);

        textComponent.font = TMP_Settings.defaultFontAsset;
        textComponent.fontSize = 22f;
        textComponent.color = Color.white;
        textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        textComponent.raycastTarget = false;
        textObject.SetActive(true);

        GameObject placeholderObject = new GameObject(
            nombre + "Placeholder",
            typeof(RectTransform)
        );
        placeholderObject.SetActive(false);
        placeholderObject.AddComponent<CanvasRenderer>();
        TextMeshProUGUI placeholderText = placeholderObject.AddComponent<TextMeshProUGUI>();
        placeholderObject.transform.SetParent(fieldObject.transform, false);

        RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(12f, 4f);
        placeholderRect.offsetMax = new Vector2(-12f, -4f);

        placeholderText.font = TMP_Settings.defaultFontAsset;
        placeholderText.fontSize = 20f;
        placeholderText.color = new Color(0.68f, 0.7f, 0.74f, 1f);
        placeholderText.text = placeholder;
        placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
        placeholderText.raycastTarget = false;
        placeholderObject.SetActive(true);

        TMP_InputField input = fieldObject.AddComponent<TMP_InputField>();
        input.textComponent = textComponent;
        input.placeholder = placeholderText;
        input.textViewport = fieldRect;
        input.targetGraphic = background;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.transition = Selectable.Transition.ColorTint;
        fieldObject.SetActive(true);

        return input;
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
                    twitchChannel = "",
                    twitchUsername = "",
                    twitchAccessToken = "",
                    youtubeChannel = "",
                    kickChannel = "",
                    sessionId = "",
                    voz = VOZ_DEFAULT,
                    caracter = CARACTER_DEFAULT,
                    volumen = 1f,
                    blacklist = ""
                };
        }

        if (datos.twitchChannel == null) datos.twitchChannel = "";
        if (datos.twitchUsername == null) datos.twitchUsername = "";
        if (datos.twitchAccessToken == null) datos.twitchAccessToken = "";
        if (datos.youtubeChannel == null) datos.youtubeChannel = "";
        if (datos.kickChannel == null) datos.kickChannel = "";

        if (string.IsNullOrWhiteSpace(datos.voz))
            datos.voz = VOZ_DEFAULT;

        if (datos.caracter == null)
            datos.caracter = "";

        if (datos.blacklist == null)
            datos.blacklist = "";

        datos.volumen =
            Mathf.Clamp01(datos.volumen);

        twitchChannelInput.text = datos.twitchChannel;
        twitchUsernameInput.text = datos.twitchUsername;
        twitchTokenInput.text = datos.twitchAccessToken;
        youtubeChannelInput.text = datos.youtubeChannel;
        kickChannelInput.text = datos.kickChannel;

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
            twitchChannel = twitchChannelInput != null ? twitchChannelInput.text.Trim() : "",
            twitchUsername = twitchUsernameInput != null ? twitchUsernameInput.text.Trim() : "",
            twitchAccessToken = twitchTokenInput != null ? twitchTokenInput.text.Trim() : "",
            youtubeChannel = youtubeChannelInput != null ? youtubeChannelInput.text.Trim() : "",
            kickChannel = kickChannelInput != null ? kickChannelInput.text.Trim() : "",

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
        string twitchChannel = twitchChannelInput != null ? twitchChannelInput.text.Trim() : "";
        string twitchUsername = twitchUsernameInput != null ? twitchUsernameInput.text.Trim() : "";
        string twitchToken = twitchTokenInput != null ? twitchTokenInput.text.Trim() : "";
        string youtubeChannel = youtubeChannelInput != null ? youtubeChannelInput.text.Trim() : "";
        string kickChannel = kickChannelInput != null ? kickChannelInput.text.Trim() : "";

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

        if (string.IsNullOrWhiteSpace(twitchChannel) &&
            string.IsNullOrWhiteSpace(youtubeChannel) &&
            string.IsNullOrWhiteSpace(kickChannel))
        {
            CambiarEstado("Ingresá al menos un canal: Twitch, YouTube o Kick.");
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

        nativeChat.DisconnectAll();

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
            CambiarEstado("Conectando chats nativos... TTS leerá todos los mensajes.");
        else
            CambiarEstado("Conectando chats nativos con filtro de activación.");

        nativeChat.ConnectAll(
            twitchChannel,
            twitchUsername,
            twitchToken,
            youtubeChannel,
            kickChannel
        );

        MostrarChat();

        Debug.Log(
            "🚫 Blacklist activa: " +
            blacklist.Count +
            " usuario(s)."
        );

        Debug.Log(
            "✅ Conectores nativos iniciados."
        );
    }

    public void Volver()
    {
        if (tts != null)
            tts.Stop();

        if (nativeChat != null)
            nativeChat.DisconnectAll();

        if (botrixChat != null)
            botrixChat.LimpiarCola();

        if (botrixChatUI != null)
            botrixChatUI.LimpiarChat();

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