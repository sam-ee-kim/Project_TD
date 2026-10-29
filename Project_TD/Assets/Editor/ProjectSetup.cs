using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 프로젝트 복구용 원클릭 셋업. 여러 번 눌러도 이미 있는 에셋은 덮어쓰지 않고 빠진 것만 채운다.
    // 데이터 에셋 → 타일·타워·이펙트·몬스터 프리팹 → SampleScene 배치 순서로 만든다.
    public static class ProjectSetup
    {
        const string DataFolder = "Assets/Data";
        const string TileModelFolder = "Assets/Models/Tiles";
        const string TowerModelFolder = "Assets/Models/Towers";
        const string PlaceholderFolder = "Assets/Prefabs/Tiles";
        const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("TowerDefence/Setup Project (All)")]
        public static void SetupAll()
        {
            SetupData();
            EnsureMeshesReadable();
            SetupTowerPrefabs();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.PrefabPath) == null)
                MuzzleFxBuilder.Build();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.FlamePrefabPath) == null)
                MuzzleFxBuilder.BuildFlameSpray();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.LightningBoltPath) == null)
                MuzzleFxBuilder.BuildLightning();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.NanoFieldPath) == null)
                MuzzleFxBuilder.BuildNano();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.EmpWavePath) == null)
                MuzzleFxBuilder.BuildEmp();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.CryoShardPath) == null)
                MuzzleFxBuilder.BuildTier2();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.PlasmaJetPath) == null)
                MuzzleFxBuilder.BuildFlameTier2();
            if (AssetDatabase.IsValidFolder("Assets/Models/Monsters"))
                MonsterPrefabBuilder.BuildAll();
            LinkViewPrefabs();
            SetupScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] 완료");
        }

        // ---------- 데이터 에셋 ----------

        [MenuItem("TowerDefence/Setup/Data Assets")]
        public static void SetupData()
        {
            EnsureFolder(DataFolder);
            EnsureFolder(DataFolder + "/Monsters");
            EnsureFolder(DataFolder + "/Towers");

            GetOrCreate<GameConfig>(DataFolder + "/GameConfig.asset", null);
            GetOrCreate<MapGenRules>(DataFolder + "/MapGenRules.asset", null);

            GetOrCreate<MonsterDef>(DataFolder + "/Monsters/normal.asset", d =>
            {
                d.id = "normal"; d.displayName = "일반 몬스터"; d.category = MonsterCategory.Normal;
                d.hp = 4f; d.speed = 1f; d.reward = 5; d.nexusDamage = 1;
                d.spawnIdleSec = 0.5f; d.viewScale = 0.25f; d.aimHeight = 0.25f;
            });
            GetOrCreate<MonsterDef>(DataFolder + "/Monsters/fast.asset", d =>
            {
                d.id = "fast"; d.displayName = "해머헤드"; d.category = MonsterCategory.Special; // 빠르고 약함 (모델: Monster_Hammerhead)
                d.hp = 2f; d.speed = 2f; d.reward = 4; d.nexusDamage = 1;
                d.viewScale = 0.22f;    // 사버팽(0.25)보다 조금 작게: 전체 크기(대각선) 약 85%. 납작하고 넓은 몸 (폭 약 0.62칸)
                d.aimHeight = 0.12f;    // 모델 높이 0.71 × 0.22 = 0.16 → 몸통 가운데보다 살짝 위
                d.walkAnimSpeed = 2f;   // 이동 속도 2배라 걷기도 2배속 (발이 미끄러지지 않게)
            });
            GetOrCreate<MonsterDef>(DataFolder + "/Monsters/tank.asset", d =>
            {
                d.id = "tank"; d.displayName = "바스티온"; d.category = MonsterCategory.Special; // 탱커 (모델: Monster_Bastion)
                d.hp = 12f; d.speed = 0.5f; d.reward = 10; d.nexusDamage = 2;
                d.viewScale = 0.35f; // 일반(0.25)보다 1.4배: 탱커라 크고 묵직하게 (폭 약 0.86칸)
                d.aimHeight = 0.39f; // 모델 높이 2.18 × viewScale 0.35의 절반쯤 (몸통 가운데)
            });
            GetOrCreate<TowerDef>(DataFolder + "/Towers/basic.asset", d =>
            {
                d.id = "basic"; d.displayName = "기본 타워";
                d.damage = 1f; d.interval = 1f;
                d.burstCount = 2; d.burstGap = 0.12f; // 이점사: 1발 1 피해 × 2
            });
            // 화염방사: 앞쪽 90° 부채꼴(길이 1.6칸) 안의 적 모두에게 0.2초마다 0.5 틱 피해
            GetOrCreate<TowerDef>(DataFolder + "/Towers/flamethrower.asset", d =>
            {
                d.id = "flamethrower"; d.displayName = "화염방사 타워";
                ApplyFlamethrowerStats(d);
            });
            // 번개: 구슬에 전기를 모았다가(차지 0.96초) 몬스터에게 번개를 날린다. 한 방이 크고 사거리가 길다 (값은 임시)
            GetOrCreate<TowerDef>(DataFolder + "/Towers/lightning.asset", ApplyLightningStats);
            // 나노봇: 발사구 7개에서 나노 탄을 연달아 쏘고, 첫 발이 맞은 자리에 감속 나노필드를 깐다 (값은 임시)
            GetOrCreate<TowerDef>(DataFolder + "/Towers/nano.asset", ApplyNanoStats);
            // EMP: 전기를 모았다가(0.42초) 타워 둘레로 원형 전자파를 방출, 파면이 닿는 몬스터 모두에게 피해 (값은 임시)
            GetOrCreate<TowerDef>(DataFolder + "/Towers/emp.asset", ApplyEmpStats);
            // 2단계 타워 (지금은 테스트로 건축 목록에 표시, 나중에 진화 전용)
            GetOrCreate<TowerDef>(DataFolder + "/Towers/minigun.asset", ApplyMinigunStats);
            GetOrCreate<TowerDef>(DataFolder + "/Towers/electro.asset", ApplyElectroStats);
            GetOrCreate<TowerDef>(DataFolder + "/Towers/cryo.asset", ApplyCryoStats);
            GetOrCreate<TowerDef>(DataFolder + "/Towers/dust.asset", ApplyDustStats);
            GetOrCreate<TowerDef>(DataFolder + "/Towers/phosphorus.asset", ApplyPhosphorusStats);
            GetOrCreate<TowerDef>(DataFolder + "/Towers/plasma.asset", ApplyPlasmaStats);
            AssetDatabase.SaveAssets();
        }

        // 화염방사 타워 값 (만들 때·메뉴로 다시 맞출 때)
        static void ApplyFlamethrowerStats(TowerDef d)
        {
            d.description = "앞쪽 90° 부채꼴로 불길을 뿜어 범위 안의 적 모두에게 피해를 줍니다. 사거리가 짧습니다.";
            d.buildCost = 70; d.damage = 0.5f; d.interval = 0.2f; d.range = 2; // 0.2초마다 0.5 틱 피해 (초당 2.5)
            d.attackType = AttackType.Cone; d.coneAngle = 90f; d.coneLength = 1.6f;
            d.lockMode = TargetLock.Lock; d.targetRule = TargetRule.First;
        }

        // 번개 타워 값 (만들 때·메뉴로 다시 맞출 때). 차지 0.96초 + 재장전 1초 = 약 2초에 4 피해 (초당 2)
        static void ApplyLightningStats(TowerDef d)
        {
            d.id = "lightning"; d.displayName = "번개 타워";
            d.description = "구슬에 전기를 모았다가 몬스터에게 번개를 날립니다. 한 방이 강하고 사거리가 깁니다.";
            d.buildCost = 90; d.damage = 4f; d.interval = 1f; d.range = 3;
            d.chargeTime = 0.96f; d.burstCount = 1;
            d.attackType = AttackType.Projectile; d.projectileSpeed = 7f; // 날아가는 번개가 눈에 보이게 d.projectileTurnSpeed = 1440f;
            d.projectileScale = 1f; d.projectileModelForward = Vector3.forward;
            d.lockMode = TargetLock.Lock; d.targetRule = TargetRule.First;
            d.viewScale = 0.3f;
        }

        // 나노봇 타워 값. 7발 × 0.3 = 2.1 피해를 2.3초마다 (초당 약 0.9) + 반지름 1칸 필드 3초 동안 이동 속도 60% 감소
        static void ApplyNanoStats(TowerDef d)
        {
            d.id = "nano"; d.displayName = "나노봇 타워";
            d.description = "발사구 7개에서 나노 탄을 연달아 쏩니다. 탄은 맞은 몬스터에게만 피해를 주고, 첫 탄이 떨어진 자리에 나노필드를 깔아 지나가는 몬스터를 느리게 합니다.";
            d.buildCost = 80; d.damage = 0.3f; d.interval = 2.3f; d.range = 2;
            d.burstCount = 7; d.burstGap = 0.045f; // 발사 연출의 발사구 셀 간격 (0.04초)에 맞춘 파바박
            d.attackType = AttackType.Projectile; d.projectileSpeed = 9f; d.projectileTurnSpeed = 1080f;
            d.projectileScale = 1f; d.projectileModelForward = Vector3.forward;
            d.lockMode = TargetLock.Lock; d.targetRule = TargetRule.First;
            d.fieldRadius = 1f; d.fieldDuration = 3f; d.fieldSlow = 0.6f;
            d.fieldFxRadius = MuzzleFxBuilder.NanoFieldModelRadius;
            d.viewScale = 0.3f;
        }

        // EMP 타워 값. 차지 0.42초(발사 연출의 방출 시점) + 재장전 1.6초 ≈ 2초마다 반지름 2칸 안 모두에게 1 피해 (기계 몬스터 2)
        static void ApplyEmpStats(TowerDef d)
        {
            d.id = "emp"; d.displayName = "EMP 타워";
            d.description = "전기를 모았다가 타워 둘레로 원형 전자파를 내보냅니다. 퍼지는 파동에 닿은 몬스터 모두가 피해를 입고, 기계 몬스터는 2배 피해를 입습니다.";
            d.buildCost = 100; d.damage = 1f; d.interval = 1.6f; d.range = 2;
            d.bonusTrait = "mechanical"; d.bonusMultiplier = 2f; // 기계 속성 2배
            d.chargeTime = 0.42f; d.burstCount = 1;
            d.attackType = AttackType.Pulse;
            d.pulseRadius = MuzzleFxBuilder.EmpWaveRadius; d.pulseSpeed = MuzzleFxBuilder.EmpWaveSpeed; // 이펙트와 같은 값
            d.lockMode = TargetLock.Lock; d.targetRule = TargetRule.First;
            d.viewScale = 0.3f;
        }

        // ---------- 2단계 타워 (값은 임시, Docs/TowerTable 참고) ----------
        // 공통: tier 2, showInPalette = true (테스트). 진화 전용으로 돌릴 때 showInPalette를 끄고 1단계의 upgradeTo에 연결한다.
        static void Tier2Common(TowerDef d)
        {
            d.tier = 2;
            d.unlockFloor = 1;
            d.showInPalette = true;
            d.attackType = AttackType.Projectile;
            d.lockMode = TargetLock.Lock; d.targetRule = TargetRule.First;
            d.projectileScale = 1f; d.projectileModelForward = Vector3.forward;
            d.viewScale = 0.3f;
            d.burstCount = 1;
        }

        // 미니건: 쏠수록 빨라져 1.5초 만에 초당 5발 (0.6초 → 0.2초 간격), 1발 1 피해
        static void ApplyMinigunStats(TowerDef d)
        {
            Tier2Common(d);
            d.id = "minigun"; d.displayName = "미니건 타워";
            d.description = "총열 6개가 돌아가며 쏠수록 빨라집니다. 1.5초 동안 예열하면 초당 5발을 퍼붓습니다.";
            d.buildCost = 150; d.damage = 1f; d.interval = 0.2f; d.range = 2;
            d.spinUpTime = 1.5f; d.spinUpStartInterval = 0.6f; // 예열 클립(Fire_Warmup) 길이와 같게
            d.projectileSpeed = 16f; d.projectileTurnSpeed = 1440f;
        }

        // 전기: 단발 플라즈마탄, 한 방 4 · 1.2초 · 사거리 3 (속성 이상효과는 나중에)
        static void ApplyElectroStats(TowerDef d)
        {
            Tier2Common(d);
            d.id = "electro"; d.displayName = "전기 타워";
            d.description = "코일로 가속한 플라즈마 탄을 한 발씩 쏩니다. 전기 속성 (이상효과는 준비 중).";
            d.buildCost = 140; d.damage = 4f; d.interval = 1.2f; d.range = 3;
            d.projectileSpeed = 12f; d.projectileTurnSpeed = 1080f;
        }

        // 냉기: 단발 얼음탄, 한 방 3 · 1초 · 사거리 2 (감속·빙결 이상효과는 나중에)
        static void ApplyCryoStats(TowerDef d)
        {
            Tier2Common(d);
            d.id = "cryo"; d.displayName = "냉기 타워";
            d.description = "냉각한 얼음 탄을 한 발씩 쏩니다. 냉기 속성 (이상효과는 준비 중).";
            d.buildCost = 130; d.damage = 3f; d.interval = 1f; d.range = 2;
            d.projectileSpeed = 10f; d.projectileTurnSpeed = 1080f;
        }

        // 화염 계열 2단계 공통: 화염방사 타워처럼 0.2초마다 부채꼴 틱 피해. 이펙트 길이 = 히트박스 길이 (크기 1배)
        static void Tier2FlameCommon(TowerDef d, float angle, float length)
        {
            Tier2Common(d);
            d.attackType = AttackType.Cone;
            d.interval = 0.2f;
            d.coneAngle = angle; d.coneLength = length; d.coneFxReach = length;
            d.range = Mathf.CeilToInt(length);
        }

        // 혼합분진: 60° · 1.6칸 · 틱 0.7 (초당 3.5). 폭·길이 = 블렌더 이펙트(FX_Dust)가 퍼지는 모양 (자가폭발은 나중에)
        static void ApplyDustStats(TowerDef d)
        {
            Tier2FlameCommon(d, MuzzleFxBuilder.DustArc, MuzzleFxBuilder.DustReach);
            d.id = "dust"; d.displayName = "혼합분진 타워";
            d.description = "가연성 분진을 섞은 짧고 넓은 불길을 뿜습니다. 튀는 불티와 분진 알갱이가 섞여 있습니다 (분진 자가폭발은 준비 중).";
            d.buildCost = 160; d.damage = 0.7f;
        }

        // 혼합백린: 40° · 1.8칸 · 틱 0.8 (초당 4). 폭·길이 = 블렌더 이펙트(FX_Phosphorus) (남아서 타는 연소 효과는 나중에)
        static void ApplyPhosphorusStats(TowerDef d)
        {
            Tier2FlameCommon(d, MuzzleFxBuilder.PhosphorusArc, MuzzleFxBuilder.PhosphorusReach);
            d.id = "phosphorus"; d.displayName = "혼합백린 타워";
            d.description = "백린을 섞은 주황 불길을 뿜습니다. 하얗게 타는 백린 덩어리와 짙은 흰 연기가 남습니다 (연소 이상효과는 준비 중).";
            d.buildCost = 170; d.damage = 0.8f;
        }

        // 플라즈마: 헤드 고정, 바라보는 방향 일직선 3칸 · 틱 1 (초당 5). 줄 위의 적 모두 (빔 길이 = 3칸)
        static void ApplyPlasmaStats(TowerDef d)
        {
            Tier2FlameCommon(d, MuzzleFxBuilder.PlasmaArc, MuzzleFxBuilder.PlasmaReach);
            d.id = "plasma"; d.displayName = "플라즈마 타워";
            d.description = "자기장으로 모은 플라즈마를 정면 일직선으로 쏩니다. 헤드가 돌지 않고, 바라보는 방향 3칸 위의 적을 모두 태웁니다.";
            d.buildCost = 180; d.damage = 1f;
            d.attackType = AttackType.Line; d.range = 3; // 헤드 고정 일직선
        }

        [MenuItem("TowerDefence/Setup/Flamethrower Stats (Reset)")]
        public static void ResetFlamethrower()
        {
            var d = AssetDatabase.LoadAssetAtPath<TowerDef>(DataFolder + "/Towers/flamethrower.asset");
            if (d == null)
                return;
            ApplyFlamethrowerStats(d);
            d.muzzleFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.FlamePrefabPath);
            EditorUtility.SetDirty(d);
            AssetDatabase.SaveAssets();
        }

        // ---------- 타워 프리팹 (TowerAim 설정) ----------

        [MenuItem("TowerDefence/Setup/Tower Prefabs")]
        public static void SetupTowerPrefabs()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/Towers");
            MakeTower("Tower_TwinCannon", "TT", "TT_Head_Eye",
                new[] { "TT_Gun_Muzzle_L", "TT_Gun_Muzzle_R" }, new[] { "TT_Gun_Recoil_L", "TT_Gun_Recoil_R" }, -25f);
            MakeTower("Tower_Flamethrower", "FT", "FT_Flame_Emitter",
                new[] { "FT_Flame_Emitter" }, null, -30f);
            if (MakeTower("Tower_Lightning", "LT", "LT_Head_Eye", new[] { "LT_Bolt_Emitter" }, null, -20f))
                ConfigureLightningAim();
            // 나노: 발사구 셀 7개 앞의 NT_Shot_Point_0~6을 발사 연출(셀이 0→6 차례로 열림)과 같은 순서로 번갈아 쓴다
            if (MakeTower("Tower_Nano", "NT", "NT_Head_Eye", new[]
                { "NT_Shot_Point_0", "NT_Shot_Point_1", "NT_Shot_Point_2", "NT_Shot_Point_3", "NT_Shot_Point_4", "NT_Shot_Point_5", "NT_Shot_Point_6" }, null, -25f))
                ConfigureNanoAim();
            // EMP: 머리 위 원반(발사 연출이 돌리고 키움) 중심 ET_Pulse_Emitter. 파동은 원형이라 조준은 머리만 돌린다
            if (MakeTower("Tower_EMP", "ET", "ET_Head_Eye", new[] { "ET_Pulse_Emitter" }, null, -15f))
                ConfigureEmpAim();
            // 2단계: 총구 빈 오브젝트(*_Muzzle)에서 쏜다. 반동·코일·냉각 링은 발사 연출이 움직인다
            // 미니건의 MG_Head_Eye는 지붕 옆(가운데가 아님)이라 조준 기준으로 쓰면 총열이 비껴간다 → 총구로 조준
            if (MakeTower("Tower_T2_Minigun", "MG", "MG_Muzzle", new[] { "MG_Muzzle" }, null, -20f))
                ConfigureAnimatedAim("Tower_T2_Minigun", new[] { "MG_Barrel_Spin", "MG_Gun_Recoil" }, 0.6f);
            if (MakeTower("Tower_T2_Electro", "EL", "EL_Head_Eye", new[] { "EL_Muzzle" }, null, -20f))
                ConfigureAnimatedAim("Tower_T2_Electro", new[] { "EL_Gun_Recoil", "EL_Gun_Coil_0", "EL_Gun_Coil_1", "EL_Gun_Coil_2", "EL_Gun_Coil_3" }, 3f);
            if (MakeTower("Tower_T2_Cryo", "CR", "CR_Head_Eye", new[] { "CR_Muzzle" }, null, -20f))
                ConfigureAnimatedAim("Tower_T2_Cryo", new[] { "CR_Gun_Recoil", "CR_Gun_CoolRing_0", "CR_Gun_CoolRing_1", "CR_Gun_CoolRing_2" }, 3f);
            // 화염 계열 2단계: 화염방사 타워처럼 노즐 빈 오브젝트(*_Flame_Emitter)로 조준하고 뿜는다 (Head_Eye는 가운데가 아님)
            if (MakeTower("Tower_T2_Dust", "DU", "DU_Flame_Emitter", new[] { "DU_Flame_Emitter" }, null, -30f))
                ConfigureAnimatedAim("Tower_T2_Dust", new[] { "DU_Auger_Wheel", "DU_Igniter_Tip_0", "DU_Igniter_Tip_1", "DU_Igniter_Tip_2", "DU_Igniter_Tip_3" }, 0f);
            if (MakeTower("Tower_T2_Phosphorus", "WP", "WP_Flame_Emitter", new[] { "WP_Flame_Emitter" }, null, -30f))
                ConfigureAnimatedAim("Tower_T2_Phosphorus", new[] { "WP_Mixer_Wheel", "WP_Nozzle_Tip", "WP_Phos_Window" }, 0f);
            if (MakeTower("Tower_T2_Plasma", "PL", "PL_Flame_Emitter", new[] { "PL_Flame_Emitter" }, null, -30f))
                ConfigureAnimatedAim("Tower_T2_Plasma", new[] { "PL_Generator_Ring_X", "PL_Generator_Ring_Z", "PL_Generator_Core",
                    "PL_Plasma_Sleeve_0", "PL_Plasma_Sleeve_1", "PL_Plasma_Sleeve_2", "PL_Plasma_Sleeve_3" }, 0f);
        }

        // 발사 연출로 움직이는 부품 지정 + 머리 튐 세기 (총구 끝 = 빈 오브젝트 위치 그대로)
        static void ConfigureAnimatedAim(string model, string[] parts, float kickPitch)
        {
            string path = "Assets/Prefabs/Towers/" + model + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var aim = root.GetComponent<TowerAim>();
                aim.animatedParts = FindAll(root.transform, parts);
                aim.muzzleTipOffset = 0f;
                aim.kickPitch = kickPitch;
                aim.kickYaw = 0f;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void ConfigureEmpAim()
        {
            const string path = "Assets/Prefabs/Towers/Tower_EMP.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var aim = root.GetComponent<TowerAim>();
                aim.animatedParts = FindAll(root.transform, new[]
                {
                    "ET_Emitter_Spin", "ET_Disc_Upper", "ET_Emitter_Core", "ET_Disc_Coil_Inner", "ET_Disc_Coil_Outer",
                    "ET_Node_0", "ET_Node_1", "ET_Node_2", "ET_Node_3", "ET_Node_4", "ET_Node_5", "ET_Node_6", "ET_Node_7",
                    "ET_Head_Eye",
                });
                aim.muzzleTipOffset = 0f;
                aim.kickPitch = 0f;
                aim.kickYaw = 0f;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 나노 타워: 발사 연출로 움직이는 부품(발사대·발사구 셀·나노 베이·눈·측면 빛)을 따로 둔다. 반동은 발사 연출이 하므로 머리 튐은 작게.
        static void ConfigureNanoAim()
        {
            const string path = "Assets/Prefabs/Towers/Tower_Nano.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var aim = root.GetComponent<TowerAim>();
                aim.animatedParts = FindAll(root.transform, new[]
                {
                    "NT_Launcher_Recoil", "NT_Vent_Cell_0", "NT_Vent_Cell_1", "NT_Vent_Cell_2", "NT_Vent_Cell_3",
                    "NT_Vent_Cell_4", "NT_Vent_Cell_5", "NT_Vent_Cell_6", "NT_Nano_Bay_Bots", "NT_Head_Eye",
                    "NT_Hex_Panel_L_Glow", "NT_Hex_Panel_R_Glow",
                });
                aim.muzzleTipOffset = 0f;   // 발사점이 이미 발사구 앞면
                aim.kickPitch = 1.5f;
                aim.kickYaw = 0f;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 번개 타워: 차지 연출로 움직이는 부품(코일 링·구슬·눈)을 따로 두고, 번개는 구슬 중심에서 나간다. 반동은 작게.
        static void ConfigureLightningAim()
        {
            const string path = "Assets/Prefabs/Towers/Tower_Lightning.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var aim = root.GetComponent<TowerAim>();
                aim.animatedParts = FindAll(root.transform, new[]
                    { "LT_Coil_Ring_0", "LT_Coil_Ring_1", "LT_Coil_Ring_2", "LT_Orb", "LT_Orb_Collar", "LT_Head_Eye" });
                aim.muzzleTipOffset = 0f;
                aim.kickPitch = 2f;
                aim.kickYaw = 0f;
                aim.maxPitch = 10f;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 새로 만들었으면 true (이미 있거나 모델이 없으면 false).
        static bool MakeTower(string model, string prefix, string muzzleName, string[] fireNames, string[] recoilNames, float minPitch)
        {
            string outPath = "Assets/Prefabs/Towers/" + model + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(outPath) != null)
                return false;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(TowerModelFolder + "/" + model + ".fbx");
            if (src == null)
            {
                Debug.LogWarning("[ProjectSetup] 타워 모델 없음: " + model);
                return false;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            try
            {
                var aim = inst.AddComponent<TowerAim>();
                aim.yawPivot = Find(inst.transform, prefix + "_Rotation_Plate");
                aim.pitchPivot = Find(inst.transform, prefix + "_Neck_Joint");
                aim.muzzle = Find(inst.transform, muzzleName);
                aim.firePoints = FindAll(inst.transform, fireNames);
                if (recoilNames != null)
                    aim.recoilPivots = FindAll(inst.transform, recoilNames);
                aim.barrelAxis = Vector3.up;
                aim.minPitch = minPitch;
                PrefabUtility.SaveAsPrefabAsset(inst, outPath);
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
            return true;
        }

        // 데이터 에셋에 모델·이펙트 프리팹 연결 (비어 있을 때만).
        static void LinkViewPrefabs()
        {
            var basic = AssetDatabase.LoadAssetAtPath<TowerDef>(DataFolder + "/Towers/basic.asset");
            if (basic != null)
            {
                if (basic.viewPrefab == null)
                    basic.viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Towers/Tower_TwinCannon.prefab");
                if (basic.projectilePrefab == null)
                    basic.projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TowerModelFolder + "/Projectile_TwinCannon_Bullet.fbx");
                if (basic.muzzleFxPrefab == null)
                    basic.muzzleFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.PrefabPath);
                EditorUtility.SetDirty(basic);
            }

            var flame = AssetDatabase.LoadAssetAtPath<TowerDef>(DataFolder + "/Towers/flamethrower.asset");
            if (flame != null)
            {
                if (flame.viewPrefab == null)
                    flame.viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Towers/Tower_Flamethrower.prefab");
                if (flame.muzzleFxPrefab == null)
                    flame.muzzleFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.FlamePrefabPath);
                EditorUtility.SetDirty(flame);
            }

            var lightning = AssetDatabase.LoadAssetAtPath<TowerDef>(DataFolder + "/Towers/lightning.asset");
            if (lightning != null)
            {
                if (lightning.viewPrefab == null)
                    lightning.viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Towers/Tower_Lightning.prefab");
                if (lightning.projectilePrefab == null)
                    lightning.projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.LightningBoltPath);
                if (lightning.muzzleFxPrefab == null)
                    lightning.muzzleFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.LightningBurstPath);
                if (lightning.hitFxPrefab == null)
                    lightning.hitFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.LightningBurstPath);
                if (lightning.chargeFxPrefab == null)
                    lightning.chargeFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.LightningChargePath);
                if (lightning.chargeClip == null)
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(TowerModelFolder + "/Tower_Lightning@Charge.fbx"))
                        if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                        {
                            lightning.chargeClip = clip;
                            lightning.chargeTime = clip.length; // 차지 = 차지 클립 한 번
                            break;
                        }
                EditorUtility.SetDirty(lightning);
            }

            var nano = AssetDatabase.LoadAssetAtPath<TowerDef>(DataFolder + "/Towers/nano.asset");
            if (nano != null)
            {
                if (nano.viewPrefab == null)
                    nano.viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Towers/Tower_Nano.prefab");
                if (nano.projectilePrefab == null)
                    nano.projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.NanoProjectilePath);
                if (nano.muzzleFxPrefab == null)
                    nano.muzzleFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.NanoMuzzlePath);
                if (nano.fieldFxPrefab == null)
                    nano.fieldFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.NanoFieldPath);
                if (nano.fireClip == null)
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(TowerModelFolder + "/Tower_Nano@Fire.fbx"))
                        if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                        {
                            nano.fireClip = clip;
                            break;
                        }
                EditorUtility.SetDirty(nano);
            }

            var emp = AssetDatabase.LoadAssetAtPath<TowerDef>(DataFolder + "/Towers/emp.asset");
            if (emp != null)
            {
                if (emp.viewPrefab == null)
                    emp.viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Towers/Tower_EMP.prefab");
                if (emp.muzzleFxPrefab == null)
                    emp.muzzleFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.EmpWavePath);
                if (emp.hitFxPrefab == null)
                    emp.hitFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.EmpHitPath);
                if (emp.fireClip == null)
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(TowerModelFolder + "/Tower_EMP@Fire.fbx"))
                        if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                        {
                            emp.fireClip = clip;
                            break;
                        }
                EditorUtility.SetDirty(emp);
            }

            LinkTier2("minigun", "Tower_T2_Minigun", MuzzleFxBuilder.MinigunTracerPath, MuzzleFxBuilder.MinigunFlashPath, MuzzleFxBuilder.MinigunHitPath, null);
            LinkTier2("electro", "Tower_T2_Electro", MuzzleFxBuilder.ElectroBoltPath, MuzzleFxBuilder.ElectroMuzzlePath, MuzzleFxBuilder.ElectroHitPath, "@Fire");
            LinkTier2("cryo", "Tower_T2_Cryo", MuzzleFxBuilder.CryoShardPath, MuzzleFxBuilder.CryoMuzzlePath, MuzzleFxBuilder.CryoHitPath, "@Fire");
            LinkTier2("dust", "Tower_T2_Dust", null, MuzzleFxBuilder.DustSprayPath, null, "@Fire");
            LinkTier2("phosphorus", "Tower_T2_Phosphorus", null, MuzzleFxBuilder.PhosphorusSprayPath, null, "@Fire");
            LinkTier2("plasma", "Tower_T2_Plasma", null, MuzzleFxBuilder.PlasmaJetPath, null, "@Fire");
            var minigun = AssetDatabase.LoadAssetAtPath<TowerDef>(DataFolder + "/Towers/minigun.asset");
            if (minigun != null)
            {
                if (minigun.spinUpClip == null) minigun.spinUpClip = FirstClip(TowerModelFolder + "/Tower_T2_Minigun@Fire_Warmup.fbx");
                if (minigun.spinLoopClip == null) minigun.spinLoopClip = FirstClip(TowerModelFolder + "/Tower_T2_Minigun@Fire_Loop.fbx");
                EditorUtility.SetDirty(minigun);
            }

            // 건축 연출: 모델 이름@Build.fbx의 Build 클립 (모든 타워 데이터, 비어 있을 때만)
            foreach (var guid in AssetDatabase.FindAssets("t:TowerDef", new[] { DataFolder }))
            {
                var def = AssetDatabase.LoadAssetAtPath<TowerDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || def.viewPrefab == null || def.buildClip != null)
                    continue;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath($"{TowerModelFolder}/{def.viewPrefab.name}@Build.fbx"))
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                    {
                        def.buildClip = clip;
                        EditorUtility.SetDirty(def);
                        break;
                    }
            }

            // 몬스터 모델이 있으면 일반 몬스터에 연결 (없으면 캡슐로 대신 표시된다).
            var normal = AssetDatabase.LoadAssetAtPath<MonsterDef>(DataFolder + "/Monsters/normal.asset");
            var saberfang = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/Monster_Saberfang.prefab");
            if (normal != null && normal.viewPrefab == null && saberfang != null)
            {
                normal.viewPrefab = saberfang;
                EditorUtility.SetDirty(normal);
            }
            // 빠른 몬스터 = 해머헤드
            var fast = AssetDatabase.LoadAssetAtPath<MonsterDef>(DataFolder + "/Monsters/fast.asset");
            var hammerhead = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/Monster_Hammerhead.prefab");
            if (fast != null && fast.viewPrefab == null && hammerhead != null)
            {
                fast.viewPrefab = hammerhead;
                EditorUtility.SetDirty(fast);
            }
            // 탱커 몬스터 = 바스티온
            var tank = AssetDatabase.LoadAssetAtPath<MonsterDef>(DataFolder + "/Monsters/tank.asset");
            var bastion = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/Monster_Bastion.prefab");
            if (tank != null && tank.viewPrefab == null && bastion != null)
            {
                tank.viewPrefab = bastion;
                EditorUtility.SetDirty(tank);
            }
        }

        // 2단계 타워 표시 연결 (비어 있을 때만): 모델·투사체·총구·명중 이펙트, 발사 클립(fireSuffix 파일)
        static void LinkTier2(string id, string model, string projectile, string muzzle, string hit, string fireSuffix)
        {
            var d = AssetDatabase.LoadAssetAtPath<TowerDef>(DataFolder + "/Towers/" + id + ".asset");
            if (d == null)
                return;
            if (d.viewPrefab == null) d.viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Towers/" + model + ".prefab");
            if (projectile != null && d.projectilePrefab == null) d.projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(projectile);
            if (d.muzzleFxPrefab == null) d.muzzleFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(muzzle);
            if (hit != null && d.hitFxPrefab == null) d.hitFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hit);
            if (fireSuffix != null && d.fireClip == null) d.fireClip = FirstClip(TowerModelFolder + "/" + model + fireSuffix + ".fbx");
            EditorUtility.SetDirty(d);
        }

        static AnimationClip FirstClip(string fbxPath)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                    return clip;
            return null;
        }

        // ---------- 타일 (FBX가 없으면 임시 큐브 타일) ----------

        static GameObject TileModel(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(TileModelFolder + "/" + name + ".fbx");
        }

        // 메시 Read/Write (작업 10): 타일은 런타임 정적 배칭, 타워는 부품 메시 합치기에 필요하다.
        [MenuItem("TowerDefence/Setup/Meshes Readable (Tiles, Towers)")]
        public static void EnsureMeshesReadable()
        {
            var folders = new List<string>();
            foreach (var folder in new[] { TileModelFolder, TowerModelFolder })
                if (AssetDatabase.IsValidFolder(folder))
                    folders.Add(folder);
            if (folders.Count == 0)
                return;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", folders.ToArray()))
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as ModelImporter;
                if (importer == null || importer.isReadable)
                    continue;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
        }

        static void ResolveTiles(out GameObject path, out GameObject buildable, out GameObject[] blocked)
        {
            path = TileModel("DirtPathTile");
            buildable = TileModel("GrassTile");
            var list = new List<GameObject>();
            for (int i = 1; i <= 5; i++)
            {
                var c = TileModel("GrassTile_Center" + i);
                if (c != null) list.Add(c);
                var t = TileModel("GrassTile_Tree" + i);
                if (t != null) list.Add(t);
            }
            blocked = list.ToArray();

            if (path == null) path = Placeholder("Placeholder_Path", new Color(0.55f, 0.45f, 0.33f), false);
            if (buildable == null) buildable = Placeholder("Placeholder_Grass", new Color(0.45f, 0.72f, 0.38f), false);
            if (blocked.Length == 0) blocked = new[] { Placeholder("Placeholder_GrassTree", new Color(0.3f, 0.55f, 0.28f), true) };
        }

        // 1×0.4×1, 피벗이 바닥인 임시 타일. 콜라이더 없음 (물리 사용 금지 규칙).
        static GameObject Placeholder(string name, Color color, bool tree)
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PlaceholderFolder);
            string path = PlaceholderFolder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            var root = new GameObject(name);
            try
            {
                var body = Primitive(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.2f, 0f), new Vector3(1f, 0.4f, 1f),
                    Material(name, color));
                body.name = "Body";
                if (tree)
                {
                    Primitive(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.12f, 0.15f, 0.12f),
                        Material("Placeholder_Trunk", new Color(0.45f, 0.3f, 0.18f))).name = "Trunk";
                    Primitive(PrimitiveType.Sphere, root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.5f, 0.5f, 0.5f),
                        Material("Placeholder_Leaves", new Color(0.2f, 0.45f, 0.2f))).name = "Leaves";
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static Material Material(string name, Color color)
        {
            string path = PlaceholderFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ---------- 행성 (맵 아래) ----------
        // HabitablePlanet 모델(반지름 10, 대기 11.2, 구름·달 포함)을 PlanetScale배로 키워, 대기 꼭대기가 타일 바닥 바로 아래에 오게 둔다.
        // 맵(최대 15칸)이 행성 위에 얹힌 모습이 되고, 맵 가장자리 너머로 행성 곡면과 구름·달이 보인다.
        const string PlanetModelPath = "Assets/Models/Environment/HabitablePlanet.fbx";
        const float PlanetScale = 4f;
        const float PlanetTilt = 45f;              // 행성 기울기 (자전축도 행성과 함께 기운다)
        const float PlanetAtmosphereRadius = 11.2f; // 모델 단위
        const float MapBottom = -0.45f;            // 타일 바닥(-0.4) 바로 아래

        static Transform SetupPlanet(out Transform clouds)
        {
            clouds = null;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlanetModelPath);
            if (model == null)
                return null;
            var existing = GameObject.Find("Planet");
            if (existing != null)
                Object.DestroyImmediate(existing);
            var planet = (GameObject)PrefabUtility.InstantiatePrefab(model);
            planet.name = "Planet";
            planet.transform.position = new Vector3(0f, MapBottom - PlanetAtmosphereRadius * PlanetScale, 0f);
            planet.transform.localScale = Vector3.one * PlanetScale;
            planet.transform.rotation = Quaternion.Euler(PlanetTilt, 0f, 0f); // 행성째 45° 기울임: 극이 맵 바로 아래가 아니게
            foreach (var r in planet.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // 맵 아래라 그림자를 드리울 일이 없다 (그림자 패스 비용 절약)
                r.receiveShadows = false;
            }
            var c = planet.transform.Find("Planet_Clouds");
            clouds = c;
            return planet.transform;
        }

        // 라운드 시작 전 몬스터 경로 미리보기 재질 (색·빛 속도는 재질 Inspector에서 조절).
        static Material PathPreviewMaterial()
        {
            const string path = "Assets/Art/FX/PathPreview.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
            var shader = Shader.Find("TowerDefence/PathPreview");
            if (shader == null)
            {
                Debug.LogWarning("[ProjectSetup] TowerDefence/PathPreview 셰이더가 없어 경로 미리보기 재질을 만들지 않습니다.");
                return null;
            }
            if (!AssetDatabase.IsValidFolder("Assets/Art/FX"))
                AssetDatabase.CreateFolder("Assets/Art", "FX");
            mat = new Material(shader) { name = "PathPreview" };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ---------- SampleScene 배치 ----------

        [MenuItem("TowerDefence/Setup/SampleScene")]
        public static void SetupScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                scene = EditorSceneManager.OpenScene(ScenePath);
            }

            // 3D 맵 (타일이 아래에서 솟아오르는 MapGenerator)
            var mapGen = Object.FindFirstObjectByType<MapGenerator>();
            if (mapGen == null)
                mapGen = new GameObject("MapGenerator").AddComponent<MapGenerator>();
            ResolveTiles(out var pathTile, out var buildTile, out var blockedTiles);
            var soMap = new SerializedObject(mapGen);
            soMap.FindProperty("pathTilePrefab").objectReferenceValue = pathTile;
            soMap.FindProperty("buildableTilePrefab").objectReferenceValue = buildTile;
            var blockedProp = soMap.FindProperty("blockedTilePrefabs");
            blockedProp.arraySize = blockedTiles.Length;
            for (int i = 0; i < blockedTiles.Length; i++)
                blockedProp.GetArrayElementAtIndex(i).objectReferenceValue = blockedTiles[i];
            soMap.FindProperty("pathPreviewMaterial").objectReferenceValue = PathPreviewMaterial();
            var planet = SetupPlanet(out var clouds);
            soMap.FindProperty("planet").objectReferenceValue = planet;
            soMap.FindProperty("planetClouds").objectReferenceValue = clouds;
            soMap.FindProperty("planetAxis").vector3Value = Vector3.up; // 행성 자체 극축 (행성이 45° 기울어 있다)
            soMap.ApplyModifiedPropertiesWithoutUndo();

            // 카메라 (탑다운/탑, Tab 전환, WASD)
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            // 배경 = 우주 (행성이 떠 있는 깊은 남색). 조명·안개 설정은 그대로
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.025f, 0.035f, 0.085f);
            var camCtl = cam.GetComponent<CameraController>();
            if (camCtl == null)
                camCtl = cam.gameObject.AddComponent<CameraController>();
            var soCam = new SerializedObject(camCtl);
            soCam.FindProperty("map").objectReferenceValue = mapGen;
            soCam.ApplyModifiedPropertiesWithoutUndo();

            // UI 캔버스
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("Canvas") { layer = LayerMask.NameToLayer("UI") };
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            // GameLoop (유일한 Update)
            var loop = Object.FindFirstObjectByType<GameLoop>();
            if (loop == null)
                loop = new GameObject("GameLoop").AddComponent<GameLoop>();
            var so = new SerializedObject(loop);
            so.FindProperty("config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameConfig>(DataFolder + "/GameConfig.asset");
            so.FindProperty("mapGenRules").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MapGenRules>(DataFolder + "/MapGenRules.asset");
            var ids = new[] { "normal", "fast", "tank" };
            var mons = so.FindProperty("monsterDefs");
            mons.arraySize = ids.Length;
            for (int i = 0; i < ids.Length; i++)
                mons.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<MonsterDef>(DataFolder + "/Monsters/" + ids[i] + ".asset");
            // 건축 목록 순서 = 이 배열 순서
            var towerIds = new[] { "basic", "flamethrower", "lightning", "nano", "emp", "minigun", "electro", "cryo", "dust", "phosphorus", "plasma" };
            var tows = so.FindProperty("towerDefs");
            tows.arraySize = 0;
            foreach (var id in towerIds)
            {
                var def = AssetDatabase.LoadAssetAtPath<TowerDef>(DataFolder + "/Towers/" + id + ".asset");
                if (def == null)
                    continue;
                tows.arraySize++;
                tows.GetArrayElementAtIndex(tows.arraySize - 1).objectReferenceValue = def;
            }
            so.FindProperty("mainCamera").objectReferenceValue = cam;
            so.FindProperty("canvas").objectReferenceValue = canvas;
            so.FindProperty("mapRenderer").objectReferenceValue = mapGen;
            so.FindProperty("cameraRig").objectReferenceValue = camCtl;
            // 테스트용 덮어쓰기 끔: 시작 캐시·몬스터 체력은 GameConfig·MonsterDef 값 그대로
            so.FindProperty("debugStartCash").intValue = 0;
            so.FindProperty("debugMonsterHp").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // ---------- 스트레스 테스트 (작업 10) ----------
        // 게임 데이터는 건드리지 않고 복사본(Assets/Data/Stress)으로 마지막 층(10층: 15×15, 복잡성 5, 스타트 지점 3개) 조건을 만든다.

        const string StressFolder = DataFolder + "/Stress";
        const string StressScenePath = "Assets/Scenes/StressTest.unity";
        const string StressBuildPath = "Builds/StressTest/StressTest.exe";
        const float StressSpawnInterval = 0.1f; // 290마리를 29초에 내보내 동시 몬스터 수를 최대로 (가장 무거운 경우)

        [MenuItem("TowerDefence/Setup/Stress Test (Data + Scene)")]
        public static void SetupStressTest()
        {
            SetupData();
            EnsureMeshesReadable();
            EnsureFolder(StressFolder);
            var baseConfig = AssetDatabase.LoadAssetAtPath<GameConfig>(DataFolder + "/GameConfig.asset");
            var basic = AssetDatabase.LoadAssetAtPath<TowerDef>(DataFolder + "/Towers/basic.asset");

            // 1) 스트레스 전용 GameConfig: 진행 규칙은 본 GameConfig와 같게, 마지막 층 웨이브 테이블 (일반 / 탱커만 / fast·tank 혼합), 넉넉한 캐시·넥서스 체력
            string configPath = StressFolder + "/GameConfig_Stress.asset";
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(configPath);
            if (config == null)
            {
                config = Object.Instantiate(baseConfig);
                AssetDatabase.CreateAsset(config, configPath);
            }
            config.finalFloor = baseConfig.finalFloor;
            config.rules = JsonUtility.FromJson<GameConfig.LevelRuleParams>(JsonUtility.ToJson(baseConfig.rules));
            config.startCash = 1000000;
            int last = baseConfig.finalFloor;
            config.nexusHp = 1000000;
            config.waves = new[]
            {
                Wave(last, 1, Line("normal", 290)),
                Wave(last, 2, Line("tank", 290)),
                Wave(last, 3, Line("fast", 37), Line("tank", 36), Line("fast", 36), Line("tank", 37),
                            Line("fast", 36), Line("tank", 36), Line("fast", 36), Line("tank", 36)),
            };
            EditorUtility.SetDirty(config);

            // 2) PreferType + typePriority ["Special"] 타워 (기본 타워 복사본, 후보 순회 비용 확인용)
            string preferPath = StressFolder + "/basic_prefer.asset";
            var prefer = AssetDatabase.LoadAssetAtPath<TowerDef>(preferPath);
            if (prefer == null)
            {
                prefer = Object.Instantiate(basic);
                AssetDatabase.CreateAsset(prefer, preferPath);
            }
            prefer.id = "basic_prefer";
            prefer.displayName = "기본 타워 (특수 우선)";
            prefer.lockMode = TargetLock.PreferType;
            prefer.typePriority = new[] { "Special" };
            EditorUtility.SetDirty(prefer);

            // 3) 설정: 일반 타워 3 : 특수 우선 타워 1
            var settings = GetOrCreate<StressTestSettings>(StressFolder + "/StressTestSettings.asset", null);
            settings.config = config;
            settings.monsterDefs = new[]
            {
                AssetDatabase.LoadAssetAtPath<MonsterDef>(DataFolder + "/Monsters/normal.asset"),
                AssetDatabase.LoadAssetAtPath<MonsterDef>(DataFolder + "/Monsters/fast.asset"),
                AssetDatabase.LoadAssetAtPath<MonsterDef>(DataFolder + "/Monsters/tank.asset"),
            };
            settings.towerDefs = new[] { basic, prefer };
            settings.towerPattern = new[] { basic, basic, basic, prefer };
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            // 4) 씬: SampleScene 복사본에 설정 연결
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            string returnTo = active.path;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StressScenePath) == null)
                AssetDatabase.CopyAsset(ScenePath, StressScenePath);
            var scene = EditorSceneManager.OpenScene(StressScenePath);
            // 씬을 열면 참조 없는 에셋 인스턴스가 내려가므로(UnloadUnusedAssets) 연 뒤에 다시 불러와 연결한다.
            settings = AssetDatabase.LoadAssetAtPath<StressTestSettings>(StressFolder + "/StressTestSettings.asset");
            var loop = Object.FindFirstObjectByType<GameLoop>();
            var so = new SerializedObject(loop);
            so.FindProperty("stressTest").objectReferenceValue = settings;
            so.FindProperty("debugStartCash").intValue = 0;
            so.FindProperty("debugMonsterHp").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!string.IsNullOrEmpty(returnTo) && returnTo != StressScenePath)
                EditorSceneManager.OpenScene(returnTo);
        }

        // PC(Windows) 개발 빌드: 에디터 부하 없이 측정. 실행하면 자동 진행 후 exe 옆에 stress_*.md를 쓰고 종료한다.
        [MenuItem("TowerDefence/Build Stress Test (Windows)")]
        public static void BuildStressTest()
        {
            SetupStressTest();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { StressScenePath },
                locationPathName = StressBuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };
            // GPU 프레임 시간 측정용 (빌드 동안만 켜고 원래 값으로 되돌린다)
            bool frameTiming = PlayerSettings.enableFrameTimingStats;
            PlayerSettings.enableFrameTimingStats = true;
            UnityEditor.Build.Reporting.BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                PlayerSettings.enableFrameTimingStats = frameTiming;
            }
            Debug.Log($"[ProjectSetup] 스트레스 빌드 {report.summary.result} ({report.summary.totalTime.TotalSeconds:0}초): {StressBuildPath}");
        }

        static GameConfig.DesignedWave Wave(int floor, int wave, params GameConfig.SpawnLine[] lines) =>
            new GameConfig.DesignedWave { floor = floor, wave = wave, lines = lines };

        static GameConfig.SpawnLine Line(string id, int count) =>
            new GameConfig.SpawnLine { monsterId = id, count = count, intervalSec = StressSpawnInterval };

        // ---------- 공용 ----------

        static T GetOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            asset = ScriptableObject.CreateInstance<T>();
            init?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform c in root)
            {
                var r = Find(c, name);
                if (r != null)
                    return r;
            }
            return null;
        }

        static Transform[] FindAll(Transform root, string[] names)
        {
            var result = new Transform[names.Length];
            for (int i = 0; i < names.Length; i++)
                result[i] = Find(root, names[i]);
            return result;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
