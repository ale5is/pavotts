using System;

public static class ChatMessageBus
{
    public static event Action<string, string, string> OnMessage;

    public static void Publish(
        string nombre,
        string mensaje,
        string plataforma)
    {
        if (string.IsNullOrWhiteSpace(mensaje))
            return;

        OnMessage?.Invoke(
            string.IsNullOrWhiteSpace(nombre) ? "User" : nombre.Trim(),
            mensaje.Trim(),
            string.IsNullOrWhiteSpace(plataforma) ? "Chat" : plataforma.Trim()
        );
    }
}
