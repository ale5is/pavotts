using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class BotrixChatUI : MonoBehaviour
{
    [Header("REFERENCIAS")]
    [SerializeField] private BotrixWebView botrix;
    [SerializeField] private TMP_Text chatText;

    [Header("CONFIGURACIÓN")]
    [SerializeField] private int maxMensajes = 24;
    [SerializeField] private bool mostrarPlataforma = true;

    private readonly Queue<string> mensajes = new Queue<string>();

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
            Debug.LogError("❌ BotrixChatUI: No se encontró ChatText.");
        }

        if (botrix == null)
        {
            Debug.LogError("❌ BotrixChatUI: No se encontró BotrixWebView.");
            return;
        }

        // Evita suscripciones duplicadas
        botrix.OnChatMessage -= RecibirMensaje;
        botrix.OnChatMessage += RecibirMensaje;

        Debug.Log("✅ BotrixChatUI conectado correctamente.");
    }

    private void RecibirMensaje(
        string nombre,
        string mensaje,
        string plataforma)
    {
        mensaje = LimpiarTexto(mensaje);
        nombre = LimpiarTexto(nombre);
        plataforma = LimpiarTexto(plataforma);

        // No mostrar mensajes vacíos
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

        // Agregar el mensaje nuevo
        mensajes.Enqueue(linea);

        // IMPORTANTE:
        // Mantener solamente los últimos 24 mensajes.
        // Dequeue() elimina el mensaje MÁS ANTIGUO.
        int limite = Mathf.Max(1, maxMensajes);

        while (mensajes.Count > limite)
        {
            string mensajeEliminado = mensajes.Dequeue();

            Debug.Log(
                "🗑️ Mensaje eliminado del chat: " +
                mensajeEliminado
            );
        }

        ActualizarChat();

        Debug.Log(
            "💬 Mensajes actuales: " +
            mensajes.Count +
            "/" +
            limite
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

        // Convertir la cola en texto.
        // El más antiguo queda arriba.
        chatText.text = string.Join("\n", mensajes);
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