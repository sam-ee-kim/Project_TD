using UnityEngine;

public static class HelloWorld
{
    // 씬에 붙이지 않아도 Play 시작 시 자동 호출된다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void PrintHello()
    {
        Debug.Log("Hello world!");
    }
}
