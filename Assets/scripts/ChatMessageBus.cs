using System;
using System.Collections.Generic;
using UnityEngine;

public static class ChatMessageBus
{
    private static readonly object Sync = new object();
    private static readonly Queue<ChatMessage> Pending = new Queue<ChatMessage>();
    private const int MaximumPendingMessages = 4096;
    private static long droppedMessages;

    public static event Action<string, string, string> OnMessage;

    public static void Publish(string nombre, string mensaje, string plataforma)
    {
        if (string.IsNullOrWhiteSpace(mensaje))
            return;

        lock (Sync)
        {
            while (Pending.Count >= MaximumPendingMessages)
            {
                Pending.Dequeue();
                droppedMessages++;
            }

            Pending.Enqueue(new ChatMessage(
                string.IsNullOrWhiteSpace(nombre) ? "User" : nombre.Trim(),
                mensaje.Trim(),
                string.IsNullOrWhiteSpace(plataforma) ? "Chat" : plataforma.Trim()
            ));
        }
    }

    public static int DispatchPending(int maximumPerFrame = 128)
    {
        int dispatched = 0;

        while (dispatched < maximumPerFrame)
        {
            ChatMessage message;

            lock (Sync)
            {
                if (Pending.Count == 0)
                    break;

                message = Pending.Dequeue();
            }

            try
            {
                OnMessage?.Invoke(
                    message.Name,
                    message.Text,
                    message.Platform
                );
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            dispatched++;
        }

        return dispatched;
    }

    public static long DroppedMessages
    {
        get
        {
            lock (Sync)
                return droppedMessages;
        }
    }

    public static void ClearPending()
    {
        lock (Sync)
            Pending.Clear();
    }

    private sealed class ChatMessage
    {
        public readonly string Name;
        public readonly string Text;
        public readonly string Platform;

        public ChatMessage(string name, string text, string platform)
        {
            Name = name;
            Text = text;
            Platform = platform;
        }
    }
}
