using System.Collections.Generic;

namespace TowerDefence
{
    // 런타임 상태: 한 판 = 객체 하나 (DataArchitecture 5절).
    // 사망·클리어 시 필드를 되돌리지 않고 통째로 새로 만든다.
    // 각 필드는 소유 모듈 한 곳만 쓴다 (DataArchitecture 3절).
    public class RunState
    {
        public int Floor;          // 소유: GameFlow
        public int Wave;           // 소유: GameFlow (층 안 웨이브 번호)
        public int TotalWaveIndex; // 소유: GameFlow (누적 웨이브 g)
        public int Cash;           // 소유: PlayerState
        public int NexusHp;        // 소유: PlayerState
        public MapData Map;        // 소유: Board
        public readonly List<TowerInstance> Towers = new List<TowerInstance>(); // 소유: TowerManager
        public readonly List<TowerDef> RecoveryList = new List<TowerDef>();     // 소유: TowerManager (타워 회수 목록)

        public static RunState CreateNew(GameConfig config)
        {
            // 새 판 시작 값 (DataArchitecture 4절): 1층, 웨이브 0, 누적 g 0, 시작 캐시·체력은 GameConfig.
            return new RunState
            {
                Floor = 1,
                Wave = 0,
                TotalWaveIndex = 0,
                Cash = config.startCash,
                NexusHp = config.nexusHp,
            };
        }
    }
}
