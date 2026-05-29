using Live2D.Cubism.Framework.Motion;
using UnityEngine;

public class CubismMotionLoopPlayer : MonoBehaviour
{
    [SerializeField] public AnimationClip idleAnimation; // 在Inspector中拖入你的待机动画
    private CubismMotionController _motionController;

    private void Start()
    {
        _motionController = GetComponent<CubismMotionController>();
        // 参数: AnimationClip clip, int layerIndex, int priority, bool isLoop
        _motionController.PlayAnimation(idleAnimation, 0, CubismMotionPriority.PriorityIdle, true);
    }
}