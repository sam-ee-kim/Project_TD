using UnityEngine;

namespace TowerDefence
{
    // 타워 건축 연출: 모델@Build 클립(부품이 날아와 조립됨)을 표시 모델에 직접 재생한다. 철거는 같은 클립을 거꾸로.
    // Animator 없이 정해진 시간의 자세를 찍는 방식(SampleAnimation)이라 되감기·중간 역재생이 자유롭고 Update도 없다.
    // 연출이 끝나면 처음에 저장해 둔 기본 자세로 되돌려, 합친 메시·조준(TowerAim)과 위치가 정확히 맞게 한다.
    public sealed class TowerBuildAnim
    {
        readonly GameObject view;
        readonly AnimationClip clip;
        readonly Transform[] parts;
        readonly Vector3[] restPosition;
        readonly Quaternion[] restRotation;
        readonly Vector3[] restScale;

        public float Length => clip.length;

        // 표시를 막 만든(기본 자세) 직후에 부른다.
        public TowerBuildAnim(GameObject view, AnimationClip clip) : this(view, clip, null) { }

        // onlyParts가 있으면 그 부품만 기본 자세를 저장·복원한다 (차지 연출: 조준 피벗은 건드리지 않는다).
        public TowerBuildAnim(GameObject view, AnimationClip clip, Transform[] onlyParts)
        {
            this.view = view;
            this.clip = clip;
            if (onlyParts != null && onlyParts.Length > 0)
            {
                parts = (Transform[])onlyParts.Clone();
            }
            else
            {
                var all = view.GetComponentsInChildren<Transform>(true);
                parts = new Transform[all.Length - 1];
                System.Array.Copy(all, 1, parts, 0, parts.Length); // 루트(배치 위치·크기)는 건드리지 않는다
            }
            restPosition = new Vector3[parts.Length];
            restRotation = new Quaternion[parts.Length];
            restScale = new Vector3[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                restPosition[i] = parts[i].localPosition;
                restRotation[i] = parts[i].localRotation;
                restScale[i] = parts[i].localScale;
            }
        }

        // time초 시점의 조립 상태 (0 = 시작, Length = 완성).
        public void Sample(float time) => clip.SampleAnimation(view, Mathf.Clamp(time, 0f, clip.length));

        // 기본(완성) 자세로.
        public void Restore()
        {
            for (int i = 0; i < parts.Length; i++)
            {
                var t = parts[i];
                t.localPosition = restPosition[i];
                t.localRotation = restRotation[i];
                t.localScale = restScale[i];
            }
        }
    }
}
