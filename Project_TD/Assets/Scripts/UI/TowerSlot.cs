using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TowerDefence
{
    // 타워 목록의 한 칸: 클릭 → 정보, 끌기 시작 → 드래그 설치. uGUI 이벤트만 받고 Update는 없다.
    public sealed class TowerSlot : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public TowerDef Def;
        public Action<TowerDef> Clicked;
        public Action<TowerDef> DragStarted;

        public void OnPointerClick(PointerEventData e)
        {
            if (!e.dragging)
                Clicked?.Invoke(Def);
        }

        public void OnBeginDrag(PointerEventData e) => DragStarted?.Invoke(Def);
        public void OnDrag(PointerEventData e) { }    // 끌기 이벤트를 받으려면 필요 (위치는 BuildController가 읽는다)
        public void OnEndDrag(PointerEventData e) { } // 놓기 판정은 BuildController (손을 뗀 칸)
    }
}
