using System;
using UnityEngine;

namespace TowerDefence
{
    // 타워가 맞힐 때 붙이는 이상효과 (TowerDef.status). 판정은 MonsterManager.ApplyDamage 한 곳.
    public enum StatusKind
    {
        None,
        Burn,       // 화상 (화염방사): hitsPerStack번 맞으면 1스택, procStacks 스택이면 공격력 × procDamageRatio 추가 피해 후 스택 제거, cooldown 동안 안 쌓임
        Dust,       // 분진: 맞을 때마다 1스택, procStacks 스택이면 공격력 × procDamageRatio 추가 피해 후 스택 제거, cooldown
        Phosphorus, // 백린: 맞을 때마다 1스택, dotInterval초마다 공격력 × min(스택 × dotPerStack, dotMax) 피해 (계속 탄다)
        Frost,      // 서리: 맞을 때마다 1스택, procStacks 스택이면 얼음 (stunSec 동안 정지, stunBonusTrait면 +stunBonusSec)
        Static,     // 정전기: 맞을 때마다 1스택, procStacks 스택이면 감전 (정지, 얼음과 같은 규칙)
    }

    [Serializable]
    public class StatusEffect
    {
        public StatusKind kind = StatusKind.None;
        public int hitsPerStack = 1;      // 이만큼 맞아야 1스택 (화상 5)
        public int procStacks = 3;        // 발동 스택 수
        public float procDamageRatio;     // 발동 추가 피해 = 그때 맞힌 공격력 × 이 비율 (화상 0.2, 분진 0.5)
        public float cooldown;            // 발동 뒤 스택이 쌓이지 않는 시간 (초)
        public float stunSec = 1f;        // 얼음·감전 정지 시간
        public string stunBonusTrait;     // 이 속성 몬스터는 정지가 더 길다 (서리 → biological, 전기 → mechanical)
        public float stunBonusSec = 1f;
        public float dotPerStack = 0.001f; // 백린: 1스택당 초당 공격력의 0.1%
        public float dotMax = 0.2f;        // 백린 최대 20%
        public float dotInterval = 1f;
        public float splashRadius;         // 분진 발동 피해·백린 지속 피해가 맞은 몬스터 주변 이 반지름(칸) 안으로 번진다 (0 = 없음)
        public int splashMaxTargets = 5;   // 번질 때 맞은 몬스터 포함 최대 마리
        public GameObject fxPrefab;        // 얼음·감전 표시 (몬스터 몸에 붙는 이펙트, 정지 시간만큼 재생)
    }

    // MonsterManager.Stunned 인자: 얼음·감전이 걸렸을 때 (표시용).
    public readonly struct MonsterStunned
    {
        public readonly Vector3 Position;  // 몬스터 몸통 가운데
        public readonly float Duration;
        public readonly StatusEffect Effect;

        public MonsterStunned(Vector3 position, float duration, StatusEffect effect)
        {
            Position = position;
            Duration = duration;
            Effect = effect;
        }
    }

    public enum MonsterState
    {
        Inactive, // 풀에서 쉬는 중
        Alive,
        Dying,    // 사망 판정 끝, 5단계 정리 대기 (보상 지급됨)
        Exited,   // 출구 도착, 5단계 정리 대기 (보상 없음)
    }

    public enum MonsterCategory
    {
        Normal,
        Special, // 일반이 아닌 특수 몬스터 (타워 종류 우선도에 사용)
    }

    public enum DamageType
    {
        Normal, // 나중: Splash, Pierce, Slow ...
    }

    public enum DamageResult
    {
        Ignored, // 무효 핸들, 이미 사망·도착
        Damaged,
        Killed,  // 이번 피해로 사망 (보상 1회)
    }

    // 타워 → 몬스터 피해 값. 투사체에 복사해 넘긴다 (DamageDesign 3-3, 7절).
    public readonly struct DamageInfo
    {
        public readonly float Amount;   // 소수 피해 가능 (화염방사 0.5/틱)
        public readonly DamageType Type;
        public readonly string BonusTrait;  // 이 속성(MonsterDef.traits)을 가진 몬스터에게는 BonusMul배 (예: EMP → "mechanical" 2배)
        public readonly float BonusMul;
        public readonly StatusEffect Status; // 맞힐 때 붙이는 이상효과 (없으면 null)

        public DamageInfo(float amount, DamageType type, string bonusTrait = null, float bonusMul = 1f, StatusEffect status = null)
        {
            Amount = amount;
            Type = type;
            BonusTrait = bonusTrait;
            BonusMul = bonusMul;
            Status = status != null && status.kind != StatusKind.None ? status : null;
        }
    }

    // MonsterManager.Killed 인자 (GDD 8-4).
    public readonly struct MonsterKilled
    {
        public readonly int Reward;

        public MonsterKilled(int reward) => Reward = reward;
    }

    // MonsterManager.ReachedExit 인자 (GDD 8-4).
    public readonly struct MonsterReachedExit
    {
        public readonly int Damage;

        public MonsterReachedExit(int damage) => Damage = damage;
    }
}
