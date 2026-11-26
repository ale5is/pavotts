using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class YouTubeInnerTubeClient : MonoBehaviour
{
    public event Action<string, string> OnMessage;
    public event Action<string> OnStatus;

    private Coroutine routine;
    private bool stopping;
    private readonly HashSet<string> seenMessageIds = new HashSet<string>();
    private readonly Queue<string> seenMessageOrder = new Queue<string>();

    public void Connect(string channelHandle)
    {
        Disconnect();
        string handle = NormalizeHandle(channelHandle);
        if (string.IsNullOrEmpty(handle))
        {
            SetStatus("Falta el handle del canal.");
            return;
        }

        stopping = false;
        seenMessageIds.Clear();
        seenMessageOrder.Clear();
        routine = StartCoroutine(ReadLiveChat(handle));
    }

    public void Disconnect()
    {
        stopping = true;
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
    }

    private IEnumerator ReadLiveChat(string handle)
    {
        while (!stopping)
        {
            yield return StartCoroutine(ReadLiveChatSession(handle));

            if (!stopping)
            {
                SetStatus("Esperando una transmisión pública con chat activo; volveré a comprobarlo en 30 segundos.");
                yield return new WaitForSecondsRealtime(30f);
            }
        }

        routine = null;
    }

    private IEnumerator ReadLiveChatSession(string handle)
    {
        string pageUrl = "https://www.youtube.com/" + handle + "/live";
        string html;

        SetStatus("Buscando una transmisión en vivo...");
        using (UnityWebRequest request = UnityWebRequest.Get(pageUrl))
        {
            request.timeout = 25;
            request.SetRequestHeader("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                SetStatus("No se pudo abrir el canal: " + request.error);
                yield break;
            }

            html = request.downloadHandler.text;
        }

        string apiKey = MatchGroup(html, "INNERTUBE_API_KEY\\s*[:=]\\s*[\"']([^\"']+)");
        string clientVersion = MatchGroup(html, "INNERTUBE_CLIENT_VERSION\\s*[:=]\\s*[\"']([^\"']+)");
        int rendererIndex = html.IndexOf("liveChatRenderer", StringComparison.Ordinal);

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(clientVersion) || rendererIndex < 0)
        {
            SetStatus("No se encontró un chat en vivo accesible para ese canal.");
            yield break;
        }

        string continuation = MatchGroup(
            html.Substring(rendererIndex),
            "\"continuation\"\\s*:\\s*\"([^\"\\\\]+)"
        );

        if (string.IsNullOrEmpty(continuation))
        {
            SetStatus("La transmisión no publicó un token de continuación del chat.");
            yield break;
        }

        string endpoint = "https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?key=" + UnityWebRequest.EscapeURL(apiKey);
        SetStatus("Chat de YouTube localizado; leyendo mensajes...");

        while (!stopping && !string.IsNullOrEmpty(continuation))
        {
            string body = BuildRequestBody(clientVersion, continuation);
            string responseText = null;

            using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                request.uploadHandler = new UploadHandlerRaw(bytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 25;
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");

                yield return request.SendWebRequest();

                if (stopping)
                    yield break;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    SetStatus("Error leyendo YouTube Live Chat: " + request.error);
                    yield return new WaitForSecondsRealtime(5f);
                    continue;
                }

                responseText = request.downloadHandler.text;
            }

            object root;
            try
            {
                root = MiniJson.Parse(responseText);
            }
            catch (Exception exception)
            {
                SetStatus("Respuesta de YouTube inválida: " + exception.Message);
                    yield break;
            }

            ProcessMessages(root);
            int delayMs;
            string next = FindContinuation(root, out delayMs);

            if (string.IsNullOrEmpty(next))
            {
                SetStatus("YouTube no devolvió otra continuación. Se detuvo la lectura para evitar repetir mensajes.");
                    yield break;
            }

            continuation = next;
            yield return new WaitForSecondsRealtime(Mathf.Clamp(delayMs / 1000f, 0.5f, 15f));
        }

    }

    private void ProcessMessages(object node)
    {
        Dictionary<string, object> dictionary = node as Dictionary<string, object>;
        if (dictionary != null)
        {
            foreach (KeyValuePair<string, object> pair in dictionary)
            {
                if (IsChatRenderer(pair.Key))
                {
                    Dictionary<string, object> renderer = pair.Value as Dictionary<string, object>;
                    if (renderer != null)
                        PublishRenderer(renderer);
                }

                ProcessMessages(pair.Value);
            }

            return;
        }

        List<object> list = node as List<object>;
        if (list == null)
            return;

        foreach (object item in list)
            ProcessMessages(item);
    }

    private bool IsChatRenderer(string key)
    {
        return key == "liveChatTextMessageRenderer" ||
               key == "liveChatPaidMessageRenderer" ||
               key == "liveChatPaidStickerRenderer" ||
               key == "liveChatMembershipItemRenderer" ||
               key == "liveChatTickerPaidMessageItemRenderer";
    }

    private void PublishRenderer(Dictionary<string, object> renderer)
    {
        string id = GetString(renderer, "id");
        if (!string.IsNullOrEmpty(id))
        {
            if (!seenMessageIds.Add(id))
                return;

            seenMessageOrder.Enqueue(id);
            while (seenMessageOrder.Count > 2048)
                seenMessageIds.Remove(seenMessageOrder.Dequeue());
        }

        string author = GetText(GetValue(renderer, "authorName"));
        string message = GetText(GetValue(renderer, "message"));

        if (string.IsNullOrEmpty(message))
            message = GetText(GetValue(renderer, "headerSubtext"));

        if (string.IsNullOrEmpty(author))
            author = "YouTube";

        if (!string.IsNullOrEmpty(message))
            OnMessage?.Invoke(author, message);
    }

    private string FindContinuation(object node, out int delayMs)
    {
        delayMs = 2000;
        Dictionary<string, object> dictionary = node as Dictionary<string, object>;
        if (dictionary != null)
        {
            foreach (KeyValuePair<string, object> pair in dictionary)
            {
                Dictionary<string, object> candidate = pair.Value as Dictionary<string, object>;
                if (candidate != null &&
                    pair.Key.IndexOf("ContinuationData", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string token = GetString(candidate, "continuation");
                    string timeout = GetString(candidate, "timeoutMs");
                    int parsed;
                    if (int.TryParse(timeout, out parsed))
                        delayMs = parsed;

                    if (!string.IsNullOrEmpty(token))
                        return token;
                }

                string nested = FindContinuation(pair.Value, out int nestedDelay);
                if (!string.IsNullOrEmpty(nested))
                {
                    delayMs = nestedDelay;
                    return nested;
                }
            }
        }

        List<object> list = node as List<object>;
        if (list != null)
        {
            foreach (object item in list)
            {
                string nested = FindContinuation(item, out int nestedDelay);
                if (!string.IsNullOrEmpty(nested))
                {
                    delayMs = nestedDelay;
                    return nested;
                }
            }
        }

        return null;
    }

    private string BuildRequestBody(string clientVersion, string continuation)
    {
        return "{\"context\":{\"client\":{\"clientName\":\"WEB\",\"clientVersion\":\"" +
            EscapeJson(clientVersion) + "\",\"hl\":\"es\",\"gl\":\"AR\"}},\"continuation\":\"" +
            EscapeJson(continuation) + "\"}";
    }

    private string NormalizeHandle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Trim();
        value = value.Replace("https://www.youtube.com/", "").Replace("https://youtube.com/", "");
        value = value.Trim('/');
        if (value.StartsWith("@", StringComparison.Ordinal))
            return value;

        if (value.StartsWith("channel/", StringComparison.OrdinalIgnoreCase))
            return value;

        if (value.StartsWith("UC", StringComparison.Ordinal))
            return "channel/" + value;

        return "@" + value;
    }

    private string MatchGroup(string input, string pattern)
    {
        Match match = Regex.Match(input ?? string.Empty, pattern);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private string GetString(Dictionary<string, object> dictionary, string key)
    {
        object value;
        if (dictionary == null || !dictionary.TryGetValue(key, out value) || value == null)
            return string.Empty;

        return Convert.ToString(value);
    }

    private object GetValue(Dictionary<string, object> dictionary, string key)
    {
        object value;
        return dictionary != null && dictionary.TryGetValue(key, out value) ? value : null;
    }

    private string GetText(object value)
    {
        Dictionary<string, object> dictionary = value as Dictionary<string, object>;
        if (dictionary != null)
        {
            string simpleText = GetString(dictionary, "simpleText");
            if (!string.IsNullOrEmpty(simpleText))
                return simpleText;

            object runsObject = GetValue(dictionary, "runs");
            List<object> runs = runsObject as List<object>;
            if (runs != null)
            {
                StringBuilder builder = new StringBuilder();
                foreach (object runObject in runs)
                {
                    Dictionary<string, object> run = runObject as Dictionary<string, object>;
                    if (run == null)
                        continue;

                    string text = GetString(run, "text");
                    if (string.IsNullOrEmpty(text))
                    {
                        object emojiObject = GetValue(run, "emoji");
                        Dictionary<string, object> emoji = emojiObject as Dictionary<string, object>;
                        if (emoji != null)
                        {
                            List<object> shortcuts = GetValue(emoji, "shortcuts") as List<object>;
                            if (shortcuts != null && shortcuts.Count > 0)
                                text = Convert.ToString(shortcuts[0]);
                        }
                    }

                    builder.Append(text);
                }

                return builder.ToString();
            }

            string directText = GetString(dictionary, "text");
            if (!string.IsNullOrEmpty(directText))
                return directText;
        }

        return value as string ?? string.Empty;
    }

    private string EscapeJson(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private void SetStatus(string message)
    {
        Debug.Log("[YouTube] " + message);
        OnStatus?.Invoke(message);
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    public static class MiniJson
    {
        public static object Parse(string json)
        {
            int index = 0;
            SkipWhitespace(json, ref index);
            return ParseValue(json, ref index);
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length)
                throw new FormatException("JSON incompleto.");

            char current = json[index];
            if (current == '{')
                return ParseObject(json, ref index);
            if (current == '[')
                return ParseArray(json, ref index);
            if (current == '"')
                return ParseString(json, ref index);
            if (current == 't' || current == 'f')
                return ParseBoolean(json, ref index);
            if (current == 'n')
            {
                index += 4;
                return null;
            }

            return ParseNumber(json, ref index);
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            index++;
            SkipWhitespace(json, ref index);

            while (index < json.Length && json[index] != '}')
            {
                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index++] != ':')
                    throw new FormatException("Falta ':' en objeto JSON.");

                result[key] = ParseValue(json, ref index);
                SkipWhitespace(json, ref index);

                if (index < json.Length && json[index] == ',')
                {
                    index++;
                    SkipWhitespace(json, ref index);
                    continue;
                }

                break;
            }

            if (index >= json.Length || json[index++] != '}')
                throw new FormatException("Objeto JSON sin cerrar.");

            return result;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            List<object> result = new List<object>();
            index++;
            SkipWhitespace(json, ref index);

            while (index < json.Length && json[index] != ']')
            {
                result.Add(ParseValue(json, ref index));
                SkipWhitespace(json, ref index);

                if (index < json.Length && json[index] == ',')
                {
                    index++;
                    SkipWhitespace(json, ref index);
                    continue;
                }

                break;
            }

            if (index >= json.Length || json[index++] != ']')
                throw new FormatException("Array JSON sin cerrar.");

            return result;
        }

        private static string ParseString(string json, ref int index)
        {
            if (json[index++] != '"')
                throw new FormatException("Cadena JSON inválida.");

            StringBuilder builder = new StringBuilder();
            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '"')
                    return builder.ToString();

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= json.Length)
                    throw new FormatException("Escape JSON incompleto.");

                char escaped = json[index++];
                switch (escaped)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (index + 4 > json.Length)
                            throw new FormatException("Unicode JSON incompleto.");
                        builder.Append((char)Convert.ToInt32(json.Substring(index, 4), 16));
                        index += 4;
                        break;
                    default: builder.Append(escaped); break;
                }
            }

            throw new FormatException("Cadena JSON sin cerrar.");
        }

        private static object ParseBoolean(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("true", StringComparison.Ordinal))
            {
                index += 4;
                return true;
            }

            if (json.Substring(index).StartsWith("false", StringComparison.Ordinal))
            {
                index += 5;
                return false;
            }

            throw new FormatException("Booleano JSON inválido.");
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            while (index < json.Length && "-+0123456789.eE".IndexOf(json[index]) >= 0)
                index++;

            string value = json.Substring(start, index - start);
            double number;
            if (!double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out number))
                throw new FormatException("Número JSON inválido.");

            return number;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;
        }
    }
}
