using System.Collections.Generic;
using UnityEngine;

public class BotrixChat : MonoBehaviour
{
    // ==================================================
    // REFERENCIAS
    // ==================================================

    [Header("REFERENCIAS")]

    [SerializeField]
    private BotrixWebView botrix;

    [SerializeField]
    private UnityTTS tts;


    // ==================================================
    // CONFIGURACIÓN
    // ==================================================

    [Header("CONFIGURACIÓN TTS")]

    [SerializeField]
    private string caracterTTS = "*";


    // ==================================================
    // COLA
    // ==================================================

    private readonly Queue<string> colaTTS =
        new Queue<string>();

    private readonly HashSet<string> mensajesProcesados =
        new HashSet<string>();

    private bool hablando = false;


    // ==================================================
    // START
    // ==================================================

    private void Start()
    {
        BuscarReferencias();
    }


    // ==================================================
    // REFERENCIAS
    // ==================================================

    private void BuscarReferencias()
    {
        if (botrix == null)
        {
            botrix =
                FindFirstObjectByType<BotrixWebView>();
        }

        if (tts == null)
        {
            tts =
                FindFirstObjectByType<UnityTTS>();
        }

        if (botrix == null)
        {
            Debug.LogError(
                "❌ No se encontró BotrixWebView"
            );

            return;
        }

        if (tts == null)
        {
            Debug.LogError(
                "❌ No se encontró UnityTTS"
            );

            return;
        }

        if (string.IsNullOrEmpty(caracterTTS))
        {
            caracterTTS = "*";
        }

        botrix.OnChatMessage -=
            ProcesarMensaje;

        botrix.OnChatMessage +=
            ProcesarMensaje;

        tts.OnFinished -=
            TTSFinalizado;

        tts.OnFinished +=
            TTSFinalizado;

        Debug.Log(
            "✅ BotrixChat conectado"
        );

        Debug.Log(
            "🎤 Carácter TTS: [" +
            caracterTTS +
            "]"
        );
    }


    // ==================================================
    // CONFIGURAR
    // ==================================================

    public void Configurar(
        string nuevoCaracter
    )
    {
        if (
            string.IsNullOrEmpty(
                nuevoCaracter
            )
        )
        {
            nuevoCaracter = "*";
        }

        caracterTTS =
            nuevoCaracter.Trim();

        Debug.Log(
            "🎤 Carácter TTS: [" +
            caracterTTS +
            "]"
        );
    }


    // ==================================================
    // DESTROY
    // ==================================================

    private void OnDestroy()
    {
        if (botrix != null)
        {
            botrix.OnChatMessage -=
                ProcesarMensaje;
        }

        if (tts != null)
        {
            tts.OnFinished -=
                TTSFinalizado;
        }
    }


    // ==================================================
    // PROCESAR MENSAJE
    // ==================================================

    private void ProcesarMensaje(
        string nombre,
        string mensaje,
        string plataforma
    )
    {
        mensaje =
            LimpiarMensaje(
                mensaje
            );

        if (
            string.IsNullOrEmpty(
                mensaje
            )
        )
        {
            return;
        }

        nombre =
            LimpiarNombre(
                nombre,
                mensaje
            );

        if (
            string.IsNullOrEmpty(
                nombre
            )
        )
        {
            nombre = "User";
        }

        Debug.Log(
            FormatoPlataforma(
                plataforma
            ) +
            " " +
            nombre +
            ": " +
            mensaje
        );


        // ==================================================
        // COMPROBAR CARÁCTER
        // ==================================================

        if (
            string.IsNullOrEmpty(
                caracterTTS
            )
        )
        {
            return;
        }

        if (
            !mensaje.StartsWith(
                caracterTTS
            )
        )
        {
            return;
        }


        // ==================================================
        // QUITAR CARÁCTER
        // ==================================================

        string texto =
            mensaje.Substring(
                caracterTTS.Length
            ).Trim();

        if (
            string.IsNullOrEmpty(
                texto
            )
        )
        {
            return;
        }


        // ==================================================
        // ID DEL MENSAJE
        // ==================================================

        string id =
            nombre +
            "|" +
            mensaje +
            "|" +
            plataforma;

        if (
            mensajesProcesados.Contains(
                id
            )
        )
        {
            return;
        }

        mensajesProcesados.Add(
            id
        );

        if (
            mensajesProcesados.Count > 1000
        )
        {
            mensajesProcesados.Clear();

            mensajesProcesados.Add(
                id
            );
        }


        // ==================================================
        // NOMBRE PARA TTS
        // ==================================================

        string nombreTTS =
            PrepararNombreTTS(
                nombre
            );


        // ==================================================
        // PRONUNCIACIÓN
        // ==================================================

        texto =
            CorregirPronunciacion(
                texto
            );


        // ==================================================
        // TEXTO FINAL
        // ==================================================

        string textoTTS =
            nombreTTS +
            " dice: " +
            texto;


        // ==================================================
        // AGREGAR A COLA
        // ==================================================

        colaTTS.Enqueue(
            textoTTS
        );

        Debug.Log(
            "🔊 TTS agregado: " +
            textoTTS
        );

        HablarSiguiente();
    }


    // ==================================================
    // HABLAR SIGUIENTE
    // ==================================================

    private void HablarSiguiente()
    {
        if (hablando)
        {
            return;
        }

        if (
            colaTTS.Count == 0
        )
        {
            return;
        }

        if (tts == null)
        {
            Debug.LogError(
                "❌ UnityTTS no está disponible"
            );

            colaTTS.Clear();

            return;
        }

        string texto =
            colaTTS.Dequeue();

        hablando = true;

        Debug.Log(
            "🗣️ HABLAR: " +
            texto
        );

        tts.Speak(
            texto
        );


        // ==================================================
        // SEGURIDAD
        // ==================================================

        if (!tts.EstaHablando)
        {
            hablando = false;

            HablarSiguiente();
        }
    }


    // ==================================================
    // TTS FINALIZADO
    // ==================================================

    private void TTSFinalizado()
    {
        hablando = false;

        Debug.Log(
            "✅ BotrixChat: TTS terminado"
        );

        HablarSiguiente();
    }


    // ==================================================
    // PREPARAR NOMBRE
    // ==================================================

    private string PrepararNombreTTS(
        string nombre
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                nombre
            )
        )
        {
            return "User";
        }

        string resultado =
            nombre
                .Replace(
                    "_",
                    " "
                )
                .Replace(
                    "@",
                    " arroba "
                );

        while (
            resultado.Contains("  ")
        )
        {
            resultado =
                resultado.Replace(
                    "  ",
                    " "
                );
        }

        return resultado.Trim();
    }


    // ==================================================
    // LIMPIAR MENSAJE
    // ==================================================

    private string LimpiarMensaje(
        string msg
    )
    {
        if (
            string.IsNullOrEmpty(
                msg
            )
        )
        {
            return "";
        }

        string texto =
            msg
                .Replace(
                    "\u200B",
                    ""
                )
                .Replace(
                    "\u200C",
                    ""
                )
                .Replace(
                    "\u200D",
                    ""
                )
                .Replace(
                    "\uFEFF",
                    ""
                )
                .Replace(
                    "\r",
                    " "
                )
                .Replace(
                    "\n",
                    " "
                )
                .Trim();

        while (
            texto.Contains("  ")
        )
        {
            texto =
                texto.Replace(
                    "  ",
                    " "
                );
        }

        return texto.Trim();
    }


    // ==================================================
    // LIMPIAR NOMBRE
    // ==================================================

    private string LimpiarNombre(
        string nombre,
        string mensaje
    )
    {
        if (
            string.IsNullOrEmpty(
                nombre
            )
        )
        {
            return "User";
        }

        string name =
            nombre.Trim();

        string msg =
            mensaje != null
                ? mensaje.Trim()
                : "";

        if (
            !string.IsNullOrEmpty(msg) &&
            name.EndsWith(msg)
        )
        {
            name =
                name.Substring(
                    0,
                    name.Length -
                    msg.Length
                ).Trim();
        }

        if (
            name.StartsWith("@")
        )
        {
            name =
                name.Substring(1);
        }

        while (
            name.Contains("  ")
        )
        {
            name =
                name.Replace(
                    "  ",
                    " "
                );
        }

        return name.Trim();
    }


    // ==================================================
    // PRONUNCIACIÓN
    // ==================================================

    private string CorregirPronunciacion(
        string texto
    )
    {
        if (
            string.IsNullOrEmpty(
                texto
            )
        )
        {
            return "";
        }

        texto =
            texto.Replace(
                "@",
                " arroba "
            );

        texto =
            texto.Replace(
                "#",
                " almohadilla "
            );

        texto =
            texto.Replace(
                "$",
                " dólar "
            );

        texto =
            texto.Replace(
                "%",
                " por ciento "
            );

        texto =
            texto.Replace(
                "Roblox",
                "Ró-bloks"
            );

        texto =
            texto.Replace(
                "roblox",
                "Ró-bloks"
            );

        texto =
            texto.Replace(
                "Minecraft",
                "Máin-cráft"
            );

        texto =
            texto.Replace(
                "minecraft",
                "Máin-cráft"
            );

        texto =
            texto.Replace(
                "Discord",
                "Dis-córd"
            );

        texto =
            texto.Replace(
                "discord",
                "Dis-córd"
            );

        texto =
            texto.Replace(
                "YouTube",
                "Yutub"
            );

        texto =
            texto.Replace(
                "youtube",
                "Yutub"
            );

        while (
            texto.Contains("  ")
        )
        {
            texto =
                texto.Replace(
                    "  ",
                    " "
                );
        }

        return texto.Trim();
    }


    // ==================================================
    // PLATAFORMA
    // ==================================================

    private string FormatoPlataforma(
        string plataforma
    )
    {
        switch (plataforma)
        {
            case "Twitch":
                return "🟣 Twitch ●";

            case "YouTube":
                return "🔴 YouTube ●";

            case "Kick":
                return "🟢 Kick ●";

            case "Discord":
                return "🔵 Discord ●";

            case "Minecraft":
                return "🟫 Minecraft ●";

            default:
                return "⚪ Chat ●";
        }
    }


    // ==================================================
    // CAMBIAR CARÁCTER
    // ==================================================

    public void CambiarCaracterTTS(
        string nuevoCaracter
    )
    {
        if (
            string.IsNullOrEmpty(
                nuevoCaracter
            )
        )
        {
            return;
        }

        caracterTTS =
            nuevoCaracter.Trim();

        Debug.Log(
            "🎤 Carácter TTS cambiado a: [" +
            caracterTTS +
            "]"
        );
    }


    // ==================================================
    // LIMPIAR COLA
    // ==================================================

    public void LimpiarCola()
    {
        colaTTS.Clear();

        Debug.Log(
            "🧹 Cola TTS limpiada"
        );
    }


    // ==================================================
    // DETENER TTS
    // ==================================================

    public void StopTTS()
    {
        colaTTS.Clear();

        hablando = false;

        if (tts != null)
        {
            tts.Stop();
        }

        Debug.Log(
            "⏹️ BotrixChat TTS detenido"
        );
    }
}