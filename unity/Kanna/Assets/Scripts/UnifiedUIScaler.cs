using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UnifiedUIScaler : MonoBehaviour
{
    [Header("控制输入框 (TMP_InputField)")]
    [SerializeField] private TMP_InputField inputFieldScaleInput;    // 控制 InputField (TMP) 的缩放
    [SerializeField] private TMP_InputField imageTextSizeInput;      // 控制 Image 下文字大小
    [SerializeField] private TMP_InputField inputFieldTextSizeInput; // 控制 InputField 的文字大小


    [Header("目标 UI")]
    [SerializeField] private RectTransform inputFieldToScale;     // InputField 的 RectTransform
    [SerializeField] private TextMeshProUGUI Text;                  // Image 下的 Text (TMP)
    [SerializeField] private TMP_Text inputFieldText;             // InputField 的 textComponent


    /// <summary>
    /// 按钮点击时统一应用四个缩放值。
    /// </summary>
    public void ApplyAllScales()
    {

        // 控制 InputField (TMP) 的整体缩放
        if (inputFieldToScale != null && inputFieldScaleInput != null)
        {
            if (float.TryParse(inputFieldScaleInput.text, out float scale))
            {
                scale = Mathf.Max(scale, 0.1f);
                inputFieldToScale.localScale = new Vector3(scale, scale, scale);
            }
            else
                Debug.LogWarning("InputField缩放输入框内容不是有效数字！");
        }

        // 控制文字的字号 (fontSize)
        if (Text != null && imageTextSizeInput != null)
        {
            if (float.TryParse(imageTextSizeInput.text, out float size))
            {
                size = Mathf.Max(size, 1f); // 字号最小为 1
                Text.fontSize = size;
                Debug.Log(size);
            }
            else
                Debug.LogWarning("Image文字大小输入框内容不是有效数字！");
        }

        // 控制 InputField 自身的文字大小
        if (inputFieldText != null && inputFieldTextSizeInput != null)
        {
            if (float.TryParse(inputFieldTextSizeInput.text, out float size))
            {
                size = Mathf.Max(size, 1f);
                inputFieldText.fontSize = size;
            }
            else
                Debug.LogWarning("InputField文字大小输入框内容不是有效数字！");
        }
    }
}