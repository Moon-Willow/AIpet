using UnityEngine;
using UnityEngine.UI;

public class PanelController : MonoBehaviour
{
    [Header("目标画板")]
    [SerializeField] private GameObject panel;        // 要显示/隐藏的 UI 面板

    [Header("按钮")]
    [SerializeField] private Button openButton;       // 打开面板的按钮
    [SerializeField] private Button closeButton;      // 面板内部的关闭按钮

    void Start()
    {
        // 初始状态：隐藏面板
        if (panel != null)
            panel.SetActive(false);

        // 绑定打开按钮事件
        if (openButton != null)
            openButton.onClick.AddListener(ShowPanel);

        // 绑定关闭按钮事件
        if (closeButton != null)
            closeButton.onClick.AddListener(HidePanel);
    }

    public void ShowPanel()
    {
        if (panel != null)
            panel.SetActive(true);
    }

    public void HidePanel()
    {
        if (panel != null)
            panel.SetActive(false);
    }

    // 可选的切换功能（按需使用）
    public void TogglePanel()
    {
        if (panel != null)
            panel.SetActive(!panel.activeSelf);
    }

    void OnDestroy()
    {
        // 清理监听器避免内存泄漏
        if (openButton != null) openButton.onClick.RemoveListener(ShowPanel);
        if (closeButton != null) closeButton.onClick.RemoveListener(HidePanel);
    }
}