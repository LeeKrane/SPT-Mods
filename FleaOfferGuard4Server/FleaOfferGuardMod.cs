using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Ragfair;
using SPTarkov.Server.Core.Utils;

namespace FleaOfferGuard4Server;

/// <summary>Thrown by a derived-value check mid add-offer; the AddPlayerOffer finalizer turns it into a normal error response.</summary>
internal sealed class FleaOfferRejectedException(string reason) : Exception(reason);

[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class FleaOfferGuardMod(
    ILogger<FleaOfferGuardMod> logger,
    HttpResponseUtil httpResponseUtil,
    EventOutputHolder eventOutputHolder,
    RagfairOfferService ragfairOfferService,
    RagfairTaxService ragfairTaxService) : IOnLoad
{
    internal static FleaOfferGuardMod? Instance;
    internal static ILogger? Log => Instance?.logger;

    private readonly ILogger logger = logger;
    private readonly HttpResponseUtil httpResponseUtil = httpResponseUtil;
    private readonly EventOutputHolder eventOutputHolder = eventOutputHolder;
    private readonly RagfairOfferService ragfairOfferService = ragfairOfferService;
    private readonly RagfairTaxService ragfairTaxService = ragfairTaxService;

    private const string Id = "krane.fleaofferguardserver";

    /// <summary>True while a guarded RagfairController call (AddPlayerOffer, ExtendOffer) runs on this thread; both are synchronous.</summary>
    [ThreadStatic] internal static bool InGuarded;

    /// <summary>Stack counts AddPlayerOffer's MULTI/PACK paths overwrite on live profile items before the checks that can reject.</summary>
    [ThreadStatic] private static List<(Item Item, Upd? Upd, double? Count)>? stackSnapshot;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var harmony = new Harmony(Id);
        try
        {
            Instance = this;
            harmony.PatchAll(typeof(FleaOfferGuardMod).Assembly);
            logger.LogInformation("[FleaOfferGuard] Player flea offers with invalid prices are now rejected");
        }
        catch (Exception e)
        {
            Instance = null;
            try { harmony.UnpatchSelf(); } catch { /* nothing more to undo */ }
            logger.LogWarning("[FleaOfferGuard] Patching failed, guard not active: {Error}", e);
        }
        return Task.CompletedTask;
    }

    /// <summary>Count must be a real number whose 2-decimal rounding (what the offer stores) is above zero.</summary>
    internal static bool IsValidCount(double? count) =>
        count is { } c && double.IsFinite(c) && Math.Round(c, 2) > 0;

    /// <summary>Normal client error response; null when the guard is not loaded (callers then let the request through).</summary>
    internal static ItemEventRouterResponse? Reject(MongoId sessionId, string message)
    {
        var self = Instance;
        return self?.httpResponseUtil.AppendErrorToOutput(self.eventOutputHolder.GetOutput(sessionId), message);
    }

    internal static void ClearStoredFee(MongoId? offerId)
    {
        if (offerId is { } id) Instance?.ragfairTaxService.ClearStoredOfferTaxById(id);
    }

    internal static void SnapshotStacks(PmcData pmcData, AddOfferRequestData request)
    {
        stackSnapshot = null;
        var ids = request.Items?.ToHashSet();
        if (ids == null || pmcData.Inventory?.Items == null) return;
        stackSnapshot = pmcData.Inventory.Items.Where(i => ids.Contains(i.Id)).Select(i => (i, i.Upd, i.Upd?.StackObjectsCount)).ToList();
    }

    internal static void DropSnapshot() => stackSnapshot = null;

    internal static void RestoreStacks()
    {
        if (stackSnapshot == null) return;
        foreach (var (item, upd, count) in stackSnapshot)
        {
            if (upd == null) item.Upd = null;
            else upd.StackObjectsCount = count;
        }
        stackSnapshot = null;
    }

    /// <summary>Ids of flea offers created during the guarded call (see CreateOfferPatch).</summary>
    [ThreadStatic] internal static List<MongoId>? CreatedOffers;

    /// <summary>Drop the flea offers the aborted request already put into the in-memory offer table (never in the profile).</summary>
    internal static void RemoveOrphanOffers(PmcData pmcData)
    {
        try
        {
            if (CreatedOffers == null) return;
            var inProfile = pmcData.RagfairInfo?.Offers?.Select(o => o.Id).ToHashSet() ?? [];
            foreach (var id in CreatedOffers.Where(id => !inProfile.Contains(id)))
                Instance?.ragfairOfferService.RemoveOfferById(id);
        }
        catch (Exception e)
        {
            Log?.LogWarning("[FleaOfferGuard] Could not clean up the rejected offer: {Error}", e.Message);
        }
    }

    internal static string Describe(PmcData pmcData, AddOfferRequestData request)
    {
        var id = request.Items?.FirstOrDefault();
        var tpl = id == null ? null : pmcData.Inventory?.Items?.FirstOrDefault(i => i.Id == id)?.Template.ToString();
        return $"item {tpl ?? "unknown"} ({request.Items?.Count ?? 0} stack(s))";
    }
}

/// <summary>
/// Entry point of every player flea listing (RagfairCallbacks.AddOffer -> RagfairController.AddPlayerOffer).
/// Prefix rejects bad requirement counts before anything is read or changed. Finalizer converts the
/// derived-value rejections below (thrown mid-flow, still before the profile is touched) into the same error.
/// </summary>
[HarmonyPatch(typeof(RagfairController), nameof(RagfairController.AddPlayerOffer))]
internal static class AddPlayerOfferPatch
{
    private static bool Prefix(PmcData pmcData, AddOfferRequestData offerRequest, MongoId sessionID, ref ItemEventRouterResponse __result)
    {
        FleaOfferGuardMod.InGuarded = true;
        FleaOfferGuardMod.CreatedOffers = [];
        try
        {
            // A missing or empty requirement list is a price of nothing; it breaks the tax math.
            var requirements = offerRequest.Requirements;
            var bad = requirements == null || requirements.Count == 0
                ? null
                : requirements.FirstOrDefault(r => !FleaOfferGuardMod.IsValidCount(r.Count));
            if (requirements == null || requirements.Count == 0 || bad != null)
            {
                var response = FleaOfferGuardMod.Reject(sessionID, "Unable to add offer: invalid price");
                if (response != null)
                {
                    FleaOfferGuardMod.Log?.LogWarning(
                        "[FleaOfferGuard] Rejected flea offer from session {Session}: {Item}, {Why}",
                        sessionID, FleaOfferGuardMod.Describe(pmcData, offerRequest),
                        bad == null ? "no requirements" : $"requirement {bad.Template} count {bad.Count?.ToString("R") ?? "null"} is not a positive finite number");
                    FleaOfferGuardMod.InGuarded = false;
                    __result = response;
                    return false;
                }
            }
            FleaOfferGuardMod.SnapshotStacks(pmcData, offerRequest);
        }
        catch (Exception e)
        {
            // Never block a listing because the guard itself failed.
            FleaOfferGuardMod.Log?.LogWarning("[FleaOfferGuard] Guard check failed, letting the request through: {Error}", e.Message);
        }
        return true;
    }

    private static Exception? Finalizer(Exception? __exception, PmcData pmcData, AddOfferRequestData offerRequest, MongoId sessionID, ref ItemEventRouterResponse __result)
    {
        FleaOfferGuardMod.InGuarded = false;
        try
        {
            string? reason = null;
            if (__exception is FleaOfferRejectedException rejected) reason = rejected.Message;
            else if (__exception == null && __result?.Warnings is { Count: > 0 }) reason = "listing failed (for example the fee could not be paid)";
            if (reason == null) return __exception;

            ItemEventRouterResponse? response = __result;
            if (__exception != null)
            {
                response = FleaOfferGuardMod.Reject(sessionID, "Unable to add offer: invalid price");
                if (response == null) return __exception;
                FleaOfferGuardMod.Log?.LogWarning(
                    "[FleaOfferGuard] Rejected flea offer from session {Session}: {Item}: {Reason}",
                    sessionID, FleaOfferGuardMod.Describe(pmcData, offerRequest), reason);
            }
            // Any failed listing: multi and pack listings had already rewritten the live stack counts, and
            // the flea offer had already been created in memory (vanilla returns early on an unpaid fee).
            FleaOfferGuardMod.RestoreStacks();
            FleaOfferGuardMod.RemoveOrphanOffers(pmcData);
            __result = response!;
            return null;
        }
        finally
        {
            FleaOfferGuardMod.DropSnapshot();
            FleaOfferGuardMod.CreatedOffers = null;
        }
    }
}

/// <summary>Remembers the id of every flea offer created while a listing is guarded, so a failed listing can remove exactly those.</summary>
[HarmonyPatch(typeof(RagfairOfferGenerator), nameof(RagfairOfferGenerator.CreateAndAddFleaOffer))]
internal static class CreateOfferPatch
{
    private static void Postfix(RagfairOffer __result)
    {
        if (FleaOfferGuardMod.InGuarded) FleaOfferGuardMod.CreatedOffers?.Add(__result.Id);
    }
}

/// <summary>
/// Renewing an offer charges CalculateTax too. The tax is computed and paid before the offer changes,
/// so a rejection here leaves the profile untouched.
/// </summary>
[HarmonyPatch(typeof(RagfairController), nameof(RagfairController.ExtendOffer))]
internal static class ExtendOfferPatch
{
    private static void Prefix() => FleaOfferGuardMod.InGuarded = true;

    private static Exception? Finalizer(Exception? __exception, ExtendOfferRequestData extendRequest, MongoId sessionId, ref ItemEventRouterResponse __result)
    {
        FleaOfferGuardMod.InGuarded = false;
        if (__exception is not FleaOfferRejectedException rejected) return __exception;
        var response = FleaOfferGuardMod.Reject(sessionId, "Unable to extend offer: invalid price");
        if (response == null) return __exception;
        FleaOfferGuardMod.Log?.LogWarning(
            "[FleaOfferGuard] Rejected renewal of offer {Offer} from session {Session}: {Reason}",
            extendRequest.OfferId, sessionId, rejected.Message);
        __result = response;
        return null;
    }
}

/// <summary>Server-side listing tax is NaN/Infinity when the item's flea price or the listed price is zero or negative; it would be charged to the profile.</summary>
[HarmonyPatch(typeof(RagfairTaxService), nameof(RagfairTaxService.CalculateTax))]
internal static class CalculateTaxPatch
{
    private static void Postfix(double? requirementsValue, double __result)
    {
        if (!FleaOfferGuardMod.InGuarded || (double.IsFinite(__result) && __result >= 0)) return;
        throw new FleaOfferRejectedException($"listing tax is {__result} (requirements value {requirementsValue})");
    }
}

/// <summary>The client reports its computed listing fee ahead of the offer and the server charges it. A missing, non-finite or negative fee is dropped (and any older cached one for the item cleared) so the server computes the tax itself.</summary>
[HarmonyPatch(typeof(RagfairTaxService), nameof(RagfairTaxService.StoreClientOfferTaxValue))]
internal static class StoreClientOfferTaxValuePatch
{
    private static bool Prefix(MongoId sessionId, StorePlayerOfferTaxAmountRequestData offer)
    {
        if (offer.Fee is { } fee && double.IsFinite(fee) && fee >= 0) return true;
        FleaOfferGuardMod.Log?.LogWarning(
            "[FleaOfferGuard] Ignored client listing fee {Fee} from session {Session} for item {Tpl}",
            offer.Fee?.ToString("R") ?? "null", sessionId, offer.Tpl);
        FleaOfferGuardMod.ClearStoredFee(offer.Id);
        return false;
    }
}
