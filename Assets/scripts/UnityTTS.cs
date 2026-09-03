using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class UnityTTS : MonoBehaviour
{
    [Header("TikTok TTS")]
    [SerializeField] private string sessionId = "";
    [SerializeField] private string voz = "es_002";

    [SerializeField]
    [Range(0f, 1f)]
    private float volumen = 1f;

    [SerializeField]
    private string apiUrl =
        "https://api16-normal-v6.tiktokv.com/media/api/text/speech/invoke/";

    private AudioSource audioSource;
    private Coroutine rutinaActual;
    private bool hablando;

    public event Action OnFinished;

    public bool EstaHablando
    {
        get { return hablando; }
    }

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.volume = volumen;
        audioSource.ignoreListenerPause = true;
    }

    public void Configurar(
        string nuevaSessionId,
        string nuevaVoz,
        float nuevoVolumen)
    {
        sessionId = nuevaSessionId != null
            ? nuevaSessionId.Trim()
            : "";

        if (!string.IsNullOrWhiteSpace(nuevaVoz))
            voz = nuevaVoz.Trim();

        volumen = Mathf.Clamp01(nuevoVolumen);

        if (audioSource != null)
            audioSource.volume = volumen;
    }

    public void Speak(string texto)
    {
        texto = LimpiarTexto(texto);

        if (string.IsNullOrEmpty(texto))
            return;

        CancelarRutina();

        if (audioSource != null)
        {
            audioSource.Stop();

            if (audioSource.clip != null)
            {
                Destroy(audioSource.clip);
                audioSource.clip = null;
            }
        }

        hablando = true;

        rutinaActual = StartCoroutine(
            GenerarTikTokTTS(texto)
        );
    }

    private IEnumerator GenerarTikTokTTS(string texto)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Debug.LogError("❌ Falta TikTok Session ID.");
            Finalizar();
            yield break;
        }

        if (string.IsNullOrWhiteSpace(voz))
        {
            Debug.LogError("❌ Falta la voz de TikTok.");
            Finalizar();
            yield break;
        }

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            Debug.LogError("❌ Falta la URL del TTS.");
            Finalizar();
            yield break;
        }

        string baseUrl = apiUrl.Trim();

        if (!baseUrl.EndsWith("/"))
            baseUrl += "/";

        string textoPreparado =
            PrepararTextoTikTok(texto);

        string url =
            baseUrl +
            "?text_speaker=" +
            UnityWebRequest.EscapeURL(voz) +
            "&req_text=" +
            UnityWebRequest.EscapeURL(textoPreparado) +
            "&speaker_map_type=0" +
            "&aid=1233";

        using (UnityWebRequest request =
               UnityWebRequest.PostWwwForm(url, ""))
        {
            request.downloadHandler =
                new DownloadHandlerBuffer();

            request.timeout = 30;

            request.SetRequestHeader(
                "User-Agent",
                "com.zhiliaoapp.musically/2022600030 " +
                "(Linux; U; Android 7.1.2; es_ES; " +
                "SM-G988N; Build/NRD90M;tt-ok/3.12.13.1)"
            );

            request.SetRequestHeader(
                "Cookie",
                "sessionid=" + sessionId.Trim()
            );

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    "❌ Error HTTP TikTok TTS: " +
                    request.error
                );

                Finalizar();
                yield break;
            }

            string respuesta =
                request.downloadHandler.text;

            if (string.IsNullOrWhiteSpace(respuesta))
            {
                Debug.LogError(
                    "❌ TikTok devolvió una respuesta vacía."
                );

                Finalizar();
                yield break;
            }

            TikTokResponse datos;

            try
            {
                datos =
                    JsonUtility.FromJson<TikTokResponse>(
                        respuesta
                    );
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "❌ Error leyendo respuesta TikTok: " +
                    e.Message
                );

                Finalizar();
                yield break;
            }

            if (datos == null)
            {
                Debug.LogError(
                    "❌ Respuesta TikTok inválida."
                );

                Finalizar();
                yield break;
            }

            if (datos.status_code != 0)
            {
                Debug.LogError(
                    "❌ TikTok rechazó la solicitud. " +
                    "Status: " +
                    datos.status_code +
                    " - " +
                    datos.status_msg
                );

                Finalizar();
                yield break;
            }

            if (datos.data == null ||
                string.IsNullOrEmpty(datos.data.v_str))
            {
                Debug.LogError(
                    "❌ TikTok no devolvió audio."
                );

                Finalizar();
                yield break;
            }

            byte[] audioBytes;

            try
            {
                audioBytes =
                    Convert.FromBase64String(
                        datos.data.v_str
                    );
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "❌ Error decodificando audio: " +
                    e.Message
                );

                Finalizar();
                yield break;
            }

            if (audioBytes.Length == 0)
            {
                Debug.LogError("❌ Audio vacío.");
                Finalizar();
                yield break;
            }

            string archivo =
                Path.Combine(
                    Application.temporaryCachePath,
                    "tiktok_tts_" +
                    Guid.NewGuid().ToString("N") +
                    ".mp3"
                );

            try
            {
                File.WriteAllBytes(
                    archivo,
                    audioBytes
                );
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "❌ No se pudo guardar MP3: " +
                    e.Message
                );

                Finalizar();
                yield break;
            }

            yield return StartCoroutine(
                ReproducirArchivo(
                    archivo
                )
            );
        }
    }

    private IEnumerator ReproducirArchivo(
        string archivo)
    {
        string audioUrl =
            "file://" +
            archivo.Replace("\\", "/");

        using (UnityWebRequest request =
               UnityWebRequestMultimedia.GetAudioClip(
                   audioUrl,
                   AudioType.MPEG))
        {
            request.timeout = 30;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    "❌ Error cargando MP3: " +
                    request.error
                );

                BorrarArchivo(archivo);
                Finalizar();
                yield break;
            }

            AudioClip clip =
                DownloadHandlerAudioClip.GetContent(
                    request
                );

            if (clip == null)
            {
                Debug.LogError(
                    "❌ AudioClip nulo."
                );

                BorrarArchivo(archivo);
                Finalizar();
                yield break;
            }

            if (audioSource == null)
            {
                audioSource =
                    gameObject.AddComponent<AudioSource>();

                audioSource.playOnAwake = false;
                audioSource.loop = false;
                audioSource.ignoreListenerPause = true;
            }

            audioSource.volume = volumen;
            audioSource.clip = clip;
            audioSource.Play();

            while (audioSource != null &&
                   audioSource.isPlaying)
            {
                yield return null;
            }

            if (audioSource != null)
            {
                audioSource.Stop();
                audioSource.clip = null;
            }

            Destroy(clip);
            BorrarArchivo(archivo);

            Finalizar();
        }
    }

    private string PrepararTextoTikTok(string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return "";

        texto = texto
            .Replace("+", "plus")
            .Replace("&", "and")
            .Replace("\r", " ")
            .Replace("\n", " ");

        while (texto.Contains("  "))
            texto = texto.Replace("  ", " ");

        return texto.Trim();
    }

    private string LimpiarTexto(string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return "";

        texto = texto
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Replace("\u200B", "")
            .Replace("\u200C", "")
            .Replace("\u200D", "")
            .Replace("\uFEFF", "");

        while (texto.Contains("  "))
            texto = texto.Replace("  ", " ");

        return texto.Trim();
    }

    private void Finalizar()
    {
        hablando = false;
        rutinaActual = null;

        OnFinished?.Invoke();
    }

    private void CancelarRutina()
    {
        if (rutinaActual != null)
        {
            StopCoroutine(rutinaActual);
            rutinaActual = null;
        }
    }

    private void BorrarArchivo(string archivo)
    {
        try
        {
            if (File.Exists(archivo))
                File.Delete(archivo);
        }
        catch
        {
            // No interrumpir el TTS por un archivo temporal bloqueado.
        }
    }

    public void Probar()
    {
        Speak(
            "Hola, esta es una prueba usando la voz de TikTok."
        );
    }

    public void CambiarVoz(string nuevaVoz)
    {
        if (string.IsNullOrWhiteSpace(nuevaVoz))
            return;

        voz = nuevaVoz.Trim();
    }

    public void CambiarVolumen(float nuevoVolumen)
    {
        volumen = Mathf.Clamp01(nuevoVolumen);

        if (audioSource != null)
            audioSource.volume = volumen;
    }

    public void Stop()
    {
        CancelarRutina();

        if (audioSource != null)
        {
            audioSource.Stop();

            if (audioSource.clip != null)
            {
                Destroy(audioSource.clip);
                audioSource.clip = null;
            }
        }

        hablando = false;
    }

    [Serializable]
    private class TikTokResponse
    {
        public int status_code;
        public string status_msg;
        public TikTokData data;
    }

    [Serializable]
    private class TikTokData
    {
        public string v_str;
        public string duration;
        public string speaker;
    }
}