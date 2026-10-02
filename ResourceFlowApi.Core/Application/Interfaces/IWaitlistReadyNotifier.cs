using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Interfaces;

/// <summary>Tells a waiting guest their resource is ready. Best effort: never throws.</summary>
public interface IWaitlistReadyNotifier
{
    Task NotifyAsync(WaitlistEntry entry, Venue venue);
}
