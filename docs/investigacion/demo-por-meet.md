# Investigación: demo por Google Meet desde el portátil Windows 11 (14-nov-2026)

## Problema
- La entrevista es por Meet desde un portátil Windows 11.
- La única webcam es la integrada, y Meet la usa para la cara.
- La demo necesita una cámara que enfoque los objetos.

## Opciones
| Opción | Montaje | Fiabilidad | Riesgo el lunes |
|---|---|---|---|
| **A. Demo en el PC Linux con el iPhone como cámara (DroidCam por URL)** | El portátil lleva Meet y la cara. Chrome abre `http://192.168.18.62:PUERTO` y se comparte esa **pestaña** | Alta: cada cámara tiene un solo dueño y YOLO corre en una CPU de 12 hilos | Bajo-medio (LAN o wifi del iPhone); lo cubre el Plan B |
| B. Todo en el portátil Windows | .NET 10 + OpenCvSharp win-x64; webcam compartida o iPhone | Media: Meet ya carga la CPU, posible throttling, entorno nuevo | Medio |
| C. El PC Linux entra también a Meet en **modo Compañero** | Comparte `localhost` desde el PC; ese modo silencia el micro y el audio | Alta | Medio: aparecen dos participantes; si se entra en modo normal, hay eco |
| D. Webcam integrada compartida ("multi-app camera" de Windows 11, 24H2 o posterior) | Interruptor en Configuración > Cámaras > Avanzado | **Sin verificar con OpenCV** | Alto, y la cámara de la cara enseñando una taza queda pobre |

## Recomendación
- **Principal: opción A.**
  - DroidCam (app del iPhone, gratis) sirve MJPEG en `http://IP-iPhone:4747/video`.
  - OpenCV lo abre como URL: no hace falta cámara virtual ni v4l2loopback.
- **Plan B:** la misma app leyendo un vídeo grabado en bucle. Puede activarse solo si se pierde la cámara.
- **Plan C:** compartir desde el PC en modo Compañero, o enseñar un MP4 grabado de la demo completa más el código.

## Pasos de montaje y prueba
1. **Instalar DroidCam en el iPhone.** Comprobar en el PC `ffplay http://<IP-iPhone>:4747/video`.
2. **Reservar la IP** del iPhone en el router (DHCP). Usar la LAN, no Tailscale, para el vídeo.
3. **Abrir el puerto en el PC.** La app escucha en `0.0.0.0:PUERTO` y se abre ufw: `sudo ufw allow from 192.168.18.0/24 to any port PUERTO proto tcp`.
4. **Preparar el iPhone:** en trípode mirando a la mesa, enchufado, sin bloqueo automático y en modo concentración.
5. **Reiniciar el PC antes del lunes.** Está arrancado el kernel 7.3.1 y el instalado es el 7.3.4.
6. **Ensayo el domingo:** una llamada de Meet de prueba de 10 min en las mismas condiciones, arrancando desde cero.

## Compartir en Meet
- **Qué compartir:**
  - Compartir una **pestaña de Chrome**, que es la única opción con vídeo de alta calidad, en lugar de una ventana o la pantalla.
  - Desactivar el audio de la pestaña.
- **Cómo debe verse el stream:**
  - MJPEG a 1280x720, 10-15 FPS, calidad JPEG 70-80.
  - Cajas y etiquetas gruesas, con mucho contraste.
- **Preparación:**
  - Activar No molestar en Windows y cerrar las apps de mensajería.
  - El portátil enchufado.

## Impacto en el plan
- **Fuente unificada `Camera:Source`:**
  - índice → `VideoCapture(index)`;
  - `http`/`rtsp` → FFmpeg;
  - archivo → bucle.
- **Reconexión con backoff** y **vuelta automática al vídeo** con un indicador en el dashboard.
- **Kestrel en `0.0.0.0` con puerto fijo** y la regla de ufw documentada.
- **Windows (si se usara):** MSMF con `OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS=0`. Evitar DSHOW: hay casos de corrupción del heap tras KB5077181.
- **Grabar** `samples/demo.mp4` y un MP4 de la demo completa.

## Sin verificar
- Si OpenCV entra como segundo consumidor de la cámara compartida de Windows.
- Si DroidCam acepta varias conexiones a la vez.
- Si el runtime nativo de OpenCvSharp4 para linux-x64 incluye FFmpeg para abrir URLs. **Probar en M2.**
- Las cifras de FPS y latencia son estimaciones.

## Fuentes
- https://pureinfotech.com/allow-multiple-apps-use-camera-windows-11/
- https://www.windowslatest.com/2026/05/30/microsoft-is-killing-the-one-app-at-a-time-camera-limit-in-windows-11-with-new-multi-app-mode/
- https://droidcam.app/help/
- https://support.google.com/meet/answer/11295507 (modo Compañero)
- https://workspaceupdates.googleblog.com/2020/04/high-quality-video-audio-meet.html
- https://forum.opencv.org/t/videocapture-crashes-on-windows-11-after-kb5077181-update-cap-dshow-heap-corruption-msmf-warnings/24373
