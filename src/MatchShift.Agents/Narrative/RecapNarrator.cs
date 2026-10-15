using MatchShift.Core.Insights;

namespace MatchShift.Agents.Narrative;

/// <summary>Renders <see cref="RecapFacts"/> into a recap in one of four styles. Tells a story, not a stat dump.</summary>
public static class RecapNarrator
{
    public const int SocialMaxLength = 280;

    public static RecapNarrative Narrate(RecapFacts recap, RecapStyle style, NarrativeContext context, IReadOnlyList<Insight> insights)
    {
        var lex = LexiconRegistry.Get(context.Profile.Language);
        var roster = context.Roster;
        var slots = BaseSlots(recap, roster, lex);
        string T(string key, NarrativeSlots? s = null, string? variant = null) => NarrativeSlots.Fill(lex.Get(key, variant), s ?? slots).Text;

        var title = T($"recap.title.{recap.Period}");
        slots["title"] = title;
        var result = T(recap.Winner is null ? "recap.result.draw" : "recap.result.lead", variant: recap.Period.ToString());

        string Momentum(MomentumPeriod m, bool casual)
        {
            var s = BaseSlots(recap, roster, lex);
            s["team"] = roster.Team(m.TeamId);
            s["from"] = m.FromMinute.ToString(lex.Culture);
            s["to"] = m.ToMinute.ToString(lex.Culture);
            s["control"] = lex.Format(m.AverageControl, MetricUnit.Index);
            return NarrativeSlots.Fill(lex.Get(casual ? "recap.momentum.casual" : "recap.momentum"), s).Text;
        }

        string Goal(GoalSummary g, bool casual)
        {
            var s = BaseSlots(recap, roster, lex);
            s["team"] = roster.Team(g.TeamId);
            s["scorer"] = roster.Player(g.ScorerId);
            s["minute"] = g.DisplayMinute.ToString(lex.Culture);
            s["homeScore"] = g.HomeScore.ToString(lex.Culture);
            s["awayScore"] = g.AwayScore.ToString(lex.Culture);
            s["assister"] = roster.Player(g.AssistId);
            s["assistPhrase"] = string.IsNullOrEmpty(s["assister"]) ? "" : NarrativeSlots.Fill(lex.Get("assistPhrase"), s).Text;
            return NarrativeSlots.Fill(lex.Get(casual ? "recap.goal.casual" : "recap.goal"), s).Text;
        }

        string Player(PlayerContribution p, bool casual)
        {
            var s = BaseSlots(recap, roster, lex);
            s["player"] = roster.Player(p.PlayerId);
            s["team"] = roster.Team(p.TeamId);
            s["goals"] = p.Goals.ToString(lex.Culture);
            s["assists"] = p.Assists.ToString(lex.Culture);
            s["keyPasses"] = p.KeyPasses.ToString(lex.Culture);
            s["shots"] = p.Shots.ToString(lex.Culture);
            return NarrativeSlots.Fill(lex.Get(casual ? "recap.player.casual" : "recap.player"), s).Text;
        }

        string AttackSides(TeamRecapStats t)
        {
            var s = BaseSlots(recap, roster, lex);
            s["team"] = roster.Team(t.TeamId);
            s["left"] = lex.Format(t.LeftAttackShare, MetricUnit.Share);
            s["centre"] = lex.Format(t.CentreAttackShare, MetricUnit.Share);
            s["right"] = lex.Format(t.RightAttackShare, MetricUnit.Share);
            return NarrativeSlots.Fill(lex.Get("recap.attackSides"), s).Text;
        }

        string Peak(PressurePeak p)
        {
            var s = BaseSlots(recap, roster, lex);
            s["team"] = roster.Team(p.TeamId);
            s["value"] = lex.Format(p.Value, MetricUnit.Index);
            s["minute"] = p.Minute.ToString(lex.Culture);
            return NarrativeSlots.Fill(lex.Get("recap.pressurePeak"), s).Text;
        }

        InsightNarrative Insight(Insight i, ViewerMode mode) =>
            TemplateInsightProvider.Narrate(i, context with { Profile = context.Profile with { Mode = mode } });

        var sections = new List<RecapSection>();
        switch (style)
        {
            case RecapStyle.Casual:
            {
                var story = new List<string> { lex.Get("recap.casual.intro") };
                foreach (var beat in recap.Story)
                {
                    if (beat.Code == "momentum")
                    {
                        var m = recap.MomentumPeriods.FirstOrDefault(p => p.TeamId == beat.TeamId && p.FromMinute == beat.Minute);
                        if (m is not null) story.Add(Momentum(m, casual: true));
                    }
                    else if (beat.Code == "goal")
                    {
                        var g = recap.Goals.First(x => x.InsightId == beat.InsightId);
                        story.Add(Goal(g, casual: true));
                    }
                    else if (beat.InsightId is not null && insights.FirstOrDefault(i => i.Id == beat.InsightId) is { } insight)
                    {
                        var n = Insight(insight, ViewerMode.TeachMe);
                        story.Add($"{n.Headline}. {n.Inference}");
                    }
                }
                sections.Add(new RecapSection("", [result]));
                sections.Add(new RecapSection(lex.Get("recap.heading.story"), story));
                if (recap.KeyPlayers.Count > 0) sections.Add(new RecapSection("", [Player(recap.KeyPlayers[0], casual: true)]));
                break;
            }

            case RecapStyle.Analyst:
            {
                sections.Add(new RecapSection(lex.Get("recap.heading.result"),
                    [result, T("recap.stats"), AttackSides(recap.Home), AttackSides(recap.Away), T("recap.chaos")]));
                sections.Add(new RecapSection(lex.Get("recap.heading.momentum"),
                    [.. recap.MomentumPeriods.Select(m => Momentum(m, casual: false)), .. recap.PressurePeaks.Select(Peak)]));
                var shifts = recap.KeyShifts.Select(i => Insight(i, ViewerMode.Analyst)).Select(n => $"{n.Headline}: {n.Fact}").ToList();
                sections.Add(new RecapSection(lex.Get("recap.heading.shifts"), shifts.Count > 0 ? shifts : [lex.Get("recap.noShifts")]));
                sections.Add(new RecapSection(lex.Get("recap.heading.story"), recap.Goals.Select(g => Goal(g, casual: false)).ToList()));
                sections.Add(new RecapSection(lex.Get("recap.heading.players"), recap.KeyPlayers.Take(4).Select(p => Player(p, casual: false)).ToList()));
                break;
            }

            case RecapStyle.Broadcast:
            {
                var lines = new List<string> { T("recap.broadcast.open") };
                foreach (var beat in recap.Story)
                {
                    string? line = beat.Code switch
                    {
                        "momentum" => recap.MomentumPeriods.FirstOrDefault(p => p.TeamId == beat.TeamId && p.FromMinute == beat.Minute) is { } m
                            ? Momentum(m, casual: false) : null,
                        _ => beat.InsightId is not null && insights.FirstOrDefault(i => i.Id == beat.InsightId) is { } insight
                            ? Insight(insight, ViewerMode.Studio).StudioLine : null,
                    };
                    if (line is null) continue;
                    var s = BaseSlots(recap, roster, lex);
                    s["minute"] = beat.Minute.ToString(lex.Culture);
                    s["line"] = line;
                    lines.Add(NarrativeSlots.Fill(lex.Get("recap.broadcast.beat"), s).Text);
                }
                lines.Add(result);
                lines.Add(lex.Get("recap.broadcast.close"));
                sections.Add(new RecapSection("", lines));
                break;
            }

            case RecapStyle.Social:
            {
                var top = recap.KeyShifts.OrderByDescending(i => i.Importance).FirstOrDefault();
                var s = BaseSlots(recap, roster, lex);
                s["title"] = title;
                s["headline"] = top is null ? result : Insight(top, ViewerMode.WhatMatters).Headline;
                var post = NarrativeSlots.Fill(lex.Get("recap.social"), s).Text;
                if (post.Length > SocialMaxLength) post = post[..(SocialMaxLength - 1)] + "…";
                sections.Add(new RecapSection("", [post]));
                break;
            }
        }

        if (style != RecapStyle.Social) sections.Add(new RecapSection("", [lex.Get("recap.synthetic")]));

        return new RecapNarrative
        {
            MatchId = recap.MatchId,
            Period = recap.Period,
            Style = style,
            Language = lex.Code,
            Title = title,
            Sections = sections,
        };
    }

    private static NarrativeSlots BaseSlots(RecapFacts r, MatchRoster roster, Lexicon lex)
    {
        var s = new NarrativeSlots
        {
            ["homeTeam"] = roster.Team(r.HomeTeamId),
            ["awayTeam"] = roster.Team(r.AwayTeamId),
            ["homeScore"] = r.HomeScore.ToString(lex.Culture),
            ["awayScore"] = r.AwayScore.ToString(lex.Culture),
            ["homePossession"] = lex.Format(r.Home.PossessionShare, MetricUnit.Share),
            ["awayPossession"] = lex.Format(r.Away.PossessionShare, MetricUnit.Share),
            ["homeShots"] = r.Home.Shots.ToString(lex.Culture),
            ["awayShots"] = r.Away.Shots.ToString(lex.Culture),
            ["homeQuality"] = lex.Format(r.Home.ChanceQuality, MetricUnit.Quality),
            ["awayQuality"] = lex.Format(r.Away.ChanceQuality, MetricUnit.Quality),
            ["share"] = lex.Format(r.ChaoticMinuteShare, MetricUnit.Share),
        };
        if (r.Winner is { } winner)
        {
            var homeWins = winner == r.HomeTeamId;
            s["leader"] = roster.Team(winner);
            s["trailer"] = roster.Team(homeWins ? r.AwayTeamId : r.HomeTeamId);
            s["leaderScore"] = (homeWins ? r.HomeScore : r.AwayScore).ToString(lex.Culture);
            s["trailerScore"] = (homeWins ? r.AwayScore : r.HomeScore).ToString(lex.Culture);
        }
        return s;
    }
}
