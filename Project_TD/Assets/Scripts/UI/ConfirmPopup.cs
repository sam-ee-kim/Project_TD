using System;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence
{
    // 화면 정중앙 확인 팝업: 문구 + [왼쪽 확정] / [오른쪽 취소] (GDD 5-1, 5-4, 5-8).
    // 기능 확인용 기본 모양. 정식 디자인은 작업 8.
    public class ConfirmPopup
    {
        GameObject root;
        Text message;
        Text warning;
        Button left;
        Text leftLabel;
        Text rightLabel;

        public bool Visible => root != null && root.activeSelf;

        public void Init(Transform canvas, Action onLeft, Action onRight)
        {
            var rt = BasicUi.Rect("ConfirmPopup", canvas, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 260));
            BasicUi.OwnCanvas(rt);
            BasicUi.Panel(rt, new Color(0.08f, 0.09f, 0.11f, 0.92f));
            root = rt.gameObject;

            var msgRt = BasicUi.Rect("Message", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(520, 60));
            message = BasicUi.Label(msgRt, "", 32, TextAnchor.MiddleCenter, Color.white);

            var warnRt = BasicUi.Rect("Warning", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -95), new Vector2(520, 40));
            warning = BasicUi.Label(warnRt, "", 24, TextAnchor.MiddleCenter, new Color(0.9f, 0.45f, 0.45f));

            left = BasicUi.Button(rt, "Left", "", new Vector2(0.5f, 0f), new Vector2(-120, 60), new Vector2(200, 64), onLeft, out leftLabel);
            BasicUi.Button(rt, "Right", "", new Vector2(0.5f, 0f), new Vector2(120, 60), new Vector2(200, 64), onRight, out rightLabel);
            root.SetActive(false);
        }

        public void Show(string text, string leftText, string rightText)
        {
            message.text = text;
            leftLabel.text = leftText;
            rightLabel.text = rightText;
            SetLeftEnabled(true, "");
            root.SetActive(true);
        }

        // 캐시 부족 등: 왼쪽 버튼 비활성 + 안내 문구.
        public void SetLeftEnabled(bool enabled, string warningText)
        {
            left.interactable = enabled;
            warning.text = warningText;
        }

        public void Hide()
        {
            if (root != null)
                root.SetActive(false);
        }
    }
}
