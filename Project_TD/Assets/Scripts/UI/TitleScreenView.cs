using System;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence
{
    // 게임 시작 화면(타이틀) 반복 애니메이션. 장면은 에디터 메뉴 TowerDefence/Setup/Title Screen Scene(TitleScreenBuilder)이 짜고
    // 이 컴포넌트에 몬스터·타워·이펙트를 연결한다. Update가 없다: GameLoop(titleScreenMode)가 Tick(dt)을 부른다.
    //   몬스터: 저 멀리에서 길을 따라 앞으로 걸어오고, 카메라 옆을 지나가면 다시 맨 뒤로 (끝없이 몰려옴)
    //           폭발을 여러 번 맞으면 쓰러졌다가(Die) 맨 뒤에서 다시 나온다
    //   타워: 가까운 몬스터를 조준해 쏜다. 탄 타워 = 발사 섬광 → 날아가는 탄 → 맞은 자리 피격 파티클만.
    //         화염·분진·백린·플라즈마 = 계속 뿜고, 맞은 몬스터는 잠시 불에 탄다 (화염 큐브 파티클). EMP = 파동
    //   폭발: 분진·백린 스택이 5개 차면 그 몬스터에서 로우폴리 불덩이가 터진다 (불빛 번쩍, 가까우면 카메라 흔들림)
    // 풀(불덩이·섬광·불꽃·탄·이펙트)은 Init에서 모두 만들어 두고 Tick에서는 켜고 끄기만 한다 (할당 없음).
    public class TitleScreenView : MonoBehaviour
    {
        [Serializable]
        public class MonsterSlot
        {
            public Transform root;
            public Animator animator;
            public float laneX;         // 길 위 좌우 위치 (월드 x)
            public float centerHeight;  // 몸통 중앙 높이 (조준점)
            public float hitsToDie = 6f;
        }

        public enum FireKind { Projectile, Spray, Pulse }

        [Serializable]
        public class TowerSlot
        {
            public TowerAim aim;
            public FireKind kind;
            public float interval = 0.6f;     // 발사 간격 (뿜는 타워는 이펙트를 다시 트는 간격)
            public GameObject muzzleFx;
            public float muzzleScale = 1.8f;
            public GameObject projectile;     // Projectile 종류만
            public float projectileScale = 0.4f;
            public Vector3 projectileForward = Vector3.forward;
            public GameObject hitFx;
            public float hitScale = 2.2f;
            public bool burns;                // 맞은 몬스터가 불에 탄다 (화염 계열)
            public bool stackBoom;            // 분진·백린: 맞을 때마다 스택, StackToBoom개면 불덩이 폭발
            public float boomSize = 0.6f;
            public float range = 9f;
        }

        [SerializeField] Camera cam;
        [SerializeField] GameObject staticFx;          // 구도 시안용 정지 이펙트 (재생하면 끈다)
        [SerializeField] MonsterSlot[] monsters = new MonsterSlot[0];
        [SerializeField] TowerSlot[] towers = new TowerSlot[0];
        [SerializeField] GameObject fireballTemplate;  // 꺼 둔 불덩이 한 벌 (불빛 포함)
        [SerializeField] GameObject flashPrefab;
        [SerializeField] GameObject sparksPrefab;
        [SerializeField] GameObject burningPrefab;     // 불에 타는 상태 파티클 (몬스터마다 하나, 맞은 동안 켬)
        [SerializeField] float burnTime = 1.6f;
        [SerializeField] float monsterSpeed = 0.9f;    // 칸/초
        [SerializeField] float roadFarZ = 21f;         // 다시 나오는 곳 (안개 속)
        [SerializeField] float roadNearZ = -2.5f;      // 카메라 옆을 지나 사라지는 곳
        [SerializeField] float spacing = 1.6f;         // 몬스터 사이 최소 간격
        [SerializeField] float projectileSpeed = 14f;
        [SerializeField] float shakeDistance = 6f;
        [SerializeField] float shakeAmount = 0.05f;

        [Header("로고 · 시작 문구")]
        [SerializeField] string logo = "PROJECT TD";                 // 임시 게임 이름
        [SerializeField] string subtitle = "TOWER DEFENCE";
        [SerializeField] string prompt = "아무 곳이나 클릭 시 게임 시작";
        [SerializeField] Color logoColor = new Color(1f, 0.93f, 0.74f);
        [SerializeField] Color logoOutline = new Color(0.16f, 0.06f, 0.05f, 1f);
        [SerializeField] float logoIntroTime = 0.9f;                  // 로고가 위에서 내려앉는 시간
        [SerializeField] string nextScene = "SampleScene";            // 클릭하면 넘어갈 씬 (로비가 있는 게임 씬)
        [SerializeField] float fadeOutTime = 0.45f;

        static readonly int WalkHash = Animator.StringToHash("Walk");
        static readonly int DieHash = Animator.StringToHash("Die");
        const float DieTime = 1.6f;
        const float BoomLife = 0.85f;
        const int StackToBoom = 5;

        // 몬스터 상태
        float[] mz;          // 길 위 z
        float[] hits;
        float[] dying;       // > 0: 쓰러지는 중 남은 시간
        float[] burning;     // > 0: 불에 타는 중 남은 시간
        int[] stacks;        // 분진·백린 스택
        ParticleSystem[] burnFx;

        // 타워 상태
        float[] cooldown;
        int[] target;
        float[] retarget;
        int[] towerPoolIndex; // 타워별 풀 시작 위치 (탄·이펙트)

        // 탄 풀 (타워마다 ShotsPerTower발)
        const int ShotsPerTower = 4;
        Transform[] shot;
        Vector3[] shotFrom, shotTo;
        float[] shotT, shotLen;
        int[] shotTower, shotTarget;

        // 이펙트 풀 (타워마다 2개씩 번갈아)
        const int FxPerTower = 2;
        ParticleSystem[] muzzle, hit;
        int[] muzzleNext, hitNext;

        // 폭발 풀
        const int Booms = 14;
        Transform[] boom;
        Light[] boomLight;
        float[] boomAge, boomSize;
        Vector3[] boomPos;
        ParticleSystem[] flash, sparks;
        int nextBoom;

        Vector3 camBase;
        float shake;
        bool ready;

        RectTransform logoRect, subRect;
        CanvasGroup logoGroup;
        Text promptLabel;
        Image fade;
        float uiTime;
        float leaving = -1f; // >= 0: 클릭 뒤 어두워지는 중

        // 화면을 클릭해 어두워지기까지 끝났으면 true → GameLoop가 nextScene을 연다
        public bool ReadyToLeave => leaving >= fadeOutTime;
        public string NextScene => nextScene;

        public void Init()
        {
            if (staticFx != null)
                staticFx.SetActive(false);
            if (cam != null)
                camBase = cam.transform.position;

            int n = monsters.Length;
            mz = new float[n];
            hits = new float[n];
            dying = new float[n];
            burning = new float[n];
            stacks = new int[n];
            burnFx = new ParticleSystem[n];
            for (int i = 0; i < n; i++)
            {
                mz[i] = monsters[i].root.position.z;
                if (monsters[i].animator != null)
                    monsters[i].animator.Play(WalkHash, 0, (i * 0.37f) % 1f);
            }

            int t = towers.Length;
            cooldown = new float[t];
            target = new int[t];
            retarget = new float[t];
            shot = new Transform[t * ShotsPerTower];
            shotFrom = new Vector3[shot.Length];
            shotTo = new Vector3[shot.Length];
            shotT = new float[shot.Length];
            shotLen = new float[shot.Length];
            shotTower = new int[shot.Length];
            shotTarget = new int[shot.Length];
            muzzle = new ParticleSystem[t * FxPerTower];
            hit = new ParticleSystem[t * FxPerTower];
            muzzleNext = new int[t];
            hitNext = new int[t];
            var poolRoot = new GameObject("TitlePools").transform;
            poolRoot.SetParent(transform, false);
            for (int i = 0; i < t; i++)
            {
                var s = towers[i];
                cooldown[i] = 0.15f + (i * 0.23f) % s.interval; // 일제히 쏘지 않게 엇갈림
                target[i] = -1;
                for (int k = 0; k < ShotsPerTower; k++)
                {
                    int j = i * ShotsPerTower + k;
                    shotT[j] = -1f;
                    if (s.kind == FireKind.Projectile && s.projectile != null)
                    {
                        var go = Instantiate(s.projectile, poolRoot);
                        go.transform.localScale = Vector3.one * s.projectileScale;
                        go.SetActive(false);
                        shot[j] = go.transform;
                    }
                }
                for (int k = 0; k < FxPerTower; k++)
                {
                    muzzle[i * FxPerTower + k] = MakeFx(s.muzzleFx, poolRoot, s.muzzleScale);
                    hit[i * FxPerTower + k] = MakeFx(s.hitFx, poolRoot, s.hitScale);
                }
            }

            boom = new Transform[Booms];
            boomLight = new Light[Booms];
            boomAge = new float[Booms];
            boomSize = new float[Booms];
            boomPos = new Vector3[Booms];
            flash = new ParticleSystem[Booms];
            sparks = new ParticleSystem[Booms];
            for (int i = 0; i < Booms; i++)
            {
                boomAge[i] = -1f;
                if (fireballTemplate != null)
                {
                    var go = Instantiate(fireballTemplate, poolRoot);
                    go.SetActive(false);
                    boom[i] = go.transform;
                    boomLight[i] = go.GetComponentInChildren<Light>(true);
                }
                flash[i] = MakeFx(flashPrefab, poolRoot, 2f);
                sparks[i] = MakeFx(sparksPrefab, poolRoot, 2.5f);
            }
            if (fireballTemplate != null)
                fireballTemplate.SetActive(false);
            for (int i = 0; i < n; i++)
                burnFx[i] = MakeFx(burningPrefab, poolRoot, 1f);
            BuildUi();
            ready = true;
        }

        static ParticleSystem MakeFx(GameObject prefab, Transform parent, float scale)
        {
            if (prefab == null)
                return null;
            var go = Instantiate(prefab, parent);
            go.transform.localScale *= scale;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.playOnAwake = false;
            }
            var root = go.GetComponent<ParticleSystem>();
            if (root == null)
                root = go.GetComponentInChildren<ParticleSystem>(true);
            root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return root;
        }

        static void PlayFx(ParticleSystem ps, Vector3 pos, Quaternion rot)
        {
            if (ps == null)
                return;
            ps.transform.SetPositionAndRotation(pos, rot);
            ps.Play(true);
        }

        // 화면 위: 로고(게임 이름 + 부제) 내려앉기·숨쉬기, 아래: 시작 문구 깜빡임
        void BuildUi()
        {
            var canvasGo = new GameObject("TitleUI", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>(); // 없으면 화면 클릭이 버튼에 닿지 않는다

            var logoRoot = BasicUi.Rect("Logo", canvasGo.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -210f), new Vector2(1400f, 300f));
            logoGroup = logoRoot.gameObject.AddComponent<CanvasGroup>();
            logoRect = logoRoot;
            var title = BasicUi.Label(logoRoot, logo, 168, TextAnchor.MiddleCenter, logoColor);
            title.fontStyle = FontStyle.Bold;
            title.rectTransform.anchorMin = new Vector2(0f, 0.32f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            Decorate(title, logoOutline, 5f, 9f);

            subRect = BasicUi.Rect("Subtitle", logoRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(900f, 70f));
            var line = BasicUi.Rect("Line", subRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 4f));
            BasicUi.Panel(line, new Color(1f, 0.62f, 0.25f, 0.95f)).raycastTarget = false;
            var sub = BasicUi.Label(subRect, subtitle, 46, TextAnchor.MiddleCenter, new Color(1f, 0.72f, 0.4f));
            sub.fontStyle = FontStyle.Bold;
            Decorate(sub, logoOutline, 3f, 5f);

            var promptRt = BasicUi.Rect("Prompt", canvasGo.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1200f, 70f));
            promptLabel = BasicUi.Label(promptRt, prompt, 40, TextAnchor.MiddleCenter, Color.white);
            Decorate(promptLabel, new Color(0f, 0f, 0f, 0.85f), 2f, 4f);

            // 화면 전체 버튼(투명): 아무 곳이나 클릭·터치 → 어두워진 뒤 로비로. 로고가 자리 잡기 전 클릭도 받는다
            var hit = BasicUi.Rect("ClickToStart", canvasGo.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            hit.anchorMin = Vector2.zero;
            hit.anchorMax = Vector2.one;
            var hitImage = BasicUi.Panel(hit, new Color(0f, 0f, 0f, 0f));
            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = hitImage;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(RequestStart);

            // 맨 위: 검은 막 (클릭 뒤 어두워짐)
            var fadeRt = BasicUi.Rect("Fade", canvasGo.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            fadeRt.anchorMin = Vector2.zero;
            fadeRt.anchorMax = Vector2.one;
            fade = BasicUi.Panel(fadeRt, new Color(0f, 0f, 0f, 0f));
            fade.raycastTarget = false;
        }

        // 화면 클릭: 한 번만 받는다 (요청만, 씬 전환은 GameLoop)
        void RequestStart()
        {
            if (leaving < 0f)
                leaving = 0f;
        }

        // 글자 테두리 + 아래로 떨어지는 그림자 (밝은 하늘·폭발 위에서도 읽히게)
        static void Decorate(Text text, Color outline, float width, float drop)
        {
            var o = text.gameObject.AddComponent<Outline>();
            o.effectColor = outline;
            o.effectDistance = new Vector2(width, -width);
            var sh = text.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.55f);
            sh.effectDistance = new Vector2(0f, -drop);
        }

        void TickUi(float dt)
        {
            if (logoRect == null)
                return;
            uiTime += dt;
            // 로고: 위에서 살짝 크게 떨어지며 나타나 자리 잡고, 그 뒤로는 천천히 숨쉬듯 커졌다 작아진다
            float t = Mathf.Clamp01(uiTime / logoIntroTime);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            logoGroup.alpha = Mathf.Clamp01(t * 1.6f);
            logoRect.anchoredPosition = new Vector2(0f, -210f + (1f - ease) * 120f);
            float pulse = t < 1f ? Mathf.Lerp(1.25f, 1f, ease) : 1f + Mathf.Sin((uiTime - logoIntroTime) * 1.6f) * 0.015f;
            logoRect.localScale = new Vector3(pulse, pulse, 1f);
            // 시작 문구: 로고가 자리 잡은 뒤 천천히 깜빡인다
            float a = uiTime < logoIntroTime ? 0f : 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin((uiTime - logoIntroTime) * 3f));
            var c = promptLabel.color;
            c.a = a;
            promptLabel.color = c;
            if (leaving >= 0f)
            {
                leaving += dt;
                fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(leaving / fadeOutTime));
            }
        }

        public void Tick(float dt)
        {
            if (!ready)
                return;
            TickUi(dt);
            TickMonsters(dt);
            TickTowers(dt);
            TickShots(dt);
            TickBooms(dt);
            TickCamera(dt);
        }

        Vector3 Center(int m) => new Vector3(monsters[m].laneX, monsters[m].centerHeight, mz[m]);
        bool Alive(int m) => dying[m] <= 0f && mz[m] < roadFarZ - 0.5f;

        void TickMonsters(float dt)
        {
            for (int i = 0; i < monsters.Length; i++)
            {
                var s = monsters[i];
                if (burning[i] > 0f)
                {
                    burning[i] -= dt;
                    if (burnFx[i] != null)
                    {
                        burnFx[i].transform.position = new Vector3(s.laneX, s.centerHeight, mz[i]);
                        if (burning[i] <= 0f)
                            burnFx[i].Stop(true, ParticleSystemStopBehavior.StopEmitting); // 남은 불씨는 저절로 사그라든다
                    }
                }
                if (dying[i] > 0f)
                {
                    dying[i] -= dt;
                    if (dying[i] <= 0f)
                        Respawn(i);
                    continue;
                }
                mz[i] -= monsterSpeed * dt;
                if (mz[i] < roadNearZ)
                {
                    Respawn(i);
                    continue;
                }
                s.root.position = new Vector3(s.laneX, 0f, mz[i]);
            }
        }

        // 맨 뒤(가장 먼 몬스터보다 spacing 뒤, 최소 roadFarZ)에서 다시 걸어 나온다
        void Respawn(int i)
        {
            float back = roadFarZ;
            for (int k = 0; k < monsters.Length; k++)
                if (k != i && dying[k] <= 0f && mz[k] + spacing > back)
                    back = mz[k] + spacing;
            mz[i] = back;
            hits[i] = 0f;
            dying[i] = 0f;
            stacks[i] = 0;
            if (burning[i] > 0f && burnFx[i] != null)
                burnFx[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            burning[i] = 0f;
            var s = monsters[i];
            s.root.position = new Vector3(s.laneX, 0f, mz[i]);
            if (s.animator != null)
                s.animator.Play(WalkHash, 0, 0f);
        }

        void Hit(int m, float amount)
        {
            if (m < 0 || dying[m] > 0f)
                return;
            hits[m] += amount;
            if (hits[m] >= monsters[m].hitsToDie)
            {
                dying[m] = DieTime;
                if (monsters[m].animator != null)
                    monsters[m].animator.Play(DieHash, 0, 0f);
            }
        }

        int PickTarget(Vector3 from, float range)
        {
            int best = -1;
            float bestD = range * range;
            for (int m = 0; m < monsters.Length; m++)
            {
                if (!Alive(m) || mz[m] < 0f)
                    continue;
                var c = Center(m);
                float dx = c.x - from.x, dz = c.z - from.z;
                float d = dx * dx + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = m;
                }
            }
            return best;
        }

        void TickTowers(float dt)
        {
            for (int i = 0; i < towers.Length; i++)
            {
                var s = towers[i];
                if (s.aim == null)
                    continue;
                s.aim.TickRecoil(dt);
                retarget[i] -= dt;
                if (target[i] < 0 || !Alive(target[i]) || retarget[i] <= 0f)
                {
                    target[i] = PickTarget(s.aim.yawPivot.position, s.range);
                    retarget[i] = 0.8f;
                }
                int m = target[i];
                if (m < 0)
                    continue;
                var aimAt = Center(m);
                s.aim.Aim(aimAt, dt);
                cooldown[i] -= dt;
                if (cooldown[i] > 0f || (s.kind != FireKind.Pulse && !s.aim.IsAimedAt(aimAt)))
                    continue;
                cooldown[i] = s.interval;
                Fire(i, m, aimAt);
            }
        }

        void Fire(int i, int m, Vector3 aimAt)
        {
            var s = towers[i];
            switch (s.kind)
            {
                case FireKind.Projectile:
                {
                    s.aim.Fire(out var pos, out var dir);
                    PlayFx(muzzle[i * FxPerTower + muzzleNext[i]], pos, Quaternion.LookRotation(dir));
                    muzzleNext[i] = (muzzleNext[i] + 1) % FxPerTower;
                    for (int k = 0; k < ShotsPerTower; k++)
                    {
                        int j = i * ShotsPerTower + k;
                        if (shot[j] == null || shotT[j] >= 0f)
                            continue;
                        shotFrom[j] = pos;
                        shotTo[j] = aimAt;
                        shotT[j] = 0f;
                        shotLen[j] = Mathf.Max(0.05f, Vector3.Distance(pos, aimAt) / projectileSpeed);
                        shotTower[j] = i;
                        shotTarget[j] = m;
                        shot[j].gameObject.SetActive(true);
                        shot[j].SetPositionAndRotation(pos, Quaternion.LookRotation(aimAt - pos) * Quaternion.FromToRotation(s.projectileForward, Vector3.forward));
                        break;
                    }
                    break;
                }
                case FireKind.Spray:
                {
                    s.aim.Fire(out var pos, out _);
                    PlayFx(muzzle[i * FxPerTower + muzzleNext[i]], pos, Quaternion.LookRotation(aimAt - pos));
                    muzzleNext[i] = (muzzleNext[i] + 1) % FxPerTower;
                    Hit(m, 0.1f);
                    if (s.burns)
                        Burn(m);
                    if (s.stackBoom && dying[m] <= 0f && ++stacks[m] >= StackToBoom * 3) // 뿜기는 0.18초마다라 3번 = 스택 1
                    {
                        stacks[m] = 0;
                        Boom(aimAt, s.boomSize);
                    }
                    break;
                }
                case FireKind.Pulse:
                {
                    var center = s.aim.yawPivot.position;
                    PlayFx(muzzle[i * FxPerTower + muzzleNext[i]], center, Quaternion.identity);
                    muzzleNext[i] = (muzzleNext[i] + 1) % FxPerTower;
                    for (int k = 0; k < monsters.Length; k++)
                    {
                        if (!Alive(k))
                            continue;
                        var c = Center(k);
                        if ((c.x - center.x) * (c.x - center.x) + (c.z - center.z) * (c.z - center.z) < 2.5f * 2.5f)
                            Hit(k, 1f);
                    }
                    break;
                }
            }
        }

        void TickShots(float dt)
        {
            for (int j = 0; j < shot.Length; j++)
            {
                if (shot[j] == null || shotT[j] < 0f)
                    continue;
                int m = shotTarget[j];
                if (m >= 0 && Alive(m))
                    shotTo[j] = Center(m); // 걷는 몬스터를 따라간다
                shotT[j] += dt / shotLen[j];
                if (shotT[j] < 1f)
                {
                    var p = Vector3.Lerp(shotFrom[j], shotTo[j], shotT[j]) + Vector3.up * (Mathf.Sin(shotT[j] * Mathf.PI) * 0.15f);
                    shot[j].position = p;
                    continue;
                }
                shotT[j] = -1f;
                shot[j].gameObject.SetActive(false);
                int i = shotTower[j];
                var s = towers[i];
                var dir = (shotTo[j] - shotFrom[j]).normalized;
                PlayFx(hit[i * FxPerTower + hitNext[i]], shotTo[j] - dir * 0.1f, Quaternion.LookRotation(-dir)); // 탄 타워: 피격 파티클만
                Hit(m, Mathf.Clamp(s.interval, 0.12f, 1f)); // 연사가 빠른 타워는 한 발이 약하게
            }
        }

        // 화염 계열에 맞으면 burnTime 동안 몸에서 불길이 오른다 (이미 타는 중이면 시간만 늘린다)
        void Burn(int m)
        {
            if (burnFx[m] == null || dying[m] > 0f)
                return;
            if (burning[m] <= 0f)
            {
                burnFx[m].transform.position = Center(m);
                burnFx[m].Play(true);
            }
            burning[m] = burnTime;
        }

        void Boom(Vector3 pos, float size)
        {
            int i = nextBoom;
            nextBoom = (nextBoom + 1) % Booms;
            boomAge[i] = 0f;
            boomSize[i] = size;
            boomPos[i] = pos;
            if (boom[i] != null)
            {
                boom[i].gameObject.SetActive(true);
                boom[i].SetPositionAndRotation(pos, Quaternion.Euler(0f, pos.x * 97f + pos.z * 31f, 0f));
                boom[i].localScale = Vector3.one * (0.2f * size);
            }
            PlayFx(flash[i], pos, Quaternion.identity);
            PlayFx(sparks[i], pos, Quaternion.Euler(-60f, pos.z * 40f, 0f));
            if (cam != null)
            {
                float d = Vector3.Distance(cam.transform.position, pos);
                if (d < shakeDistance)
                    shake = Mathf.Max(shake, (1f - d / shakeDistance) * size);
            }
        }

        void TickBooms(float dt)
        {
            for (int i = 0; i < Booms; i++)
            {
                if (boomAge[i] < 0f)
                    continue;
                boomAge[i] += dt;
                float t = boomAge[i];
                if (t >= BoomLife)
                {
                    boomAge[i] = -1f;
                    if (boom[i] != null)
                        boom[i].gameObject.SetActive(false);
                    continue;
                }
                if (boom[i] == null)
                    continue;
                // 0~0.1초: 확 부풀고, 그 뒤 천천히 줄며 위로 떠오른다
                float scale = t < 0.1f ? Mathf.Lerp(0.2f, 1.15f, t / 0.1f) : Mathf.Lerp(1.15f, 0f, Mathf.Pow((t - 0.1f) / (BoomLife - 0.1f), 1.6f));
                boom[i].localScale = Vector3.one * (scale * boomSize[i]);
                boom[i].position = boomPos[i] + Vector3.up * (t * 0.35f * boomSize[i]);
                if (boomLight[i] != null)
                    boomLight[i].intensity = 4f * boomSize[i] * Mathf.Max(0f, 1f - t / 0.45f);
            }
        }

        void TickCamera(float dt)
        {
            if (cam == null)
                return;
            if (shake <= 0f)
            {
                cam.transform.position = camBase;
                return;
            }
            shake = Mathf.Max(0f, shake - dt * 4f);
            float a = shake * shake * shakeAmount;
            float time = Time.time * 47f;
            cam.transform.position = camBase + new Vector3(Mathf.Sin(time) * a, Mathf.Sin(time * 1.37f + 1f) * a, 0f);
        }
    }
}
