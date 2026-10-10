namespace Nexora.Application;

/// <summary>
/// Dashboard content for Phase 1. Real companies show an honest empty state because no financial
/// modules exist yet. The demonstration company shows fixed illustrative figures, labelled as such.
/// Phase 8 replaces this with queries over posted transactions.
/// </summary>
public static class DemonstrationDashboard
{
    public const string DemoNotice =
        "Demonstration company. Figures are illustrative samples, not posted transactions. Delete this company from Settings when you no longer need it.";

    public const string EmptyNotice =
        "No transactions yet. Figures appear here once sales, purchases and accounting are recorded.";

    public static DashboardSnapshot Empty() => new(false, EmptyNotice,
        [
            new("Sales", 0m, "This month"),
            new("Purchases", 0m, "This month"),
            new("Receivables", 0m, "Outstanding"),
            new("Payables", 0m, "Outstanding"),
            new("Cash and bank", 0m, "Closing balance"),
            new("Inventory value", 0m, "At cost"),
            new("Gross profit", 0m, "This month"),
            new("Net profit", 0m, "This month"),
        ],
        [], [], [], [], []);

    public static DashboardSnapshot Sample(DateTimeOffset now) => new(true, DemoNotice,
        [
            new("Sales", 1_842_350m, "+6.2% vs last month"),
            new("Purchases", 1_126_900m, "+2.4% vs last month"),
            new("Receivables", 612_480m, "38 open invoices"),
            new("Payables", 358_200m, "14 open bills"),
            new("Cash and bank", 947_615m, "3 accounts"),
            new("Inventory value", 2_318_740m, "At weighted average cost"),
            new("Gross profit", 498_120m, "27.0% margin"),
            new("Net profit", 211_460m, "11.5% margin"),
        ],
        [
            new("May", 1_412_000m, 1_118_000m),
            new("Jun", 1_538_000m, 1_204_000m),
            new("Jul", 1_486_000m, 1_242_000m),
            new("Aug", 1_652_000m, 1_301_000m),
            new("Sep", 1_734_000m, 1_356_000m),
            new("Oct", 1_842_350m, 1_406_890m),
        ],
        [
            new(now.AddMinutes(-12), "Sample invoice INV-0412", "Sharma Traders · ₹48,260"),
            new(now.AddMinutes(-47), "Sample receipt RCT-0187", "Patel Hardware · ₹1,20,000"),
            new(now.AddHours(-3), "Sample purchase PUR-0094", "Kaveri Distributors · ₹2,16,400"),
            new(now.AddHours(-5), "Sample stock adjustment", "Main warehouse · 4 items"),
        ],
        [
            new("CBL-HDMI-2M", "HDMI cable 2 m", 6m, 25m),
            new("LED-B22-9W", "LED bulb B22 9 W", 18m, 60m),
            new("SW-6A-1W", "Switch 6 A one-way", 32m, 80m),
        ],
        [
            new("LED panel 18 W", 286_400m),
            new("Copper wire 1.5 sq mm", 241_900m),
            new("MCB 32 A double pole", 198_250m),
            new("Ceiling fan 1200 mm", 164_800m),
            new("Modular switch plate", 92_300m),
        ],
        [
            new("0–30 days", 318_200m),
            new("31–60 days", 164_930m),
            new("61–90 days", 81_150m),
            new("Over 90 days", 48_200m),
        ]);
}
