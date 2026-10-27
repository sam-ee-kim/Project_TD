using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence
{
    // 타워 정보 패널 (타워 목록 아이콘 클릭). 지금은 TowerDef 값으로 채운다.
    // 설명(description)과 항목 구성은 나중에 데이터로 옮길 수 있게 이 클래스 한 곳에서만 만든다.
    public class TowerInfoView
    {
        static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.09f, 0.94f);
        static readonly Color Accent = new Color(1f, 0.85f, 0.45f);
        const string NoDescription = "설명 준비 중";

        GameObject root;
        RawImage icon;
        Text title, description, stats;

        public TowerDef Shown { get; private set; }
        public bool Visible => root != null && root.activeSelf;

        public void Init(Transform canvas, Action onClose)
        {
            var rt = BasicUi.Rect("TowerInfo", canvas, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 300f), new Vector2(460f, 520f));
            BasicUi.OwnCanvas(rt);
            BasicUi.Panel(rt, PanelColor);
            root = rt.gameObject;

            var titleRt = BasicUi.Rect("Title", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(170f, -18f), new Vector2(230f, 50f));
            title = BasicUi.Label(titleRt, "", 30, TextAnchor.MiddleLeft, Accent);

            var iconRt = BasicUi.Rect("Icon", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(140f, 140f));
            icon = iconRt.gameObject.AddComponent<RawImage>();
            icon.raycastTarget = false;

            var descRt = BasicUi.Rect("Description", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(170f, -72f), new Vector2(270f, 90f));
            description = BasicUi.Label(descRt, "", 20, TextAnchor.UpperLeft, new Color(0.85f, 0.85f, 0.85f));

            var statsRt = BasicUi.Rect("Stats", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -176f), new Vector2(412f, 320f));
            stats = BasicUi.Label(statsRt, "", 22, TextAnchor.UpperLeft, Color.white);
            stats.lineSpacing = 1.25f;

            BasicUi.Button(rt, "Close", "닫기", new Vector2(1f, 0f), new Vector2(-70f, 34f), new Vector2(110f, 46f), onClose, out _);
            root.SetActive(false);
        }

        public void Show(TowerDef def, Texture iconTexture)
        {
            if (root == null || def == null)
                return;
            Shown = def;
            title.text = def.displayName;
            description.text = string.IsNullOrEmpty(def.description) ? NoDescription : def.description;
            icon.texture = iconTexture;
            icon.enabled = iconTexture != null;
            stats.text = BuildStats(def);
            root.SetActive(true);
        }

        public void Hide()
        {
            Shown = null;
            if (root != null && root.activeSelf)
                root.SetActive(false);
        }

        // 정보 항목 (클릭 때만 만든다).
        static string BuildStats(TowerDef def)
        {
            int side = def.range * 2 + 1;
            var sb = new StringBuilder();
            sb.AppendLine($"건축 비용   {def.buildCost} 캐시");
            int shots = Mathf.Max(1, def.burstCount);
            sb.AppendLine($"공격력      {def.damage:0.##}" + (def.attackType == AttackType.Cone ? " (틱 피해)" : shots > 1 ? $" × {shots}연발" : ""));
            sb.AppendLine($"공격 간격   {def.interval:0.##}초 (초당 {def.damage * shots / Mathf.Max(0.01f, def.interval):0.##} 피해)");
            if (!string.IsNullOrEmpty(def.bonusTrait) && def.bonusMultiplier != 1f)
                sb.AppendLine($"상성        {TraitName(def.bonusTrait)} 몬스터 {def.bonusMultiplier:0.##}배 피해");
            if (def.attackType == AttackType.Cone)
                sb.AppendLine($"사거리      {def.coneLength:0.#}칸 부채꼴 {def.coneAngle:0}°");
            else if (def.attackType == AttackType.Pulse)
                sb.AppendLine($"사거리      반지름 {def.pulseRadius:0.#}칸 원형 (닿는 적 모두)");
            else
                sb.AppendLine($"사거리      {def.range}칸 ({side}×{side})");
            sb.AppendLine($"공격 방식   {AttackName(def)}");
            sb.AppendLine($"대상 선택   {RuleName(def.targetRule)}");
            sb.AppendLine($"대상 유지   {LockName(def.lockMode)}");
            if (def.typePriority != null && def.typePriority.Length > 0)
                sb.AppendLine($"우선 공격   {string.Join(", ", Array.ConvertAll(def.typePriority, PriorityName))}");
            return sb.ToString().TrimEnd();
        }

        static string AttackName(TowerDef def)
        {
            switch (def.attackType)
            {
                case AttackType.Hitscan: return "즉시 명중";
                case AttackType.Cone: return "범위 공격 (부채꼴 안 모두)";
                case AttackType.Pulse: return "전자파 (원형으로 퍼지며 닿는 적 모두)";
                default: return $"투사체 (속도 {def.projectileSpeed:0.#}칸/초)";
            }
        }

        // 몬스터 속성 이름 (MonsterDef.traits)
        static string TraitName(string trait)
        {
            switch (trait)
            {
                case "mechanical": return "기계";
                case "flying": return "비행";
                default: return trait;
            }
        }

        static string RuleName(TargetRule rule)
        {
            switch (rule)
            {
                case TargetRule.First: return "가장 앞선 적";
                case TargetRule.Last: return "가장 뒤의 적";
                case TargetRule.LowestHp: return "체력이 가장 낮은 적";
                case TargetRule.HighestHp: return "체력이 가장 높은 적";
                default: return rule.ToString();
            }
        }

        static string LockName(TargetLock mode)
        {
            switch (mode)
            {
                case TargetLock.Lock: return "한 번 잡으면 고정";
                case TargetLock.PreferType: return "고정, 우선 종류가 오면 교체";
                case TargetLock.Retarget: return "매 공격마다 새로 선택";
                default: return mode.ToString();
            }
        }

        static string PriorityName(string entry)
        {
            if (entry == nameof(MonsterCategory.Special)) return "특수 몬스터";
            if (entry == nameof(MonsterCategory.Normal)) return "일반 몬스터";
            return entry;
        }
    }
}
