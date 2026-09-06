using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public class BotrixChat : MonoBehaviour
{
    [Header("REFERENCIAS")]
    [SerializeField] private BotrixWebView botrix;
    [SerializeField] private UnityTTS tts;
    [SerializeField] private BotrixChatUI chatUI;

    [Header("CONFIGURACIÓN TTS")]
    [Tooltip("Dejar vacío para leer TODOS los mensajes.")]
    [SerializeField] private string caracterTTS = "*";

    [SerializeField] private int maxMensajesTTS = 24;

    private readonly Queue<string> colaTTS =
        new Queue<string>();

    private readonly HashSet<string> mensajesProcesados =
        new HashSet<string>();

    private bool hablando;

    private static readonly Regex EspaciosRegex =
        new Regex(
            @"\s+",
            RegexOptions.Compiled
        );

    private void Start()
    {
        BuscarReferencias();
    }

    private void OnDestroy()
    {
        if (botrix != null)
            botrix.OnChatMessage -= ProcesarMensaje;

        if (tts != null)
            tts.OnFinished -= TTSFinalizado;
    }

    private void BuscarReferencias()
    {
        if (botrix == null)
            botrix =
                FindFirstObjectByType<BotrixWebView>();

        if (tts == null)
            tts =
                FindFirstObjectByType<UnityTTS>();

        if (chatUI == null)
            chatUI =
                FindFirstObjectByType<BotrixChatUI>();

        if (botrix == null)
        {
            Debug.LogError(
                "❌ No se encontró BotrixWebView."
            );

            return;
        }

        if (tts == null)
        {
            Debug.LogError(
                "❌ No se encontró UnityTTS."
            );

            return;
        }

        if (chatUI == null)
        {
            Debug.LogWarning(
                "⚠️ No se encontró BotrixChatUI."
            );
        }

        /*
         * IMPORTANTE:
         *
         * NO ponemos "*" automáticamente si está vacío.
         *
         * Vacío = leer todos los mensajes.
         */
        if (caracterTTS == null)
            caracterTTS = "";

        caracterTTS =
            caracterTTS.Trim();

        botrix.OnChatMessage -= ProcesarMensaje;
        botrix.OnChatMessage += ProcesarMensaje;

        tts.OnFinished -= TTSFinalizado;
        tts.OnFinished += TTSFinalizado;

        if (string.IsNullOrEmpty(caracterTTS))
        {
            Debug.Log(
                "🎤 TTS conectado: LEERÁ TODOS LOS MENSAJES."
            );
        }
        else
        {
            Debug.Log(
                "🎤 TTS conectado. " +
                "Carácter requerido: [" +
                caracterTTS +
                "]"
            );
        }
    }

    public void Configurar(
        string nuevoCaracter)
    {
        /*
         * Si recibe null o vacío,
         * significa que no hay carácter requerido.
         */
        if (nuevoCaracter == null)
            nuevoCaracter = "";

        caracterTTS =
            nuevoCaracter.Trim();

        if (string.IsNullOrEmpty(caracterTTS))
        {
            Debug.Log(
                "🎤 Carácter TTS vacío: " +
                "ahora se leerán TODOS los mensajes."
            );
        }
        else
        {
            Debug.Log(
                "🎤 Carácter TTS: [" +
                caracterTTS +
                "]"
            );
        }
    }

    private void ProcesarMensaje(
        string nombre,
        string mensaje,
        string plataforma)
    {
        mensaje =
            LimpiarMensaje(mensaje);

        if (string.IsNullOrEmpty(mensaje))
            return;

        nombre =
            LimpiarNombre(
                nombre,
                mensaje
            );

        if (string.IsNullOrEmpty(nombre))
            nombre = "User";

        /*
         * ==================================================
         * DETERMINAR SI EL MENSAJE SE LEE
         * ==================================================
         *
         * caracterTTS vacío:
         *
         *     Lee TODOS los mensajes.
         *
         * caracterTTS = "*":
         *
         *     Solo lee:
         *
         *     * hola
         *
         * caracterTTS = "!":
         *
         *     Solo lee:
         *
         *     ! hola
         */

        string texto;

        if (string.IsNullOrEmpty(caracterTTS))
        {
            /*
             * NO HAY CARÁCTER.
             *
             * Se utiliza el mensaje completo.
             */
            texto = mensaje;
        }
        else
        {
            /*
             * HAY CARÁCTER.
             *
             * Solo aceptar mensajes que
             * comiencen con ese carácter.
             */
            if (!mensaje.StartsWith(
                    caracterTTS,
                    StringComparison.Ordinal))
            {
                return;
            }

            /*
             * Quitar el carácter antes
             * de mandarlo al TTS.
             *
             * Ejemplo:
             *
             * * hola
             *
             * se convierte en:
             *
             * hola
             */
            texto =
                mensaje
                    .Substring(
                        caracterTTS.Length
                    )
                    .Trim();
        }

        if (string.IsNullOrEmpty(texto))
            return;

        /*
         * Evitar mensajes duplicados.
         */
        string id =
            nombre +
            "|" +
            mensaje +
            "|" +
            plataforma;

        if (!mensajesProcesados.Add(id))
            return;

        if (mensajesProcesados.Count > 1000)
        {
            mensajesProcesados.Clear();

            mensajesProcesados.Add(id);
        }

        nombre =
            PrepararNombreTTS(nombre);

        texto =
            CorregirPronunciacion(texto);

        string textoTTS =
            nombre +
            " dice: " +
            texto;

        /*
         * Límite de la cola TTS.
         */
        int limite =
            Mathf.Max(
                1,
                maxMensajesTTS
            );

        /*
         * Si la cola está llena,
         * sacar los mensajes más antiguos.
         */
        while (colaTTS.Count >= limite)
        {
            string eliminado =
                colaTTS.Dequeue();

            Debug.Log(
                "🗑️ TTS eliminado de la cola: " +
                eliminado
            );
        }

        colaTTS.Enqueue(textoTTS);

        Debug.Log(
            "🎤 TTS en cola: " +
            colaTTS.Count +
            "/" +
            limite +
            " | " +
            textoTTS
        );

        HablarSiguiente();
    }

    private void HablarSiguiente()
    {
        if (hablando)
            return;

        if (tts == null)
            return;

        if (colaTTS.Count == 0)
            return;

        string texto =
            colaTTS.Dequeue();

        if (string.IsNullOrEmpty(texto))
        {
            HablarSiguiente();
            return;
        }

        hablando = true;

        tts.Speak(texto);
    }

    private void TTSFinalizado()
    {
        hablando = false;

        HablarSiguiente();
    }

    public void LimpiarCola()
    {
        colaTTS.Clear();

        Debug.Log(
            "🧹 Cola TTS limpiada."
        );
    }

    public void StopTTS()
    {
        colaTTS.Clear();

        hablando = false;

        if (tts != null)
            tts.Stop();

        Debug.Log(
            "🛑 TTS detenido."
        );
    }

    public void CambiarCaracterTTS(
        string nuevoCaracter)
    {
        if (nuevoCaracter == null)
            nuevoCaracter = "";

        caracterTTS =
            nuevoCaracter.Trim();

        if (string.IsNullOrEmpty(caracterTTS))
        {
            Debug.Log(
                "🎤 Carácter TTS vacío. " +
                "Se leerán TODOS los mensajes."
            );
        }
        else
        {
            Debug.Log(
                "🎤 Nuevo carácter TTS: [" +
                caracterTTS +
                "]"
            );
        }
    }

    private string PrepararNombreTTS(
        string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return "User";

        nombre =
            nombre
                .Replace("_", " ")
                .Replace("@", " arroba ");

        return EspaciosRegex
            .Replace(nombre, " ")
            .Trim();
    }

    private string LimpiarMensaje(
        string mensaje)
    {
        if (string.IsNullOrEmpty(mensaje))
            return string.Empty;

        mensaje =
            mensaje
                .Replace("\u200B", "")
                .Replace("\u200C", "")
                .Replace("\u200D", "")
                .Replace("\uFEFF", "")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ");

        return EspaciosRegex
            .Replace(mensaje, " ")
            .Trim();
    }

    private string LimpiarNombre(
        string nombre,
        string mensaje)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return "User";

        nombre =
            nombre.Trim();

        mensaje =
            mensaje != null
                ? mensaje.Trim()
                : "";

        if (!string.IsNullOrEmpty(mensaje) &&
            nombre.EndsWith(
                mensaje,
                StringComparison.Ordinal))
        {
            nombre =
                nombre
                    .Substring(
                        0,
                        nombre.Length -
                        mensaje.Length
                    )
                    .Trim();
        }

        if (nombre.StartsWith("@"))
        {
            nombre =
                nombre.Substring(1);
        }

        return EspaciosRegex
            .Replace(nombre, " ")
            .Trim();
    }

    private string CorregirPronunciacion(
        string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return string.Empty;

        texto =
            texto
                .Replace("@", " arroba ")
                .Replace("#", " almohadilla ")
                .Replace("$", " dólar ")
                .Replace("%", " por ciento ")
                .Replace("_", " ");

        texto =
            Regex.Replace(
                texto,
                @"\bRoblox\b",
                "Ró-bloks",
                RegexOptions.IgnoreCase
            );

        texto =
            Regex.Replace(
                texto,
                @"\bMinecraft\b",
                "Máin-cráft",
                RegexOptions.IgnoreCase
            );

        texto =
            Regex.Replace(
                texto,
                @"\bDiscord\b",
                "Dis-córd",
                RegexOptions.IgnoreCase
            );

        texto =
            Regex.Replace(
                texto,
                @"\bYouTube\b",
                "Yutub",
                RegexOptions.IgnoreCase
            );

        return EspaciosRegex
            .Replace(texto, " ")
            .Trim();
    }
}