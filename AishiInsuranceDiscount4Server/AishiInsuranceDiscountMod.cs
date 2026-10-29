using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aishi;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;

namespace AishiInsuranceDiscount4Server;

/// <summary>Raw config; DiscountPercent is a JsonElement so a wrong type is reported instead of failing the parse.</summary>
public sealed record DiscountConfig
{
    [JsonPropertyName("DiscountPercent")]
    public JsonElement? DiscountPercent { get; init; }
}

[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class AishiInsuranceDiscountMod(ILogger<AishiInsuranceDiscountMod> logger, ModHelper modHelper) : IOnLoad
{
    private const double DefaultDiscountPercent = 75;
    private const double MaxDiscountPercent = 99;

    /// <summary>Price multiplier applied to Aishi insurance (1 - discount/100). Set in OnLoad.</summary>
    internal static double Multiplier = 1;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var percent = LoadDiscountPercent();
            Multiplier = 1 - percent / 100;
            new Harmony("krane.aishiinsurancediscount").PatchAll(typeof(AishiInsuranceDiscountMod).Assembly);
            logger.LogInformation("[AishiInsuranceDiscount4Server] Aishi insurance {Percent}% cheaper", percent);
        }
        catch (Exception e)
        {
            Multiplier = 1;
            logger.LogWarning("[AishiInsuranceDiscount4Server] Patching failed, discount not applied: {Error}", e);
        }
        return Task.CompletedTask;
    }

    private double LoadDiscountPercent()
    {
        double value;
        try
        {
            var folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            var raw = modHelper.GetJsonDataFromFile<DiscountConfig>(folder, "config.json")?.DiscountPercent;
            if (raw is not { ValueKind: JsonValueKind.Number } el || !el.TryGetDouble(out value) || !double.IsFinite(value))
            {
                logger.LogWarning("[AishiInsuranceDiscount4Server] invalid DiscountPercent in config.json (must be a number), using {Default}", DefaultDiscountPercent);
                return DefaultDiscountPercent;
            }
        }
        catch (Exception e)
        {
            logger.LogWarning("[AishiInsuranceDiscount4Server] Could not read config.json ({Error}), using {Default}", e.Message, DefaultDiscountPercent);
            return DefaultDiscountPercent;
        }

        var clamped = Math.Clamp(value, 0, MaxDiscountPercent);
        if (clamped != value)
            logger.LogWarning("[AishiInsuranceDiscount4Server] DiscountPercent {Value} out of range, clamped to {Clamped}", value, clamped);
        return clamped;
    }
}

/// <summary>
/// Every Aishi insurance price (Cost quote via QuoteInsurance, Insure payment via the
/// GetRoublePriceToInsureItemWithTrader postfix) goes through DollarServicePolicy.InsurancePrice
/// exactly once; scale its result there. Harmony 2.16 marks patched originals NoInlining, so
/// callers cannot bypass the patch by inlining it.
/// </summary>
[HarmonyPatch(typeof(DollarServicePolicy), nameof(DollarServicePolicy.InsurancePrice))]
internal static class AishiInsurancePricePatch
{
    private static void Postfix(MongoId trader, ref double __result)
    {
        if (!DollarServicePolicy.IsAishi(trader)) return;
        __result = Math.Ceiling(__result * AishiInsuranceDiscountMod.Multiplier);
    }
}
