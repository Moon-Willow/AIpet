using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 按钮点击后：隐藏主画板，显示指定的目标画板。
/// 将此脚本挂载到每个功能按钮上，并分别指定 mainPanel 和 targetPanel。
/// </summary>
[RequireComponent(typeof(Button))]
public class PanelSwitcher : MonoBehaviour
{
    [Tooltip("要隐藏的主画板（通常是主面板）")]
    public GameObject mainPanel;

    [Tooltip("点击后要显示的目标功能画板")]
    public GameObject targetPanel;

    private void Start()
    {
        // 获取按钮组件，绑定点击事件
        Button btn = GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(SwitchPanel);
        }
    }

    private void SwitchPanel()
    {
        // 隐藏主画板
        if (mainPanel != null)
            mainPanel.SetActive(false);

        // 显示目标画板
        if (targetPanel != null)
            targetPanel.SetActive(true);
    }
}