namespace TowerDefence
{
    // GameFlow 상태 머신 (GDD 2-1).
    public enum GameState
    {
        Ready,
        Break,
        Wave,
        FloorTransition,
        GameOver,
        GameClear,
    }

    // GameFlow.StateChanged 인자 (GDD 8-4).
    public readonly struct StateChange
    {
        public readonly GameState State;
        public readonly int Floor;
        public readonly int Wave;

        public StateChange(GameState state, int floor, int wave)
        {
            State = state;
            Floor = floor;
            Wave = wave;
        }
    }

    // GameFlow.TimerSecondChanged 인자 (GDD 8-4). 초가 바뀔 때만 발행.
    public readonly struct TimerSecond
    {
        public readonly int Seconds;

        public TimerSecond(int seconds) => Seconds = seconds;
    }
}
