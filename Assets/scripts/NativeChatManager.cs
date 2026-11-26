using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NativeWebSocket;
using UnityEngine;
using UnityEngine.Networking;

public class NativeChatManager : MonoBehaviour
{
    private const string TwitchSocketUrl = "wss://irc-ws.chat.twitch.tv:443";
    private const string KickApiBase = "https://web.kick.com/api/v1/realtime";
    private const string KickOrigin = "https://kick.com";

    private YouTubeInnerTubeClient youtubeClient;
    private WebSocket twitchSocket;
    private WebSocket kickSocket;
    private Coroutine twitchKeepAliveRoutine;
    private Coroutine kickKeepAliveRoutine;
    private string twitchChannel;
    private string twitchUsername;
    private string twitchAccessToken;
    private string kickChannel;
    private string kickChatroomId;
    private string kickClientId;
    private bool kickConnected;
    private bool kickSubscribed;
    private bool twitchAuthenticated;
    private bool twitchAuthenticationRejected;
    private bool shuttingDown;

    public event Action<string, string> OnPlatformStatus;

    public void ConnectAll(
        string twitch,
        string twitchUser,
        string twitchToken,
        string youtube,
        string kick)
    {
        DisconnectAll();
        shuttingDown = false;
        twitchAuthenticated = false;
        twitchAuthenticationRejected = false;
        Application.runInBackground = true;
        twitchChannel = NormalizeChannel(twitch);
        twitchUsername = NormalizeChannel(twitchUser);
        twitchAccessToken = NormalizeToken(twitchToken);
        kickChannel = NormalizeChannel(kick);

        if (!string.IsNullOrEmpty(twitchChannel))
            twitchKeepAliveRoutine = StartCoroutine(KeepTwitchConnected());

        if (!string.IsNullOrEmpty(youtube))
        {
            youtubeClient = GetComponent<YouTubeInnerTubeClient>();
            if (youtubeClient == null)
                youtubeClient = gameObject.AddComponent<YouTubeInnerTubeClient>();

            youtubeClient.OnMessage -= OnYouTubeMessage;
            youtubeClient.OnMessage += OnYouTubeMessage;
            youtubeClient.OnStatus -= OnYouTubeStatus;
            youtubeClient.OnStatus += OnYouTubeStatus;
            youtubeClient.Connect(youtube);
        }

        if (!string.IsNullOrEmpty(kickChannel))
            kickKeepAliveRoutine = StartCoroutine(KeepKickConnected());
    }

    public void DisconnectAll()
    {
        shuttingDown = true;
        kickConnected = false;
        kickSubscribed = false;
        kickPendingToken = null;

        if (twitchKeepAliveRoutine != null)
        {
            StopCoroutine(twitchKeepAliveRoutine);
            twitchKeepAliveRoutine = null;
        }

        if (kickKeepAliveRoutine != null)
        {
            StopCoroutine(kickKeepAliveRoutine);
            kickKeepAliveRoutine = null;
        }

        if (youtubeClient != null)
        {
            youtubeClient.OnMessage -= OnYouTubeMessage;
            youtubeClient.OnStatus -= OnYouTubeStatus;
            youtubeClient.Disconnect();
        }

        if (twitchSocket != null)
        {
            WebSocket old = twitchSocket;
            twitchSocket = null;
            old.Close();
        }

        if (kickSocket != null)
        {
            WebSocket old = kickSocket;
            kickSocket = null;
            old.Close();
        }

        SetStatus("Twitch", "Desconectado.");
        SetStatus("YouTube", "Desconectado.");
        SetStatus("Kick", "Desconectado.");
    }

    private void OnYouTubeMessage(string name, string message)
    {
        if (shuttingDown)
            return;

        ChatMessageBus.Publish(name, message, "YouTube");
    }

    private void OnYouTubeStatus(string status)
    {
        SetStatus("YouTube", status);
    }

    private IEnumerator KeepTwitchConnected()
    {
        if (string.IsNullOrEmpty(twitchUsername) ||
            string.IsNullOrEmpty(twitchAccessToken))
        {
            SetStatus("Twitch", "Falta usuario y token OAuth con permiso chat:read.");
            twitchKeepAliveRoutine = null;
            yield break;
        }

        int retryDelay = 3;

        while (!shuttingDown)
        {
            yield return StartCoroutine(ConnectTwitchRoutine());

            if (shuttingDown || twitchAuthenticationRejected)
                break;

            float authenticationWait = 0f;
            while (!shuttingDown && !twitchAuthenticationRejected &&
                   !twitchAuthenticated && authenticationWait < 15f &&
                   twitchSocket != null && twitchSocket.State == WebSocketState.Open)
            {
                authenticationWait += Time.unscaledDeltaTime;
                yield return null;
            }

            if (shuttingDown || twitchAuthenticationRejected)
                break;

            if (!twitchAuthenticated)
            {
                SetStatus("Twitch", "No se confirmó el inicio de sesión en 15 segundos; reconectando.");
                if (twitchSocket != null)
                    twitchSocket.Close();
                yield return new WaitForSecondsRealtime(retryDelay);
                retryDelay = Mathf.Min(retryDelay * 2, 30);
                continue;
            }

            if (twitchSocket == null || twitchSocket.State != WebSocketState.Open)
            {
                yield return new WaitForSecondsRealtime(retryDelay);
                retryDelay = Mathf.Min(retryDelay * 2, 30);
                continue;
            }

            retryDelay = 3;

            while (!shuttingDown && twitchSocket != null &&
                   twitchSocket.State == WebSocketState.Open)
                yield return null;

            if (shuttingDown)
                break;

            WebSocket old = twitchSocket;
            twitchSocket = null;
            twitchAuthenticated = false;
            if (old != null)
                old.Close();

            yield return new WaitForSecondsRealtime(retryDelay);
            retryDelay = Mathf.Min(retryDelay * 2, 30);
        }

        twitchKeepAliveRoutine = null;
    }

    private IEnumerator KeepKickConnected()
    {
        int retryDelay = 3;

        while (!shuttingDown)
        {
            yield return StartCoroutine(ConnectKickRoutine());

            if (shuttingDown)
                break;

            if (kickSocket == null || kickSocket.State != WebSocketState.Open)
            {
                yield return new WaitForSecondsRealtime(retryDelay);
                retryDelay = Mathf.Min(retryDelay * 2, 30);
                continue;
            }

            float handshakeSeconds = 0f;
            while (!shuttingDown && !kickSubscribed && handshakeSeconds < 20f &&
                   kickSocket != null && kickSocket.State == WebSocketState.Open)
            {
                handshakeSeconds += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!kickSubscribed)
            {
                SetStatus("Kick", "No se confirmó la suscripción en 20 segundos; reconectando.");
                if (kickSocket != null)
                    kickSocket.Close();
                yield return new WaitForSecondsRealtime(retryDelay);
                retryDelay = Mathf.Min(retryDelay * 2, 30);
                continue;
            }

            retryDelay = 3;
            float leaseSeconds = 0f;

            while (!shuttingDown && leaseSeconds < 2100f &&
                   kickSocket != null && kickSocket.State == WebSocketState.Open)
            {
                leaseSeconds += Time.unscaledDeltaTime;
                yield return null;
            }

            if (shuttingDown)
                break;

            WebSocket old = kickSocket;
            kickSocket = null;
            kickConnected = false;
            kickSubscribed = false;

            if (old != null)
                old.Close();

            SetStatus("Kick", "Renovando token temporal antes de su vencimiento.");
            yield return new WaitForSecondsRealtime(2f);
        }

        kickKeepAliveRoutine = null;
    }

    private IEnumerator ConnectTwitchRoutine()
    {
        SetStatus("Twitch", "Conectando por IRC seguro...");

        if (twitchSocket != null)
        {
            WebSocket previous = twitchSocket;
            twitchSocket = null;
            previous.Close();
        }

        twitchAuthenticated = false;
        twitchSocket = new WebSocket(TwitchSocketUrl);
        twitchSocket.OnOpen += OnTwitchOpen;
        twitchSocket.OnError += error => SetStatus("Twitch", "Error: " + error);
        twitchSocket.OnClose += code => SetStatus("Twitch", "Conexión cerrada: " + code);
        twitchSocket.OnMessage += OnTwitchMessage;

        var task = twitchSocket.Connect();
        while (!task.IsCompleted)
            yield return null;

        if (task.IsFaulted)
            SetStatus("Twitch", "No se pudo abrir el WebSocket.");
    }

    private async void OnTwitchOpen()
    {
        if (twitchSocket == null || shuttingDown)
            return;

        try
        {
            SetStatus("Twitch", "WebSocket abierto; autenticando...");
            await twitchSocket.SendText("PASS oauth:" + twitchAccessToken + "\r\n");
            await twitchSocket.SendText("NICK " + twitchUsername + "\r\n");
            await twitchSocket.SendText("CAP REQ :twitch.tv/tags twitch.tv/commands\r\n");
            await twitchSocket.SendText("JOIN #" + twitchChannel + "\r\n");
        }
        catch (Exception exception)
        {
            SetStatus("Twitch", "Error enviando autenticación IRC: " + exception.Message);
            if (twitchSocket != null)
                twitchSocket.Close();
        }
    }

    private async void OnTwitchMessage(byte[] bytes)
    {
        if (shuttingDown)
            return;

        string payload = Encoding.UTF8.GetString(bytes);
        string[] lines = payload.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            if (line.StartsWith("PING ", StringComparison.Ordinal))
            {
                try
                {
                    if (twitchSocket != null && twitchSocket.State == WebSocketState.Open)
                        await twitchSocket.SendText("PONG " + line.Substring(5) + "\r\n");
                }
                catch (Exception exception)
                {
                    SetStatus("Twitch", "No se pudo responder al PING: " + exception.Message);
                }
                continue;
            }

            if (line.IndexOf(" 001 ", StringComparison.Ordinal) >= 0)
            {
                twitchAuthenticated = true;
                SetStatus("Twitch", "Conectado a #" + twitchChannel + ".");
                continue;
            }

            if (line.IndexOf("Login authentication failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                twitchAuthenticationRejected = true;
                SetStatus("Twitch", "Twitch rechazó el token. Revisá chat:read y la cuenta; no se reintentará hasta volver a conectar.");
                if (twitchSocket != null)
                    twitchSocket.Close();
                continue;
            }

            int commandIndex = line.IndexOf(" PRIVMSG ", StringComparison.Ordinal);
            if (commandIndex < 0)
                continue;

            int bodyIndex = line.IndexOf(" :", commandIndex, StringComparison.Ordinal);
            if (bodyIndex < 0)
                continue;

            int prefixStart = line.StartsWith("@", StringComparison.Ordinal)
                ? line.IndexOf(" :", StringComparison.Ordinal) + 2
                : 1;

            if (prefixStart < 1 || prefixStart >= commandIndex)
                continue;

            string prefix = line.Substring(prefixStart, commandIndex - prefixStart);
            string message = line.Substring(bodyIndex + 2);
            string username = ExtractTwitchUsername(prefix);
            string displayName = ExtractTwitchTag(line, "display-name");

            if (!string.IsNullOrEmpty(displayName))
                username = displayName;

            if (!string.IsNullOrEmpty(message))
                ChatMessageBus.Publish(username, message, "Twitch");
        }
    }

    private IEnumerator ConnectKickRoutine()
    {
        SetStatus("Kick", "Resolviendo el canal...");
        string slug = UnityWebRequest.EscapeURL(kickChannel);
        using (UnityWebRequest request = UnityWebRequest.Get("https://kick.com/api/v2/channels/" + slug))
        {
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");
            request.timeout = 20;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Kick", "No se pudo resolver el canal: " + request.error);
                yield break;
            }

            KickChannelResponse channelResponse;
            try
            {
                channelResponse = JsonUtility.FromJson<KickChannelResponse>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                SetStatus("Kick", "Respuesta del canal inválida: " + exception.Message);
                yield break;
            }

            if (channelResponse == null || channelResponse.chatroom == null ||
                channelResponse.chatroom.id <= 0)
            {
                SetStatus("Kick", "Kick no devolvió un chatroom ID para ese canal.");
                yield break;
            }

            kickChatroomId = channelResponse.chatroom.id.ToString();
        }

        kickClientId = Guid.NewGuid().ToString();
        yield return StartCoroutine(ConnectKickRealtimeRoutine());
    }

    private IEnumerator ConnectKickRealtimeRoutine()
    {
        SetStatus("Kick", "Solicitando endpoint realtime...");
        string body = "{\"client\":{\"id\":\"" + kickClientId +
            "\",\"type\":\"web\"},\"capabilities\":{\"accepted_providers\":[{\"provider\":\"centrifugo\"}]}}";

        string websocketUrl = null;
        using (UnityWebRequest request = CreateKickPost(
            KickApiBase + "/channels/" + kickChatroomId + "/chat/connection", body))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Kick", "Falló la negociación realtime: " + request.error);
                yield break;
            }

            try
            {
                KickConnectionResponse response =
                    JsonUtility.FromJson<KickConnectionResponse>(request.downloadHandler.text);

                if (response != null && response.data != null &&
                    response.data.connections != null)
                {
                    foreach (KickConnection candidate in response.data.connections)
                    {
                        if (candidate != null && candidate.provider == "centrifugo" &&
                            candidate.credentials != null)
                        {
                            websocketUrl = candidate.credentials.url;
                            break;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                SetStatus("Kick", "Respuesta realtime inválida: " + exception.Message);
                yield break;
            }
        }

        if (string.IsNullOrEmpty(websocketUrl))
        {
            SetStatus("Kick", "Kick no ofreció un endpoint Centrifugo.");
            yield break;
        }

        SetStatus("Kick", "Solicitando token temporal...");
        string token = null;
        using (UnityWebRequest request = CreateKickPost(
            KickApiBase + "/auth/connection",
            "{\"client_id\":\"" + kickClientId + "\"}"))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Kick", "No se pudo obtener el token invitado: " + request.error);
                yield break;
            }

            try
            {
                KickTokenResponse response =
                    JsonUtility.FromJson<KickTokenResponse>(request.downloadHandler.text);
                if (response != null && response.data != null)
                    token = response.data.token;
            }
            catch (Exception exception)
            {
                SetStatus("Kick", "Token realtime inválido: " + exception.Message);
                yield break;
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            SetStatus("Kick", "Kick devolvió un token vacío.");
            yield break;
        }

        if (kickSocket != null)
        {
            WebSocket previous = kickSocket;
            kickSocket = null;
            previous.Close();
        }

        Dictionary<string, string> headers = new Dictionary<string, string>
        {
            { "Origin", KickOrigin }
        };

        kickSocket = new WebSocket(websocketUrl, headers);
        kickSocket.OnOpen += OnKickOpen;
        kickSocket.OnError += error => SetStatus("Kick", "WebSocket: " + error);
        kickSocket.OnClose += code => SetStatus("Kick", "Conexión cerrada: " + code);
        kickSocket.OnMessage += OnKickMessage;

        kickPendingToken = token;
        var task = kickSocket.Connect();
        while (!task.IsCompleted)
            yield return null;

        if (task.IsFaulted)
            SetStatus("Kick", "No se pudo abrir el WebSocket Centrifugo.");
    }

    private string kickPendingToken;

    private async void OnKickOpen()
    {
        if (kickSocket == null || shuttingDown)
            return;

        try
        {
            await kickSocket.SendText(
                "{\"id\":1,\"connect\":{\"token\":\"" + kickPendingToken + "\",\"name\":\"unity-pavotts\"}}"
            );
            SetStatus("Kick", "WebSocket abierto; autenticando realtime...");
        }
        catch (Exception exception)
        {
            SetStatus("Kick", "No se pudo enviar el token realtime: " + exception.Message);
            if (kickSocket != null)
                kickSocket.Close();
        }
    }

    private async void OnKickMessage(byte[] bytes)
    {
        if (shuttingDown)
            return;

        string raw = Encoding.UTF8.GetString(bytes);
        string[] frames = raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (string frame in frames)
        {
            Dictionary<string, object> root;
            try
            {
                root = YouTubeInnerTubeClient.MiniJson.Parse(frame) as Dictionary<string, object>;
            }
            catch
            {
                continue;
            }

            if (root == null)
                continue;

            object idValue;
            int id = 0;
            if (root.TryGetValue("id", out idValue))
                int.TryParse(Convert.ToString(idValue), out id);

            object connect;
            if (id == 1 && root.TryGetValue("connect", out connect) && connect != null && !kickConnected)
            {
                kickConnected = true;
                if (kickSocket != null && kickSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await kickSocket.SendText(
                        "{\\"id\\":2,\\"subscribe\\":{\\"channel\\":\\"chatrooms." +
                        kickChatroomId + ".v2\\"}}"
                    );

                    }
                    catch (Exception exception)
                    {
                        kickSubscribed = false;
                        SetStatus("Kick", "No se pudo suscribir al chat: " + exception.Message);
                        if (kickSocket != null)
                            kickSocket.Close();
                    }
                }
                SetStatus("Kick", "Autenticado; esperando confirmación de suscripción.");
                continue;
            }

            object subscribe;
            if (id == 2 && root.TryGetValue("subscribe", out subscribe) && subscribe != null)
            {
                kickSubscribed = true;
                SetStatus("Kick", "Suscripción al chat confirmada.");
                continue;
            }

            object pushValue;
            Dictionary<string, object> push = root.TryGetValue("push", out pushValue)
                ? pushValue as Dictionary<string, object>
                : null;

            if (push == null)
                continue;

            object ping;
            if (push.TryGetValue("ping", out ping) && ping != null)
            {
                try
                {
                    if (kickSocket != null && kickSocket.State == WebSocketState.Open)
                        await kickSocket.SendText("{\\"pong\\":{}}");
                }
                catch (Exception exception)
                {
                    SetStatus("Kick", "No se pudo responder al ping: " + exception.Message);
                    if (kickSocket != null)
                        kickSocket.Close();
                }
                continue;
            }

            object publicationValue;
            Dictionary<string, object> publication = push.TryGetValue("pub", out publicationValue)
                ? publicationValue as Dictionary<string, object>
                : null;

            if (publication == null)
                continue;

            object data;
            if (publication.TryGetValue("data", out data))
                ProcessKickPublication(data);
        }
    }

    private void ProcessKickPublication(object publication)
    {
        object parsed = publication;
        string raw = publication as string;

        if (raw != null)
        {
            try
            {
                parsed = YouTubeInnerTubeClient.MiniJson.Parse(raw);
            }
            catch
            {
                return;
            }
        }

        Dictionary<string, object> envelope = parsed as Dictionary<string, object>;
        if (envelope == null)
            return;

        object eventValue;
        if (!envelope.TryGetValue("event", out eventValue) ||
            !string.Equals(Convert.ToString(eventValue), "App\\Events\\ChatMessageEvent", StringComparison.Ordinal))
            return;

        object dataValue;
        if (!envelope.TryGetValue("data", out dataValue) || dataValue == null)
            return;

        string nestedJson = dataValue as string;
        if (nestedJson != null)
        {
            try
            {
                dataValue = YouTubeInnerTubeClient.MiniJson.Parse(nestedJson);
            }
            catch
            {
                return;
            }
        }

        Dictionary<string, object> chatEvent = dataValue as Dictionary<string, object>;
        if (chatEvent == null)
            return;

        object contentValue;
        if (!chatEvent.TryGetValue("content", out contentValue))
            return;

        string content = Convert.ToString(contentValue);
        object senderValue;
        Dictionary<string, object> sender = chatEvent.TryGetValue("sender", out senderValue)
            ? senderValue as Dictionary<string, object>
            : null;

        string username = "Kick";
        object usernameValue;
        if (sender != null && sender.TryGetValue("username", out usernameValue))
            username = Convert.ToString(usernameValue);

        if (!string.IsNullOrWhiteSpace(content))
            ChatMessageBus.Publish(username, content, "Kick");
    }

    private UnityWebRequest CreateKickPost(string url, string json)
    {
        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");
        request.SetRequestHeader("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");
        request.timeout = 20;
        return request;
    }

    private string NormalizeChannel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().TrimStart('@', '#').ToLowerInvariant();
    }

    private string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Trim();
        if (value.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(6);

        return value;
    }

    private string ExtractTwitchUsername(string prefix)
    {
        int bang = prefix.IndexOf('!');
        if (bang >= 0)
            return prefix.Substring(0, bang).TrimStart(':');

        return prefix.TrimStart(':');
    }

    private string ExtractTwitchTag(string line, string key)
    {
        if (!line.StartsWith("@", StringComparison.Ordinal))
            return string.Empty;

        int space = line.IndexOf(' ');
        if (space < 0)
            return string.Empty;

        string[] tags = line.Substring(1, space - 1).Split(';');
        string prefix = key + "=";

        foreach (string tag in tags)
        {
            if (tag.StartsWith(prefix, StringComparison.Ordinal))
                return tag.Substring(prefix.Length).Replace("\\s", " ").Replace("\\:", ";")
                    .Replace("\\\\", "\\").Replace("\\r", "\r").Replace("\\n", "\n");
        }

        return string.Empty;
    }

    private void SetStatus(string platform, string status)
    {
        Debug.Log("[" + platform + "] " + status);
        OnPlatformStatus?.Invoke(platform, status);
    }

    private void OnDestroy()
    {
        DisconnectAll();
    }

    [Serializable] private class KickChannelResponse { public KickChatroom chatroom; }
    [Serializable] private class KickChatroom { public long id; }
    [Serializable] private class KickConnectionResponse { public KickConnectionData data; }
    [Serializable] private class KickConnectionData { public KickConnection[] connections; }
    [Serializable] private class KickConnection { public string provider; public KickCredentials credentials; }
    [Serializable] private class KickCredentials { public string url; }
    [Serializable] private class KickTokenResponse { public KickTokenData data; }
    [Serializable] private class KickTokenData { public string token; }
    [Serializable] private class KickRealtimeFrame { public int id; public KickConnectReply connect; public KickPush push; }
    [Serializable] private class KickConnectReply { public string client; public string version; }
    [Serializable] private class KickSubscribeReply { public string channel; }
    [Serializable] private class KickPing { }

    [Serializable] private class KickPush { public KickPublication pub; }
    [Serializable] private class KickPublication { public string data; }
    [Serializable] private class KickEventEnvelope { public string @event; public string data; }
    [Serializable] private class KickChatEvent { public string content; public KickSender sender; }
    [Serializable] private class KickSender { public string username; }
}
