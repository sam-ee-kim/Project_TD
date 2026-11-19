using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 로비 화면: Ready에서만 도시·로비 카메라·핀이 보이고, 게임이 시작되면 게임 카메라로 돌아간다.
    public class LobbyViewTests
    {
        GameObject root, canvasGo, gameCamGo;
        LobbyView view;
        Camera lobbyCam, gameCam;
        GameObject city;
        Transform anchor;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Lobby");
            city = new GameObject("City");
            city.transform.SetParent(root.transform);
            anchor = new GameObject("PinAnchor").transform;
            anchor.SetParent(city.transform);
            anchor.position = new Vector3(0f, 10f, 0f);
            lobbyCam = new GameObject("LobbyCamera").AddComponent<Camera>();
            lobbyCam.transform.SetParent(root.transform);
            lobbyCam.transform.SetPositionAndRotation(new Vector3(0f, 100f, -50f), Quaternion.Euler(62f, 0f, 0f));
            gameCamGo = new GameObject("Main Camera");
            gameCam = gameCamGo.AddComponent<Camera>();
            canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            view = root.AddComponent<LobbyView>();
            var so = new SerializedObject(view);
            so.FindProperty("lobbyCamera").objectReferenceValue = lobbyCam;
            so.FindProperty("city").objectReferenceValue = city;
            var pins = so.FindProperty("pins");
            pins.arraySize = 1;
            pins.GetArrayElementAtIndex(0).FindPropertyRelative("label").stringValue = "공학연구소";
            pins.GetArrayElementAtIndex(0).FindPropertyRelative("anchor").objectReferenceValue = anchor;
            so.ApplyModifiedPropertiesWithoutUndo();
            view.Init(canvasGo.GetComponent<Canvas>(), gameCam);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(canvasGo);
            Object.DestroyImmediate(gameCamGo);
        }

        [Test]
        public void StartsHidden_GameCameraOn()
        {
            Assert.IsFalse(view.IsShown);
            Assert.IsFalse(city.activeSelf, "게임 중에는 도시를 그리지 않는다");
            Assert.IsFalse(lobbyCam.enabled);
            Assert.IsTrue(gameCam.enabled);
            Assert.AreEqual(1, view.PinCount);
        }

        [Test]
        public void Show_SwapsCamera_AndShowsCity()
        {
            view.Show();
            Assert.IsTrue(view.IsShown);
            Assert.IsTrue(city.activeSelf);
            Assert.IsTrue(lobbyCam.enabled);
            Assert.IsFalse(gameCam.enabled);
            Assert.IsTrue(gameCamGo.activeSelf, "AudioListener가 남도록 카메라 컴포넌트만 끈다");

            view.Hide();
            Assert.IsFalse(city.activeSelf);
            Assert.IsFalse(lobbyCam.enabled);
            Assert.IsTrue(gameCam.enabled);
        }

        [Test]
        public void Tick_PlacesPinAtAnchorScreenPoint()
        {
            view.Show();
            view.Tick(0.1f);
            var pin = view.GetPinRect(0);
            Assert.IsTrue(pin.gameObject.activeSelf);
            // 기준점이 카메라 앞에 있으면 화면 가운데 근처 (캔버스 중심 기준 좌표)
            var screen = lobbyCam.WorldToScreenPoint(anchor.position);
            Assert.Greater(screen.z, 0f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvasGo.transform, screen, null, out var local);
            Assert.AreEqual(local.x, pin.anchoredPosition.x, 0.01f);
            Assert.AreEqual(local.y, pin.anchoredPosition.y, 0.01f);
        }
    }
}
