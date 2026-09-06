using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class BotrixChatUI : MonoBehaviour
{
    [Header("REFERENCIAS")]
    [SerializeField] private BotrixWebView botrix;
    [SerializeField] private TMP_Text chatText;

    [Header("CONFIGURACIÓN")]
    [SerializeField] private int maxLineas = 24;
    [SerializeField] private bool mostrarPlataforma = true;

    private readonly List<string> mensajes =
        new List<string>();

    public int MaxLineas
    {
        get
        {
            return Mathf.Max(1, maxLineas);
        }
    }

    public int CantidadMensajes
    {
        get
        {
            return mensajes.Count;
        }
    }

    private void Start()
    {
        BuscarReferencias();
        ActualizarChat();
    }

    private void OnDestroy()
    {
        if (botrix != null)
            botrix.OnChatMessage -= RecibirMensaje;
    }

    private void BuscarReferencias()
    {
        if (botrix == null)
            botrix = FindFirstObjectByType<BotrixWebView>();

        if (chatText == null)
        {
            Debug.LogError(
                "❌ BotrixChatUI: No se encontró ChatText."
            );
        }

        if (botrix == null)
        {
            Debug.LogError(
                "❌ BotrixChatUI: No se encontró BotrixWebView."
            );

            return;
        }

        botrix.OnChatMessage -= RecibirMensaje;
        botrix.OnChatMessage += RecibirMensaje;

        Debug.Log(
            "✅ BotrixChatUI conectado correctamente."
        );
    }

    private void RecibirMensaje(
        string nombre,
        string mensaje,
        string plataforma)
    {
        mensaje = LimpiarTexto(mensaje);
        nombre = LimpiarTexto(nombre);
        plataforma = LimpiarTexto(plataforma);

        if (string.IsNullOrEmpty(mensaje))
            return;

        if (string.IsNullOrEmpty(nombre))
            nombre = "User";

        if (string.IsNullOrEmpty(plataforma))
            plataforma = "Chat";

        string linea;

        if (mostrarPlataforma)
        {
            linea =
                FormatoPlataforma(plataforma) +
                " " +
                nombre +
                ": " +
                mensaje;
        }
        else
        {
            linea =
                nombre +
                ": " +
                mensaje;
        }

        mensajes.Add(linea);

        ActualizarChat();

        Debug.Log(
            "💬 Líneas actuales: " +
            ObtenerLineasActuales() +
            "/" +
            MaxLineas +
            " | Mensajes: " +
            mensajes.Count
        );
    }

    private void ActualizarChat()
    {
        if (chatText == null)
            return;

        if (mensajes.Count == 0)
        {
            chatText.text = string.Empty;
            return;
        }

        /*
         * Volvemos a construir el texto completo.
         *
         * TMP se encarga de decidir cuántas
         * líneas visuales ocupa cada mensaje.
         */
        chatText.text =
            string.Join("\n", mensajes);

        chatText.ForceMeshUpdate();

        /*
         * Ahora comprobamos las líneas reales.
         */
        RecortarLineasExcedentes();

        chatText.ForceMeshUpdate();
    }

    private void RecortarLineasExcedentes()
    {
        if (chatText == null)
            return;

        if (string.IsNullOrEmpty(chatText.text))
            return;

        chatText.ForceMeshUpdate();

        int lineasActuales =
            chatText.textInfo.lineCount;

        /*
         * Ejemplos:
         *
         * 24 - 24 = 0
         * 25 - 24 = 1
         * 26 - 24 = 2
         * 30 - 24 = 6
         */
        int lineasASobrar =
            lineasActuales - MaxLineas;

        if (lineasASobrar <= 0)
            return;

        /*
         * Si hay que eliminar 2 líneas:
         *
         * lineasASobrar = 2
         *
         * Conservamos desde la línea índice 2.
         *
         * Línea 0 -> eliminar
         * Línea 1 -> eliminar
         * Línea 2 -> conservar
         */
        int lineaInicio =
            lineasASobrar;

        if (lineaInicio >= lineasActuales)
        {
            chatText.text = string.Empty;
            mensajes.Clear();

            return;
        }

        TMP_LineInfo infoLinea =
            chatText.textInfo.lineInfo[
                lineaInicio
            ];

        int indiceCharacterInfo =
            infoLinea.firstCharacterIndex;

        if (indiceCharacterInfo < 0)
        {
            Debug.LogWarning(
                "⚠️ No se pudo obtener el primer carácter de la línea."
            );

            return;
        }

        if (indiceCharacterInfo >=
            chatText.textInfo.characterCount)
        {
            Debug.LogWarning(
                "⚠️ Índice de carácter fuera de rango."
            );

            return;
        }

        TMP_CharacterInfo characterInfo =
            chatText.textInfo.characterInfo[
                indiceCharacterInfo
            ];

        /*
         * Este es el índice REAL dentro de
         * chatText.text.
         */
        int indiceTexto =
            characterInfo.index;

        if (indiceTexto < 0 ||
            indiceTexto >= chatText.text.Length)
        {
            Debug.LogWarning(
                "⚠️ Índice de texto inválido."
            );

            return;
        }

        /*
         * Conservamos todo desde el primer carácter
         * de la primera línea que NO queremos eliminar.
         */
        string textoOriginal =
            chatText.text;

        string textoNuevo =
            textoOriginal.Substring(
                indiceTexto
            );

        chatText.text = textoNuevo;

        chatText.ForceMeshUpdate();

        /*
         * Comprobación.
         */
        int lineasDespues =
            chatText.textInfo.lineCount;

        Debug.Log(
            "✂️ Se eliminaron " +
            lineasASobrar +
            " líneas visuales. " +
            "Antes: " +
            lineasActuales +
            " | Después: " +
            lineasDespues +
            "/" +
            MaxLineas
        );
    }

    public int ObtenerLineasActuales()
    {
        if (chatText == null)
            return 0;

        if (string.IsNullOrEmpty(chatText.text))
            return 0;

        chatText.ForceMeshUpdate();

        return chatText.textInfo.lineCount;
    }

    public int ObtenerMaxLineas()
    {
        return MaxLineas;
    }

    private string LimpiarTexto(string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return string.Empty;

        texto = texto
            .Replace("\u200B", "")
            .Replace("\u200C", "")
            .Replace("\u200D", "")
            .Replace("\uFEFF", "")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Trim();

        while (texto.Contains("  "))
            texto = texto.Replace("  ", " ");

        return texto;
    }

    private string FormatoPlataforma(
        string plataforma)
    {
        switch (plataforma)
        {
            case "Twitch":
                return
                    "<color=#9146FF>[Twitch]</color>";

            case "YouTube":
                return
                    "<color=#FF0000>[YouTube]</color>";

            case "Kick":
                return
                    "<color=#53FC18>[Kick]</color>";

            case "Discord":
                return
                    "<color=#5865F2>[Discord]</color>";

            case "Minecraft":
                return
                    "<color=#55AA55>[Minecraft]</color>";

            default:
                return "[Chat]";
        }
    }

    public void LimpiarChat()
    {
        mensajes.Clear();

        if (chatText != null)
            chatText.text = string.Empty;

        Debug.Log(
            "🧹 Chat limpiado."
        );
    }
}