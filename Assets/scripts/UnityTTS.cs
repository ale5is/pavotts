using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class UnityTTS : MonoBehaviour
{
    // =========================================================
    // CONFIGURACIÓN
    // =========================================================

    [Header("TikTok TTS")]

    [SerializeField]
    private string sessionId = "";

    [SerializeField]
    private string voz = "es_002";

    [SerializeField]
    [Range(0f, 1f)]
    private float volumen = 1f;

    [SerializeField]
    private string apiUrl =
        "https://api16-normal-v6.tiktokv.com/media/api/text/speech/invoke/";


    // =========================================================
    // AUDIO
    // =========================================================

    private AudioSource audioSource;

    private bool hablando = false;

    private Coroutine rutinaActual;


    // =========================================================
    // EVENTO
    // =========================================================

    public event Action OnFinished;


    // =========================================================
    // ESTADO
    // =========================================================

    public bool EstaHablando
    {
        get
        {
            return hablando;
        }
    }


    // =========================================================
    // AWAKE
    // =========================================================

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.volume = volumen;

        // IMPORTANTE:
        // El audio continúa aunque cambies de panel,
        // minimices la UI o desactives otros objetos.
        audioSource.ignoreListenerPause = true;

        Debug.Log("UnityTTS preparado.");
    }


    // =========================================================
    // CONFIGURAR
    // =========================================================

    public void Configurar(
        string nuevaSessionId,
        string nuevaVoz,
        float nuevoVolumen
    )
    {
        sessionId =
            nuevaSessionId != null
                ? nuevaSessionId.Trim()
                : "";

        if (!string.IsNullOrWhiteSpace(nuevaVoz))
        {
            voz = nuevaVoz.Trim();
        }

        volumen =
            Mathf.Clamp01(nuevoVolumen);

        if (audioSource != null)
        {
            audioSource.volume = volumen;
        }

        Debug.Log("UnityTTS configurado.");
    }


    // =========================================================
    // HABLAR
    // =========================================================

    public void Speak(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return;
        }

        texto = LimpiarTexto(texto);

        if (string.IsNullOrWhiteSpace(texto))
        {
            return;
        }

        // Si ya está hablando, NO se bloquea.
        // Detiene el anterior y reproduce el nuevo.
        if (rutinaActual != null)
        {
            StopCoroutine(rutinaActual);
            rutinaActual = null;
        }

        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }

        hablando = true;

        Debug.Log(
            "TTS: " +
            texto
        );

        rutinaActual =
            StartCoroutine(
                GenerarTikTokTTS(texto)
            );
    }


    // =========================================================
    // TIKTOK TTS
    // =========================================================

    private IEnumerator GenerarTikTokTTS(
        string texto
    )
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Debug.LogError(
                "Falta TikTok Session ID."
            );

            Finalizar();

            yield break;
        }


        if (string.IsNullOrWhiteSpace(voz))
        {
            Debug.LogError(
                "Falta la voz de TikTok."
            );

            Finalizar();

            yield break;
        }


        string baseUrl =
            apiUrl.Trim();

        if (!baseUrl.EndsWith("/"))
        {
            baseUrl += "/";
        }


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


        Debug.Log(
            "TikTok TTS solicitado."
        );


        UnityWebRequest request =
            UnityWebRequest.PostWwwForm(
                url,
                ""
            );


        request.downloadHandler =
            new DownloadHandlerBuffer();


        request.SetRequestHeader(
            "User-Agent",
            "com.zhiliaoapp.musically/2022600030 " +
            "(Linux; U; Android 7.1.2; es_ES; " +
            "SM-G988N; Build/NRD90M;tt-ok/3.12.13.1)"
        );


        request.SetRequestHeader(
            "Cookie",
            "sessionid=" +
            sessionId.Trim()
        );


        yield return
            request.SendWebRequest();


        if (
            request.result !=
            UnityWebRequest.Result.Success
        )
        {
            Debug.LogError(
                "Error HTTP TikTok TTS: " +
                request.error
            );

            request.Dispose();

            Finalizar();

            yield break;
        }


        string respuesta =
            request.downloadHandler.text;


        request.Dispose();


        if (string.IsNullOrWhiteSpace(respuesta))
        {
            Debug.LogError(
                "TikTok devolvió una respuesta vacía."
            );

            Finalizar();

            yield break;
        }


        TikTokResponse datos = null;


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
                "Error leyendo respuesta TikTok: " +
                e.Message
            );

            Finalizar();

            yield break;
        }


        if (datos == null)
        {
            Debug.LogError(
                "Respuesta TikTok inválida."
            );

            Finalizar();

            yield break;
        }


        if (datos.status_code != 0)
        {
            Debug.LogError(
                "TikTok rechazó la solicitud."
            );

            Debug.LogError(
                "Status: " +
                datos.status_code
            );

            Debug.LogError(
                "Mensaje: " +
                datos.status_msg
            );

            Finalizar();

            yield break;
        }


        if (
            datos.data == null ||
            string.IsNullOrEmpty(
                datos.data.v_str
            )
        )
        {
            Debug.LogError(
                "TikTok no devolvió audio."
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
                "Error decodificando audio: " +
                e.Message
            );

            Finalizar();

            yield break;
        }


        if (
            audioBytes == null ||
            audioBytes.Length == 0
        )
        {
            Debug.LogError(
                "Audio vacío."
            );

            Finalizar();

            yield break;
        }


        // =====================================================
        // ARCHIVO TEMPORAL
        // =====================================================

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
                "No se pudo guardar MP3: " +
                e.Message
            );

            Finalizar();

            yield break;
        }


        string audioUrl =
            "file://" +
            archivo.Replace(
                "\\",
                "/"
            );


        // =====================================================
        // CARGAR AUDIO
        // =====================================================

        UnityWebRequest audioRequest =
            UnityWebRequestMultimedia.GetAudioClip(
                audioUrl,
                AudioType.MPEG
            );


        yield return
            audioRequest.SendWebRequest();


        if (
            audioRequest.result !=
            UnityWebRequest.Result.Success
        )
        {
            Debug.LogError(
                "Error cargando MP3: " +
                audioRequest.error
            );

            audioRequest.Dispose();

            BorrarArchivo(archivo);

            Finalizar();

            yield break;
        }


        AudioClip clip =
            DownloadHandlerAudioClip.GetContent(
                audioRequest
            );


        audioRequest.Dispose();


        if (clip == null)
        {
            Debug.LogError(
                "AudioClip nulo."
            );

            BorrarArchivo(archivo);

            Finalizar();

            yield break;
        }


        // =====================================================
        // REPRODUCIR
        // =====================================================

        if (audioSource == null)
        {
            audioSource =
                gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.ignoreListenerPause = true;
        }


        audioSource.volume =
            volumen;

        audioSource.clip =
            clip;

        audioSource.Play();


        Debug.Log(
            "TTS reproduciendo."
        );


        // =====================================================
        // ESPERAR HASTA TERMINAR
        // =====================================================

        while (
            audioSource != null &&
            audioSource.isPlaying
        )
        {
            yield return null;
        }


        // =====================================================
        // LIMPIAR
        // =====================================================

        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }


        Destroy(clip);


        BorrarArchivo(
            archivo
        );


        Finalizar();
    }


    // =========================================================
    // PREPARAR TEXTO
    // =========================================================

    private string PrepararTextoTikTok(
        string texto
    )
    {
        if (string.IsNullOrEmpty(texto))
        {
            return "";
        }


        texto =
            texto
                .Replace("+", "plus")
                .Replace("&", "and")
                .Replace("\r", " ")
                .Replace("\n", " ");


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


        return texto.Trim();
    }


    // =========================================================
    // LIMPIAR TEXTO
    // =========================================================

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
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\u200B", "")
                .Replace("\u200C", "")
                .Replace("\u200D", "")
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


    // =========================================================
    // FINALIZAR
    // =========================================================

    private void Finalizar()
    {
        hablando = false;

        rutinaActual = null;

        Debug.Log(
            "TTS terminado."
        );

        OnFinished?.Invoke();
    }


    // =========================================================
    // BORRAR ARCHIVO
    // =========================================================

    private void BorrarArchivo(
        string archivo
    )
    {
        try
        {
            if (
                File.Exists(
                    archivo
                )
            )
            {
                File.Delete(
                    archivo
                );
            }
        }
        catch
        {
        }
    }


    // =========================================================
    // PROBAR
    // =========================================================

    public void Probar()
    {
        Speak(
            "Hola, esta es una prueba usando la voz de TikTok."
        );
    }


    // =========================================================
    // CAMBIAR VOZ
    // =========================================================

    public void CambiarVoz(
        string nuevaVoz
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                nuevaVoz
            )
        )
        {
            return;
        }

        voz =
            nuevaVoz.Trim();
    }


    // =========================================================
    // CAMBIAR VOLUMEN
    // =========================================================

    public void CambiarVolumen(
        float nuevoVolumen
    )
    {
        volumen =
            Mathf.Clamp01(
                nuevoVolumen
            );

        if (audioSource != null)
        {
            audioSource.volume =
                volumen;
        }
    }


    // =========================================================
    // DETENER
    // =========================================================

    public void Stop()
    {
        if (rutinaActual != null)
        {
            StopCoroutine(
                rutinaActual
            );

            rutinaActual = null;
        }


        if (audioSource != null)
        {
            audioSource.Stop();

            AudioClip clip =
                audioSource.clip;

            audioSource.clip = null;

            if (clip != null)
            {
                Destroy(clip);
            }
        }


        hablando = false;


        Debug.Log(
            "TTS detenido."
        );
    }


    // =========================================================
    // JSON TIKTOK
    // =========================================================

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