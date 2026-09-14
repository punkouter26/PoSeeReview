namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Service for overlaying readable text onto comic images.
/// Image models render lettering as decoration, so every caption this app ships is drawn here.
/// <para>
/// Taking the captions as an argument rather than a narrative is the whole point: this service
/// used to own a <see cref="IChatCompletionService"/> and call it, which made a drawing step
/// depend on a model call and put that call on the critical path after the image existed. It is
/// now purely presentational — text in, pixels out — and the model lives in exactly one place.
/// </para>
/// </summary>
public interface IComicTextOverlayService
{
    /// <summary>
    /// Draws one caption box per panel onto the comic image.
    /// </summary>
    /// <param name="imageBytes">Original comic image</param>
    /// <param name="captions">One caption per panel, in panel order</param>
    /// <param name="panelCount">Number of panels (1-4)</param>
    /// <param name="cancellationToken">Cancels the work if the caller abandons the request</param>
    /// <returns>Modified image with text overlay</returns>
    Task<byte[]> AddTextOverlayAsync(byte[] imageBytes, IReadOnlyList<string> captions, int panelCount, CancellationToken cancellationToken = default);
}
