using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 点击按钮后退出应用程序（编辑器模式下停止播放）
/// </summary>
[RequireComponent(typeof(Button))]
public class QuitApplicationButton : MonoBehaviour
{
    private void Start()
    {
        GetComponent<Button>().onClick.AddListener(Quit);
    }

    private void Quit()
    {
        // 在编辑器模式下停止播放，构建后正常退出
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}