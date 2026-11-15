# Spike: Cámara del móvil → PC (baja latencia)

Sustituye DroidCam: el iPhone abre una web en Safari, captura la cámara trasera y envía frames JPEG al PC.

## Certificado (HTTPS)

iOS exige HTTPS para `getUserMedia` (la cámara). El certificado se genera con openssl en `certs/` (esa carpeta está en `.gitignore`, no se sube al repositorio):

```bash
mkdir -p spikes/phone-cam/certs
openssl req -x509 -newkey rsa:2048 -nodes -days 365 \
  -keyout spikes/phone-cam/certs/key.pem -out spikes/phone-cam/certs/cert.pem \
  -subj "/CN=192.168.18.62" -addext "subjectAltName=IP:192.168.18.62,IP:127.0.0.1"
```

## Ejecutar

```bash
cd spikes/phone-cam
python3 server.py --host 0.0.0.0 --port 8490 --cert certs/cert.pem --key certs/key.pem
```

Opciones: `--port` (por defecto 8490), `--host` (por defecto `127.0.0.1`), `--cert` y `--key` (si se indican ambos, el servidor usa HTTPS).

## Probar

1. iPhone: abrir `https://192.168.18.62:8490/`. Como el certificado es autofirmado, Safari mostrará una advertencia: pulsar "Mostrar detalles" → "visitar este sitio web" → confirmar. Después pulsar "Kamera starten" y permitir la cámara.
2. PC: abrir `https://192.168.18.62:8490/view` (o `https://127.0.0.1:8490/view`) para ver la imagen en vivo con FPS y edad del frame. También aparecerá la advertencia del certificado.
3. Otros endpoints: `/stats` (JSON), `/stream` (MJPEG).

Prueba sin móvil (con `curl -k` porque el certificado es autofirmado):

```bash
ffmpeg -f lavfi -i testsrc=size=640x360 -frames:v 1 /tmp/qv-test.jpg -y
curl -sk -X POST -H 'Content-Type: image/jpeg' --data-binary @/tmp/qv-test.jpg https://127.0.0.1:8490/frame
curl -sk https://127.0.0.1:8490/stats
```

El servidor imprime una línea por segundo: fps de entrada, KB medios por frame, visores.
Ajustes en el móvil: resolución, calidad JPEG, FPS máx. Solo hay una petición en vuelo a la vez (sin latencia acumulada).
