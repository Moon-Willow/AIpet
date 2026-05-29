using UnityEngine;

public class Live2DDragWithUI : MonoBehaviour
{
    [Header("拖拽与UI")]
    [SerializeField] private RectTransform uiElementToFollow; // 要跟随的 UI

    [Header("缩放")]
    [SerializeField] private float zoomSpeed = 0.5f;
    [SerializeField] private float minScale = 0.5f;
    [SerializeField] private float maxScale = 10.0f;

    [Header("右键菜单")]
    [SerializeField] private GameObject contextMenu;          // 右键弹出的 UI 面板

    private Vector3 offset;            // 模型与鼠标在世界空间的差值
    private Vector2 uiOffset;          // UI 与模型在屏幕空间的初始差值
    private bool isDragging;

    void Start()
    {
        // 初始隐藏菜单
        if (contextMenu != null)
            contextMenu.SetActive(false);
    }

    void Update()
    {
        // ===================== 右键菜单（弹出/关闭） =====================
        if (Input.GetMouseButtonDown(1))
        {
            RaycastHit2D hit = Physics2D.Raycast(
                Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero);
            if (hit.collider != null && hit.collider.gameObject == gameObject)
            {
                if (contextMenu != null)
                    contextMenu.SetActive(!contextMenu.activeSelf);
            }
            else
            {
                if (contextMenu != null)
                    contextMenu.SetActive(false);
            }
        }

        // ===================== 左键拖拽开始 =====================
        if (Input.GetMouseButtonDown(0))
        {
            RaycastHit2D hit = Physics2D.Raycast(
                Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero);
            if (hit.collider != null && hit.collider.gameObject == gameObject)
            {
                isDragging = true;

                Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(
                    new Vector3(Input.mousePosition.x, Input.mousePosition.y, 10f));
                offset = transform.position - mouseWorldPos;

                if (uiElementToFollow != null)
                {
                    Vector3 modelScreenPos = Camera.main.WorldToScreenPoint(transform.position);
                    uiOffset = uiElementToFollow.position - modelScreenPos;
                }
            }
        }

        // ===================== 左键拖拽结束 =====================
        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
        }

        // ===================== 滚轮缩放（仅菜单显示时） =====================
        if (contextMenu != null && contextMenu.activeSelf)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                Vector3 newScale = transform.localScale + Vector3.one * scroll * zoomSpeed;
                newScale = ClampScale(newScale);
                transform.localScale = newScale;
            }
        }

        // ===================== 拖拽中更新位置 =====================
        if (isDragging)
        {
            Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(
                new Vector3(Input.mousePosition.x, Input.mousePosition.y, 10f));
            transform.position = mouseWorldPos + offset;

            if (uiElementToFollow != null)
            {
                Vector3 modelScreenPos = Camera.main.WorldToScreenPoint(transform.position);
                uiElementToFollow.position = modelScreenPos + (Vector3)uiOffset;
            }
        }
    }

    private Vector3 ClampScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Clamp(scale.x, minScale, maxScale),
            Mathf.Clamp(scale.y, minScale, maxScale),
            Mathf.Clamp(scale.z, minScale, maxScale)
        );
    }

    public void CloseContextMenu()
    {
        if (contextMenu != null)
            contextMenu.SetActive(false);
    }
}