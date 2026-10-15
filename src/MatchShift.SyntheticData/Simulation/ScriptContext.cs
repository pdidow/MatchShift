using MatchShift.Core.Domain;

namespace MatchShift.SyntheticData.Simulation;

/// <summary>
/// Fluent API used by <see cref="ScriptedBeat"/>s to write fixed event sequences
/// (goals, substitutions, the flagship dangerous turnover). Coordinates are always
/// in the frame of the team that currently has the ball, matching <see cref="Pitch"/>.
/// </summary>
public sealed class ScriptContext
{
    private readonly MatchSimulator _sim;

    internal ScriptContext(MatchSimulator sim) => _sim = sim;

    public int Second => _sim.Second;

    private string TeamId(TeamSide side) => side == TeamSide.Home ? _sim.Script.Home.Id : _sim.Script.Away.Id;

    /// <summary>
    /// Give the ball to <paramref name="shirt"/> of <paramref name="side"/> at (x, y).
    /// If possession changes hands, a ball-recovery event is recorded for the gaining team.
    /// </summary>
    public ScriptContext Gain(TeamSide side, int shirt, double x, double y)
    {
        var teamId = TeamId(side);
        var player = _sim.OnPitch(teamId, shirt);
        if (_sim.PossessionTeamId != teamId)
        {
            _sim.Emit(EventType.BallRecovery, teamId, player.Id, x, y, outcome: EventOutcome.Won);
            _sim.SetPossession(teamId, player, x, y, allowCounter: false);
        }
        else
        {
            _sim.MoveBall(player, x, y);
        }
        return Wait(1);
    }

    /// <summary>Completed pass from the current holder to a team-mate.</summary>
    public ScriptContext Pass(int toShirt, double x, double y, int seconds = 3, string? qualifier = null)
    {
        var to = _sim.OnPitch(_sim.PossessionTeamId, toShirt);
        _sim.Emit(EventType.Pass, _sim.PossessionTeamId, _sim.Holder.Id, _sim.BallX, _sim.BallY, x, y,
            EventOutcome.Complete, to.Id, qualifier);
        _sim.MoveBall(to, x, y);
        return Wait(seconds);
    }

    public ScriptContext Carry(double x, double y, int seconds = 3)
    {
        _sim.Emit(EventType.Carry, _sim.PossessionTeamId, _sim.Holder.Id, _sim.BallX, _sim.BallY, x, y, EventOutcome.Complete);
        _sim.MoveBall(_sim.Holder, x, y);
        return Wait(seconds);
    }

    /// <summary>
    /// The holder plays a pass towards (x, y) that is intercepted by
    /// <paramref name="interceptorShirt"/> of the opposing team. Possession flips.
    /// </summary>
    public ScriptContext PassIntercepted(int intendedShirt, double x, double y, int interceptorShirt, int seconds = 2)
    {
        var team = _sim.PossessionTeamId;
        var opponent = _sim.OpponentOf(team);
        var intended = _sim.OnPitch(team, intendedShirt);
        _sim.Emit(EventType.Pass, team, _sim.Holder.Id, _sim.BallX, _sim.BallY, x, y, EventOutcome.Incomplete, intended.Id);
        _sim.Second += 1;
        var (fx, fy) = Pitch.Flip(x, y);
        var interceptor = _sim.OnPitch(opponent, interceptorShirt);
        _sim.Emit(EventType.Interception, opponent, interceptor.Id, fx, fy, outcome: EventOutcome.Won);
        _sim.SetPossession(opponent, interceptor, fx, fy, allowCounter: false);
        return Wait(seconds);
    }

    /// <summary>The holder is tackled by <paramref name="tacklerShirt"/>. Possession flips.</summary>
    public ScriptContext Dispossessed(int tacklerShirt, int seconds = 2)
    {
        var team = _sim.PossessionTeamId;
        var opponent = _sim.OpponentOf(team);
        var (fx, fy) = Pitch.Flip(_sim.BallX, _sim.BallY);
        var tackler = _sim.OnPitch(opponent, tacklerShirt);
        _sim.Emit(EventType.Tackle, opponent, tackler.Id, fx, fy, outcome: EventOutcome.Won, related: _sim.Holder.Id);
        _sim.Emit(EventType.Turnover, team, _sim.Holder.Id, _sim.BallX, _sim.BallY, outcome: EventOutcome.Lost, qualifier: "Dispossessed");
        _sim.SetPossession(opponent, tackler, fx, fy, allowCounter: false);
        return Wait(seconds);
    }

    /// <summary>Shot by the current holder; play then continues per the outcome (save, corner, kick-off...).</summary>
    public ScriptContext Shot(EventOutcome outcome, string? qualifier = null)
    {
        _sim.ResolveShot(outcome, qualifier);
        return this;
    }

    public ScriptContext Corner(TeamSide side)
    {
        _sim.Corner(TeamId(side));
        return this;
    }

    public ScriptContext Substitute(TeamSide side, int offShirt, int onShirt)
    {
        _sim.Substitute(TeamId(side), offShirt, onShirt);
        return Wait(30);
    }

    public ScriptContext Wait(int seconds)
    {
        _sim.Second += seconds;
        return this;
    }
}
