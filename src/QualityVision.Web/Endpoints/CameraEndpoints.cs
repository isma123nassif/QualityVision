using System.Buffers;
using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

using QualityVision.Infrastructure.Camera;
using QualityVision.Web.Pipeline;

namespace QualityVision.Web.Endpoints;

/// <summary>Camera-related HTTP endpoints: MJPEG stream, phone frame upload, status and the phone capture page.</summary>
public static class CameraEndpoints
{
    /// <summary>The phone page and its frame endpoint are served only on the HTTPS endpoint (getUserMedia needs a secure context).</summary>
    private const string HttpsHost = "*:5443";
    private const int MaxFrameBytes = 2 * 1024 * 1024;
    private const string JpegContentType = "image/jpeg";

    private static readonly byte[] CrLf = "\r\n"u8.ToArray();
    private static readonly byte[] JpegMagicBytes = [0xFF, 0xD8, 0xFF];

    public static IEndpointRouteBuilder MapCameraEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/stream", StreamAsync);

        endpoints.MapPost("/api/camera/frame", PostFrameAsync)
            .RequireHost(HttpsHost);

        endpoints.MapGet("/api/camera/status", GetStatus);

        // RequireHost matches the port only; IsHttps additionally guarantees a secure context (404 otherwise).
        endpoints.MapGet("/kamera", (HttpContext context) => context.Request.IsHttps
                ? Results.File("kamera.html", contentType: "text/html; charset=utf-8")
                : Results.NotFound())
            .RequireHost(HttpsHost);

        return endpoints;
    }

    /// <summary>
    /// <c>multipart/x-mixed-replace</c> MJPEG stream of the latest annotated frame. Each client waits on the
    /// broadcaster independently; a slow client skips frames. Ends quietly when the client disconnects.
    /// </summary>
    private static async Task StreamAsync(
        HttpContext context,
        FrameBroadcaster broadcaster,
        IHostApplicationLifetime lifetime)
    {
        // Also end on host shutdown, otherwise open streams would delay application stop.
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted, lifetime.ApplicationStopping);
        CancellationToken ct = linked.Token;
        HttpResponse response = context.Response;
        response.ContentType = "multipart/x-mixed-replace; boundary=frame";
        response.Headers.CacheControl = "no-store";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        long lastSequence = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                (byte[] jpeg, long sequence) = await broadcaster.WaitForNextAsync(lastSequence, ct);
                lastSequence = sequence;

                byte[] partHeader = Encoding.ASCII.GetBytes(string.Create(
                    CultureInfo.InvariantCulture,
                    $"--frame\r\nContent-Type: {JpegContentType}\r\nContent-Length: {jpeg.Length}\r\n\r\n"));

                await response.Body.WriteAsync(partHeader, ct);
                await response.Body.WriteAsync(jpeg, ct);
                await response.Body.WriteAsync(CrLf, ct);
                await response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected or host stopping: normal end of the stream.
        }
    }

    /// <summary>
    /// Receives one JPEG from the phone capture page and hands it to <see cref="PhoneFrameSource"/>.
    /// Decoding happens here; inference does not (the request never waits for the detector).
    /// </summary>
    private static async Task<IResult> PostFrameAsync(
        HttpContext context,
        PhoneFrameSource phoneSource,
        FrameUploadGate gate,
        IOptionsMonitor<CameraOptions> cameraOptions,
        CancellationToken ct)
    {
        HttpRequest request = context.Request;
        if (!request.IsHttps)
        {
            return Results.NotFound();
        }

        IHttpMaxRequestBodySizeFeature? bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false })
        {
            bodyLimit.MaxRequestBodySize = MaxFrameBytes + 1;
        }

        string? mediaType = request.ContentType?.Split(';', 2)[0].Trim();
        if (!string.Equals(mediaType, JpegContentType, StringComparison.OrdinalIgnoreCase))
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType, "Nur image/jpeg wird akzeptiert.");
        }

        if (request.ContentLength > MaxFrameBytes)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "Bild ist größer als 2 MB.");
        }

        if (cameraOptions.CurrentValue.Source != CameraSourceKind.Phone)
        {
            return Problem(StatusCodes.Status409Conflict, "Kamera-Quelle ist nicht auf Handy eingestellt.");
        }

        // Read at most MaxFrameBytes + 1 bytes: enough to detect an oversized body (even without Content-Length)
        // without buffering it. The buffer is pooled because the phone posts several frames per second.
        const int limit = MaxFrameBytes + 1;
        int size = request.ContentLength is > 0 and <= MaxFrameBytes
            ? (int)request.ContentLength.Value + 1
            : limit;

        // At most two uploads are read and decoded at the same time; the rest gets 503 immediately.
        if (!gate.TryEnter())
        {
            return Problem(StatusCodes.Status503ServiceUnavailable, "Kamera-Upload ausgelastet");
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            int total = 0;
            while (true)
            {
                int read = await request.Body.ReadAsync(buffer.AsMemory(total, size - total), ct);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > MaxFrameBytes)
                {
                    return Problem(StatusCodes.Status413PayloadTooLarge, "Bild ist größer als 2 MB.");
                }
            }

            return PushJpeg(phoneSource, buffer.AsSpan(0, total));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            gate.Release();
        }
    }

    private static IResult PushJpeg(PhoneFrameSource phoneSource, ReadOnlySpan<byte> jpeg)
    {
        if (!jpeg.StartsWith(JpegMagicBytes.AsSpan()))
        {
            return Problem(StatusCodes.Status400BadRequest, "Die Daten sind kein JPEG-Bild.");
        }

        // TryPush decodes and queues the frame for the pipeline; it does not wait for detection.
        return phoneSource.TryPush(jpeg)
            ? Results.NoContent()
            : Problem(StatusCodes.Status400BadRequest, "Das JPEG-Bild konnte nicht dekodiert werden.");
    }

    private static IResult GetStatus(ICameraStatus status) =>
        Results.Ok(new
        {
            status.SourceName,
            status.IsConnected,
            status.FpsIn,
            status.LastFrameAt,
            status.LastError,
        });

    // A response body keeps UseStatusCodePagesWithReExecute from replacing API errors with the HTML not-found page.
    private static IResult Problem(int statusCode, string detail) =>
        Results.Problem(detail: detail, statusCode: statusCode);
}
