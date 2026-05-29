using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 关闭当前功能画板，返回主画板。
/// 挂载到每个功能画板的关闭按钮上。
/// </summary>
[RequireComponent(typeof(Button))]
public class ClosePanelButton : MonoBehaviour
{
    [Tooltip("当前按钮所属的功能画板（需要被隐藏的）")]
    public GameObject currentPanel;

    [Tooltip("主画板（需要被重新显示的）")]
    public GameObject mainPanel;

    private void Start()
    {
        Button btn = GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(ClosePanel);
        }
    }

    private void ClosePanel()
    {
        // 隐藏当前功能画板
        if (currentPanel != null)
            currentPanel.SetActive(false);

        // 显示主画板
        if (mainPanel != null)
            mainPanel.SetActive(true);
    }
}