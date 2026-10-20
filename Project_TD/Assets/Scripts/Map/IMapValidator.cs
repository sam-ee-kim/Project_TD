using System.Collections.Generic;

namespace TowerDefence
{
    // 맵 검사 경계 (GDD 8-2). 규칙 ID별로 독립 검사해 어느 규칙이 깨졌는지 보고한다.
    public interface IMapValidator
    {
        ValidationReport Validate(MapData map, MapGenRules rules);
    }

    // 검사 결과. 위반이 없으면 유효한 맵.
    public class ValidationReport
    {
        public readonly List<RuleViolation> Violations = new List<RuleViolation>();

        public bool IsValid => Violations.Count == 0;
    }

    // 규칙 하나의 위반 (RuleId = GDD 7장 규칙 ID, 예: "B2").
    public readonly struct RuleViolation
    {
        public readonly string RuleId;
        public readonly string Detail;

        public RuleViolation(string ruleId, string detail)
        {
            RuleId = ruleId;
            Detail = detail;
        }
    }
}
