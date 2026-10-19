using MatchShift.Analytics;
using MatchShift.Cli;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.Infrastructure.Evaluation;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Scenarios;

var command = args.Length > 0 ? args[0] : "help";
var options = ParseOptions(args.Skip(1).ToArray());

switch (command)
{
    case "verify-ai":
    case "compare-agents":
        await NarrationValidation.Run(command, options);
        break;
    case "agent-workflow":
        await AgentWorkflowCli.Run(options);
        break;
    case "foundry-eval":
        await FoundryEvalCli.Run(options);
        break;
    case "inspect":
        Inspect(SyntheticMatchGenerator.GenerateFlagship().Match);
        foreach (var seed in new long[] { 1, 2, 3 }) Inspect(SyntheticMatchGenerator.GenerateMatch(seed));
        break;

    case "calibrate":
        Calibration.PrintTimeline(SyntheticMatchGenerator.GenerateFlagship().Match);
        break;

    case "analyze":
    {
        var match = options.TryGetValue("seed", out var s)
            ? options.TryGetValue("scenario", out var k)
                ? SyntheticMatchGenerator.GenerateScenario(Enum.Parse<ScenarioKind>(k, true), long.Parse(s)).Match
                : SyntheticMatchGenerator.GenerateMatch(long.Parse(s))
            : SyntheticMatchGenerator.GenerateFlagship().Match;
        PrintInsights(match);
        break;
    }

    case "evaluate":
    {
        var count = int.Parse(options.GetValueOrDefault("matches", "60"));
        var start = long.Parse(options.GetValueOrDefault("seed", "1000"));
        var mix = options.TryGetValue("scenario", out var only)
            ? ScenarioMix.Only(Enum.Parse<ScenarioKind>(only, true))
            : ScenarioMix.Default;
        var dataset = SyntheticMatchGenerator.GenerateDataset(count, start, mix);
        PrintEvaluation(DetectorEvaluator.Evaluate(dataset.Matches));
        break;
    }

    case "robustness":
    {
        // Same flagship script, different seeds: does the story survive the dice?
        var count = int.Parse(options.GetValueOrDefault("matches", "20"));
        var runs = Enumerable.Range(1, count).Select(i =>
            new LabelledMatch(
                MatchShift.SyntheticData.Simulation.MatchSimulator.Simulate(FlagshipScenario.Script(), FlagshipScenario.Seed + i),
                FlagshipScenario.GroundTruth()));
        PrintEvaluation(DetectorEvaluator.Evaluate(runs));
        break;
    }

    case "sweep-channel":
    {
        var count = int.Parse(options.GetValueOrDefault("matches", "60"));
        var matches = SyntheticMatchGenerator.GenerateDataset(count, 5000, ScenarioMix.Only(ScenarioKind.Stationary)).Matches
            .Concat(SyntheticMatchGenerator.GenerateDataset(count, 6000, ScenarioMix.Only(ScenarioKind.AttackChannelShift)).Matches)
            .Concat(Enumerable.Range(1, 10).Select(i => new LabelledMatch(
                MatchShift.SyntheticData.Simulation.MatchSimulator.Simulate(FlagshipScenario.Script(), FlagshipScenario.Seed + i),
                FlagshipScenario.GroundTruth())))
            .ToList();
        var detectors = new MatchShift.Analytics.Detection.IShiftDetector[] { new MatchShift.Analytics.Detection.Detectors.AttackChannelShiftDetector() };
        foreach (var recent in new[] { 10.0, 12.0 })
        foreach (var z in new[] { 2.4, 2.7 })
        foreach (var ticks in new[] { 1, 2 })
        {
            var diff = 0.25;
            var o = MatchShift.Analytics.Detection.DetectionOptions.Default with
            {
                RequiredConsecutiveTicks = ticks,
                AttackChannel = MatchShift.Analytics.Detection.DetectionOptions.Default.AttackChannel with
                {
                    RecentMinutes = recent, MinZScore = z, MinShareIncrease = diff,
                },
            };
            var report = DetectorEvaluator.Evaluate(matches, o, detectors: detectors);
            var row = report.Types.FirstOrDefault(t => t.Type == InsightType.AttackChannelShift);
            Console.WriteLine($"recent={recent} z={z} ticks={ticks}: recall={row?.Recall:0.00} precision={row?.Precision:0.00} " +
                              $"falseAlarms/negMatch={row?.FalseAlarmsPerNegativeMatch:0.000} detections={row?.Detections} aftermath={row?.Aftermath}");
        }
        break;
    }

    case "replay":
    {
        var match = SyntheticMatchGenerator.GenerateFlagship().Match;
        var analysis = LiveMatchAnalyzer.AnalyzeFull(match);
        var insight = options.TryGetValue("insight", out var iid)
            ? analysis.Insights.First(i => i.Id == iid)
            : analysis.Insights.First(i => i.Type == InsightType.DangerousTurnover && i.DisplayMinute == 35);
        var replay = MatchShift.Analytics.Replay.ReplayBuilder.ForInsight(match, insight);
        Console.WriteLine($"{insight.Id} {insight.Type.Code()} -> {replay.Clips.Count} clip(s), {replay.TotalDuration:0.0}s");
        foreach (var clip in replay.Clips)
        {
            Console.WriteLine($"== {clip.Label} {MatchClock.Format(clip.StartSecond)}-{MatchClock.Format(clip.EndSecond)} attack={clip.Camera.AttackingTeamId} dir={clip.Camera.AttackDirection} focus={clip.Camera.FocusPlayerId}");
            foreach (var e in clip.Events) Console.WriteLine($"   t={e.Time,6:0.00}-{e.EndTime,6:0.00} {e.Clock} {e.Label,-24} {e.PlayerId,-7} -> {e.RelatedPlayerId,-7} ({e.X},{e.Y})->({e.EndX},{e.EndY}){(e.IsEvidence ? " *" : "")}");
            foreach (var o in clip.Overlays) Console.WriteLine($"   overlay {o.Kind} {o.Label} {o.X0},{o.Y0}..{o.X1},{o.Y1} players={string.Join(",", o.PlayerIds)}");
            var prov = clip.Tracks.SelectMany(t => t.Samples).GroupBy(s => s.Provenance).Select(g => $"{g.Key}={g.Count()}");
            Console.WriteLine($"   tracks={clip.Tracks.Count} samples: {string.Join(" ", prov)}  ball segments={clip.Ball.Count}");
        }
        break;
    }

    case "export":
    {
        var count = int.Parse(options.GetValueOrDefault("matches", "50"));
        var start = long.Parse(options.GetValueOrDefault("seed", "1000"));
        var outDir = options.GetValueOrDefault("out", Path.Combine("datasets", "out", $"matchshift-{start}-{count}"));
        var dataset = SyntheticMatchGenerator.GenerateDataset(count, start);
        var manifest = await MatchShift.Infrastructure.Export.DatasetExporter.ExportAsync(dataset, outDir, parquet: !options.ContainsKey("no-parquet"));
        Console.WriteLine($"Exported {manifest.Matches} synthetic matches, {manifest.Events} events, {manifest.Insights} insights to {Path.GetFullPath(outDir)}");
        foreach (var f in manifest.Files) Console.WriteLine($"  {f}");
        break;
    }

    case "narrate":
    {
        var match = SyntheticMatchGenerator.GenerateFlagship().Match;
        var analysis = LiveMatchAnalyzer.AnalyzeFull(match);
        var profile = new MatchShift.Agents.Narrative.ViewerProfile(
            Enum.Parse<MatchShift.Agents.Narrative.ViewerMode>(options.GetValueOrDefault("mode", "Analyst"), true),
            options.GetValueOrDefault("lang", "en"), options.GetValueOrDefault("team"));
        var ctx = new MatchShift.Agents.Narrative.NarrativeContext(new MatchShift.Agents.Narrative.MatchRoster(match), profile);
        foreach (var i in MatchShift.Agents.Narrative.NarrativeFilter.Select(analysis.Insights, profile))
        {
            var n = MatchShift.Agents.Narrative.TemplateInsightProvider.Narrate(i, ctx);
            Console.WriteLine($"[{i.DisplayMinute}'] {n.TypeLabel.ToUpperInvariant()} - {n.Headline}" + Environment.NewLine + $"   FACT: {n.Fact}" + Environment.NewLine + $"   INFERENCE: {n.Inference}" + Environment.NewLine + $"   EXPLANATION: {n.Explanation}");
            if (n.Teaching is not null) Console.WriteLine($"   TEACH: {n.Teaching}");
            if (n.Perspective is not null) Console.WriteLine($"   {n.Perspective}");
            Console.WriteLine($"   STUDIO: {n.StudioLine}");
        }
        if (options.ContainsKey("recap"))
        {
            var facts = MatchShift.Analytics.Story.RecapBuilder.Build(analysis, RecapPeriod.FullTime);
            Console.WriteLine(MatchShift.Agents.Narrative.RecapNarrator.Narrate(facts,
                Enum.Parse<MatchShift.Agents.Narrative.RecapStyle>(options["recap"], true), ctx, analysis.Insights).Text);
        }
        break;
    }

    case "recap":
    {
        var match = SyntheticMatchGenerator.GenerateFlagship().Match;
        var analysis = LiveMatchAnalyzer.AnalyzeFull(match);
        foreach (var period in new[] { RecapPeriod.HalfTime, RecapPeriod.FullTime })
        {
            var recap = MatchShift.Analytics.Story.RecapBuilder.Build(analysis, period);
            Console.WriteLine($"== {period}: {recap.HomeScore}-{recap.AwayScore}  chaotic minutes {recap.ChaoticMinuteShare:P0}");
            foreach (var m in recap.MomentumPeriods) Console.WriteLine($"  momentum {m.TeamId} {m.FromMinute}-{m.ToMinute} avg {m.AverageControl}");
            foreach (var b in recap.Story) Console.WriteLine($"  {b.Minute,3}' {b.Code} {b.TeamId}");
            foreach (var p in recap.KeyPlayers) Console.WriteLine($"  player {match.FindPlayer(p.PlayerId)?.Name} g{p.Goals} a{p.Assists} kp{p.KeyPasses} s{p.Shots} rec{p.Recoveries} m{p.InsightMentions} = {p.Score}");
            Console.WriteLine($"  home: poss {recap.Home.PossessionShare:P0} shots {recap.Home.Shots} attacks L/C/R {recap.Home.LeftAttackShare:P0}/{recap.Home.CentreAttackShare:P0}/{recap.Home.RightAttackShare:P0}");
            Console.WriteLine($"  away: poss {recap.Away.PossessionShare:P0} shots {recap.Away.Shots} attacks L/C/R {recap.Away.LeftAttackShare:P0}/{recap.Away.CentreAttackShare:P0}/{recap.Away.RightAttackShare:P0}");
        }
        Console.WriteLine("== Highlights");
        foreach (var h in MatchShift.Analytics.Story.HighlightRanker.Rank(analysis).Take(12))
            Console.WriteLine($"  {h.DisplayMinute,3}' {h.Kind,-18} {h.TeamId,-4} {h.ImportanceScore,5} {h.WhyItMattersCode}");
        break;
    }

    case "seed-search":
    {
        // Find flagship seeds where every scripted pattern is discovered by the live detector.
        var from = long.Parse(options.GetValueOrDefault("from", "20260300"));
        var count = int.Parse(options.GetValueOrDefault("count", "40"));
        var truth = FlagshipScenario.GroundTruth();
        for (var seed = from; seed < from + count; seed++)
        {
            var match = MatchShift.SyntheticData.Simulation.MatchSimulator.Simulate(FlagshipScenario.Script(), seed);
            var insights = LiveMatchAnalyzer.AnalyzeFull(match).Insights;
            var hits = truth.ExpectedPatterns.Count(p => insights.Any(i =>
                p.Pattern == i.Type && (p.TeamId == "*" || p.TeamId == i.TeamId) &&
                i.DetectedAtSecond / 60.0 >= p.WindowStartMinute - 2 && i.DetectedAtSecond / 60.0 <= p.WindowEndMinute + 4));
            var acs = insights.FirstOrDefault(i => i.Type == InsightType.AttackChannelShift && i.TeamId == "WHA");
            var acsText = acs is null ? "-" : acs.DisplayMinute.ToString();
            Console.WriteLine($"seed {seed}: {hits}/{truth.ExpectedPatterns.Count} patterns, insights={insights.Count}, WHA channel shift at {acsText}");
        }
        break;
    }

    case "fp-channel":
    {
        var detectors = new MatchShift.Analytics.Detection.IShiftDetector[] { new MatchShift.Analytics.Detection.Detectors.AttackChannelShiftDetector() };
        foreach (var m in SyntheticMatchGenerator.GenerateDataset(30, 5000, ScenarioMix.Only(ScenarioKind.Stationary)).Matches)
        foreach (var i in LiveMatchAnalyzer.AnalyzeFull(m.Match, null, detectors).Insights.Where(x => x.Type == InsightType.AttackChannelShift))
        {
            var p = i.PrimaryEvidence!;
            var z = i.Evidence.First(e => e.Metric == "ShiftStrengthZ").RecentValue;
            Console.WriteLine($"{m.Match.Id} {i.DisplayMinute}' {i.TeamId} {i.Fact("channel")} {p.BaselineValue:0.00}->{p.RecentValue:0.00} n={p.BaselineSampleSize}/{p.SampleSize} z={z:0.0}");
        }
        break;
    }

    case "effect":
    {
        double sumBase = 0, sumPhase = 0, nBase = 0, nPhase = 0; var k = 0;
        foreach (var m in SyntheticMatchGenerator.GenerateDataset(40, 6000, ScenarioMix.Only(ScenarioKind.AttackChannelShift)).Matches)
        {
            var p = m.GroundTruth.ExpectedPatterns[0];
            var state = MatchShift.Analytics.State.MatchState.FromEvents(MatchShift.Analytics.State.MatchContext.From(m.Match), m.Match.Events);
            var ch = p.Note.Contains("right") ? AttackChannel.Right : AttackChannel.Left;
            var b = MatchShift.Analytics.Metrics.MetricsCalculator.Compute(state, MatchShift.Analytics.Metrics.AnalysisWindow.OfMinutes(0, p.WindowStartMinute - 3)).For(p.TeamId);
            var r = MatchShift.Analytics.Metrics.MetricsCalculator.Compute(state, MatchShift.Analytics.Metrics.AnalysisWindow.OfMinutes(p.WindowStartMinute - 3, p.WindowEndMinute)).For(p.TeamId);
            sumBase += b.ChannelShare(ch); sumPhase += r.ChannelShare(ch); nBase += b.Attacks; nPhase += r.Attacks; k++;
            Console.WriteLine($"{m.Match.Id} {p.TeamId} {ch} base {b.ChannelShare(ch):0.00} (n={b.Attacks}, {b.Attacks / Math.Max(1, (p.WindowStartMinute - 3.0)):0.00}/min)  phase {r.ChannelShare(ch):0.00} (n={r.Attacks}, {r.Attacks / (double)(p.WindowEndMinute - p.WindowStartMinute + 3):0.00}/min) actions/attack={r.AttackingThirdActions / Math.Max(1.0, r.Attacks):0.0}");
        }
        Console.WriteLine($"mean base share {sumBase / k:0.00}, phase share {sumPhase / k:0.00}, base n {nBase / k:0.0}, phase n {nPhase / k:0.0}");
        break;
    }

    case "channels":
    {
        var labelled = options.TryGetValue("seed", out var s2)
            ? SyntheticMatchGenerator.GenerateScenario(Enum.Parse<ScenarioKind>(options.GetValueOrDefault("scenario", "AttackChannelShift"), true), long.Parse(s2))
            : SyntheticMatchGenerator.GenerateFlagship();
        foreach (var p in labelled.GroundTruth.ExpectedPatterns) Console.WriteLine($"expect {p}");
        foreach (var i in LiveMatchAnalyzer.AnalyzeFull(labelled.Match).Insights.Where(i => i.Type == InsightType.AttackChannelShift))
            Console.WriteLine($"detected {i.DisplayMinute}' {i.TeamId} {i.Fact("channel")} z={i.Evidence.First(e => e.Metric == "ShiftStrengthZ").RecentValue}");
        Calibration.PrintChannels(labelled.Match);
        break;
    }

    default:
        Console.WriteLine("""
            MatchShift CLI (all data is synthetic)

              inspect                         Summary of the flagship and a few seeded matches
              calibrate                       Rolling index timeline for the flagship match
              analyze [--seed N] [--scenario K]  Run the live detector and print insights
              evaluate [--matches N] [--seed S] [--scenario K]  Precision/recall vs ground truth
              robustness [--matches N]        Flagship script re-simulated with other seeds
              export [--matches N] [--seed S] [--out DIR] [--no-parquet]  Fabric-ready dataset (JSONL/CSV/Parquet)
              narrate [--lang en|fr|es] [--mode M] [--recap STYLE]  Print rendered narratives for the flagship
            """);
        break;
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal)) result[args[i][2..]] = args[++i];
    }
    return result;
}

static void Inspect(Match match)
{
    Console.WriteLine($"{match.HomeTeam.Name} {match.ScoreAt(int.MaxValue)} {match.AwayTeam.Name}  events={match.Events.Count}");
    foreach (var team in new[] { match.HomeTeam, match.AwayTeam })
    {
        var ev = match.Events.Where(e => e.TeamId == team.Id).ToList();
        Console.WriteLine($"  {team.Code}: passes={ev.Count(e => e.Type == EventType.Pass)} " +
                          $"complete={ev.Count(e => e.Type == EventType.Pass && e.Outcome == EventOutcome.Complete)} " +
                          $"shots={ev.Count(e => e.Type == EventType.Shot)} corners={ev.Count(e => e.Type == EventType.Corner)}");
    }
}

static void PrintInsights(Match match)
{
    var analyzer = LiveMatchAnalyzer.AnalyzeFull(match);
    Console.WriteLine($"{match.Id}: {match.HomeTeam.Name} {match.ScoreAt(int.MaxValue)} {match.AwayTeam.Name}");
    foreach (var i in analyzer.Insights)
    {
        var p = i.PrimaryEvidence;
        var detail = p is null ? "" : $"{p.Metric} {p.BaselineValue?.ToString("0.###") ?? "-"} -> {p.RecentValue:0.###}";
        Console.WriteLine($"{i.DisplayMinute,3}' {i.Type.Code(),-26} {i.TeamId ?? "-",-4} {i.Severity,-6} {i.Confidence,-6} " +
                          $"imp={i.Importance,5:0.0} {detail} {string.Join(",", i.Facts.Select(f => $"{f.Key}={f.Value}"))}");
    }
}

static void PrintEvaluation(EvaluationReport report)
{
    Console.WriteLine($"Matches: {report.Matches}   Forbidden-pattern violations: {report.ForbiddenViolations}");
    Console.WriteLine($"{"Type",-26} {"Expected",8} {"Recalled",8} {"Recall",7} {"Detect",7} {"Matched",8} {"Precision",9} {"FA/neg",7} {"Unlabelled/match",16}");
    foreach (var t in report.Types)
    {
        Console.WriteLine($"{t.Type.Code(),-26} {t.Expected,8} {t.Recalled,8} {Fmt(t.Recall),7} {t.Detections,7} {t.MatchedDetections,8} " +
                          $"{(t.Strict ? Fmt(t.Precision) : "n/a"),9} {(t.Strict ? Fmt(t.FalseAlarmsPerNegativeMatch) : "n/a"),7} {t.UnlabelledPerMatch,16:0.00}");
    }
    foreach (var s in report.Scenarios) Console.WriteLine($"  {s.Scenario,-22} recall {s.Recalled}/{s.Expected} = {s.Recall:0.00}");

    static string Fmt(double? v) => v is null ? "-" : v.Value.ToString("0.00");
}
