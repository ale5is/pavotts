using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class WindowManager : MonoBehaviour
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowLongPtr(
        IntPtr hWnd,
        int nIndex
    );

    [DllImport("user32.dll")]
    private static extern int SetWindowLongPtr(
        IntPtr hWnd,
        int nIndex,
        int dwNewLong
    );

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags
    );

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(
        IntPtr hWnd,
        int nCmdShow
    );

    private const int GWL_STYLE = -16;

    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SYSMENU = 0x00080000;

    private const int SW_RESTORE = 9;
    private const int SW_MAXIMIZE = 3;
    private const int SW_MINIMIZE = 6;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private IntPtr ventana;

#endif

    [Header("PRIMERA EJECUCIÓN")]

    [SerializeField]
    private int anchoInicial = 1280;

    [SerializeField]
    private int altoInicial = 720;

    [Header("TAMAÑO MÍNIMO")]

    [SerializeField]
    private int anchoMinimo = 800;

    [SerializeField]
    private int altoMinimo = 450;


    private void Awake()
    {
        Application.runInBackground = true;

        AudioListener.pause = false;
    }


    private void Start()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

        Invoke(
            nameof(ConfigurarVentana),
            1.0f
        );

#endif
    }


#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

    private void ConfigurarVentana()
    {
        ventana = GetActiveWindow();

        if (ventana == IntPtr.Zero)
        {
            return;
        }

        // =====================================================
        // SOLO LA PRIMERA VEZ
        // =====================================================

        bool primeraVez =
            !PlayerPrefs.HasKey(
                "WindowManager_PrimeraVez"
            );

        if (primeraVez)
        {
            Screen.fullScreenMode =
                FullScreenMode.Windowed;

            Screen.fullScreen = false;

            Screen.SetResolution(
                Mathf.Max(
                    anchoInicial,
                    anchoMinimo
                ),
                Mathf.Max(
                    altoInicial,
                    altoMinimo
                ),
                FullScreenMode.Windowed
            );

            PlayerPrefs.SetInt(
                "WindowManager_PrimeraVez",
                1
            );

            PlayerPrefs.Save();
        }


        // =====================================================
        // CONFIGURAR VENTANA WINDOWS
        // =====================================================

        int estilo =
            GetWindowLongPtr(
                ventana,
                GWL_STYLE
            );

        estilo |= WS_CAPTION;
        estilo |= WS_THICKFRAME;
        estilo |= WS_MINIMIZEBOX;
        estilo |= WS_MAXIMIZEBOX;
        estilo |= WS_SYSMENU;

        SetWindowLongPtr(
            ventana,
            GWL_STYLE,
            estilo
        );


        // =====================================================
        // ACTUALIZAR MARCO
        // =====================================================

        SetWindowPos(
            ventana,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SWP_NOMOVE |
            SWP_NOSIZE |
            SWP_NOZORDER |
            SWP_FRAMECHANGED
        );


        ShowWindow(
            ventana,
            SW_RESTORE
        );
    }

#endif


    public void Minimizar()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

        ObtenerVentana();

        if (ventana != IntPtr.Zero)
        {
            ShowWindow(
                ventana,
                SW_MINIMIZE
            );
        }

#endif
    }


    public void Maximizar()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

        ObtenerVentana();

        if (ventana != IntPtr.Zero)
        {
            ShowWindow(
                ventana,
                SW_MAXIMIZE
            );
        }

#endif
    }


    public void Restaurar()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

        ObtenerVentana();

        if (ventana != IntPtr.Zero)
        {
            ShowWindow(
                ventana,
                SW_RESTORE
            );
        }

#endif
    }


    public void CambiarTamano(
        int ancho,
        int alto
    )
    {
        ancho = Mathf.Max(
            ancho,
            anchoMinimo
        );

        alto = Mathf.Max(
            alto,
            altoMinimo
        );

        Screen.SetResolution(
            ancho,
            alto,
            FullScreenMode.Windowed
        );
    }


    public void Tamano720p()
    {
        CambiarTamano(
            1280,
            720
        );
    }


    public void Tamano900p()
    {
        CambiarTamano(
            1600,
            900
        );
    }


    public void Tamano1080p()
    {
        CambiarTamano(
            1920,
            1080
        );
    }


    public void AlternarPantallaCompleta()
    {
        if (
            Screen.fullScreenMode ==
            FullScreenMode.Windowed
        )
        {
            Screen.fullScreenMode =
                FullScreenMode.FullScreenWindow;
        }
        else
        {
            Screen.fullScreenMode =
                FullScreenMode.Windowed;
        }
    }


    public void CerrarJuego()
    {
        Application.Quit();
    }


#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

    private void ObtenerVentana()
    {
        if (ventana == IntPtr.Zero)
        {
            ventana = GetActiveWindow();
        }
    }

#endif
}