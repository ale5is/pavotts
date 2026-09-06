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

    private readonly Queue<string> mensajes = new Queue<string>();

    public int MaxLineas
    {
        get { return Mathf.Max(1, maxLineas); }
    }

    public int CantidadMensajes
    {
        get { return mensajes.Count; }
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

        // Agregar el nuevo mensaje.
        mensajes.Enqueue(linea);

        // Eliminar mensajes antiguos hasta que
        // TODO el texto entre dentro de las 24 líneas.
        AjustarMensajesALimite();

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

    private void AjustarMensajesALimite()
    {
        if (chatText == null)
            return;

        if (mensajes.Count == 0)
            return;

        while (mensajes.Count > 0)
        {
            string textoCompleto =
                string.Join("\n", mensajes);

            // Poner temporalmente todos los mensajes
            // en el TMP para que TextMeshPro haga
            // exactamente el mismo wrapping que vemos
            // en el Canvas.
            chatText.text = textoCompleto;

            chatText.ForceMeshUpdate();

            int lineas = ObtenerLineasActuales();

            if (lineas <= MaxLineas)
                break;

            // Se pasó del límite.
            // Eliminar el mensaje MÁS ANTIGUO completo.
            string eliminado = mensajes.Dequeue();

            Debug.Log(
                "🗑️ Eliminado por superar " +
                MaxLineas +
                " líneas: " +
                eliminado
            );
        }
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

        chatText.text =
            string.Join("\n", mensajes);

        chatText.ForceMeshUpdate();
    }

    /// <summary>
    /// Devuelve las líneas visuales reales que
    /// TextMeshPro está mostrando.
    /// </summary>
    public int ObtenerLineasActuales()
    {
        if (chatText == null)
            return 0;

        if (string.IsNullOrEmpty(chatText.text))
            return 0;

        chatText.ForceMeshUpdate();

        return chatText.textInfo.lineCount;
    }

    /// <summary>
    /// Devuelve el máximo de líneas configurado.
    /// BotrixChat utiliza este valor para la cola TTS.
    /// </summary>
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

    private string FormatoPlataforma(string plataforma)
    {
        switch (plataforma)
        {
            case "Twitch":
                return "<color=#9146FF>[Twitch]</color>";

            case "YouTube":
                return "<color=#FF0000>[YouTube]</color>";

            case "Kick":
                return "<color=#53FC18>[Kick]</color>";

            case "Discord":
                return "<color=#5865F2>[Discord]</color>";

            case "Minecraft":
                return "<color=#55AA55>[Minecraft]</color>";

            default:
                return "[Chat]";
        }
    }

    public void LimpiarChat()
    {
        mensajes.Clear();

        ActualizarChat();

        Debug.Log("🧹 Chat limpiado.");
    }
}