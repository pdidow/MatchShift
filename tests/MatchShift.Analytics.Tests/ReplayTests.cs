using System.Text.Json;
using MatchShift.Analytics.Replay;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Clubs;

namespace MatchShift.Analytics.Tests;

public class ReplayTests
{
    private static readonly Match Flagship = SyntheticMatchGenerator.GenerateFlagship().Match;
    private static readonly LiveMatchAnalyzer Analysis = LiveMatchAnalyzer.AnalyzeFull(Flagship);

    /// <summary>The demo sequence: Northbridge's press wins the ball at 34' and they score through the left channel.</summary>
    private static Insight PressTurnover => Analysis.Insights.Single(i =>
        i.Type == InsightType.DangerousTurnover && i.TeamId == ClubCatalog.WesthavenId && i.DisplayMinute == 35);

    private static Insight ChannelShift => Analysis.Insights.Single(i => i.Type == InsightType.AttackChannelShift);

    private static ReplaySequence Build(Insight i) => ReplayBuilder.ForInsight(Flagship, i);

    [Fact]
    public void Demo_turnover_replays_the_press_the_left_channel_attack_and_the_goal()
    {
        var replay = Build(PressTurnover);
        var clip = Assert.Single(replay.Clips);
        var labels = clip.Events.Select(e => e.Label).ToList();

        var interception = clip.Events.Single(e => e.Label == "HIGH_PRESS_INTERCEPTION");
        Assert.Equal("NBR-04", interception.PlayerId);
        Assert.Contains(clip.Events, e => e.Type == "Pass" && e.PlayerId == "NBR-04" && e.RelatedPlayerId == "NBR-11");
        Assert.Contains("CUTBACK", labels);
        Assert.Equal("GOAL", clip.Events.Last(e => e.Type == "Shot").Label);
        Assert.True(labels.IndexOf("PASS_LOST") < labels.IndexOf("HIGH_PRESS_INTERCEPTION"));
        Assert.True(labels.IndexOf("HIGH_PRESS_INTERCEPTION") < labels.IndexOf("GOAL"));

        Assert.Equal(ClubCatalog.NorthbridgeId, clip.Camera.AttackingTeamId);
        Assert.Equal(1, clip.Camera.AttackDirection);
        Assert.Contains(clip.Overlays, o => o.Kind == "Channel" && o.Label == "ATTACK_ROUTE:Left");
        Assert.Contains(clip.Overlays, o => o.Kind == "PressingPlayers" && o.PlayerIds.Contains("NBR-04"));
        Assert.Contains(clip.Overlays, o => o.Kind == "Marker" && o.Label == "TURNOVER_WON");
        Assert.Equal(new Score(0, 0), clip.ScoreAtStart);
    }

    [Fact]
    public void Every_evidence_event_inside_the_clip_is_flagged_and_the_clip_overlaps_the_insight()
    {
        var insight = PressTurnover;
        var clip = Build(insight).Clips[0];
        var evidence = insight.SupportingEventIds.ToHashSet();
        Assert.All(clip.Events.Where(e => evidence.Contains(e.EventId)), e => Assert.True(e.IsEvidence));
        Assert.True(clip.Events.Count(e => e.IsEvidence) >= 4);
        Assert.True(clip.StartSecond <= insight.Span.ToSecond && clip.EndSecond >= insight.Span.FromSecond);
    }

    [Fact]
    public void Events_are_in_sequence_and_time_order_inside_the_clip()
    {
        foreach (var clip in Build(ChannelShift).Clips.Concat(Build(PressTurnover).Clips))
        {
            Assert.Equal(clip.Events.OrderBy(e => e.Sequence).Select(e => e.EventId), clip.Events.Select(e => e.EventId));
            for (var i = 1; i < clip.Events.Count; i++) Assert.True(clip.Events[i].Time >= clip.Events[i - 1].Time);
            Assert.All(clip.Events, e => Assert.InRange(e.Time, 0, clip.Duration));
            Assert.All(clip.Events, e => Assert.True(e.EndTime >= e.Time));
            Assert.InRange(clip.Duration, ReplayBuilder.MinClipSeconds, ReplayBuilder.MaxClipSeconds + 1);
        }
    }

    [Fact]
    public void Coordinates_are_converted_to_the_absolute_frame()
    {
        var clip = Build(PressTurnover).Clips[0];
        foreach (var re in clip.Events.Where(e => e.X is not null))
        {
            var source = Flagship.Events.Single(e => e.Id == re.EventId);
            var (x, y) = Pitch.ToAbsolute(source.StartX!.Value, source.StartY!.Value, Flagship.IsHome(source.TeamId));
            Assert.Equal(Math.Round(x, 2), re.X!.Value, 2);
            Assert.Equal(Math.Round(y, 2), re.Y!.Value, 2);
        }
        // Westhaven's (away) lost pass and Northbridge's interception describe the same absolute point.
        var lost = clip.Events.Single(e => e.Label == "PASS_LOST");
        var won = clip.Events.Single(e => e.Label == "HIGH_PRESS_INTERCEPTION");
        Assert.Equal(lost.EndX!.Value, won.X!.Value, 1);
        Assert.Equal(lost.EndY!.Value, won.Y!.Value, 1);
    }

    [Fact]
    public void Tracks_cover_all_22_players_for_the_whole_clip()
    {
        var clip = Build(PressTurnover).Clips[0];
        Assert.Equal(22, clip.Tracks.Count);
        Assert.Equal(22, clip.Tracks.Select(t => t.PlayerId).Distinct().Count());
        foreach (var track in clip.Tracks)
        {
            Assert.Equal(0, track.Samples[0].T);
            Assert.Equal(clip.Duration, track.Samples[^1].T, 3);
            for (var i = 1; i < track.Samples.Count; i++) Assert.True(track.Samples[i].T > track.Samples[i - 1].T);
            Assert.All(track.Samples, s => { Assert.InRange(s.X, 0, 100); Assert.InRange(s.Y, 0, 100); });
        }
    }

    [Fact]
    public void Positions_recorded_in_events_are_marked_actual_and_match_the_event()
    {
        var clip = Build(PressTurnover).Clips[0];
        var interception = clip.Events.Single(e => e.Label == "HIGH_PRESS_INTERCEPTION");
        var track = clip.Tracks.Single(t => t.PlayerId == interception.PlayerId);
        var sample = track.Samples.Single(s => Math.Abs(s.T - interception.Time) < 1e-6);
        Assert.Equal(PositionProvenance.ActualEventPosition, sample.Provenance);
        Assert.Equal(interception.X!.Value, sample.X, 1);
        Assert.Equal(interception.Y!.Value, sample.Y, 1);

        // A pass recipient is recorded where the pass arrives.
        var pass = clip.Events.Single(e => e.Type == "Pass" && e.PlayerId == "NBR-04" && e.RelatedPlayerId == "NBR-11");
        var receiver = clip.Tracks.Single(t => t.PlayerId == "NBR-11").Samples.Single(s => Math.Abs(s.T - pass.EndTime) < 1e-6);
        Assert.Equal(PositionProvenance.ActualEventPosition, receiver.Provenance);
        Assert.Equal(pass.EndX!.Value, receiver.X, 1);
    }

    [Fact]
    public void Positions_between_events_are_interpolated_and_uninvolved_players_are_estimated()
    {
        var clip = Build(PressTurnover).Clips[0];
        var carry = clip.Events.Single(e => e.Type == "Carry" && e.PlayerId == "NBR-11");
        var mid = (carry.Time + carry.EndTime) / 2;
        var track = clip.Tracks.Single(t => t.PlayerId == "NBR-11");
        var between = track.Samples.Where(s => s.T > carry.Time && s.T < carry.EndTime).ToList();
        Assert.NotEmpty(between);
        Assert.All(between, s => Assert.Equal(PositionProvenance.InterpolatedPosition, s.Provenance));
        var near = between.OrderBy(s => Math.Abs(s.T - mid)).First();
        var f = (near.T - carry.Time) / (carry.EndTime - carry.Time);
        Assert.Equal(carry.X!.Value + (carry.EndX!.Value - carry.X.Value) * f, near.X, 1);

        // Goalkeepers far from the play are never recorded in this clip: estimated throughout.
        var keeper = clip.Tracks.Single(t => t.PlayerId == "NBR-01");
        Assert.Contains(keeper.Samples, s => s.Provenance == PositionProvenance.EstimatedPosition);
        // Players in no event near the clip (including passes that start just before it) have no recorded position.
        var involved = Flagship.Events.Where(e => e.MatchSecond >= clip.StartSecond - 10 && e.MatchSecond <= clip.EndSecond + 10)
            .SelectMany(e => new[] { e.PlayerId, e.RelatedPlayerId }).OfType<string>().ToHashSet();
        var uninvolved = clip.Tracks.Where(t => !involved.Contains(t.PlayerId)).ToList();
        Assert.NotEmpty(uninvolved);
        Assert.All(uninvolved, t => Assert.All(t.Samples, s => Assert.NotEqual(PositionProvenance.ActualEventPosition, s.Provenance)));
    }

    [Fact]
    public void Ball_segments_are_contiguous_and_passes_run_between_event_positions()
    {
        var clip = Build(PressTurnover).Clips[0];
        Assert.Equal(0, clip.Ball[0].T0);
        Assert.Equal(clip.Duration, clip.Ball[^1].T1, 3);
        for (var i = 1; i < clip.Ball.Count; i++) Assert.Equal(clip.Ball[i - 1].T1, clip.Ball[i].T0, 3);

        var pass = clip.Events.Single(e => e.Type == "Pass" && e.PlayerId == "NBR-04" && e.RelatedPlayerId == "NBR-11");
        var segment = clip.Ball.Single(b => b.EventId == pass.EventId);
        Assert.Equal(BallMotion.Pass, segment.Motion);
        Assert.Equal(pass.X!.Value, segment.X0, 1);
        Assert.Equal(pass.EndX!.Value, segment.X1, 1);
        Assert.Equal(pass.Time, segment.T0, 3);
        Assert.Equal(pass.EndTime, segment.T1, 3);

        var shot = clip.Ball.Single(b => b.Motion == BallMotion.Shot);
        Assert.True(shot.X1 > 100, "A goal travels into the net beyond the goal line");
    }

    [Fact]
    public void Ball_position_interpolates_along_a_segment()
    {
        var segment = new BallSegment(0, 2, BallMotion.Pass, 10, 20, 30, 60, 0, 0, 0, null, "E", PositionProvenance.InterpolatedPosition);
        var (x, y) = ReplayBuilder.BallAt([segment], 1);
        Assert.Equal(20, x, 6);
        Assert.Equal(40, y, 6);
        Assert.Equal((30.0, 60.0), ReplayBuilder.BallAt([segment], 5));
    }

    [Fact]
    public void Tactical_shift_replays_before_and_after_from_the_right_windows()
    {
        var insight = ChannelShift;
        var replay = Build(insight);
        Assert.Equal(["before", "after"], replay.Clips.Select(c => c.Key));
        var before = replay.Clips[0];
        var after = replay.Clips[1];
        var baseline = insight.PrimaryEvidence!.BaselineWindow!;
        var recent = insight.PrimaryEvidence.RecentWindow!;
        Assert.InRange(before.StartSecond, baseline.FromSecond - 20, baseline.ToSecond);
        Assert.InRange(after.EndSecond, recent.FromSecond, insight.DetectedAtSecond + 10);
        Assert.True(before.EndSecond <= after.StartSecond);
        Assert.Contains(after.Events, e => e.IsEvidence);
        Assert.All(replay.Clips, c => Assert.Contains(c.Overlays, o => o.Kind == "Channel" && o.Label == "INSIGHT_CHANNEL:Right"));
        Assert.All(replay.Clips, c => Assert.Equal(-1, c.Camera.AttackDirection));
        Assert.Contains("WHA-17", replay.RelevantPlayerIds);
    }

    [Fact]
    public void Every_flagship_insight_produces_a_playable_replay()
    {
        foreach (var insight in Analysis.Insights)
        {
            var replay = Build(insight);
            Assert.NotEmpty(replay.Clips);
            Assert.All(replay.Clips, c =>
            {
                Assert.NotEmpty(c.Events);
                Assert.NotEmpty(c.Ball);
                Assert.Equal(22, c.Tracks.Count);
            });
        }
    }

    [Fact]
    public void Replay_is_deterministic()
    {
        var a = JsonSerializer.Serialize(Build(PressTurnover));
        var b = JsonSerializer.Serialize(ReplayBuilder.ForInsight(SyntheticMatchGenerator.GenerateFlagship().Match, PressTurnover));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Lineup_reflects_substitutions()
    {
        var lineup = ReplayBuilder.LineupAt(Flagship, 60 * 60);
        Assert.Contains(lineup, p => p.Id == "WHA-17");
        Assert.DoesNotContain(lineup, p => p.Id == "WHA-11");
        Assert.Equal(22, lineup.Count);
    }

    [Fact]
    public void Estimates_follow_the_ball_and_mirror_for_the_away_team()
    {
        var player = Flagship.HomeTeam.Squad.Single(p => p.Id == "NBR-09");
        var deep = ReplayBuilder.Estimate(player, true, (30, 50));
        var high = ReplayBuilder.Estimate(player, true, (80, 50));
        Assert.True(high.X > deep.X, "The block moves up the pitch with the ball");

        var away = Flagship.AwayTeam.Squad.Single(p => p.Id == "WHA-09");
        var est = ReplayBuilder.Estimate(away, false, (50, 50));
        Assert.True(est.X < 50, "Away striker estimate sits in the home half (away attacks towards X = 0)");
    }

    /// <summary>The flagship match as an external feed would deliver it: same events and lineups, no behaviour profiles.</summary>
    private static Match WithoutProfiles(Match m)
    {
        static Team Strip(Team t) => t with { Squad = t.Squad.Select(p => p with { Profile = new BehaviourProfile() }).ToList() };
        return m with { HomeTeam = Strip(m.HomeTeam), AwayTeam = Strip(m.AwayTeam) };
    }

    [Fact]
    public void Replay_works_for_data_without_generator_profiles()
    {
        var external = WithoutProfiles(Flagship);
        Assert.All(external.HomeTeam.Squad, p => Assert.False(ReplayBuilder.HasProfilePosition(p)));
        var insights = LiveMatchAnalyzer.AnalyzeFull(external).Insights;
        Assert.Equal(Analysis.Insights.Select(i => i.Id), insights.Select(i => i.Id)); // analytics never used profiles

        foreach (var insight in insights)
        {
            foreach (var clip in ReplayBuilder.ForInsight(external, insight).Clips)
            {
                Assert.Equal(22, clip.Tracks.Count);
                // Outfield players of a team must be spread out, not stacked on the centre spot.
                foreach (var team in new[] { external.HomeTeam.Id, external.AwayTeam.Id })
                {
                    var points = clip.Tracks
                        .Where(t => t.PlayerId.StartsWith(team, StringComparison.Ordinal) && !t.PlayerId.EndsWith("-01", StringComparison.Ordinal) && !t.PlayerId.EndsWith("-13", StringComparison.Ordinal))
                        .Select(t => t.Samples[0]).ToList();
                    for (var a = 0; a < points.Count; a++)
                        for (var b = a + 1; b < points.Count; b++)
                            Assert.True(Math.Abs(points[a].X - points[b].X) + Math.Abs(points[a].Y - points[b].Y) > 1.5,
                                $"{insight.Id}: players stacked at ({points[a].X},{points[a].Y})");
                    var spreadY = points.Max(p => p.Y) - points.Min(p => p.Y);
                    Assert.True(spreadY > 40, $"{insight.Id}: team {team} only spans {spreadY} across the pitch");
                }
            }
        }
    }

    [Fact]
    public void Anchors_follow_each_players_own_events()
    {
        var external = WithoutProfiles(Flagship);
        var lineup = ReplayBuilder.LineupAt(external, 60 * 60);
        var anchors = ReplayBuilder.RoleAnchors(external, lineup, 60 * 60);
        // Right-back: his events are on his right, so the anchor is too, without any profile.
        var rightBack = lineup.Single(p => p.Id == "NBR-02");
        Assert.True(anchors[rightBack.Id].Y > 60, $"right-back anchor Y {anchors[rightBack.Id].Y}");
        var observed = external.Events.Where(e => e.PlayerId == rightBack.Id && e.HasLocation && e.MatchSecond <= 3600).Average(e => e.StartY!.Value);
        Assert.InRange(anchors[rightBack.Id].Y, Math.Min(observed, 85) - 1, Math.Max(observed, 85) + 1);
        Assert.Equal(22, anchors.Count);
    }

    [Fact]
    public void Role_defaults_spread_players_who_share_a_role()
    {
        var a = ReplayBuilder.SpreadRole(ReplayBuilder.RoleDefault(Position.CentreBack), 0, 2);
        var b = ReplayBuilder.SpreadRole(ReplayBuilder.RoleDefault(Position.CentreBack), 1, 2);
        Assert.Equal(38, a.Y);
        Assert.Equal(62, b.Y);
        Assert.Equal(ReplayBuilder.RoleDefault(Position.Striker), ReplayBuilder.SpreadRole(ReplayBuilder.RoleDefault(Position.Striker), 0, 1));
        Assert.True(ReplayBuilder.RoleDefault(Position.LeftWinger).Y < 33 && ReplayBuilder.RoleDefault(Position.RightBack).Y > 66);
    }
}
