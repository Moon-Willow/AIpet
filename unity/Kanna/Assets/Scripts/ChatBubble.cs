using TMPro;
using UnityEngine;
using DG.Tweening;

public class ChatBubble : MonoBehaviour
{
    [Header("组件")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI textComponent;
    [SerializeField] private RectTransform tailRect;
    [SerializeField] private RectTransform bubbleRect;

    [Header("动画参数")]
    [SerializeField] private float fadeDuration = 0.4f;    // 淡出动画时长

    private Tween scaleTween, fadeTween, moveTween;
    private Vector3 originalLocalPos;

    void Awake()
    {
        if (bubbleRect == null) bubbleRect = transform as RectTransform;
        originalLocalPos = transform.localPosition;
    }

    /// <summary>
    /// 设置气泡文本
    /// </summary>
    public void SetText(string message)
    {
        if (textComponent != null)
            textComponent.text = message;
    }

    /// <summary>
    /// 弹出气泡
    /// </summary>
    public void Show()
    {
        StopAllAnimations();
        gameObject.SetActive(true);
        transform.localPosition = originalLocalPos;

        canvasGroup.alpha = 0f;
        transform.localScale = Vector3.zero;

        scaleTween = transform.DOScale(1f, 0.35f).SetEase(Ease.OutBack);
        fadeTween = canvasGroup.DOFade(1f, 0.3f);
    }

    /// <summary>
    /// 隐藏气泡：上飘并淡出
    /// </summary>
    public void Hide()
    {
        StopAllAnimations();

        moveTween = transform.DOLocalMoveY(originalLocalPos.y + 30f, fadeDuration);
        fadeTween = canvasGroup.DOFade(0f, fadeDuration)
            .OnComplete(() =>
            {
                gameObject.SetActive(false);
                transform.localPosition = originalLocalPos;
            });
    }

    /// <summary>
    /// 让尾巴指向某个世界坐标（例如模型头部）
    /// </summary>
    public void PointTailTowards(Vector3 worldPosition, Camera cam = null)
    {
        if (tailRect == null) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 screenPos = cam.WorldToScreenPoint(worldPosition);
        Vector2 direction = (Vector2)screenPos - (Vector2)tailRect.position;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        tailRect.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
    }

    private void StopAllAnimations()
    {
        scaleTween?.Kill();
        fadeTween?.Kill();
        moveTween?.Kill();
    }

    void OnDestroy()
    {
        StopAllAnimations();
    }
}