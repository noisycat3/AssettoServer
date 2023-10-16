using AssettoServer.Server.Ai.Splines;
using AssettoServer.Shared.Model;
using System;

namespace AssettoServer.Server.Ai;

/// <summary>
/// The AI Brain processes behaviour tree and produces ControlInput, which is translated to 
/// </summary>
public class AiBrain
{
    public enum ELightsState
    {
        Off,
        Road,
        HighBeam,
    }

    public enum EIndicatorState
    {
        Off,
        Left,
        Right,
        Hazards
    }

    public struct ControlInput
    {
        public float Acceleration;
        public float Brakes;
        public float Steering;

        public ELightsState Lights;
        public EIndicatorState Indicator;
        public bool IsHonking;
    }

    private IEntryCar? _car;
    private CarStatus? _status;

    //private readonly Func<EntryCar, AiState> _aiStateFactory;
    private readonly AiSpline? _spline;
}

