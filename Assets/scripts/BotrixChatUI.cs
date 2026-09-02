using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class BotrixChatUI : MonoBehaviour
{
    // ==================================================
    // REFERENCIAS
    // ==================================================

    [Header("REFERENCIAS")]

    [SerializeField]
    private BotrixWebView botrix;

    [SerializeField]
    private TMP_Text chatText;


    // ==================================================
    // CONFIGURACIÓN
    // ==================================================

    [Header("CONFIGURACIÓN")]

    [SerializeField]
    private int maxMensajes = 30;

    [SerializeField]
    private bool mostrarPlataforma = true;


    // ==================================================
    // COLA DE MENSAJES
    // ==================================================

    private readonly Queue<string> mensajes =
        new Queue<string>();


    // ==================================================
    // START
    // ==================================================

    private void Start()
    {
        BuscarReferencias();

        ActualizarChat();
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

        if (chatText == null)
        {
            Debug.LogError(
                "❌ No se encontró ChatText"
            );
        }

        if (botrix == null)
        {
            Debug.LogError(
                "❌ No se encontró BotrixWebView"
            );

            return;
        }

        botrix.OnChatMessage -=
            RecibirMensaje;

        botrix.OnChatMessage +=
            RecibirMensaje;


        Debug.Log(
            "✅ BotrixChatUI conectado"
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
                RecibirMensaje;
        }
    }


    // ==================================================
    // RECIBIR MENSAJE
    // ==================================================

    private void RecibirMensaje(
        string nombre,
        string mensaje,
        string plataforma
    )
    {
        if (string.IsNullOrWhiteSpace(mensaje))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(nombre))
        {
            nombre = "User";
        }

        if (string.IsNullOrWhiteSpace(plataforma))
        {
            plataforma = "Chat";
        }


        // ==============================================
        // LIMPIAR
        // ==============================================

        nombre =
            LimpiarTexto(nombre);

        mensaje =
            LimpiarTexto(mensaje);


        // ==============================================
        // FORMATO
        // ==============================================

        string linea;

        if (mostrarPlataforma)
        {
            linea =
                FormatoPlataforma(
                    plataforma
                ) +
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


        // ==============================================
        // AGREGAR
        // ==============================================

        mensajes.Enqueue(
            linea
        );


        // ==============================================
        // LIMITE
        // ==============================================

        while (
            mensajes.Count >
            maxMensajes
        )
        {
            mensajes.Dequeue();
        }


        ActualizarChat();
    }


    // ==================================================
    // ACTUALIZAR CHAT
    // ==================================================

    private void ActualizarChat()
    {
        if (chatText == null)
        {
            return;
        }

        if (mensajes.Count == 0)
        {
            chatText.text = "";
            return;
        }


        chatText.text =
            string.Join(
                "\n",
                mensajes
            );
    }


    // ==================================================
    // LIMPIAR TEXTO
    // ==================================================

    private string LimpiarTexto(
        string texto
    )
    {
        if (string.IsNullOrEmpty(texto))
        {
            return "";
        }

        texto =
            texto
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

        return texto;
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


    // ==================================================
    // LIMPIAR CHAT
    // ==================================================

    public void LimpiarChat()
    {
        mensajes.Clear();

        ActualizarChat();

        Debug.Log(
            "🧹 Chat visual limpiado"
        );
    }
}