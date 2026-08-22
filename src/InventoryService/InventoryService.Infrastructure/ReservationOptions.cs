namespace InventoryService.Infrastructure;

public sealed class ReservationOptions
{
    public const string SectionName = "Reservation";

    public int TtlSeconds { get; init; } = 900;
    public int ExpirySweepIntervalSeconds { get; init; } = 30;
}
