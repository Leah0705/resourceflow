using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

public class MediaService(
    IBrandSettingsRepository brandRepository,
    IVenueRepository venueRepository,
    IMediaStore media,
    IAuditScope? audit = null)
{
    /// <summary>
    /// Castle's generated proxy constructors drop default values, so a Moq class mock reaches only
    /// a constructor of exactly matching arity. This is that constructor.
    /// </summary>
    public MediaService(
        IBrandSettingsRepository brand,
        IVenueRepository venues,
        IMediaStore mediaStore)
        : this(brand, venues, mediaStore, null) { }

    private readonly IAuditScope _audit = audit ?? NullAuditScope.Instance;
    private readonly IBrandSettingsRepository _brandRepository = brandRepository;
    private readonly IVenueRepository _venueRepository = venueRepository;
    private readonly IMediaStore _media = media;

    public virtual async Task<string> UploadHeroAsync(Stream fileStream, string contentType)
    {
        string filename = await _media.ReplaceAsync(HeroSlot, GetExtension(contentType), fileStream);

        string url = $"/media/{filename}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        BrandSettings? brand = await _brandRepository.GetAsync();
        bool isNew = false;
        if (brand == null)
        {
            brand = new BrandSettings();
            isNew = true;
        }
        brand.HeaderImageUrl = url;

        if (isNew)
        {
            await _brandRepository.AddAsync(brand);
        }
        else
        {
            await _brandRepository.SaveChangesAsync();
        }

        DescribeMedia(AuditActions.MediaUpload, HeroSlot, "Homepage header image", null,
            "Uploaded a new homepage header image");
        return url;
    }

    public virtual async Task DeleteHeroAsync()
    {
        BrandSettings? brand = await _brandRepository.GetAsync();
        if (brand?.HeaderImageUrl != null)
        {
            // Clear the persisted reference first so a missing/invalid physical file
            // never blocks removal. Best-effort deletion of the file on disk.
            string url = brand.HeaderImageUrl;
            brand.HeaderImageUrl = null;
            await _brandRepository.SaveChangesAsync();
            TryDeleteFile(url);

            DescribeMedia(AuditActions.MediaDelete, HeroSlot, "Homepage header image", null,
                "Removed the homepage header image");
        }
    }

    public virtual async Task<string?> UploadLocationAsync(int id, Stream fileStream, string contentType)
    {
        Venue? venue = await _venueRepository.FindByIdAsync(id);
        if (venue == null) return null;

        string filename = await _media.ReplaceAsync(LocationSlot(id), GetExtension(contentType), fileStream);

        string url = $"/media/{filename}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        venue.ImageUrl = url;
        await _venueRepository.SaveChangesAsync();

        DescribeMedia(AuditActions.MediaUpload, LocationSlot(id), venue.Name, id,
            $"Uploaded a new photo for {venue.Name}");
        return url;
    }

    public virtual async Task<bool> DeleteLocationAsync(int id)
    {
        Venue? venue = await _venueRepository.FindByIdAsync(id);
        if (venue == null) return false;
        if (venue.ImageUrl != null)
        {
            // Clear the persisted reference first so a missing/invalid physical file
            // never blocks removal. Best-effort deletion of the file on disk.
            string url = venue.ImageUrl;
            venue.ImageUrl = null;
            await _venueRepository.SaveChangesAsync();
            TryDeleteFile(url);

            DescribeMedia(AuditActions.MediaDelete, LocationSlot(id), venue.Name, id,
                $"Removed the photo for {venue.Name}");
        }
        return true;
    }

    public virtual async Task<string?> UploadGuideAsync(int id, Stream fileStream)
    {
        Venue? venue = await _venueRepository.FindByIdAsync(id);
        if (venue == null) return null;

        // Only the instance-served guide file occupies the guide-<id> slot, so any
        // previous upload (regardless of extension) is cleared before writing the
        // new one. External links live in the same GuideUrl column; if the previous
        // value was a link rather than a served file, there is no file to clear and
        // the link is simply replaced.
        string filename = await _media.ReplaceAsync(GuideSlot(id), "pdf", fileStream);

        string url = $"/media/{filename}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        venue.GuideUrl = url;
        await _venueRepository.SaveChangesAsync();

        DescribeMedia(AuditActions.MediaUpload, GuideSlot(id), $"{venue.Name} guide", id,
            $"Uploaded a new guide for {venue.Name}");
        return url;
    }

    public virtual async Task<bool> DeleteGuideAsync(int id)
    {
        Venue? venue = await _venueRepository.FindByIdAsync(id);
        if (venue == null) return false;
        if (venue.GuideUrl != null)
        {
            // Clear the persisted reference first so a missing/invalid physical file
            // never blocks removal. Best-effort deletion of the file on disk.
            string url = venue.GuideUrl;
            venue.GuideUrl = null;
            await _venueRepository.SaveChangesAsync();
            TryDeleteFile(url);

            DescribeMedia(AuditActions.MediaDelete, GuideSlot(id), $"{venue.Name} guide", id,
                $"Removed the guide for {venue.Name}");
        }
        return true;
    }

    /// <summary>The <see cref="IMediaStore"/> slots an upload writes into, also used as audit target ids.</summary>
    private const string HeroSlot = "hero";
    private static string LocationSlot(int venueId) => $"location-{venueId}";
    private static string GuideSlot(int venueId) => $"guide-{venueId}";

    private void DescribeMedia(string action, string slot, string label, int? venueId, string summary)
        => _audit.Describe(action, AuditTargets.Media, slot, label, venueId, summary);

    /// <summary>
    /// Best-effort deletion of the file a stored <c>/media/…?v=…</c> URL points at, so that
    /// removing an image always succeeds even when the stored URL is invalid or the file has
    /// already been deleted.
    /// </summary>
    private void TryDeleteFile(string url)
        => _media.TryDelete(url.Contains('?') ? url[..url.IndexOf('?')] : url);

    private static string GetExtension(string contentType) => contentType switch
    {
        "image/jpeg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        _ => "bin"
    };
}
