using UnityEngine;

namespace TowerDefence
{
    // F1 디버그 패널 (에디터·개발 빌드). GameLoop가 켤 때 붙이고 끌 때 떼어 낸다.
    // OnGUI는 컴포넌트에 있기만 해도 매 프레임 IMGUI 이벤트 처리(GC 할당 포함)가 돌기 때문에,
    // 패널을 끈 평소 상태에서는 이 컴포넌트 자체가 없다 (GDD 8-5 웨이브 중 GC 0B).
    public class DebugPanel : MonoBehaviour
    {
        public System.Action Draw;

        void OnGUI() => Draw?.Invoke();
    }
}
