namespace FimpleCaseStudy.Domain.Cash;

public static class LowCashPolicy
{
    public const int Threshold = Cassette.Capacity / 10;
    public static bool ShouldAlert(CassetteKind kind, int before, int after, bool firstService) =>
        kind == CassetteKind.Recycling && after < Threshold && (before >= Threshold || firstService);
}
