namespace MatchShift.Analytics.Indices;

/// <summary>
/// One transparent ingredient of a composite index. Contribution = Normalized x Weight x 100,
/// so the components of an index always sum to the index value.
/// </summary>
public sealed record IndexComponent(
    string Key,
    string Label,
    double RawValue,
    double Normalized,
    double Weight,
    double Contribution);

/// <summary>Linear normalization of a raw value into 0..1 between a floor and a ceiling.</summary>
public sealed record ComponentScale(string Key, string Label, double Floor, double Ceiling, double Weight)
{
    public IndexComponent Score(double raw)
    {
        var normalized = Ceiling <= Floor ? 0 : Math.Clamp((raw - Floor) / (Ceiling - Floor), 0, 1);
        return new IndexComponent(Key, Label, Math.Round(raw, 3), Math.Round(normalized, 3), Weight,
            Math.Round(normalized * Weight * 100, 2));
    }
}
