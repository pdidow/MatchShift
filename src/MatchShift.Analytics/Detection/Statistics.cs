using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Detection;

/// <summary>Small, dependency-free statistics used to grade detections.</summary>
public static class Statistics
{
    /// <summary>Standard normal CDF (Abramowitz &amp; Stegun 7.1.26, |error| &lt; 1.5e-7).</summary>
    public static double NormalCdf(double z)
    {
        var sign = z < 0 ? -1 : 1;
        var x = Math.Abs(z) / Math.Sqrt(2);
        var t = 1 / (1 + 0.3275911 * x);
        var y = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return 0.5 * (1 + sign * y);
    }

    /// <summary>
    /// Two-proportion z statistic for p2 &gt; p1 using a pooled estimate.
    /// Returns 0 when either sample is empty.
    /// </summary>
    public static double TwoProportionZ(double p1, int n1, double p2, int n2)
    {
        if (n1 <= 0 || n2 <= 0) return 0;
        var pooled = (p1 * n1 + p2 * n2) / (n1 + n2);
        var se = Math.Sqrt(pooled * (1 - pooled) * (1.0 / n1 + 1.0 / n2));
        return se <= 1e-9 ? (p2 > p1 ? 10 : 0) : (p2 - p1) / se;
    }

    /// <summary>Confidence from a z-score, damped when samples are small.</summary>
    public static double ConfidenceFromZ(double z, int smallestSample, int fullConfidenceSample = 15)
    {
        var sampleFactor = Math.Min(1, 0.7 + 0.3 * smallestSample / fullConfidenceSample);
        return Math.Round(Math.Clamp(NormalCdf(z) * sampleFactor, 0, 0.99), 2);
    }

    /// <summary>
    /// Confidence for index-based detections that have no closed-form test:
    /// grows with how far the change exceeds the threshold and with sample size.
    /// </summary>
    public static double ConfidenceFromMargin(double changeOverThreshold, int sample, int fullConfidenceSample = 40)
    {
        var margin = Math.Clamp((changeOverThreshold - 1) / 1.0, 0, 1);
        var sampleFactor = Math.Clamp((double)sample / fullConfidenceSample, 0, 1);
        return Math.Round(Math.Clamp(0.6 + 0.25 * margin + 0.14 * sampleFactor, 0, 0.99), 2);
    }

    public static ConfidenceLevel LevelOf(double score) =>
        score >= 0.9 ? ConfidenceLevel.High : score >= 0.75 ? ConfidenceLevel.Medium : ConfidenceLevel.Low;

    /// <summary>Severity from how far a change exceeds its detection threshold.</summary>
    public static Severity SeverityOf(double changeOverThreshold) =>
        changeOverThreshold >= 1.75 ? Severity.High : changeOverThreshold >= 1.3 ? Severity.Medium : Severity.Low;

    /// <summary>Rate ratio with +1 smoothing so a zero baseline does not explode.</summary>
    public static double RateRatio(double recentCount, double recentMinutes, double baselineCount, double baselineMinutes)
    {
        if (recentMinutes <= 0 || baselineMinutes <= 0) return 0;
        var recentRate = recentCount / recentMinutes;
        var baselineRate = (baselineCount + 1) / baselineMinutes;
        return recentRate / baselineRate;
    }
}
