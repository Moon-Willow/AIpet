using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 给 UI 物体添加拖拽功能，仅负责移动位置，不处理缩放。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UIDragger : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private RectTransform rectTransform;
    private Vector2 dragOffset;      // 鼠标与 UI 本地位置的差值
    private Camera uiCamera;         // Canvas 对应的相机（Overlay 为 null）

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        // 根据父 Canvas 的渲染模式确定需要传入的相机
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null && parentCanvas.renderMode == RenderMode.ScreenSpaceCamera)
            uiCamera = parentCanvas.worldCamera;
        else
            uiCamera = null;  // Overlay 或 World Space 均适合用 null
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // 将屏幕坐标转换为父 RectTransform 的本地坐标
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform.parent as RectTransform,
            eventData.position,
            uiCamera,
            out Vector2 localPointerPos);

        dragOffset = rectTransform.localPosition - (Vector3)localPointerPos;
    }

    public void OnDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform.parent as RectTransform,
            eventData.position,
            uiCamera,
            out Vector2 localPointerPos);

        rectTransform.localPosition = localPointerPos + dragOffset;
    }

    public void OnEndDrag(PointerEventData eventData) { }
}